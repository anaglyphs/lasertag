package com.anaglyph.wifidirect;

import android.app.Service;
import android.content.Intent;
import android.os.IBinder;
import android.util.Log;

/**
 * Runs while this app is in a Wi-Fi Direct group, and leaves the group when the user closes the app.
 * The manifest must declare it with android:stopWithTask="false", or closing the app stops it unheard.
 */
public final class WifiDirectGroupTaskWatch extends Service {
    private static final String TAG = "AnaglyphWifiDirectGroup";

    static WifiDirectGroup watchedGroup;

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        return START_NOT_STICKY;
    }

    @Override
    public void onTaskRemoved(Intent rootIntent) {
        Log.i(TAG, "app closed, leaving the group");
        if (watchedGroup != null) watchedGroup.leave();
        stopSelf();
    }

    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }
}
