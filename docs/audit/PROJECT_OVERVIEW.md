# Project overview

## Purpose and present state

ARN is a Unity 6 Android prototype for turning a raster floor-plan image into editable map data, generating a simple 3D representation, and previewing routes inside it. It supports manual annotation and scale/GPS calibration alongside heuristic image analysis. Its data model and runtime geometry pipeline are more developed than its user-facing product flow.

The repository does **not** show automatic conversion of arbitrary images into a semantically complete building. `ArchitecturalWallMaskProcessor` extracts raster wall-like marks and candidate regions; `FloorPlanExtraction` combines those into annotations. The user can review/edit geometry, and `ProceduralBuildingGenerator` makes meshes from the resulting structured project. This is procedural reconstruction from geometry, with heuristic assistance.

## Intended workflow and user journey

1. Android launches `Assets/ARNav/Scenes/Boot.unity`; `AppBootstrap` sets portrait and loads `MapSetup`.
2. `MapSetupController` opens the active project or creates an empty one. The user imports a PNG/JPEG through Android's document picker.
3. The image is displayed in `FloorPlanViewer`. The user can analyze it, reveal the source image, annotate/edit rooms/walls/doors, and calibrate map scale by selecting two pixels and entering a known distance.
4. Geographic calibration is an optional separate operation using one or more latitude/longitude anchors and a heading when there is only one anchor.
5. The user can open a 3D preview or proceed to `Navigation`. Generation creates room/wall/floor geometry and room anchors; `RuntimeNavMeshBuilder` builds a walkable NavMesh from generated geometry.
6. The user chooses a destination room and a start/waypoint interaction. `NavigationRouteService` calculates a complete path and the controller displays a route line and waypoint objects. The device GPS provider can supply outdoor location readings, but it does not itself locate a user within an indoor floor plan.

The screens and code contain development seed/smoke paths. Their presence does not mean the final end-user flow has been validated.

## Major feature status

| Area | Status from repository evidence |
| --- | --- |
| Android image import | Implemented via SAF Java bridge and Unity response parser; physical-device behavior unverified here. |
| Image analysis | Implemented heuristic thresholding, component filtering, wall runs/path extraction and diagnostics; not general-purpose semantic recognition. |
| Manual map editing | Room/wall/door annotation, selection, rename, undo/redo and deletion code exists. |
| Scale calibration | Two-point pixel-to-meter calibration and regeneration code exists. |
| Geographic calibration | Anchor and coordinate transform services exist; this aligns maps geographically, not indoor position tracking. |
| 3D generation | Runtime mesh creation for structured geometry is implemented. |
| Navigation | Runtime NavMesh and route calculation/display are implemented for a single floor/project path. |
| Persistence | JSON project repository under `Application.persistentDataPath`; map images are managed by import storage service. |
| Export | GLB save and JSON clipboard paths exist. |
| Multi-floor routing | Data has floors, but a complete inter-floor connector/route flow is not evidenced. |
| AR / indoor positioning | No ARCore integration or indoor radio/QR positioning implementation found. |

## System communication

`MapSetupController` coordinates import, analysis, annotations, calibration and state transitions. It obtains a `MapSession` from `MapSessionHost`, persists via `LocalFileMapRepository`, displays through `FloorPlanViewer`, and invokes `FloorPlanAnalysisController`. Structured `MapProjectData` carries source image metadata, floors, annotations and derived geometry. Generation converts floor data into meshes. Navigation uses both Unity NavMesh (route calculation on generated geometry) and a `NavigationGrid`/waypoint graph for additional connectivity checks and visualization. Location readings enter through `ILocationProvider` and are interpreted by `UserMapPositionService` after geographic calibration.

## Technology

- Unity Editor: `6000.6.0f1` (`ProjectSettings/ProjectVersion.txt`).
- Render pipeline: URP package `17.6.0` in `Packages/manifest.json`, with URP assets in `Assets/Settings/`.
- UI: UGUI (`com.unity.ugui` 2.6.0), legacy `Text` and `InputField` visible in scenes and runtime UI generation.
- Input: Input System 1.20.0 and `InputSystemUIInputModule` in the Navigation scene; also inspect existing pointer/event code for any direct input assumptions.
- Navigation: AI Navigation 2.0.10 and runtime NavMesh APIs.
- Tests: Unity Test Framework 1.8.0; Edit Mode assembly exists.
- Android native code: Java `ArnavImagePicker` and custom `ArnavUnityActivity` under `Assets/Plugins/Android`.
- Android manifest: portrait-only, non-resizable custom activity. Project settings show min SDK 26, target SDK 0 (Unity default), ARM64-only (`AndroidTargetArchitectures: 2`), and IL2CPP (`scriptingBackend: Android: 1`).

## Known evidence limits

The working tree had existing modifications in package and project settings. The reports describe the current working tree and do not treat those modifications as audit edits. APK runtime behavior, profiling data, exact active device target API and final generated APK size were not available for measurement in this audit.
