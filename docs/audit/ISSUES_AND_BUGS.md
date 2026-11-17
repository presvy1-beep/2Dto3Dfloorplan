# Issues and code-quality findings

Severity here reflects likely impact if the described condition occurs. These are source findings or risks, not reproduced runtime failures unless explicitly stated. Exact device performance and scene-reference correctness require Unity/device verification.

## High: Extraction performs full-image work on the main thread

- **Location:** `Assets/ARNav/Runtime/Extraction/FloorPlanAnalysisController.cs`, `Analyze` (pixel copy/luminance loop) and `Run` (extractor call after one `yield return null`).
- **Evidence:** all pixel/mask processing is regular synchronous code; the coroutine yields once, not between processing chunks or on a worker thread.
- **Why/risk:** high-resolution sources create multiple area-sized arrays and lists and can block rendering/input or exceed available memory on mid-range devices.
- **Verify:** import progressively larger PNG/JPEG images; record Unity Profiler CPU Timeline, GC Alloc and Memory Profiler snapshots during Analyze.
- **Impact:** visible freeze, long wait, or process termination on large images.
- **Fix:** enforce dimensions/decoded-byte limits, downsample analysis while retaining source coordinates, process cancellable chunks or a safe pure-data worker pipeline, and show progress/errors.
- **Risk of fix:** algorithm changes can alter extraction quality; threading Unity texture access is unsafe unless pixel data is copied before dispatch.

## High: Release Android identity and target SDK need explicit setting

- **Location:** `ProjectSettings/ProjectSettings.asset` (`applicationIdentifier` Standalone shown as `com.DefaultCompany.2D-URP`; no Android-specific identifier was evident in inspected fields; `AndroidTargetSdkVersion: 0`).
- **Evidence:** current values retain Unity template identity and defer target SDK to Unity's default. This is a release-readiness issue, not proof that the installed APK has the same ID; generated build settings can override.
- **Verify:** inspect Android Player Settings and built APK manifest with `apkanalyzer manifest application-id` and `target-sdk`.
- **Impact:** app identity/update/signing mismatch; target behavior can vary with Unity version and store requirements.
- **Severity:** High for distribution; Low for a private sideload prototype.
- **Fix:** choose stable reverse-domain application ID and explicitly choose a currently supported target SDK after checking store/device requirements.
- **Risk:** changing ID prevents in-place updates to an existing differently signed/identified installation; target changes can expose permission/behavior changes.

## Medium: JSON project writes are not atomic

- **Location:** `Assets/ARNav/Core/Serialization/LocalFileMapRepository.cs`, `SaveProject`.
- **Evidence:** `File.WriteAllText` writes directly to the final `map.json`; there is no temporary file, flush/replace or backup in this class.
- **Verify:** inspect write path; a power/process interruption test is appropriate after adding safe recovery.
- **Impact:** interrupted save can leave unreadable project data.
- **Fix:** serialize/validate fully, write a sibling temp file, flush and replace with backup/recovery handling appropriate to supported platforms.
- **Risk:** replacement semantics differ by filesystem/platform; retain a recoverable backup and test Android behavior.

## Medium: Import/extraction state and UI feedback need lifecycle verification

- **Location:** `Assets/ARNav/Runtime/Import/UnityImageImportService.cs`, `MapSetupController.cs`, `FloorPlanAnalysisController.cs`.
- **Evidence:** native picker has callback recovery and a `busy` guard, but analysis work is not cooperatively cancellable; session/hash checks prevent stale results from being committed. User-visible failure recovery and repeated import behavior depend on scene references and should be verified.
- **Verify:** cancel picker, background/restore app, process recreation, import twice rapidly, import while analyzing, then inspect status and active project.
- **Impact:** confusing stuck/busy states or delayed feedback; stale result commit is guarded by identifiers in setup controller.
- **Fix:** centralized operation state, explicit progress/cancel, deterministic recovery and guaranteed UI reset in success/failure/cancel paths.
- **Risk:** state changes can introduce callbacks firing twice; preserve current deduplication and session/hash checks.

## Medium: Default UI uses legacy UGUI controls and needs responsive-device review

