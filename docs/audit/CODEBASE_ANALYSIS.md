# Codebase and architecture analysis

## Folder map

- `Assets/ARNav/Core/`: engine-light data, geometry, calibration, navigation algorithms, validation and JSON persistence.
- `Assets/ARNav/Runtime/`: Unity-facing orchestration, UI, native boundary, extraction, mesh generation, location and NavMesh building.
- `Assets/ARNav/Editor/`: editor scene/build setup and visual/debug probes; not runtime app features.
- `Assets/ARNav/Scenes/`: Boot, MapSetup and Navigation scenes.
- `Assets/ARNav/Tests/EditMode/`: edit-mode NUnit tests.
- `Assets/Plugins/Android/`: custom Android manifest and Java activity/document picker.
- `Assets/Settings/`: URP, input actions and Unity build profile assets.
- `Packages/`: package manifest and lock file.
- `ProjectSettings/`: editor, player, input, graphics, quality and build scene configuration.
- `Assets/Scenes/SampleScene.unity`: extra sample scene remains enabled in build settings; inspect whether it is still required.

## Scene transitions and startup

Build order is `Boot`, `MapSetup`, `Navigation`, `SampleScene` (`ProjectSettings/EditorBuildSettings.asset`). `Boot.unity` contains `AppBootstrap`. `Awake` locks portrait and attaches `MapSessionHost`; `Start` loads `MapSetup` unless already there. `AppBootstrap.Update` polls `DisplayOrientationState.RefreshIfChanged`. The Android activity and manifest independently force portrait, so orientation is constrained at both native and Unity layers.

`MapSetupController.Open3DPreview` and navigation actions are the setup-to-navigation transition points; `NavigationController.BackToSetup` returns. The Navigation scene contains `RuntimeNavMeshBuilder`, `ProceduralBuildingGenerator`, location provider objects, UGUI controls and EventSystem. No evidence of additive scene composition was found.

## Main data and setup flow

`Core/Data/MapProjectData.cs` and `FloorPlanAnnotationData.cs` define project/floor/geometry records. `Runtime/App/MapSession.cs` stores active project and setup state; `MapSessionHost` provides a persistent host across scene loads. `MapProjectFactory` and `TestMapFactory` create projects/test data.

Import: `UnityImageImportService.SelectImage` uses Editor file panel or Java SAF. Android `ArnavImagePicker` copies `content://` data to app cache on a background thread, then returns JSON to Unity; `AndroidDocumentBridge` supports GLB document save and clipboard verification. `MapSetupController.HandleImportResult` and `MapImageStorageService` validate/store image data and update the current session. The picker handles cancellation, MIME checks, pending result recovery and old picker-cache cleanup. Image decode dimensions/size limits should be verified in `UnityImageImportService` and storage routines.

Persistence: `LocalFileMapRepository` stores `ARNav/Maps/<id>/map.json` and project image files under `Application.persistentDataPath`. `MapJsonSerializer`, `MapValidator` and `IMapRepository` separate serialization and contract. Repository `SaveProject` writes directly to `map.json`; no atomic temp-and-replace sequence is present.

## Image processing and 2D-to-3D flow

`FloorPlanAnalysisController.Analyze` captures `Texture2D.GetPixels32()`, builds a luminance buffer, wraps it in `FloorPlanImageData`, and starts a coroutine. The coroutine yields once, then calls `IFloorPlanExtractor.Extract` on the main thread. `SimpleFloorPlanExtractor` delegates to `ArchitecturalWallMaskProcessor`, which uses Otsu thresholding, background/color heuristics, line protection, connected components and glyph-like component suppression to produce wall mask runs, wall paths, and candidate regions/diagnostics.

This is not a learned model and does not reliably understand arbitrary symbols, text, doors or room semantics. Manual annotations in `Runtime/MapEditor/FloorPlanAnnotationController` and `Core/Annotation` are important for correction. `ScaleCalibrationService`, `FloorPlanCoordinateMapper` and `PixelToLocalGeometryConverter` translate image pixel coordinates to local meter geometry. The data and renderer apply a Z reflection in some paths; navigation code explicitly converts `z` with `2 * mapReflectionZ - z`. That convention must stay consistent across geometry, camera, route and any future serialization migration.

