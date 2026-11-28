package com.anaglyph.camera2;

import android.content.Context;
import android.graphics.ImageFormat;
import android.graphics.Rect;
import android.hardware.camera2.CameraCaptureSession;
import android.hardware.camera2.CameraCharacteristics;
import android.hardware.camera2.CameraDevice;
import android.hardware.camera2.CameraManager;
import android.hardware.camera2.CameraMetadata;
import android.hardware.camera2.CaptureRequest;
import android.hardware.camera2.params.OutputConfiguration;
import android.hardware.camera2.params.SessionConfiguration;
import android.hardware.camera2.params.StreamConfigurationMap;
import android.media.Image;
import android.media.ImageReader;
import android.os.Handler;
import android.os.HandlerThread;
import android.os.SystemClock;
import android.util.Log;
import android.util.Size;

import java.nio.ByteBuffer;
import java.util.Collections;

/**
 * Streams one Camera2 camera's luma plane into a few direct buffers that Unity reads in place.
 * Timestamps are CLOCK_MONOTONIC nanoseconds. A buffer stays untouched while held, from
 * acquireLatest until its release.
 */
public final class Camera2FrameSource {
    private static final String TAG = "Camera2FrameSource";
    private static final int BUFFER_COUNT = 4;

    private final Object lock = new Object();
    private final ByteBuffer[] buffers = new ByteBuffer[BUFFER_COUNT];
    private final long[] timestampsNs = new long[BUFFER_COUNT];
    private final int[] holds = new int[BUFFER_COUNT];
    private int latest = -1;

    private int width;
    private int height;
    private float[] intrinsics;
    private boolean timestampsAreBoottime;
    private String description = "";

    private volatile boolean opening;
    private volatile boolean running;

    private HandlerThread thread;
    private Handler handler;
    private CameraDevice device;
    private CameraCaptureSession session;
    private ImageReader reader;

    /** Opens the camera at the widest YUV size no wider than maxWidth. False when it can't even try. */
    public boolean start(Context context, String cameraId, int maxWidth) {
        stop();
        try {
            CameraManager manager = (CameraManager) context.getSystemService(Context.CAMERA_SERVICE);
            CameraCharacteristics characteristics = manager.getCameraCharacteristics(cameraId);

            Size size = chooseSize(characteristics, maxWidth);
            if (size == null) {
                Log.e(TAG, "camera " + cameraId + " has no YUV_420_888 output");
                return false;
            }

            Integer timestampSource = characteristics.get(CameraCharacteristics.SENSOR_INFO_TIMESTAMP_SOURCE);
            Integer poseReference = characteristics.get(CameraCharacteristics.LENS_POSE_REFERENCE);
            timestampsAreBoottime = timestampSource != null &&
                    timestampSource == CameraMetadata.SENSOR_INFO_TIMESTAMP_SOURCE_REALTIME;

            synchronized (lock) {
                if (width != size.getWidth() || height != size.getHeight()) {
                    width = size.getWidth();
                    height = size.getHeight();
                    for (int i = 0; i < BUFFER_COUNT; i++) {
                        buffers[i] = ByteBuffer.allocateDirect(width * height);
                        holds[i] = 0;
                    }
                }
                latest = -1;
            }
            intrinsics = computeIntrinsics(characteristics, width, height);
            description = "camera " + cameraId + " " + width + "x" + height +
                    ", timestamp source " + timestampSource + " (1 = boot time)" +
                    ", lens pose reference " + poseReference + " (0 primary camera, 1 gyroscope, 2 undefined)" +
                    ", intrinsics " + (intrinsics == null ? "none" :
                    intrinsics[0] + " " + intrinsics[1] + " " + intrinsics[2] + " " + intrinsics[3]);

            thread = new HandlerThread(TAG);
            thread.start();
            handler = new Handler(thread.getLooper());

            reader = ImageReader.newInstance(width, height, ImageFormat.YUV_420_888, 3);
            reader.setOnImageAvailableListener(this::onImageAvailable, handler);

            opening = true;
            manager.openCamera(cameraId, deviceCallback, handler);
            return true;
        } catch (Exception e) {
            Log.e(TAG, "could not open camera " + cameraId, e);
            stop();
            return false;
        }
    }

    public void stop() {
        opening = false;
        running = false;
        if (session != null) { session.close(); session = null; }
        if (device != null) { device.close(); device = null; }
        if (reader != null) { reader.close(); reader = null; }
        if (thread != null) { thread.quitSafely(); thread = null; handler = null; }
        synchronized (lock) { latest = -1; }
    }

    /** Opening or streaming: no need to start again. */
    public boolean isActive() { return opening || running; }

    public boolean isRunning() { return running; }

    public int getWidth() { return width; }

    public int getHeight() { return height; }

    /** fx, fy, cx, cy in output pixels, or null when the camera doesn't publish a calibration. */
    public float[] getIntrinsics() { return intrinsics; }

    public String getDescription() { return description; }

    public int getBufferCount() { return BUFFER_COUNT; }

    public ByteBuffer getBuffer(int index) { return buffers[index]; }

    /** Holds the newest frame's buffer and returns its index, or -1 when there is none yet. */
    public int acquireLatest() {
        synchronized (lock) {
            if (latest < 0) return -1;
            holds[latest]++;
            return latest;
        }
    }

