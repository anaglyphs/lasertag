package com.anaglyph.toastprobe;

import android.app.Activity;
import android.content.Context;
import android.graphics.Color;
import android.os.SystemClock;
import android.util.Log;
import android.widget.TextView;
import android.widget.Toast;

final class ToastKinds {
    static final String TAG = "ToastProbe";

    static void show(Context context, String kind, String text) {
        if (text == null) text = "Toast probe";
        switch (kind) {
            case "none":
                break;
            case "text":
                Toast.makeText(context.getApplicationContext(), text, Toast.LENGTH_SHORT).show();
                break;
            case "text-long":
                Toast.makeText(context.getApplicationContext(), text, Toast.LENGTH_LONG).show();
                break;
            case "activity-context":
                if (!(context instanceof Activity)) {
                    Log.e(TAG, "activity-context needs the activity");
                    return;
                }
                Toast.makeText(context, text, Toast.LENGTH_SHORT).show();
                break;
            case "custom-view":
                TextView view = new TextView(context);
                view.setText(text);
                view.setTextColor(Color.WHITE);
                view.setBackgroundColor(Color.DKGRAY);
                view.setPadding(24, 12, 24, 12);
                Toast customToast = new Toast(context);
                customToast.setView(view);
                customToast.setDuration(Toast.LENGTH_SHORT);
                customToast.show();
                break;
            default:
                Log.e(TAG, "unknown kind " + kind);
                return;
        }
        Log.i(TAG, "shown kind=" + kind + " uptimeMs=" + SystemClock.uptimeMillis());
    }

    private ToastKinds() {}
}
