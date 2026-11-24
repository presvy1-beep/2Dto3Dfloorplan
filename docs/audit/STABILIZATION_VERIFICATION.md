# Stabilization verification (2026-11-24)

## Code findings and repairs

| Finding | Root cause in source | Repair |
| --- | --- | --- |
| Copy JSON failed without visible feedback | Both setup and navigation wrote clipboard failures only to `Debug.Log`; the overlay hid the authored status UI. | Shared clipboard verification now returns a readable error. The runtime overlay shows workflow status and exposes Export JSON for clipboard limits. |
| JSON could not drive a fresh 3D session | There was no JSON file picker, and `Open3DPreview` regenerated geometry from pixels on every open. | Editor/Android JSON import validates and saves a new local project; existing local geometry is used directly for 3D. Source image bytes remain a separate file. |
| Irregular plans could appear as a solid rectangle | `ProceduralBuildingGenerator` added `PresentationBase` from the wall mesh bounding box. | Removed that generated slab. Room floor patches and architectural wall paths remain. Holed rooms use occupied raster cells or skip the patch if no safe fallback exists. |
| Preview dimensions looked measured | `EnsureDevelopmentScale` silently stored 0.01 m/pixel as valid scale. | Estimated preview scale is explicit in JSON and UI; existing development-scale JSON is recognized on load. Measured calibration remains accessible. |
| GLB failures were opaque | Export was silently refused when model identity was stale; callback errors were summarized without detail. | Visible refusal, save progress and completion messages; document writes compare bytes copied to source length; mesh exporter handles invalid mesh data as a returned error. |
| Camera orbit was absent in the active 3D scene | `NavigationController` used a fixed yaw of 35 degrees; `BuildingPreviewController` input code was not the active preview path. | Added Input System mouse orbit/scroll and touch orbit/pinch to `NavigationController`, with UI hit checks, Fit and Reset. |
| GPS status could misstate altitude and freshness | Readouts printed any altitude as a number and did not update when a fix aged out. | Altitude is shown unavailable when vertical accuracy is missing; readouts include horizontal accuracy and stale/waiting states, checked every two seconds. |
| Setup screen showed inert controls | The overlay created Angle and Scale sliders with null callbacks. | Removed those sliders and kept the working measured-scale calibration panel accessible. |

`RoomMeasurements.TryFromRoom` derives bounding length/breadth, area, and centroid from the saved room polygon and holes, or from the occupied raster runs when the floor mesh uses them. Overlapping raster runs fail measurement instead of double-counting. These are derived values; no second copy is persisted in JSON.

## Verified

- Unity 6000.6.0f1 Edit Mode after raster measurement correction: 138 passed, 0 failed, 0 skipped (`Logs/stabilization-editmode-raster.xml`). This includes eight new stabilization cases.
- Android release APK build from commit `ad97179`: succeeded with 0 errors and 23 warnings. `Builds/arn.apk` is 33,473,481 bytes. The manifest has `com.arnav.filepicker.ArnavUnityActivity`, arm64, minimum SDK 26, target SDK 36, and fine/coarse location permissions.
- Existing tests cover rectangular grids, L/U shapes, doorway gaps, text suppression, wall continuity/junctions, serialization, generation, GLB header/chunks, camera state, and location quality. Test fixtures do not prove arbitrary architectural plans are reconstructed correctly.
- `git diff --check` found no whitespace errors in changed source.

## Remaining verification and limits

- The Android save and JSON open pickers require an interactive device run. The connected phone disconnected before installation or inspection, and its existing `com.DefaultCompany.arn` installation was untouched. Java callback state is held in process memory; process death during a picker may lose the result. Export success must be confirmed by opening the saved document in an independent glTF viewer or validator.
- A source image is not embedded in exported JSON. Imported geometry can generate 3D without the image, while pixel-based review requires importing the image separately.
- Floor-plan extraction remains heuristic and runs substantial raster work on the main thread. Manual correction is still needed for ambiguous symbols, text, and room semantics. The existing image size limit bounds input but does not make all supported images fast on every Android device.
- `MinimalMobileUi` still overlays authored scene controls. The review and calibration panels are reachable, but a full UI consolidation needs device layout checks and scene-reference review before deleting assets.
- Android GPS is outdoor device location. It does not establish an indoor position without a valid geographic map calibration and an accuracy suitable for the floor plan.

## Manual acceptance run

1. In Editor, open `Boot`, import a synthetic and a representative plan, wait for analysis, use Review/Edit to inspect walls, room regions and door gaps, and confirm geometry. Calibrate scale with a known distance.
2. Copy JSON and paste it into a text editor. Export JSON, reopen that file with Import JSON, then build 3D. Check that room sizes and openings match the reviewed geometry without repeating extraction.
3. In 3D, rotate with mouse drag or one finger, zoom with wheel or pinch, use Top View, Fit and Reset. Drag/scroll over UI controls and verify the camera stays still.
4. Export GLB, wait for a saved status with a nonzero byte count, then open the saved file in Blender or a glTF 2.0 validator. Check floor, walls, openings, materials, and orientation against the visible preview.
5. On Android, repeat the import, JSON save/open and GLB save paths. Cancel each picker once; background and resume during a picker; compare saved file sizes. Test GPS denied, disabled, waiting, fresh, stale, and unavailable altitude states. Confirm the app never labels camera X/Y/Z as latitude/longitude.
