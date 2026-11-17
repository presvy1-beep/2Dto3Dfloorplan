# Testing plan

## Existing coverage

Unity Test Framework 1.8.0 is installed. `Assets/ARNav/Tests/EditMode/` contains `MapFoundationTests.cs`, `FloorPlanExtractionTests.cs` and `ArchitecturalWallMaskProcessorTests.cs`, configured by `ARNav.EditModeTests.asmdef`. These cover some data/geometry/extraction foundations. No Play Mode test assembly or CI workflow was found in the inspected project file inventory. `ARNavDeviceSmokeRunner` provides a development-only on-device diagnostic path, but it is not a substitute for automated assertions and this audit did not run it.

## Tests that can run immediately

- Open the project in Unity 6000.6.0f1 and run the existing Edit Mode test assembly in Test Runner.
- Inspect test results and Editor log for compile errors; no claim is made here that current tests pass.
- Manually inspect scenes and serialized references in Editor. Build a Development APK only after establishing a baseline; no build was run as part of this audit.

## Recommended test layers

### Edit Mode

- Map validation: missing IDs, null collections, duplicate rooms, invalid polygons, non-finite coordinates, incorrect door parent references.
- Serialization: valid project round-trip preserves IDs, image metadata, annotations, derived geometry, scale and geo calibration; malformed/old schema gives recoverable errors.
- Repository: create/list/load/delete, invalid IDs and filenames, image copy, overwrite behavior, failed/partial write recovery.
- Geometry/calibration: pixel-to-local forward/back mapping, scale from known pixel distances, image Y/Z reflection and non-square images.
- Extraction: blank, inverted, colored-background, text-heavy, disconnected walls, openings, skewed/noisy and multiple resolutions; assert diagnostics and geometry bounds, not only a success flag.
- Navigation: route through doorway, disconnected rooms, wall-crossing prevention, start/destination outside room, route length and waypoint progression.
- Polygon triangulation and wall opening planner: valid concave shapes, degeneracy, self-intersection, narrow door and extreme units.

### Play Mode

- Bootstrap transitions to MapSetup and creates exactly one session host.
- Scene references and button wiring: import, analyze, edit, undo/redo, calibration, preview, navigation and back.
- Async callback lifecycle: double import, cancel, scene unload during analysis, stale session/hash result, duplicate native response.
- Runtime mesh generation, NavMesh build/clear/rebuild and route object cleanup; ensure no accumulating meshes/materials/game objects after repeated operations.
- Pause/resume while provider is running; ensure event subscriptions and state are restored once.

### Android physical device

Use at least one ARM64 mid-range device at minimum supported Android API where available, one recent Android 14–16 device, and a small-screen/high-density device. Confirm final APK effective target/min API and merged permissions first.

- SAF import: PNG/JPEG, provider cloud/local URI, long filename, MIME mismatch, cancellation, unreadable/deleted source, process recreation while picker open.
- Invalid images: corrupt data, zero/large dimensions, oversized compressed file, highly compressed/decompression bomb-like dimensions, transparent/colored/inverted plan.
- Full flow: import → analyze → manually correct → scale → save → kill/relaunch → reopen → preview → build NavMesh → select route → clear → back.
- Android back, Home/resume, lock/unlock, rotate request, keyboard, font scale, gesture navigation and permission deny/revoke.
- Location: disabled service, no fix, poor accuracy, permission denial, pause/resume; verify UI never presents GPS as guaranteed indoor location.
- Export GLB through document picker; confirm saved file opens in an independent viewer and cancel/failure cleans up staging data.

### Performance regression

Create three fixed fixture maps (small, typical, maximum supported) and a representative source image per fixture. Record checksums and test device details. In Development and release configurations, measure cold startup, decode, analysis, mesh generation, NavMesh build, route computation, peak memory, GC allocations, sustained FPS/frame time and APK size. Set budgets based on device measurements before optimizing. Keep route/extraction expected outputs to detect quality regressions.

## Test infrastructure additions

1. Add test fixtures to a dedicated test-only folder and reference via Test Framework; avoid shipping bulky sample images.
2. Add Play Mode test assembly for scene lifecycle and generated object cleanup.
3. Add a documented Android smoke checklist (or automate with Unity Test Framework device tests only if setup remains maintainable).
4. Capture logs with stable `[ARNav]` markers and include test case, device, Android version, Unity version, map fixture and result.
5. Add a schema version/migration test before enabling map sharing or long-lived map storage.

## Current limitations

This audit did not launch Unity, compile scripts, run Test Runner, build APK, inspect a phone, or measure performance. Thus there are no test pass claims, runtime crash claims, frame-rate numbers or APK-size numbers in these reports.