- **Location:** `Assets/ARNav/Scenes/MapSetup.unity`, `Navigation.unity`; `Assets/ARNav/Runtime/App/MinimalMobileUi.cs`; `Assets/ARNav/Editor/ARNavSceneSetup.cs`.
- **Evidence:** scene/editor code uses `UnityEngine.UI.Text` and `InputField`; several compact button labels and controls are present. A safe-area helper exists, but no device matrix evidence is available.
- **Verify:** test smallest supported aspect ratio, gesture navigation inset, large font scale, keyboard open and landscape/system rotation requests.
- **Impact:** clipped or hard-to-hit controls, poor readability and keyboard overlap.
- **Fix:** establish design tokens, dynamic layouts, >=48 dp touch targets, scalable typography and explicit safe-area/keyboard layouts.
- **Risk:** broad layout work can break serialized scene references; implement screen-by-screen with screenshots and acceptance checks.

## Medium: Nav grid construction can scale poorly

- **Location:** `Assets/ARNav/Runtime/Navigation/NavigationController.cs`, `BuildNavigationGrid`.
- **Evidence:** 5 cm grid, up to 500,000 cells; each cell loops through room containment and non-walkable regions, then `IntersectsWall` loops walls and doors. The cap protects allocation size but does not bound total iteration cost.
- **Verify:** profile maps with increasing area and counts of rooms/walls/doors; capture main-thread duration and allocation.
- **Impact:** navigation scene load/rebuild stalls on large or highly segmented maps.
- **Fix:** spatial indexing/bounds filtering, rasterized room/wall occupancy, configurable adaptive cell size and clear maximum-map feedback.
- **Risk:** changed grid resolution/geometry can alter reachability and route validity; compare route fixtures and wall clearance.

## Medium: Portrait-only and non-resizable Android activity constrain device behavior

- **Location:** `Assets/Plugins/Android/AndroidManifest.xml`; `Assets/Plugins/Android/src/main/java/com/arnav/filepicker/ArnavUnityActivity.java`; `AppBootstrap.cs`.
- **Evidence:** manifest declares `resizeableActivity=false` and portrait; activity forces normal portrait on create; bootstrap locks orientation.
- **Verify:** tablet/foldable/multi-window and Android system rotation behavior.
- **Impact:** limits tablets, split-screen and orientation flexibility; likely intentional for current UI but should be a product decision.
- **Fix:** support adaptive/resizable layouts before relaxing manifest/orientation constraints.
- **Risk:** unsupported layout can become clipped or distort camera/input if constraints are removed prematurely.

## Low: Extra sample scene remains in build list

- **Location:** `ProjectSettings/EditorBuildSettings.asset`.
- **Evidence:** `Assets/Scenes/SampleScene.unity` is enabled after the three ARN scenes; `AppBootstrap` redirects to MapSetup from any other active scene.
- **Verify:** inspect whether intended for development and whether included in release build.
- **Impact:** unnecessary build content/scene entry and potential confusion; likely small unless it references substantial assets.
- **Fix:** exclude from release build if unused, after checking build-profile workflows.
- **Risk:** development tooling depending on sample scene may stop working.

## Low: Android image cache cleanup only occurs when opening the picker

- **Location:** `Assets/Plugins/Android/src/main/java/com/arnav/filepicker/ArnavImagePicker.java`, `cleanupOldCacheFiles`.
- **Evidence:** old `arnav-picker-*` files are deleted on next picker launch, not on application startup or after Unity finishes loading the copied file. Repeatedly importing without another picker launch is bounded by user behavior rather than explicit cleanup.
- **Verify:** import repeatedly and inspect app cache size.
- **Impact:** temporary cache growth until next picker launch or Android cleanup.
- **Fix:** delete copied cache file after durable app-private image copy succeeds, with failure-safe lifecycle cleanup.
- **Risk:** premature deletion can race texture decode; only clean after import pipeline confirms it no longer needs the path.

## Security/privacy notes

The SAF flow requests a user-selected document and does not declare broad storage permission in the custom manifest. Project maps and imported image copies are app-private under persistent storage/cache. Geographic anchors and imported building layouts can be sensitive if later exported/shared; no network upload backend was found. The GLB export uses an explicit save picker. No privacy policy/analytics audit can be concluded from source alone; package presence is not proof of runtime data collection.

## Unverified checks

No compile errors are asserted. Scene references, Java compilation against the installed Unity activity API, Android 12–16 picker behavior, runtime permission prompts, exceptions, battery use, frame rate, APK size and actual memory use require Unity Editor/build/device evidence.
