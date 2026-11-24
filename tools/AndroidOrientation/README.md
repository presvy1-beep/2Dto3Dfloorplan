# Android orientation capture

Install the diagnostic build, connect an unlocked phone with USB debugging, and run from the repository root:

```powershell
adb install -r Builds/arn.apk
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/AndroidOrientation/Collect-Orientation.ps1
```

Use `-Serial <adb-serial>` if more than one device is connected. The script closes the app, records the Home screen, cold-starts the app, records it after eight seconds, returns Home, and records again. It saves `before-*`, `running-*`, and `after-*` under `Logs/orientation-capture`. It reads `accelerometer_rotation` and `user_rotation`; it never writes either setting.

For each phase, inspect `*-state.txt`, `*-summary.txt`, and `*-orientation-logcat.txt`. The full `display`, `window`, `activity`, `input`, and `package` dumps are retained in that folder. Compare the Home screen rotation before and after, the foreground activity and its requested orientation, and the Unity `[ARNav][Orientation]` and `[ARNav][Display]` messages. Record whether the splash, app, and Home screen look upright while running the script. These device observations determine whether the app request is ignored, Unity reports a different orientation, or the system display itself remains rotated.

The APK currently targets Android API 36. On Android 16, fixed activity orientation can be ignored on displays with smallest width at least 600 dp; the device model, Android version, size, and density in this capture are needed to determine whether that rule applies. The capture contains device logs and should be shared only as needed for debugging.
