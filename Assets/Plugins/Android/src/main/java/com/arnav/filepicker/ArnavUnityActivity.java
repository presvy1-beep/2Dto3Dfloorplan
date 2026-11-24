package com.arnav.filepicker;

import android.app.Activity;
import android.content.Intent;
import android.os.Bundle;
import com.unity3d.player.UnityPlayerGameActivity;

/** Unity activity that forwards the system document-picker result to the small ARNav bridge. */
public class ArnavUnityActivity extends UnityPlayerGameActivity {
    @Override protected void onCreate(Bundle savedInstanceState) {
        // The manifest locks reversePortrait before this activity and the Unity
        // splash are created. Do not issue a competing orientation request here.
        super.onCreate(savedInstanceState);
    }

    @Override protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode == ArnavImagePicker.REQUEST_CODE || requestCode == ArnavImagePicker.EXPORT_REQUEST_CODE || requestCode == ArnavImagePicker.JSON_OPEN_REQUEST_CODE) {
            ArnavImagePicker.handleActivityResult(this, requestCode, resultCode, data);
        }
    }
}
