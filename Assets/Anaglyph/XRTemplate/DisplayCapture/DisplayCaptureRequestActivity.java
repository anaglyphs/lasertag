package com.trev3d.DisplayCapture;

import static android.content.ContentValues.TAG;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.media.projection.MediaProjectionManager;
import android.os.Bundle;
import android.util.Log;

public class DisplayCaptureRequestActivity extends Activity {

	private static final int REQUEST_MEDIA_PROJECTION = 1;

	@Override
	public void onCreate(Bundle savedInstanceState) {
		super.onCreate(savedInstanceState);

		var manager = (MediaProjectionManager) getSystemService(Context.MEDIA_PROJECTION_SERVICE);
		startActivityForResult(manager.createScreenCaptureIntent(), REQUEST_MEDIA_PROJECTION);
	}

	@Override
	public void onActivityResult(int requestCode, int resultCode, Intent intent) {
		if (requestCode != REQUEST_MEDIA_PROJECTION) return;

		Log.i(TAG, "Got screen capture permission!");

		DisplayCaptureManager.getInstance().onPermissionResponse(resultCode, intent);

		finish();
	}
}