`ProceduralBuildingGenerator` orchestrates `BuildingGenerationPlan`, `WallPathMeshBuilder`/`ArchitecturalWallMaskMeshBuilder`, `RuntimeMeshFactory`, materials and floor mesh construction. `GeneratedBuildingMetadata` and `GeneratedRoomNavAnchor` connect generated visuals/room identity back to project geometry. `RuntimeGlbExporter` emits generated model output through the document bridge.

## Navigation and route flow

`RuntimeNavMeshBuilder.Build` gathers generated mesh sources, treats the dedicated navigation floor as walkable and walls as blockers, then uses `NavMeshBuilder.BuildNavMeshData` and registers a transient NavMesh. `NavigationController` loads the active project, generates geometry, creates room anchors and a separate occupancy `NavigationGrid` (5 cm cells; allocation cap 500,000). It validates waypoint-network segments against grid and wall geometry. `NavigationRouteService.TryCalculate` resolves a start and room destination onto the NavMesh, requires a complete path, samples corners into waypoints and calculates distance. The controller builds a `LineRenderer` and route waypoint objects.

The route model is shortest path according to Unity NavMesh path cost/default area costs; no walking time model, live obstacle map, multi-floor connector or automatic reroute loop is evident. A user GPS reading can be translated through `GeoCoordinateTransformer` and scored via `UserMapPositionService`, but GPS is typically too imprecise indoors and does not provide continuous reliable indoor fixes.

## UI and interaction

`MapSetupController`, `NavigationController`, `MinimalMobileUi`, `SafeAreaLayout`, calibration overlays, `FloorPlanViewer` and annotation controllers form the app UI. The serialized scenes use Unity UGUI and controls. `ARNavSceneSetup` is an Editor utility that creates/configures scenes and UI; it is not called as the user's runtime screen builder except where the source explicitly adds a component (e.g. `MinimalMobileUi`). The visual hierarchy is largely manual/legacy UGUI and includes developer test controls gated to Editor/development builds.

Floor-plan interaction is pointer-driven through Unity event interfaces and overlay components. The setup supports fit/source visibility and map editing modes. Navigation has destination selection, route start/clear/rebuild/fit/back and GPS controls. Inspect UI layout on devices: safe area helpers are present, but portrait-only orientation and legacy controls constrain responsive behavior. Camera behavior and exact touch gesture mappings live primarily in `BuildingPreviewController` and `NavigationController`; inspect these before changing interaction conventions.

## Coordinate systems and calibration

The source map is pixel-space; scale calibration derives meters per pixel from two selected pixels and a known distance. `PixelToLocalGeometryConverter` creates local meter coordinates. Floors have elevation; 3D uses Unity X/Y/Z with floor elevation in Y. Some generated navigation geometry reflects map Z around a project reflection axis, and explicit conversions occur in navigation code. Geographic services (`GeoCalibrationService`, `GeoProjectionService`, `GeoCoordinateTransformer`, `GeoPredictionService`) map local geometry to latitude/longitude using anchor-derived transforms. Do not conflate this calibration with live indoor localization.

## Android integration and lifecycle

`AndroidManifest.xml` declares `ArnavUnityActivity`, portrait orientation, `resizeableActivity=false`, and no explicit app permissions. The app uses SAF rather than broad external-storage permission. Java `ArnavUnityActivity` calls `super.onActivityResult` and forwards picker/export results; `ArnavImagePicker` handles asynchronous file IO and stores a one-shot pending result in SharedPreferences for activity/process recovery. `UnityLocationProvider` uses Unity location APIs; runtime location permission behavior should be checked on target Android versions. `OnApplicationPause`/focus behavior exists for location and picker recovery, but overall app resume state has not been exercised here.

## Performance and dependencies

Image extraction holds decoded texture data plus a `Color32[]`, luminance bytes, masks, labels, queue and component pixel lists. These allocations scale with image area and processing is synchronous on Unity's main thread after a single-frame yield. Navigation grid build iterates all grid cells against rooms and walls, so larger maps and high annotation counts can be expensive even within its cell cap. Mesh generation, NavMesh build and material/object creation are one-time or explicit actions but should be profiled on device.

URP and UI package choices are valid for Unity 6; package manifest has a broad set of template packages (2D, Visual Scripting, Timeline, cloud build, Learn framework etc.). Their mere presence does not prove they inflate the final APK; compare build reports before removal. ARM64-only and IL2CPP are appropriate modern release choices but omit 32-bit devices. No custom Android storage permission is declared, which is consistent with SAF access.
