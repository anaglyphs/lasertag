package com.anaglyph.toastprobe;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.os.Handler;
import android.os.Looper;

/** Toasts with no activity of this app on screen, e.g. over an immersive app. */
public class ToastReceiver extends BroadcastReceiver {
    @Override
    public void onReceive(Context context, Intent intent) {
        String kind = intent.getStringExtra("kind");
        ToastKinds.show(context, kind == null ? "text" : kind, intent.getStringExtra("text"));
        PendingResult keepProcessAliveWhileToastShows = goAsync();
        new Handler(Looper.getMainLooper()).postDelayed(keepProcessAliveWhileToastShows::finish, 5000);
    }
}
