# Android compatibility and performance review

## Configuration evidence

- Unity Editor: 6000.6.0f1.
- `ProjectSettings/ProjectSettings.asset`: minimum SDK 26 (Android 8), target SDK `0` (Unity default), ARM64 architecture only, Android scripting backend enum value `1` (IL2CPP in Unity Player Settings), application ID fields retain `com.DefaultCompany.2D-URP` for Standalone and override default enabled. Verify Android-specific ID in Editor and built APK.
- Manifest: custom Unity Game Activity subclass, portrait, `resizeableActivity=false`, no explicit `uses-permission` declarations.
- Storage: Android Storage Access Framework via `ACTION_OPEN_DOCUMENT` and `ACTION_CREATE_DOCUMENT`; no broad storage permission is needed for user-selected files.
- Location: Unity location API is used through `UnityLocationProvider`; manifest shown here contains no explicit location permissions. Unity may merge required permissions at build time; inspect the final merged manifest and request flow.
- URP 17.6.0, Input System 1.20.0, AI Navigation 2.0.10, UGUI 2.6.0.

## Compatibility interpretation

Min SDK 26 excludes Android 7.x and earlier. ARM64-only omits 32-bit devices, usually acceptable for a modern release but must match the intended audience. Because target SDK is left at Unity default, do not infer a concrete Android 12–16 target level from the source setting. Check Unity 6000.6's effective target and merged manifest from the actual APK. Android version alone does not validate API compatibility: test SAF, lifecycle, location permissions and rendering on target devices.

The activity locks to portrait twice (native activity and Unity bootstrap), and the manifest disables resizing. This is consistent internally but limits tablets, foldables, split-screen and rotation-based layouts. Supporting those form factors needs adaptive UI/camera work first.

## Performance risks (not measurements)

1. `FloorPlanAnalysisController` materializes full pixel and luminance arrays; `ArchitecturalWallMaskProcessor` allocates several masks, labels, queue buffers, and component lists. Extraction runs on the main thread after one frame. This is the clearest likely responsiveness risk.
2. `NavigationController.BuildNavigationGrid` can create up to 500,000 5 cm cells and checks room/wall geometry repeatedly. The cap is useful but the upper end can still be costly.
3. Procedural geometry and NavMesh are built on explicit user actions. Large numbers of meshes, colliders, route markers and line renderers can increase memory/CPU; actual counts vary with plan complexity.
4. URP quality/shadow/render settings are project-controlled; inspect `ProjectSettings/QualitySettings.asset`, URP asset and build report on the target profile. Do not assume desktop defaults are mobile-appropriate.
5. No APK size, frame rate, battery, startup or memory numbers were measured.

## Mid-range phone measurement procedure

1. Use a Development Build with Autoconnect Profiler for diagnosis; repeat a non-development/release build for realistic performance. Record device model, Android version, display resolution, ABI, Unity version and APK size.
2. Unity Profiler: record startup through MapSetup, import/decode, analysis, 3D generation/NavMesh build, route creation and 60 seconds idle. Inspect CPU Timeline main-thread spikes, GC Alloc, render thread, GPU/frame time and memory. Use Memory Profiler snapshots before import, after analysis, after leaving Navigation, and after repeated imports.
3. On device, capture `adb logcat -c`, reproduce each flow, then `adb logcat -d -v time` and filter Unity/AndroidRuntime/ARNavOrientation/ARNavImagePicker. Record crash/ANR and permission messages.
4. Compare sustained frame pacing during camera gestures and route view; use Android GPU rendering/profile tools and Unity Profiler GPU module when device support permits. Repeat unplugged for thermal and battery behavior.
5. Measure startup from launch intent to interactive setup (video or timestamped log marker); report median across at least five cold launches, separating first launch/import shader work.
6. Inspect output with Android Studio APK Analyzer or `apkanalyzer`: package ID, target/min SDK, ABI, merged permissions, compressed/uncompressed asset size and native libraries.

## Lifecycle and permissions tests

- Open picker, cancel/select, press Home while picker is visible, restore app and verify one result; force-stop/relaunch only as a separate process-death test.
- Deny/allow location permission, disable system location, revoke permission in settings, resume app; confirm meaningful UI and provider recovery.
- Press Home/lock during analysis and during NavMesh building; resume and verify UI, callback and memory state.
- Verify Android back button in MapSetup and Navigation. No clear app-level `Escape`/back handler was identified in the inspected searches; device test required.
- Test Android 12 through 16 where available, including SAF MIME types and document providers, gesture navigation, display cutouts and font scaling.

## Practical recommendations

Prioritize bounded image sizes and incremental analysis, profile generated geometry/grid build, select explicit application ID/target SDK, and test lifecycle. Keep SAF instead of adding broad storage permissions. Keep ARM64-only unless 32-bit support is a stated requirement. Avoid adding ARCore or other large dependencies until the core map workflow has stable measured performance.
