package com.trev3d.Camera;

import static android.content.ContentValues.TAG;
import static android.hardware.camera2.CameraCharacteristics.*;

import android.Manifest;
import android.content.Context;
import android.content.pm.PackageManager;
import android.graphics.ImageFormat;
import android.graphics.Rect;
import android.media.Image;
import android.media.ImageReader;
import android.os.Handler;
import android.os.HandlerThread;
import android.hardware.camera2.*;
import android.util.Log;

import java.nio.ByteBuffer;
import java.util.List;

import androidx.annotation.NonNull;

import com.unity3d.player.UnityPlayer;

public class CameraReader {

	private final Context context;
	private final CameraManager manager;

	private CameraCaptureSession session;
	private CameraDevice device;
	private ImageReader reader;
	private Handler handler;

	private String cameraId;
	private int imgWidth;
	private int imgHeight;

	private ByteBuffer byteBuffer;
	private long timestamp;

	private UnityInterface unityInterface;

	private class UnityInterface {

		public String gameObjectName;

		public UnityInterface(String gameObjectName) {
			this.gameObjectName = gameObjectName;
		}

		public void Call(String functionName) {
			UnityPlayer.UnitySendMessage(gameObjectName, functionName, "");
		}

		public void Call(String functionName, String arg) {
			UnityPlayer.UnitySendMessage(gameObjectName, functionName, arg);
		}

		public void OnDeviceOpened() { Call("OnDeviceOpened"); }
		public void OnDeviceClosed() { Call("OnDeviceClosed"); }
		public void OnDeviceDisconnected() { Call("OnDeviceDisconnected"); }
		public void OnDeviceError(String errorCodeAsString) { Call("OnDeviceError", errorCodeAsString); }
		public void OnConfigured() { Call("OnConfigured"); }
		public void OnConfigureFailed() { Call("OnConfigureFailed"); }
		public void OnImageAvailable() { Call("OnImageAvailable"); }
	}

	public CameraReader(String gameObjectName) {
		
		unityInterface = new UnityInterface(gameObjectName);

		context = UnityPlayer.currentContext;
		manager = (CameraManager) context.getSystemService(Context.CAMERA_SERVICE);
	}

	public static CameraReader create(String gameObjectName) {
        return new CameraReader(gameObjectName);
    }

	private final CameraDevice.StateCallback deviceCallback = new CameraDevice.StateCallback() {
		@Override
		public void onOpened(@NonNull CameraDevice camera) {
			try {

				device = camera;

				var format = ImageFormat.YUV_420_888;
				reader = ImageReader.newInstance(imgWidth, imgHeight, format, 3);
				reader.setOnImageAvailableListener(imageCallback, handler);

				int bufferSize = imgWidth * imgHeight;

				byteBuffer = ByteBuffer.allocateDirect(bufferSize);
				device.createCaptureSession(List.of(reader.getSurface()), sessionCallback, handler);

				unityInterface.OnDeviceOpened();

			} catch (CameraAccessException e) {
				Log.e(TAG, e.toString());
				close();
			}
		}

		@Override
		public void onClosed(@NonNull CameraDevice camera) {
			unityInterface.OnDeviceClosed();
			cleanup();
		}

		@Override
		public void onDisconnected(@NonNull CameraDevice camera) {
			unityInterface.OnDeviceDisconnected();
			cleanup();
		}

		@Override
		public void onError(@NonNull CameraDevice camera, int error) {
			unityInterface.OnDeviceError(Integer.toString(error));
			cleanup();
		}
	};

	private final CameraCaptureSession.StateCallback sessionCallback = new CameraCaptureSession.StateCallback() {
		@Override
		public void onConfigured(@NonNull CameraCaptureSession session) {

			CameraReader.this.session = session;

			try {

				var useCase = CameraDevice.TEMPLATE_PREVIEW;
				CaptureRequest.Builder captureRequest = device.createCaptureRequest(useCase);
				captureRequest.addTarget(reader.getSurface());
				session.setRepeatingRequest(captureRequest.build(), null, handler);

				unityInterface.OnConfigured();

			} catch (CameraAccessException e) {
				Log.e(TAG, e.toString());
				cleanup();
			}
		}

		@Override
		public void onConfigureFailed(@NonNull CameraCaptureSession session) {
			unityInterface.OnConfigureFailed();
			cleanup();
		}
	};

