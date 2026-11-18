package com.anaglyph.recording;

import android.content.ContentResolver;
import android.content.ContentValues;
import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.Matrix;
import android.media.MediaCodec;
import android.media.MediaCodecInfo;
import android.media.MediaFormat;
import android.media.MediaMuxer;
import android.net.Uri;
import android.os.Environment;
import android.os.Handler;
import android.os.HandlerThread;
import android.provider.MediaStore;
import android.util.Log;
import android.view.Surface;

import java.io.File;
import java.io.FileInputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.ByteBuffer;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.concurrent.atomic.AtomicBoolean;

/** Writes RGBA frames and 16-bit audio into an H.264 .mp4 with one AAC track per audio stream, then adds the file to the device's Movies. */
public final class VideoEncoder {
    private static final String TAG = "AnaglyphVideoEncoder";

    // matches Anaglyph.Recording.VideoEncoderState
    private static final int IDLE = 0, RECORDING = 1, FINISHING = 2, SAVED = 3, FAILED = 4;
    // streams; the audio ones match Anaglyph.Recording.AudioTrack
    private static final int VIDEO = 0, GAME_AUDIO = 1, MICROPHONE = 2, STREAMS = 3;

    // bits per pixel per frame; 1080p at 30 fps comes to about 12 Mbit/s
    private static final double VIDEO_BITS_PER_PIXEL = 0.2;
    // AAC refuses more bits than a low sample rate can carry; two per sample suits every rate
    private static final int AUDIO_BITS_PER_SAMPLE_PER_CHANNEL = 2;
    private static final int KEY_FRAME_SECONDS = 1;
    private static final long WAIT_MICROS = 10000;
    private static final int END_WAITS = 500;
    private static final String FOLDER = Environment.DIRECTORY_MOVIES + "/Lasertag";

    private static final class Packet {
        int stream;
        ByteBuffer data;
        MediaCodec.BufferInfo info;
    }

    private final Context context;
    private volatile int state = IDLE;
    private volatile String error = "";

    private HandlerThread thread;
    private Handler handler;
    private File file;
    private final MediaCodec[] codecs = new MediaCodec[STREAMS];
    private final MediaFormat[] formats = new MediaFormat[STREAMS];
    private final int[] tracks = new int[STREAMS];
    private final long[] lastMicros = new long[STREAMS];
    private final int[] sampleRates = new int[STREAMS], channelCounts = new int[STREAMS];
    private Surface surface;
    private MediaMuxer muxer;
    private boolean muxing;
    private final ArrayList<Packet> heldUntilMuxing = new ArrayList<>();
    private final ArrayDeque<Long> drawnFrameMicros = new ArrayDeque<>();
    private Bitmap frame;
    private final Matrix bottomRowFirst = new Matrix();
    private final AtomicBoolean frameFree = new AtomicBoolean(true);

    public VideoEncoder(Context context) {
        this.context = context.getApplicationContext();
    }

    public int getState() { return state; }
    public String getError() { return error; }

    /** A microphoneSampleRate of 0 leaves out the microphone track. */
    public boolean begin(String path, int width, int height, int framesPerSecond, int sampleRate, int channels,
                         int microphoneSampleRate, int microphoneChannels) {
        if (state == RECORDING || state == FINISHING) return false;
        try {
            file = new File(path);
            file.delete();

            MediaFormat videoRequest = MediaFormat.createVideoFormat(MediaFormat.MIMETYPE_VIDEO_AVC, width, height);
            videoRequest.setInteger(MediaFormat.KEY_COLOR_FORMAT, MediaCodecInfo.CodecCapabilities.COLOR_FormatSurface);
            videoRequest.setInteger(MediaFormat.KEY_BIT_RATE, (int) (width * (double) height * framesPerSecond * VIDEO_BITS_PER_PIXEL));
            videoRequest.setInteger(MediaFormat.KEY_FRAME_RATE, framesPerSecond);
            videoRequest.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, KEY_FRAME_SECONDS);
            MediaCodec video = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_VIDEO_AVC);
            codecs[VIDEO] = video;
            video.configure(videoRequest, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
            surface = video.createInputSurface();
            video.start();

            startAudioEncoder(GAME_AUDIO, sampleRate, channels);
            if (microphoneSampleRate > 0) {
                try {
                    startAudioEncoder(MICROPHONE, microphoneSampleRate, microphoneChannels);
                } catch (Exception e) {
                    Log.w(TAG, "recording without the microphone", e);
                    releaseCodec(MICROPHONE);
                }
            }

            muxer = new MediaMuxer(path, MediaMuxer.OutputFormat.MUXER_OUTPUT_MPEG_4);
            for (int stream = 0; stream < STREAMS; stream++) {
                formats[stream] = null;
                tracks[stream] = -1;
                lastMicros[stream] = -1;
            }
            muxing = false;
            heldUntilMuxing.clear();
            drawnFrameMicros.clear();

            frame = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888);
            frame.setHasAlpha(false);
            bottomRowFirst.setScale(1, -1);
            bottomRowFirst.postTranslate(0, height);
            frameFree.set(true);

