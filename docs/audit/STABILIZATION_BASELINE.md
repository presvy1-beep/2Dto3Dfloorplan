# Stabilization baseline (2026-11-23)

Scope: repository source, existing `docs/audit`, Unity scenes and settings, package manifest, Android Java bridge, and Edit Mode tests. This is a code audit; device behavior remains unverified.

## Architecture and classification

| Class | Code and purpose | Finding |
| --- | --- | --- |
| A: core | `MapSetupController`, import/storage, `FloorPlanAnalysisController`, `FloorPlanExtraction`, `ArchitecturalWallMaskProcessor`, `WallPathExtractor`, annotations, `PixelToLocalGeometryConverter` | Source pixels and reviewable annotations become local metre geometry. Mask and path logic is substantial and should be retained. |
| A: core | `MapProjectData`, `MapJsonSerializer`, `LocalFileMapRepository`, generation planner, mesh builders, `ProceduralBuildingGenerator`, `RuntimeGlbExporter`, Android document bridge | JSON is the stored project and generation input. Repository already uses staged writes and backup recovery. |
| A: core | `NavigationController` camera state, `MinimalMobileUi`, `BuildingPreviewController` | The Navigation scene is currently the 3D preview. `MinimalMobileUi` overlays and hides authored controls, including correction and calibration. `BuildingPreviewController` appears to be an alternate preview controller; no deletion without checking scene references. |
| B: optional | GPS providers, geographic calibration, location HUD, routing/NavMesh | GPS is device location, not map or camera position. Routing is gated off by `NavigationFeatureConfiguration` and can remain isolated. |
| C: development | seed project service, device smoke runner, extraction/wall visual probes, template SampleScene and broad template packages | Preserve pending scene/GUID and build-reference review. Development buttons are already gated to Editor/development builds. |
| D: incomplete | JSON file export/import, user-visible copy/export errors, explicit estimated scale, room measurements, reliable Android export lifecycle | Clipboard-only JSON cannot cover large Android maps. No production JSON import/reload path. Export callback and app lifecycle require device verification. |

## Traced workflow and concrete defects

1. Import stores image identity and automatically analyzes it. Analysis writes source annotations to JSON, clears old derived geometry, then reloads the project. `Open3DPreview` invokes `EnsureDevelopmentScale`, which silently assigns 0.01 m/pixel and saves it as valid calibration. This gives estimated dimensions the appearance of measurements.
2. `MinimalMobileUi.BuildSetupControls` disables `AnnotationEditorPanel` and `ScaleCalibrationPanel` without a control to reopen them. The existing correction workflow is present in code but inaccessible in the primary UI.
3. `ProceduralBuildingGenerator` builds a `PresentationBase` rectangular prism from wall bounds. This fills empty parts of irregular plans in both preview and GLB.
4. `CopyCurrentJson` and `MinimalMobileUi.CopyActiveJson` report failures only to Debug.Log. A copy attempted before analysis has no visible response. Android clipboard size failure has no file-export fallback.
5. GLB export is wired in `MinimalMobileUi` and writes through `AndroidDocumentBridge` to the Java `ACTION_CREATE_DOCUMENT` flow. `NavigationController.ExportCurrent3D` silently refuses stale/unavailable models. Android completion reports success after stream close, but byte count is not checked against staged file length. Native callbacks are held only in static fields, so activity/process recreation can lose an export result. Actual on-device failure cause is not yet reproduced.

## Existing safeguards and test boundary

- Raster wall occupancy, derived wall paths, room polygons/raster fallback, polygon triangulation, and wall opening planning already exist. The extraction heuristic cannot identify arbitrary architectural conventions with certainty; corrections remain necessary.
- `LocalFileMapRepository` already validates, stages, backs up, and recovers JSON writes. No persistence rewrite is warranted without failing evidence.
- Existing Edit Mode tests cover portions of extraction, serialization, geometry and GLB bytes. A Unity 6000.6.0f1 editor currently has this project open, so a second batch editor cannot safely take the project lock. Test execution and Android device results must be recorded separately.
- Four pre-existing edits in `.vscode/settings.json`, `Assets/Settings/UniversalRenderPipelineGlobalSettings.asset`, `ProjectSettings/EditorBuildSettings.asset`, and `ProjectSettings/ProjectAuditorSettings.asset` are outside this work.