	private final ImageReader.OnImageAvailableListener imageCallback = new ImageReader.OnImageAvailableListener() {

		@Override
		public void onImageAvailable(@NonNull ImageReader imageReader) {
			Image image = imageReader.acquireLatestImage();
                if (image == null) return;
            
                Image.Plane yPlane = image.getPlanes()[0];
                ByteBuffer yBuffer = yPlane.getBuffer();
            
                int rowStride = yPlane.getRowStride();
                int pixelStride = yPlane.getPixelStride(); // usually 1
            
                int width = image.getWidth();
                int height = image.getHeight();
            
                int expectedSize = width * height;
            
                if (byteBuffer == null || byteBuffer.capacity() < expectedSize) {
                    byteBuffer = ByteBuffer.allocateDirect(expectedSize);
                }
            
                byteBuffer.clear();
            
                byte[] row = new byte[rowStride];
            
                for (int y = 0; y < height; y++) {
                    int rowStart = y * rowStride;
                    yBuffer.position(rowStart);
                    yBuffer.get(row, 0, rowStride);
            
                    // Copy only real pixels (skip padding)
                    for (int x = 0; x < width; x++) {
                        byteBuffer.put(row[x * pixelStride]);
                    }
                }
            
                byteBuffer.flip();
            
                timestamp = image.getTimestamp();
                image.close();
            
                unityInterface.OnImageAvailable();
		}
	};

	// Called by Unity

	public void setup(String gameObjectName) {
		unityInterface = new UnityInterface(gameObjectName);
	}

	public void configure(int camIndex, int width, int height) {
		try {
			imgWidth = width;
			imgHeight = height;

			String[] camIds = manager.getCameraIdList();
			cameraId = camIds[camIndex];

		} catch (CameraAccessException e) {
			Log.e(TAG, e.toString());
		}
	}

	public void open() {

		if (device != null)
			return;

		try {
			Log.i(TAG, "Starting camera capture");

			HandlerThread handlerThread = new HandlerThread("CameraBackground");
			handlerThread.start();
			handler = new Handler(handlerThread.getLooper());

			var granted = PackageManager.PERMISSION_GRANTED;
			if (context.checkSelfPermission(Manifest.permission.CAMERA) != granted) {
				Log.e(TAG, "No camera permission");
				return;
			}

			manager.openCamera(cameraId, deviceCallback, handler);

			unityInterface.OnDeviceOpened();

		} catch (CameraAccessException e) {
			Log.e(TAG, e.toString());
			cleanup();
		}
	}

	public void close() {
		cleanup();
	}

	private void cleanup() {
		if(session != null) {
			session.close();
			session = null;
		}

		if(device != null) {
			device.close();
			device = null;
		}

		if(reader != null) {
			reader.close();
			reader = null;
		}

		if(handler != null) {
			handler.getLooper().quit();
			handler = null;
		}
	}

	public ByteBuffer getByteBuffer() {
		return byteBuffer;
	}

	public long getTimestamp() {
		return timestamp;
	}

	public float[] getCamPoseOnDevice() {

		try {

			var c = manager.getCameraCharacteristics(cameraId);
			float[] pos = c.get(LENS_POSE_TRANSLATION);
			float[] quat = c.get(LENS_POSE_ROTATION);

			return new float[]{
					pos[0], pos[1], pos[2], quat[0], quat[1], quat[2], quat[3]
			};

		} catch (CameraAccessException e) {
			Log.e(TAG, e.toString());
		}

		return null;
	}

	public float[] getCamIntrinsics() {

		try {

			var c = manager.getCameraCharacteristics(cameraId);

			float[] intrins = c.get(LENS_INTRINSIC_CALIBRATION);
			Rect size = c.get(SENSOR_INFO_PRE_CORRECTION_ACTIVE_ARRAY_SIZE);

			return new float[]{
					intrins[0], intrins[1], intrins[2], intrins[3], intrins[4], size.right, size.bottom
			};

		} catch (CameraAccessException e) {
			Log.e(TAG, e.toString());
		}

		return null;

	}
}