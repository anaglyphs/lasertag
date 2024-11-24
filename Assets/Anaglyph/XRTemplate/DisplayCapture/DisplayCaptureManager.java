package com.trev3d.DisplayCapture;

import static android.content.ContentValues.TAG;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.graphics.PixelFormat;
import android.hardware.display.DisplayManager;
import android.hardware.display.VirtualDisplay;
import android.media.Image;
import android.media.ImageReader;
import android.media.projection.MediaProjection;
import android.media.projection.MediaProjectionManager;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;

import androidx.annotation.NonNull;

import com.unity3d.player.UnityPlayer;

import java.nio.ByteBuffer;
import java.util.ArrayList;

public class DisplayCaptureManager implements ImageReader.OnImageAvailableListener {

	public static DisplayCaptureManager instance = null;
	public ArrayList<IDisplayCaptureReceiver> receivers;

	private ImageReader reader;
	private MediaProjection projection;
	private VirtualDisplay virtualDisplay;
	private Intent notifServiceIntent;

	private ByteBuffer byteBuffer;

	private int width;
	private int height;

	private UnityInterface unityInterface;

	private record UnityInterface(String gameObjectName) {

		private void Call(String functionName) {
			UnityPlayer.UnitySendMessage(gameObjectName, functionName, "");
		}

		public void OnCaptureStarted() {
			Call("OnCaptureStarted");
		}

		public void OnPermissionDenied() {
			Call("OnPermissionDenied");
		}

		public void OnCaptureStopped() {
			Call("OnCaptureStopped");
		}

		public void OnNewFrameAvailable() {
			Call("OnNewFrameAvailable");
		}
	}

	public DisplayCaptureManager() {
		receivers = new ArrayList<IDisplayCaptureReceiver>();
	}

	public static synchronized DisplayCaptureManager getInstance() {
		if (instance == null)
			instance = new DisplayCaptureManager();

		return instance;
	}

	public void onPermissionResponse(int resultCode, Intent intent) {

		if (resultCode != Activity.RESULT_OK) {
			unityInterface.OnPermissionDenied();
			Log.i(TAG, "Screen capture permission denied!");
			return;
		}

		notifServiceIntent = new Intent(
				UnityPlayer.currentContext,
				DisplayCaptureNotificationService.class);
		UnityPlayer.currentContext.startService(notifServiceIntent);

		new Handler(Looper.getMainLooper()).postDelayed(() -> {

			Log.i(TAG, "Starting screen capture...");

			var projectionManager = (MediaProjectionManager)
					UnityPlayer.currentContext.getSystemService(Context.MEDIA_PROJECTION_SERVICE);
			projection = projectionManager.getMediaProjection(resultCode, intent);

			projection.registerCallback(new MediaProjection.Callback() {
				@Override
				public void onStop() {

					Log.i(TAG, "Screen capture ended!");

					handleScreenCaptureEnd();
				}
			}, new Handler(Looper.getMainLooper()));

			virtualDisplay = projection.createVirtualDisplay("ScreenCapture",
					width, height, 300,
					DisplayManager.VIRTUAL_DISPLAY_FLAG_AUTO_MIRROR,
					reader.getSurface(), null, null);

			unityInterface.OnCaptureStarted();

		}, 100);

		Log.i(TAG, "Screen capture started!");
	}

	@Override
	public void onImageAvailable(@NonNull ImageReader imageReader) {
		Image image = imageReader.acquireLatestImage();

		if (image == null) return;

		ByteBuffer buffer = image.getPlanes()[0].getBuffer();
		buffer.rewind();

		// Clear the buffer for new data by resetting the position of the buffer to zero
		byteBuffer.clear();
		byteBuffer.put(buffer);

		long timestamp = image.getTimestamp();

		image.close();

		for(int i = 0; i < receivers.size(); i++) {
			buffer.rewind();
			receivers.get(i).onNewImage(byteBuffer, width, height, timestamp);
		}

		unityInterface.OnNewFrameAvailable();
	}

	private void handleScreenCaptureEnd() {

		virtualDisplay.release();
		UnityPlayer.currentContext.stopService(notifServiceIntent);

		unityInterface.OnCaptureStopped();
	}

	// Called by Unity

	public void setup(String gameObjectName, int width, int height) {

		unityInterface = new UnityInterface(gameObjectName);

		this.width = width;
		this.height = height;

		// Calculate the exact buffer size required (4 bytes per pixel for RGBA_8888)
		int bufferSize = width * height * 4;

		// Allocate a direct ByteBuffer for better performance
		byteBuffer = ByteBuffer.allocateDirect(bufferSize);

		reader = ImageReader.newInstance(width, height, PixelFormat.RGBA_8888, 2);

		reader.setOnImageAvailableListener(this, new Handler(Looper.getMainLooper()));
	}

	public void requestCapture() {
		Log.i(TAG, "Asking for screen capture permission...");
		Intent intent = new Intent(
				UnityPlayer.currentActivity,
				DisplayCaptureRequestActivity.class);
		UnityPlayer.currentActivity.startActivity(intent);
	}

	public void stopCapture() {
		Log.i(TAG, "Stopping screen capture...");

		if(projection == null) return;
		projection.stop();
	}

	public ByteBuffer getByteBuffer() {
		return byteBuffer;
	}
}
