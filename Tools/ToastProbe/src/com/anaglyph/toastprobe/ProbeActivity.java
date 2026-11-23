package com.anaglyph.toastprobe;

import android.app.Activity;
import android.content.Intent;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.widget.TextView;

public class ProbeActivity extends Activity {
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        TextView label = new TextView(this);
        label.setText("Toast probe. Driven by Tools/ToastProbe/toast-probe.sh");
        label.setTextSize(24);
        label.setPadding(48, 48, 48, 48);
        setContentView(label);
        showRequestedToast(getIntent());
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        showRequestedToast(intent);
    }

    private void showRequestedToast(Intent intent) {
        String kind = intent.getStringExtra("kind");
        if (kind == null) return;
        String text = intent.getStringExtra("text");
        new Handler(Looper.getMainLooper()).postDelayed(
                () -> ToastKinds.show(this, kind, text),
                intent.getIntExtra("delayMs", 0));
    }
}