    public long getTimestampNs(int index) {
        synchronized (lock) { return timestampsNs[index]; }
    }

    public void release(int index) {
        synchronized (lock) {
            if (holds[index] > 0) holds[index]--;
        }
    }

    private int freeBuffer() {
        for (int i = 0; i < BUFFER_COUNT; i++)
            if (i != latest && holds[i] == 0) return i;
        return -1;
    }

    private void onImageAvailable(ImageReader source) {
        Image image = source.acquireLatestImage();
        if (image == null) return;
        try {
            int target;
            synchronized (lock) { target = freeBuffer(); }
            if (target < 0) return;

            Image.Plane luma = image.getPlanes()[0];
            ByteBuffer from = luma.getBuffer();
            int rowStride = luma.getRowStride();
            ByteBuffer to = buffers[target];
            to.clear();
            for (int row = 0; row < height; row++) {
                from.limit(from.capacity());
                from.position(row * rowStride);
                from.limit(row * rowStride + width);
                to.put(from);
            }

            long timestampNs = image.getTimestamp();
            if (timestampsAreBoottime)
                timestampNs -= SystemClock.elapsedRealtimeNanos() - System.nanoTime();

            synchronized (lock) {
                timestampsNs[target] = timestampNs;
                latest = target;
            }
        } finally {
            image.close();
        }
    }

    private final CameraDevice.StateCallback deviceCallback = new CameraDevice.StateCallback() {
        @Override
        public void onOpened(CameraDevice camera) {
            device = camera;
            try {
                OutputConfiguration output = new OutputConfiguration(reader.getSurface());
                SessionConfiguration configuration = new SessionConfiguration(
                        SessionConfiguration.SESSION_REGULAR,
                        Collections.singletonList(output),
                        handler::post,
                        sessionCallback);
                camera.createCaptureSession(configuration);
            } catch (Exception e) {
                Log.e(TAG, "could not create capture session", e);
                stop();
            }
        }

        @Override
        public void onDisconnected(CameraDevice camera) {
            Log.w(TAG, "camera disconnected");
            camera.close();
            if (device == camera) device = null;
            opening = false;
            running = false;
        }

        @Override
        public void onError(CameraDevice camera, int error) {
            Log.e(TAG, "camera error " + error);
            camera.close();
            if (device == camera) device = null;
            opening = false;
            running = false;
        }
    };

    private final CameraCaptureSession.StateCallback sessionCallback = new CameraCaptureSession.StateCallback() {
        @Override
        public void onConfigured(CameraCaptureSession configured) {
            session = configured;
            try {
                CaptureRequest.Builder request = device.createCaptureRequest(CameraDevice.TEMPLATE_PREVIEW);
                request.addTarget(reader.getSurface());
                configured.setRepeatingRequest(request.build(), null, handler);
                running = true;
                Log.i(TAG, "streaming " + description);
            } catch (Exception e) {
                Log.e(TAG, "could not start repeating request", e);
                stop();
            } finally {
                opening = false;
            }
        }

        @Override
        public void onConfigureFailed(CameraCaptureSession failed) {
            Log.e(TAG, "capture session configuration failed");
            stop();
        }
    };

    private static Size chooseSize(CameraCharacteristics characteristics, int maxWidth) {
        StreamConfigurationMap map = characteristics.get(CameraCharacteristics.SCALER_STREAM_CONFIGURATION_MAP);
        if (map == null) return null;
        Size[] sizes = map.getOutputSizes(ImageFormat.YUV_420_888);
        if (sizes == null || sizes.length == 0) return null;

        Size best = null;
        Size smallest = sizes[0];
        for (Size size : sizes) {
            if (size.getWidth() < smallest.getWidth()) smallest = size;
            if (size.getWidth() <= maxWidth && (best == null || size.getWidth() > best.getWidth()))
                best = size;
        }
        return best != null ? best : smallest;
    }

    // the calibration is in pixels of the pre-correction active array; an output stream is the
    // largest centered crop of that array with the output's aspect, scaled uniformly
    private static float[] computeIntrinsics(CameraCharacteristics characteristics, int width, int height) {
        float[] calibration = characteristics.get(CameraCharacteristics.LENS_INTRINSIC_CALIBRATION);
        Rect array = characteristics.get(CameraCharacteristics.SENSOR_INFO_PRE_CORRECTION_ACTIVE_ARRAY_SIZE);
        if (array == null) array = characteristics.get(CameraCharacteristics.SENSOR_INFO_ACTIVE_ARRAY_SIZE);
        if (calibration == null || calibration.length < 4 || array == null) return null;

        float arrayWidth = array.width();
        float arrayHeight = array.height();
        float cropWidth = Math.min(arrayWidth, arrayHeight * width / (float) height);
        float cropHeight = cropWidth * height / (float) width;
        float scale = width / cropWidth;
        float offsetX = (arrayWidth - cropWidth) * 0.5f;
        float offsetY = (arrayHeight - cropHeight) * 0.5f;

        return new float[] {
                calibration[0] * scale,
                calibration[1] * scale,
                (calibration[2] - offsetX) * scale,
                (calibration[3] - offsetY) * scale,
        };
    }
}
