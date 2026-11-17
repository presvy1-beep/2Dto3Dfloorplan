# ARN technical audit summary

Audit scope: source, Unity assets/scenes/settings, packages and Android plugin as present in this working tree. No Unity build or phone test was run. Existing local modifications in settings/package files and `.vscode/` were present before report creation and were left untouched.

## What works in the code

- A Unity 6 Android app flow starts in `Boot` then opens the map setup screen.
- PNG/JPEG import uses Android Storage Access Framework through a small Java bridge, copying the selected document into cache before Unity loads it.
- Maps have JSON project data, source-image identity, annotations, scale calibration, optional geographic calibration, and local persistent storage.
- Floor-plan analysis creates wall runs and candidate room regions; annotation tools let the user correct/author rooms, walls and doors.
- Geometry can be generated as runtime meshes; the Navigation scene can build a NavMesh and calculate/display routes between selected room waypoints.
- GLB export and GPS location provider abstractions exist. GPS is outdoor device location, not indoor positioning.
- A limited Edit Mode suite covers foundational map data, extraction and wall-mask processing.

These are code-path findings, not a claim that every scene reference or Android flow was exercised during this audit.

## Biggest technical weaknesses

- No project-wide build, Play Mode or physical-device verification was run; authored scene wiring and actual performance remain unverified.
- The app is portrait-only and the native manifest is `resizeableActivity=false`.
- Android package identity resolves to `com.DefaultCompany.2D-URP`; target SDK is configured as 0 (Unity default), so release identity/target behavior needs explicit review.
- Analysis copies every decoded pixel and performs multiple full-image passes synchronously in a coroutine after only one yielded frame. Large images can cause memory pressure and visible stalls.
- Extraction is heuristic raster processing; room detection is not robust semantic recognition. Input image quality and floor-plan conventions strongly affect output.
- The runtime UI uses legacy UGUI `Text`/`InputField` and generated/basic controls. It has useful functionality but does not yet look or behave like a finished consumer navigation app.
- Local JSON saves overwrite the target file directly rather than writing/replacing atomically. An interruption during write could leave invalid JSON.
- GPS APIs and calibration do not provide reliable indoor position. Real indoor navigation requires a defined positioning method and venue infrastructure or user calibration.

## Five most important problems to address

1. Build and exercise a reproducible Android test matrix, including import, extraction, save/reopen, 3D build and route; first establish actual compile/runtime baseline.
2. Set and verify the final Android application ID and target SDK for release; keep min SDK 26 only if it matches intended device coverage.
3. Bound image dimensions/decoded bytes and move costly extraction work into incremental/cancellable processing with progress and failure feedback.
4. Make persistence resilient using validated temporary writes and recovery; add save/load round-trip and interrupted-write tests.
5. Make app navigation explicitly user-facing: clear setup stages, destination selection and back behavior, with touch-sized controls and accessible contrast/text.

## Five high-value features

1. Guided map workflow that explains import → detection review → geometry correction → scale → route preview.
2. Robust map library (create/rename/select/delete, duplicate/export/share) with verified image and JSON persistence.
3. Better extraction review/editing, including room labels, doorway placement and wall-height/scale controls.
4. Clear 2D/3D navigation presentation with destination search, route distance/time, progress and camera reset.
5. Multi-floor transitions only after adding explicit floor connectors and route graph semantics.

## Visual direction

Use a calm, high-contrast Android mapping style: warm off-white map canvas, deep navy structural geometry, one saturated teal or blue route color, distinct accessible labels, compact bottom-sheet controls, safe-area-aware layout, restrained motion, and persistent 2D/3D/fit controls. Use large touch targets (around 48 dp or larger) and never encode route meaning by color alone.

## Top ten next tasks

1. Open the project in the exact Unity 6000.6 editor and capture compile/import errors.
2. Build/install the current commit on the known Android device; record OS, ABI, target SDK and logs.
3. Verify scene references and complete the import → save → reopen → preview → route journey.
4. Choose final package ID, versioning, target SDK and release signing approach.
5. Add file-size/dimension limits and readable import failure messages.
6. Profile extraction on representative large plans; move work off a single frame and show progress/cancel.
7. Add tests for save/load, calibration transforms, route geometry and malformed images.
8. Add explicit app back behavior and loading/error/empty states.
9. Replace development-oriented UI styling and legacy controls with a consistent responsive system.
10. Add a repeatable Android smoke checklist and record profiler baselines before adding advanced features.

## Best first improvement

Establish a reproducible Android acceptance baseline (Unity version, device/API/ABI, build identity, logs, and a documented import-to-route walkthrough). This distinguishes actual defects from unverified assumptions and guides the highest-risk fixes.

## Important audit boundary

No functional changes, package changes, builds, or tests were made. See the companion reports for evidence, severity, practical feasibility and test procedures.
