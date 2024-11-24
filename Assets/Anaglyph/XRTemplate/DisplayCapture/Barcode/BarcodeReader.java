package com.trev3d.DisplayCapture;

import static android.content.ContentValues.TAG;

import android.graphics.Bitmap;

import com.google.android.gms.tasks.Task;
import com.google.gson.Gson;
//import com.google.mlkit.vision.barcode.BarcodeScanner;
//import com.google.mlkit.vision.barcode.BarcodeScannerOptions;
//import com.google.mlkit.vision.barcode.BarcodeScanning;
//import com.google.mlkit.vision.barcode.common.Barcode;
//import com.google.mlkit.vision.common.InputImage;
import com.google.zxing.BinaryBitmap;
import com.google.zxing.LuminanceSource;
import com.google.zxing.RGBLuminanceSource;
import com.google.zxing.Result;
import com.google.zxing.ResultPoint;
import com.google.zxing.common.HybridBinarizer;
import com.google.zxing.qrcode.QRCodeReader;
import com.unity3d.player.UnityPlayer;

import java.io.Serializable;
import java.nio.ByteBuffer;
import java.util.List;
import java.util.Objects;

import android.util.Log;

public class BarcodeReader implements IDisplayCaptureReceiver {

	private static class Result implements Serializable {
		public String text;
		public Point[] points;
		public long timestamp;

		public Result(com.google.zxing.Result result, long timestamp) {
			this(result.getText(), result.getResultPoints(), timestamp);
		}

		public Result(String text, ResultPoint[] points, long timestamp) {
			this.text = text;
			this.timestamp = timestamp;

			this.points = new Point[points.length];
			for(int i = 0; i < points.length; i++)
				this.points[i] = new Point(points[i]);
		}
	}

	private static class Results implements Serializable {
		public BarcodeReader.Result[] results;

		public Results(int size) {
			results = new BarcodeReader.Result[size];
		}
	}

	private static class Point implements Serializable {
		public float x, y;

		public Point(android.graphics.Point point) {
			x = point.x;
			y = point.y;
		}

		public Point(ResultPoint point) {
			x = point.getX();
			y = point.getY	();
		}
	}



	public static BarcodeReader instance = null;

	private final QRCodeReader scanner;
	private final Gson gson;
	
	private boolean enabled;
	private volatile boolean readingBarcode = false;

	private UnityInterface unityInterface;

	private record UnityInterface(String gameObjectName) {
		private void Call(String functionName) {
			UnityPlayer.UnitySendMessage(gameObjectName, functionName, "");
		}

		public void OnBarcodeResults(String json) {
			UnityPlayer.UnitySendMessage(gameObjectName, "OnBarcodeResults", json);
		}
	}

	public BarcodeReader() {

//		var optBuilder = new BarcodeScannerOptions.Builder();
//		optBuilder.setBarcodeFormats(Barcode.FORMAT_QR_CODE);
//		optBuilder.build();
//
//		scanner = BarcodeScanning.getClient(optBuilder.build());

		scanner = new QRCodeReader();

		gson = new Gson();
	}

	public static synchronized BarcodeReader getInstance()
	{
		if (instance == null)
			instance = new BarcodeReader();

		return instance;
	}

	public void setEnabled(boolean enabled) {
		if(this.enabled == enabled)
			return;

		this.enabled = enabled;

		if(this.enabled) {
			DisplayCaptureManager.getInstance().receivers.add(this);
		} else {
			DisplayCaptureManager.getInstance().receivers.remove(this);
		}
	}

	@Override
	public void onNewImage(ByteBuffer byteBuffer, int width, int height, long timestamp) {

		if(readingBarcode)
			return;

		readingBarcode = true;

		new Thread(() -> {

			var bitmap = Bitmap.createBitmap(
				width,
				height,
				Bitmap.Config.ARGB_8888
			);

			byteBuffer.rewind();
			bitmap.copyPixelsFromBuffer(byteBuffer);

			int[] pixels = new int[width * height];
			bitmap.getPixels(pixels, 0, width, 0, 0, width, height);
			BinaryBitmap binaryBitmap = new BinaryBitmap(new HybridBinarizer(new RGBLuminanceSource(width, height, pixels)));

			Result result;

			try {
				var barcodeResult = scanner.decode(binaryBitmap);
				result = new Result(barcodeResult, timestamp);

			} catch (Exception e) {
				readingBarcode = false;
				return;
			}

			readingBarcode = false;

			Results results = new Results(1);
			results.results[0] = result;

			String resultsAsJson = gson.toJson(results);
			Log.i(TAG, "JSON: " + resultsAsJson);
			unityInterface.OnBarcodeResults(resultsAsJson);
		}).start();

		/*InputImage input = InputImage.fromBitmap(bitmap, 0);

		readingBarcode = true;
		scanner.process(input).addOnCompleteListener(task -> {

			readingBarcode = false;

			if (!task.isSuccessful()) {
				Log.v(TAG, "No barcode found.");
				return;
			}

			var taskResult = task.getResult();
			Results results = new Results(taskResult.size());

			Log.i(TAG, taskResult.size() + " barcodes found.");

			for(int i = 0; i < taskResult.size(); i++) {
				Barcode barcode = taskResult.get(i);
				Log.i(TAG, "Barcode: " + barcode.getDisplayValue());

				Result result = new Result();
				result.text = barcode.getDisplayValue();
				result.timestamp = timestamp;

				var cornerPoints = Objects.requireNonNull(barcode.getCornerPoints());
				result.points = new Point[cornerPoints.length];
				for(int j = 0; j < cornerPoints.length; j++)
					result.points[j] = new Point(cornerPoints[j]);
				results.results[i] = result;
			}

			String resultsAsJson = gson.toJson(results);
			Log.i(TAG, "JSON: " + resultsAsJson);
			unityInterface.OnBarcodeResults(resultsAsJson);
		}); */
	}

	// called by Unity
	public void setup(String gameObjectName) {
		unityInterface = new UnityInterface(gameObjectName);
	}

//	public BarcodeResult[] getResults() {
//		return barcodeResults;
//	}
}