            thread = new HandlerThread(TAG);
            thread.start();
            handler = new Handler(thread.getLooper());
            error = "";
            state = RECORDING;
            return true;
        } catch (Exception e) {
            fail(e);
            release();
            return false;
        }
    }

    private void startAudioEncoder(int stream, int sampleRate, int channels) throws Exception {
        sampleRates[stream] = sampleRate;
        channelCounts[stream] = channels;
        MediaFormat request = MediaFormat.createAudioFormat(MediaFormat.MIMETYPE_AUDIO_AAC, sampleRate, channels);
        request.setInteger(MediaFormat.KEY_AAC_PROFILE, MediaCodecInfo.CodecProfileLevel.AACObjectLC);
        request.setInteger(MediaFormat.KEY_BIT_RATE, AUDIO_BITS_PER_SAMPLE_PER_CHANNEL * sampleRate * channels);
        MediaCodec codec = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_AUDIO_AAC);
        codecs[stream] = codec;
        codec.configure(request, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
        codec.start();
    }

    /** RGBA bytes, bottom row first. The buffer is only read during this call. A frame arriving while the last is still being drawn is skipped. */
    public void appendVideo(ByteBuffer rgba, final long micros) {
        if (state != RECORDING || !frameFree.compareAndSet(true, false)) return;
        rgba.rewind();
        frame.copyPixelsFromBuffer(rgba);
        handler.post(new Runnable() {
            @Override public void run() {
                try {
                    if (state != RECORDING) return;
                    Canvas canvas = surface.lockHardwareCanvas();
                    canvas.drawBitmap(frame, bottomRowFirst, null);
                    surface.unlockCanvasAndPost(canvas);
                    drawnFrameMicros.add(micros);
                    drain(VIDEO, false);
                } catch (Exception e) {
                    fail(e);
                } finally {
                    frameFree.set(true);
                }
            }
        });
    }

    /** Interleaved 16-bit samples in native byte order. The buffer is only read during this call. */
    public void appendAudio(final int stream, ByteBuffer samples, int byteCount, final long micros) {
        if (state != RECORDING || codecs[stream] == null) return;
        final byte[] copy = new byte[byteCount];
        samples.rewind();
        samples.get(copy, 0, byteCount);
        handler.post(new Runnable() {
            @Override public void run() {
                try {
                    if (state != RECORDING) return;
                    MediaCodec audio = codecs[stream];
                    int bytesPerFrame = 2 * channelCounts[stream];
                    int offset = 0;
                    while (offset < copy.length) {
                        int index = audio.dequeueInputBuffer(WAIT_MICROS);
                        if (index < 0) break;
                        ByteBuffer input = audio.getInputBuffer(index);
                        input.clear();
                        int count = Math.min(copy.length - offset, input.capacity());
                        count -= count % bytesPerFrame;
                        input.put(copy, offset, count);
                        audio.queueInputBuffer(index, 0, count, micros + 1000000L * (offset / bytesPerFrame) / sampleRates[stream], 0);
                        offset += count;
                        drain(stream, false);
                    }
                } catch (Exception e) {
                    fail(e);
                }
            }
        });
    }

    public void finish() {
        if (state != RECORDING && state != FAILED) return;
        final boolean failed = state == FAILED;
        if (!failed) state = FINISHING;
        if (handler == null) return;
        handler.post(new Runnable() {
            @Override public void run() {
                try {
                    if (!failed) {
                        codecs[VIDEO].signalEndOfInputStream();
                        drain(VIDEO, true);
                        endAudio(GAME_AUDIO);
                        endAudio(MICROPHONE);
                        if (!muxing) startMuxing();
                        if (!muxing) throw new IllegalStateException("Nothing was recorded");
                        muxer.stop();
                    }
                    release();
                    if (!failed) {
                        addToMovies();
                        state = SAVED;
                    }
                } catch (Exception e) {
                    fail(e);
                    release();
                } finally {
                    if (file != null) file.delete();
                    thread.quitSafely();
                }
            }
        });
    }

    private void endAudio(int stream) {
        MediaCodec audio = codecs[stream];
        if (audio == null) return;
        int index = audio.dequeueInputBuffer(WAIT_MICROS * END_WAITS);
        if (index < 0) return;
        audio.queueInputBuffer(index, 0, 0, lastMicros[stream] + 1, MediaCodec.BUFFER_FLAG_END_OF_STREAM);
        drain(stream, true);
    }

    private void drain(int stream, boolean untilEnd) {
        MediaCodec codec = codecs[stream];
        MediaCodec.BufferInfo info = new MediaCodec.BufferInfo();
        int waits = 0;
        while (true) {
            int index = codec.dequeueOutputBuffer(info, untilEnd ? WAIT_MICROS : 0);
            if (index == MediaCodec.INFO_TRY_AGAIN_LATER) {
                if (!untilEnd || ++waits > END_WAITS) return;
                continue;
            }
            if (index == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
                formats[stream] = codec.getOutputFormat();
                if (allFormatsKnown()) startMuxing();
                continue;
            }
            if (index < 0) continue;

            boolean setup = (info.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) != 0;
            if (!setup && info.size > 0) {
                // a frame's time is when the game drew it, not when it reached the encoder
                if (stream == VIDEO) {
                    Long drawn = drawnFrameMicros.poll();
                    info.presentationTimeUs = drawn != null ? drawn : 0;
                }
                info.presentationTimeUs = Math.max(info.presentationTimeUs, lastMicros[stream] + 1);
                lastMicros[stream] = info.presentationTimeUs;
                ByteBuffer data = codec.getOutputBuffer(index);
                data.position(info.offset);
                data.limit(info.offset + info.size);
                write(stream, data, info);
            }
            boolean ended = (info.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0;
            codec.releaseOutputBuffer(index, false);
            if (ended) return;
        }
    }

    private void write(int stream, ByteBuffer data, MediaCodec.BufferInfo info) {
        if (muxing) {
            if (tracks[stream] >= 0) muxer.writeSampleData(tracks[stream], data, info);
            return;
        }
        Packet packet = new Packet();
        packet.stream = stream;
        packet.data = ByteBuffer.allocate(info.size);
        packet.data.put(data);
        packet.data.flip();
        packet.info = new MediaCodec.BufferInfo();
        packet.info.set(0, info.size, info.presentationTimeUs, info.flags);
        heldUntilMuxing.add(packet);
    }

    private boolean allFormatsKnown() {
        for (int stream = 0; stream < STREAMS; stream++)
            if (codecs[stream] != null && formats[stream] == null) return false;
        return true;
    }

    // a stream whose format never arrived is left out of the file
    private void startMuxing() {
        if (muxing || formats[VIDEO] == null) return;
        for (int stream = 0; stream < STREAMS; stream++)
            if (formats[stream] != null) tracks[stream] = muxer.addTrack(formats[stream]);
        muxer.start();
        muxing = true;
        for (Packet packet : heldUntilMuxing) {
            int track = tracks[packet.stream];
            if (track >= 0) muxer.writeSampleData(track, packet.data, packet.info);
        }
        heldUntilMuxing.clear();
    }

    private void addToMovies() throws Exception {
        ContentResolver resolver = context.getContentResolver();
        ContentValues values = new ContentValues();
        values.put(MediaStore.Video.Media.DISPLAY_NAME, file.getName());
        values.put(MediaStore.Video.Media.MIME_TYPE, "video/mp4");
        values.put(MediaStore.Video.Media.RELATIVE_PATH, FOLDER);
        values.put(MediaStore.Video.Media.IS_PENDING, 1);
        Uri item = resolver.insert(MediaStore.Video.Media.getContentUri(MediaStore.VOLUME_EXTERNAL_PRIMARY), values);
        if (item == null) throw new IllegalStateException("The device refused the video");
        try (InputStream in = new FileInputStream(file); OutputStream out = resolver.openOutputStream(item)) {
            byte[] chunk = new byte[1 << 16];
            for (int read; (read = in.read(chunk)) > 0; ) out.write(chunk, 0, read);
        } catch (Exception e) {
            resolver.delete(item, null, null);
            throw e;
        }
        values.clear();
        values.put(MediaStore.Video.Media.IS_PENDING, 0);
        resolver.update(item, values, null, null);
    }

    private void release() {
        for (int stream = 0; stream < STREAMS; stream++) releaseCodec(stream);
        try { if (surface != null) surface.release(); } catch (Exception ignored) { }
        try { if (muxer != null) muxer.release(); } catch (Exception ignored) { }
        if (frame != null) frame.recycle();
        surface = null;
        muxer = null;
        frame = null;
    }

    private void releaseCodec(int stream) {
        try { if (codecs[stream] != null) { codecs[stream].stop(); codecs[stream].release(); } } catch (Exception ignored) { }
        codecs[stream] = null;
    }

    private void fail(Exception e) {
        Log.e(TAG, "recording failed", e);
        error = e.getMessage() != null ? e.getMessage() : e.toString();
        state = FAILED;
    }
}
