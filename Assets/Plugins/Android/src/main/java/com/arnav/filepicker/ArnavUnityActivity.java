package com.arnav.filepicker;

import android.app.Activity;
import android.content.pm.ActivityInfo;
import android.content.Intent;
import android.os.Bundle;
import com.unity3d.player.UnityPlayerGameActivity;

/** Unity activity that forwards the system document-picker result to the small ARNav bridge. */
public class ArnavUnityActivity extends UnityPlayerGameActivity {
    @Override protected void onCreate(Bundle savedInstanceState) {
        // Lock the native window before Unity creates its first surface. This prevents
        // the sensor/reverse-portrait transition from being shown at startup.
        setRequestedOrientation(ActivityInfo.SCREEN_ORIENTATION_PORTRAIT);
        super.onCreate(savedInstanceState);
        setRequestedOrientation(ActivityInfo.SCREEN_ORIENTATION_PORTRAIT);
        android.util.Log.i("ARNavOrientation", "Native activity locked to normal portrait");
    }

    @Override protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode == ArnavImagePicker.REQUEST_CODE || requestCode == ArnavImagePicker.EXPORT_REQUEST_CODE || requestCode == ArnavImagePicker.JSON_OPEN_REQUEST_CODE) {
            ArnavImagePicker.handleActivityResult(this, requestCode, resultCode, data);
        }
    }
}
