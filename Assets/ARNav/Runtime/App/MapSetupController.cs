using System;
using System.Collections;
using System.Globalization;
using ARNav.Core.Data;
using ARNav.Core.Calibration;
using ARNav.Core.Geometry;
using ARNav.Core.Serialization;
using ARNav.Core.Validation;
using ARNav.Core.Geo;
using ARNav.Runtime.Import;
using ARNav.Runtime.MapEditor;
using ARNav.Runtime.Location;
using ARNav.Runtime.Development;
using ARNav.Runtime.Extraction;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace ARNav.Runtime.App
{
    public sealed class MapSetupController : MonoBehaviour
    {
        [SerializeField] private FloorPlanViewer viewer;
        [SerializeField] private UnityImageImportService imageImporter;
        [SerializeField] private Button importButton;
        [SerializeField] private Button fitButton;
        [SerializeField] private Text statusText;
        [SerializeField] private FloorPlanAnnotationController annotationController;
        [SerializeField] private FloorPlanAnnotationOverlay annotationOverlay;
        [SerializeField] private ScaleCalibrationController scaleCalibrationController;
        [SerializeField] private ScaleCalibrationOverlay scaleCalibrationOverlay;
        [SerializeField] private GameObject scaleCalibrationPanel;
        [SerializeField] private Button calibrateScaleButton, applyScaleButton, cancelScaleButton;
        [SerializeField] private InputField knownDistanceInput;
        [SerializeField] private Text scaleInfoText;
        [SerializeField] private GeoCalibrationController geoCalibrationController;
        [SerializeField] private GeoAnchorOverlay geoAnchorOverlay;
        [SerializeField] private GameObject geoCalibrationPanel;
        [SerializeField] private Button calibrateGeoButton, addGeoAnchorButton, editGeoAnchorButton, deleteGeoAnchorButton, applyGeoButton, cancelGeoButton;
        [SerializeField] private InputField latitudeInput, longitudeInput, headingInput, anchorLabelInput;
        [SerializeField] private Text geoInfoText, predictionText;
        [SerializeField] private LocationTrackingController locationTrackingController;
        [SerializeField] private Button panButton, roomButton, wallButton, doorButton, selectButton, undoButton, redoButton, deleteButton, finishButton, cancelButton, confirmGeometryButton, renameButton;
        [SerializeField] private Button preview3DButton;
        [SerializeField] private Button seedTestButton;
        [SerializeField] private Button seedNavigationButton;
        [SerializeField] private Button analyzeButton;
        [SerializeField] private Button showOriginalButton;
        [SerializeField] private FloorPlanAnalysisController analysisController;
        [SerializeField] private Button editMapButton;
        [SerializeField] private GameObject annotationEditorPanel;
        [SerializeField] private InputField roomNameInput;
        private LocalFileMapRepository repository;
        private MapImageStorageService imageStorage;
        private MapSession session;
        private string analysisSessionId;
        private bool importOperationActive;
        private bool processingImageImport;
        public event Action<string> StatusChanged;

        private void Awake()
        {
            repository = new LocalFileMapRepository(); imageStorage = new MapImageStorageService(repository); var host = MapSessionHost.Instance; if (host == null) { var hostObject = new GameObject("MapSessionHost"); host = hostObject.AddComponent<MapSessionHost>(); } session = host.Session;
            if (importButton != null) importButton.onClick.AddListener(ImportFloorPlan);
            if (fitButton != null) fitButton.onClick.AddListener(FitView);
            if (analyzeButton != null) analyzeButton.onClick.AddListener(AnalyzeFloorPlan);
            if (showOriginalButton != null) showOriginalButton.onClick.AddListener(ToggleOriginalImage);
            if (imageImporter != null) imageImporter.ResultRecovered += HandleImportResult;
            if (analysisController != null) { analysisController.ProgressChanged += HandleAnalysisProgress; analysisController.AnalysisStateChanged += HandleAnalysisState; }
            if (editMapButton != null) editMapButton.onClick.AddListener(ToggleEditorPanel);
            if (annotationEditorPanel != null) annotationEditorPanel.SetActive(false);
            // Keep the location service available to the minimal GPS control; its
            // legacy editor panel is hidden by MinimalMobileUi instead.
            UpdatePrimaryControls();
            BindAnnotationButtons();
            BindScaleButtons();
            BindGeoButtons();
            if (preview3DButton != null) preview3DButton.onClick.AddListener(Open3DPreview);
            if (seedTestButton != null) { seedTestButton.onClick.AddListener(CreateSeedTestProject); seedTestButton.gameObject.SetActive(Debug.isDebugBuild || Application.isEditor); }
            if (seedNavigationButton != null) { seedNavigationButton.onClick.AddListener(OpenSeedNavigation); seedNavigationButton.gameObject.SetActive(Debug.isDebugBuild || Application.isEditor); }
            OpenFirstOrCreate();
            if (gameObject.GetComponent<MinimalMobileUi>() == null) gameObject.AddComponent<MinimalMobileUi>();
        }

        private void AnalyzeFloorPlan()
        {
            if (analysisController == null || viewer == null || !viewer.IsLoaded || session == null || session.ActiveProject == null) { SetStatus("Import a floor plan before analyzing it."); return; }
            SetStatus("Analyzing floor plan...");
            analysisSessionId = session.SessionId;
            var expectedSessionId = analysisSessionId;
            var expectedHash = session.ActiveProject.sourceImage.sha256;
            analysisController.Analyze(GetViewerTexture(), viewer.SourceWidthPixels, viewer.SourceHeightPixels, expectedSessionId, expectedHash, session.ActiveProject.sourceImage.sourceByteLength, result =>
            {
                if (session == null || session.SessionId != expectedSessionId || session.ActiveProject == null || session.ActiveProject.sourceImage.sha256 != expectedHash || result == null || result.sessionId != expectedSessionId || result.sourceSha256 != expectedHash)
                { Debug.LogWarning("[ARNav] Discarded extraction result from an obsolete map session/hash."); return; }
                HandleExtractionResult(result);
            });
        }

        /// <summary>Development-only entry point used by the on-device smoke runner.</summary>
        public void AnalyzeForSmoke(Action<bool> completed)
        {
            if (analysisController == null || viewer == null || !viewer.IsLoaded || session == null || session.ActiveProject == null)
            { completed?.Invoke(false); SetStatus("Smoke analysis has no loaded image."); return; }
            SetStatus("Pipeline Test: analyzing floor plan...");
            var expectedSessionId = session.SessionId;
            var expectedHash = session.ActiveProject.sourceImage.sha256;
            analysisController.Analyze(GetViewerTexture(), viewer.SourceWidthPixels, viewer.SourceHeightPixels, expectedSessionId, expectedHash, session.ActiveProject.sourceImage.sourceByteLength, result =>
            {
                if (session == null || session.SessionId != expectedSessionId || session.ActiveProject == null || session.ActiveProject.sourceImage.sha256 != expectedHash || result == null || result.sessionId != expectedSessionId || result.sourceSha256 != expectedHash) { completed?.Invoke(false); return; }
                var success = result != null && result.succeeded;
                HandleExtractionResult(result);
                completed?.Invoke(success);
            });
        }

        /// <summary>Shows the structured review from the current session data, without the source raster.</summary>
        public void ShowStructuredReviewForSmoke()
        {
            viewer?.SetOriginalImageVisible(false);
            FitStructuredMap();
            SetStatus("Pipeline Test  |  JSON 2D ✓");
            Debug.Log("[ARNav][2D] Structured review shown from loaded project data.");
        }

        public void SetOriginalImageForSmoke(bool visible)
        {
            if (viewer == null) return;
            viewer.SetOriginalImageVisible(visible);
            if (visible) viewer.SetOriginalImageOpacity(0.28f);
            SetStatus(visible ? "Pipeline Test  |  JSON 2D + source reference" : "Pipeline Test  |  JSON 2D ✓");
        }

        private Texture2D GetViewerTexture()
        {
            // Viewer owns the texture; expose it through the existing RawImage without duplicating it.
            return viewer.SourceTexture;
        }

        private void HandleExtractionResult(FloorPlanExtractionResult result)
        {
            if (result == null || !result.succeeded) { SetStatus(result == null ? "Floor-plan analysis failed." : result.message); return; }
            if (session == null || session.ActiveProject == null || result.sessionId != session.SessionId || result.sourceSha256 != session.ActiveProject.sourceImage.sha256) { Debug.LogWarning("[ARNav] Rejected extraction commit with mismatched session/hash."); return; }
            if (session.ActiveProject.floors == null || session.ActiveProject.floors.Count == 0) { SetStatus("The active map has no floor."); return; }
            var floor = session.ActiveProject.floors[0]; floor.sourceAnnotations = result.annotations; floor.mapSessionId = result.sessionId; floor.sourceSha256 = result.sourceSha256;
            floor.rooms.Clear(); floor.walls.Clear(); floor.doors.Clear();
            session.ActiveProject.scaleCalibration = new ScaleCalibrationData();
            if (session.ActiveProject.geographicCalibration != null) { session.ActiveProject.geographicCalibration.anchors.Clear(); session.ActiveProject.geographicCalibration.isValid = false; session.ActiveProject.geographicCalibration.validationMessage = "Source geometry changed; recalibration required."; }
            session.SetSetupState(MapSetupState.ImageImported);
            try
            {
                repository.SaveProject(session.ActiveProject); session.MarkSaved();
                var projectId = session.ActiveProject.projectId;
                var reloaded = repository.LoadProject(projectId);
                session.Open(reloaded, MapSetupState.ImageImported);
                if (reloaded.floors != null && reloaded.floors.Count > 0) reloaded.floors[0].mapSessionId = session.SessionId;
                repository.SaveProject(reloaded); session.MarkSaved();
                annotationController?.Initialize(reloaded, repository, HandleAnnotationsCommitted);
                scaleCalibrationController?.Initialize(viewer, scaleCalibrationOverlay, SetExternalStatus);
                geoCalibrationController?.Initialize(reloaded, viewer, geoAnchorOverlay, SetExternalStatus);
                LoadStoredImage();
                viewer?.SetOriginalImageVisible(false);
                FitStructuredMap();
                Debug.Log("[ARNav][2D] Reloaded project from JSON");
                Debug.Log("[ARNav][2D] Rendering " + result.annotations.rooms.Count + " rooms, " + result.annotations.walls.Count + " walls, " + result.annotations.doors.Count + " doors");
                analysisController?.DiscardTransientState();
                SetStatus("Detected " + result.annotations.rooms.Count + " rooms, " + result.annotations.walls.Count + " walls, " + result.annotations.doors.Count + " doors. Review before confirming.");
                Debug.Log("[ARNav][Extract] Saved detected geometry to map.json.");
            }
            catch (Exception exception) { Debug.LogException(exception); SetStatus("Could not save detected geometry."); }
            UpdatePrimaryControls();
        }

        private void ToggleOriginalImage()
        {
            if (viewer == null) return;
            var visible = !viewer.OriginalImageVisible;
            viewer.SetOriginalImageVisible(visible); if (visible) viewer.SetOriginalImageOpacity(0.28f); SetStatus(visible ? "Original image shown as reference." : "Structured map shown from saved JSON.");
        }

        private void ToggleEditorPanel()
        {
            if (annotationEditorPanel == null) return;
            annotationEditorPanel.SetActive(!annotationEditorPanel.activeSelf);
            if (annotationEditorPanel.activeSelf) { SetScalePanelVisible(false); SetGeoPanelVisible(false); annotationController?.SetTool(AnnotationTool.SelectEdit); }
            SetStatus(annotationEditorPanel.activeSelf ? "Edit Map: choose a correction tool." : "Map review controls hidden.");
        }

        public void ReviewGeometry() { ToggleEditorPanel(); }

        public void CreateSeedTestProject()
        {
            if (!(Debug.isDebugBuild || Application.isEditor)) return;
            if (!DevelopmentSeedProjectService.TryCreate(repository, imageStorage, out var project, out var error)) { SetStatus("Could not create DEV seed: " + error); return; }
            session.Open(project, MapSetupState.GeoCalibrated);
            annotationController?.Initialize(project, repository, HandleAnnotationsCommitted);
            scaleCalibrationController?.Initialize(viewer, scaleCalibrationOverlay, SetExternalStatus);
            geoCalibrationController?.Initialize(project, viewer, geoAnchorOverlay, SetExternalStatus);
            locationTrackingController?.Initialize(project);
                LoadStoredImage(); UpdateScaleInfo(); UpdateGeoInfo(); UpdatePredictions(); SetStatus("DEV seed ready: " + project.projectName);
        }

        public void OpenSeedNavigation()
        {
            if (!(Debug.isDebugBuild || Application.isEditor)) return;
            if (session == null || session.ActiveProject == null || session.ActiveProject.projectId != DevelopmentSeedProjectService.ProjectId) { SetStatus("Create the DEV seed before opening DEV navigation."); return; }
            SceneManager.LoadScene("Navigation");
        }

        public void OpenFirstOrCreate()
        {
            try
            {
                // A launch always starts a new in-memory session. Repository data remains
                // available for explicit import/export workflows, but is never auto-restored.
                var project = MapProjectFactory.CreateBlank("map-" + Guid.NewGuid().ToString("N"), "My Floor Plan");
                session.Open(project, DetermineState(project));
                if (annotationController != null) annotationController.Initialize(project, repository, HandleAnnotationsCommitted);
                if (scaleCalibrationController != null) scaleCalibrationController.Initialize(viewer, scaleCalibrationOverlay, SetExternalStatus);
                if (geoCalibrationController != null) geoCalibrationController.Initialize(project, viewer, geoAnchorOverlay, SetExternalStatus);
                if (locationTrackingController != null) locationTrackingController.Initialize(project);
                if (project.scaleCalibration != null && project.scaleCalibration.isValid && scaleCalibrationController != null) scaleCalibrationController.LoadPoints(project.scaleCalibration.firstPixel, project.scaleCalibration.secondPixel);
                SetScalePanelVisible(false); SetGeoPanelVisible(false);
                UpdateScaleInfo(); UpdateGeoInfo(); UpdatePredictions();
                LoadStoredImage();
            }
            catch (Exception exception) { SetStatus(exception.Message); }
        }

        public void ImportFloorPlan()
        {
            if (analysisController != null && analysisController.IsAnalyzing) { analysisController.CancelAnalysis(); SetStatus("Floor-plan analysis cancelled."); return; }
            if (importOperationActive || processingImageImport) { SetStatus("An image import is already in progress."); return; }
            if (imageImporter == null || session == null || session.ActiveProject == null) { SetStatus("Image import is not configured."); return; }
            importOperationActive = true;
            if (importButton != null) importButton.interactable = false;
            SetStatus("Choose a PNG or JPEG floor plan...");
            try { imageImporter.SelectImage(HandleImportResult); }
            catch (Exception exception)
            {
                CompleteImageImport();
                SetStatus(MapImageStorageService.ToUserFacingError(exception));
                Debug.LogException(exception);
            }
        }

        private void HandleImportResult(ImageImportResult result)
        {
            if (result == null) { CompleteImageImport(); SetStatus("The image picker returned no result. Try selecting the image again."); return; }
            if (result.cancelled) { CompleteImageImport(); SetStatus("Image selection cancelled. Your current map is unchanged."); return; }
            if (!result.succeeded) { CompleteImageImport(); SetStatus(string.IsNullOrWhiteSpace(result.error) ? "The image could not be selected. Try again." : result.error); return; }
            if (processingImageImport) return; // Ignore duplicate callbacks without touching the active source file.
            importOperationActive = true; processingImageImport = true;
            if (importButton != null) importButton.interactable = false;
            StartCoroutine(ImportImageRoutine(result));
        }

        private IEnumerator ImportImageRoutine(ImageImportResult result)
        {
            SetStatus("Reading image...");
            yield return null;
            SetStatus("Validating file and decoding image...");
            yield return null;

            MapProjectData candidate;
            StoredMapImage stored;
            string oldPath;
            string error;
            if (!TryPrepareImageCandidate(result, out candidate, out stored, out oldPath, out error))
            {
                SetStatus(error); FinishImageImport(result); yield break;
            }

            SetStatus("Preparing map...");
            yield return null;
            if (!TryCommitImageCandidate(candidate, stored, oldPath, result.displayName, result.sourceUri, out error)) SetStatus(error);
            FinishImageImport(result);
        }

        private bool TryPrepareImageCandidate(ImageImportResult result, out MapProjectData candidate, out StoredMapImage stored, out string oldPath, out string error)
        {
            candidate = null; stored = null; oldPath = null; error = null;
            try
            {
                if (session == null || session.ActiveProject == null) throw new InvalidOperationException("Open a map session before importing an image.");
                var previous = session.ActiveProject;
                oldPath = previous.sourceImage == null ? null : previous.sourceImage.pathReference;
                // Work against a detached candidate. The active project, viewer and
                // saved JSON remain untouched unless every import step succeeds.
                candidate = MapJsonSerializer.Deserialize(MapJsonSerializer.Serialize(previous));
                stored = imageStorage.ImportAndStore(candidate, result.sourcePath);
                return true;
            }
            catch (Exception exception)
            {
                if (stored != null)
                {
                    try { imageStorage.DeleteStoredImage(candidate, stored.relativePath); }
                    catch (Exception cleanupException) { Debug.LogWarning("ARNav: failed import image cleanup failed: " + cleanupException.Message); }
                    if (stored.texture != null) Destroy(stored.texture);
                }
                error = MapImageStorageService.ToUserFacingError(exception);
                Debug.LogException(exception);
                return false;
            }
        }

        private bool TryCommitImageCandidate(MapProjectData candidate, StoredMapImage stored, string oldPath, string displayName, string sourceUri, out string error)
        {
            error = null;
            var projectSaved = false;
            try
            {
                candidate.sourceImage.fileName = string.IsNullOrWhiteSpace(displayName) ? System.IO.Path.GetFileName(stored.absolutePath) : displayName;
                candidate.sourceImage.pathReference = stored.relativePath;
                candidate.sourceImage.originalWidthPixels = stored.width;
                candidate.sourceImage.originalHeightPixels = stored.height;
                candidate.sourceImage.format = stored.format;
                candidate.sourceImage.sha256 = stored.sha256;
                candidate.sourceImage.sourceByteLength = stored.sourceByteLength;
                candidate.sourceImage.sourceUri = sourceUri;
                candidate.sourceImage.importedUtc = DateTime.UtcNow.ToString("o");
                var importedFloor = candidate.floors != null && candidate.floors.Count > 0 ? candidate.floors[0] : null;
                if (importedFloor != null)
                {
                    importedFloor.sourceAnnotations = new FloorPlanAnnotationData();
                    importedFloor.rooms.Clear(); importedFloor.walls.Clear(); importedFloor.doors.Clear();
                }
                candidate.scaleCalibration = new ScaleCalibrationData();
                if (candidate.geographicCalibration != null)
                {
                    candidate.geographicCalibration.anchors.Clear();
                    candidate.geographicCalibration.isValid = false;
                    candidate.geographicCalibration.validationMessage = "Source image changed; recalibration required.";
                }

                repository.SaveProject(candidate);
                projectSaved = true;
                analysisController?.DiscardTransientState();
                session.Open(candidate, MapSetupState.ImageImported);
                analysisSessionId = session.SessionId; session.MarkSaved();
                annotationController?.Initialize(candidate, repository, HandleAnnotationsCommitted);
                scaleCalibrationController?.Initialize(viewer, scaleCalibrationOverlay, SetExternalStatus);
                geoCalibrationController?.Initialize(candidate, viewer, geoAnchorOverlay, SetExternalStatus);
                locationTrackingController?.Initialize(candidate);
                if (viewer != null) { viewer.Load(stored.texture, stored.width, stored.height); stored.texture = null; } // FloorPlanViewer now owns and releases the texture.
                else { if (stored.texture != null) Destroy(stored.texture); stored.texture = null; }
                annotationController?.Refresh();
                scaleCalibrationController?.ResetPoints();
                geoCalibrationController?.SetActive(false);
                UpdateScaleInfo(); UpdateGeoInfo(); UpdatePredictions(); UpdatePrimaryControls();
                try { imageStorage.DeleteIfObsolete(candidate, oldPath, candidate.sourceImage.pathReference); }
                catch (Exception cleanupException) { Debug.LogWarning("ARNav: old imported image could not be cleaned up: " + cleanupException.Message); }
                SetStatus("Import complete. Image: " + candidate.sourceImage.originalWidthPixels + " x " + candidate.sourceImage.originalHeightPixels + ".");
                AnalyzeFloorPlan(); // Preserve the existing automatic analysis workflow.
                return true;
            }
            catch (Exception exception)
            {
                if (!projectSaved)
                {
                    try { imageStorage.DeleteStoredImage(candidate, stored.relativePath); }
                    catch (Exception cleanupException) { Debug.LogWarning("ARNav: failed import image cleanup failed: " + cleanupException.Message); }
                }
                if (stored.texture != null) Destroy(stored.texture);
                error = projectSaved ? "The image was saved, but the map view could not be refreshed. Reopen the map to continue." : MapImageStorageService.ToUserFacingError(exception);
                Debug.LogException(exception);
                return false;
            }
        }

        private void FinishImageImport(ImageImportResult result)
        {
            if (result != null && result.temporary && !string.IsNullOrWhiteSpace(result.sourcePath))
                try { if (System.IO.File.Exists(result.sourcePath)) System.IO.File.Delete(result.sourcePath); }
                catch (Exception cleanupException) { Debug.LogWarning("ARNav: temporary picker file cleanup failed: " + cleanupException.Message); }
            CompleteImageImport();
        }

        private void CompleteImageImport()
        {
            importOperationActive = false; processingImageImport = false;
            if (importButton != null) importButton.interactable = true;
        }

        public void LoadStoredImage()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(session.ActiveProject.sourceImage.pathReference)) { SetStatus("No floor plan imported."); return; }
                var loaded = imageStorage.LoadStoredTexture(session.ActiveProject, out var width, out var height);
                if (viewer != null) viewer.Load(loaded, width, height);
                annotationController?.Refresh();
                var hasAnnotations = session.ActiveProject.floors != null && session.ActiveProject.floors.Count > 0 && session.ActiveProject.floors[0].sourceAnnotations != null && session.ActiveProject.floors[0].sourceAnnotations.rooms != null && session.ActiveProject.floors[0].sourceAnnotations.rooms.Count > 0;
                viewer?.SetOriginalImageVisible(!hasAnnotations);
                if (hasAnnotations) FitStructuredMap();
                if (hasAnnotations) { var source = session.ActiveProject.floors[0].sourceAnnotations; Debug.Log("[ARNav][2D] Rendering " + source.rooms.Count + " rooms, " + source.walls.Count + " walls, " + source.doors.Count + " doors"); }
                UpdateScaleInfo();
                UpdatePrimaryControls();
                SetStatus("Loaded " + width + " x " + height + " image.");
            }
            catch (Exception exception) { SetStatus(exception.Message); }
        }

        /// <summary>Development harness hook: adopts the explicitly prepared host session.</summary>
        public void UseHostSessionForDevelopment()
        {
            var host = MapSessionHost.Instance;
            if (host == null || host.Session == null || host.Session.ActiveProject == null) return;
            session = host.Session;
            annotationController?.Initialize(session.ActiveProject, repository, HandleAnnotationsCommitted);
            scaleCalibrationController?.Initialize(viewer, scaleCalibrationOverlay, SetExternalStatus);
            geoCalibrationController?.Initialize(session.ActiveProject, viewer, geoAnchorOverlay, SetExternalStatus);
            LoadStoredImage();
        }

        private void UpdatePrimaryControls()
        {
            var loaded = viewer != null && viewer.IsLoaded;
            var hasAnnotations = session != null && session.ActiveProject != null && session.ActiveProject.floors != null && session.ActiveProject.floors.Count > 0 && session.ActiveProject.floors[0].sourceAnnotations != null && session.ActiveProject.floors[0].sourceAnnotations.rooms != null && session.ActiveProject.floors[0].sourceAnnotations.rooms.Count > 0;
            SetButtonVisible(importButton, true); SetButtonVisible(analyzeButton, loaded); SetButtonVisible(showOriginalButton, hasAnnotations); SetButtonVisible(editMapButton, hasAnnotations); SetButtonVisible(fitButton, loaded);
            var scaleReady = session != null && (session.SetupState == MapSetupState.GeometryReady || session.SetupState == MapSetupState.ScaleCalibrated || session.SetupState == MapSetupState.GeoCalibrated || session.SetupState == MapSetupState.MapGenerated);
            SetButtonVisible(calibrateScaleButton, scaleReady); SetButtonVisible(calibrateGeoButton, session != null && (session.SetupState == MapSetupState.ScaleCalibrated || session.SetupState == MapSetupState.GeoCalibrated)); SetButtonVisible(preview3DButton, session != null && (session.SetupState == MapSetupState.ScaleCalibrated || session.SetupState == MapSetupState.GeoCalibrated || session.SetupState == MapSetupState.MapGenerated));
        }

        private static void SetButtonVisible(Button button, bool visible) { if (button != null && button.gameObject.activeSelf != visible) button.gameObject.SetActive(visible); }

        public void FitView() { if (viewer == null) return; if (!FitStructuredMap()) viewer.FitToView(); }

        private bool FitStructuredMap()
        {
            if (viewer == null || session == null || session.ActiveProject == null || session.ActiveProject.floors == null || session.ActiveProject.floors.Count == 0) return false;
            var annotations = session.ActiveProject.floors[0].sourceAnnotations;
            if (annotations == null) return false;
            var hasPoint = false; var minX = 0f; var minY = 0f; var maxX = 0f; var maxY = 0f;
            Action<PixelPoint> include = point => { if (!hasPoint) { minX = maxX = point.x; minY = maxY = point.y; hasPoint = true; } else { minX = Mathf.Min(minX, point.x); minY = Mathf.Min(minY, point.y); maxX = Mathf.Max(maxX, point.x); maxY = Mathf.Max(maxY, point.y); } };
            if (annotations.rooms != null) foreach (var room in annotations.rooms) if (room != null && room.vertices != null) foreach (var point in room.vertices) include(point);
            if (annotations.walls != null) foreach (var wall in annotations.walls) if (wall != null) { include(wall.start); include(wall.end); }
            if (annotations.wallRuns != null) foreach (var run in annotations.wallRuns) if (run != null) { include(new PixelPoint(run.minX, run.minY)); include(new PixelPoint(run.maxX, run.maxY)); }
            if (!hasPoint) return false;
            var padX = Mathf.Max(12f, (maxX - minX) * 0.03f); var padY = Mathf.Max(12f, (maxY - minY) * 0.03f);
            viewer.FitToPixelBounds(minX - padX, minY - padY, maxX + padX, maxY + padY);
            return true;
        }
        public void Open3DPreview()
        {
            if (session == null || session.ActiveProject == null) { SetStatus("Open a map before previewing it."); return; }
            if (!EnsureDevelopmentScale()) { SetStatus("The imported map has no usable geometry or scale."); return; }
            SceneManager.LoadScene("Navigation");
        }

        /// <summary>Copies the complete current repository model; no presentation subset is used.</summary>
        public void CopyCurrentJson()
        {
            if (session == null || session.ActiveProject == null || session.ActiveProject.floors == null || session.ActiveProject.floors.Count == 0 || session.ActiveProject.floors[0].sourceAnnotations == null)
            { SetStatus("Analyze a floor plan before copying JSON."); return; }
            if (session.ActiveProject.sourceImage == null || string.IsNullOrWhiteSpace(session.ActiveProject.sourceImage.sha256))
            { SetStatus("Current map has no source identity. Import an image first."); return; }
            var json = MapJsonSerializer.Serialize(session.ActiveProject);
            if (AndroidDocumentBridge.TryCopyTextAndVerify(json, out var readBack, out var error))
                SetStatus("Map JSON copied (" + readBack.Length + " characters).");
            else
                SetStatus("Copy JSON failed: " + (error ?? "clipboard did not match") + " Use Export JSON.");
        }

        public void ExportCurrentJson()
        {
            if (session == null || session.ActiveProject == null || session.ActiveProject.sourceImage == null ||
                string.IsNullOrWhiteSpace(session.ActiveProject.sourceImage.sha256))
            { SetStatus("Import and analyze a floor plan before exporting JSON."); return; }
            var bridge = GetComponent<AndroidDocumentBridge>() ?? gameObject.AddComponent<AndroidDocumentBridge>();
            var name = "FloorPlan_" + session.ActiveProject.projectId + ".json";
            var json = MapJsonSerializer.Serialize(session.ActiveProject);
            SetStatus("Choose where to save the map JSON...");
            if (!bridge.BeginJsonSave(json, name, OnJsonSaveCompleted, out var error)) SetStatus("Export JSON failed: " + error);
        }

        private void OnJsonSaveCompleted(bool success, string destination, long bytes)
        { SetStatus(success ? "Map JSON saved (" + bytes + " bytes)." : destination == "Export cancelled." ? "JSON export cancelled." : "Export JSON failed: " + destination); }

        private bool EnsureDevelopmentScale()
        {
            var project = session == null ? null : session.ActiveProject;
            if (project == null || project.floors == null || project.floors.Count == 0 || project.sourceImage == null) return false;
            var floor = project.floors[0];
            var image = project.sourceImage;
            if (floor.sourceAnnotations == null || floor.sourceAnnotations.rooms == null || floor.sourceAnnotations.rooms.Count == 0) return false;
            if (project.scaleCalibration == null || !project.scaleCalibration.isValid)
            {
                var first = new PixelPoint(0, 0); var second = new PixelPoint(Mathf.Min(100, image.originalWidthPixels), 0);
                var calculation = ScaleCalibrationService.Calculate(first, second, Mathf.Min(1f, Mathf.Max(0.001f, second.x - first.x) * 0.01f), image.originalWidthPixels, image.originalHeightPixels);
                if (!calculation.IsValid) return false;
                project.scaleCalibration = new ScaleCalibrationData { firstPixel = first, secondPixel = second, knownDistanceMetres = calculation.PixelDistance * 0.01f, pixelDistance = calculation.PixelDistance, metresPerPixel = 0.01f, isValid = true, calibratedUtc = DateTime.UtcNow.ToString("o"), validationMessage = "Development scale; replace with surveyed calibration." };
            }
            if (!PixelToLocalGeometryConverter.TryRegenerate(floor, project.scaleCalibration, image.originalWidthPixels, image.originalHeightPixels, new MapGenerationDefaults(), out var result)) { Debug.LogWarning("[ARNav] Development scale conversion failed: " + string.Join(" ", result.errors.ToArray())); return false; }
            try { repository.SaveProject(project); session.SetSetupState(MapSetupState.ScaleCalibrated); session.MarkSaved(); } catch (Exception exception) { Debug.LogException(exception); return false; }
            return true;
        }
        public void ConfirmGeometry()
        {
            if (annotationController == null) { SetStatus("Annotation editor is not configured."); return; }
            var validation = annotationController.ValidateCurrent(); if (!validation.IsValid) { SetStatus(string.Join(" ", validation.errors.ToArray())); return; }
            session.SetSetupState(MapSetupState.GeometryReady); try { repository.SaveProject(session.ActiveProject); session.MarkSaved(); SetStatus("Geometry confirmed."); } catch (Exception exception) { SetStatus("Could not save geometry confirmation."); Debug.LogException(exception); } UpdatePrimaryControls();
        }
        public void SetExternalStatus(string message) { SetStatus(message); }
        public void BeginScaleCalibration()
        {
            if (session == null || session.ActiveProject == null || (session.SetupState != MapSetupState.GeometryReady && session.SetupState != MapSetupState.ScaleCalibrated)) { SetStatus("Confirm valid geometry before calibrating scale."); return; }
            if (scaleCalibrationController == null) { SetStatus("Scale calibration is not configured."); return; }
            annotationEditorPanel?.SetActive(false); SetGeoPanelVisible(false); scaleCalibrationController.SetActive(true); SetScalePanelVisible(true); SetStatus("Select scale point A, then point B.");
        }

        public void CancelScaleCalibration()
        { scaleCalibrationController?.SetActive(false); SetScalePanelVisible(false); SetStatus("Scale calibration cancelled."); }

        public void ApplyScaleCalibration()
        {
            if (scaleCalibrationController == null || !scaleCalibrationController.HasFirstPoint || !scaleCalibrationController.HasSecondPoint) { SetStatus("Select both scale points first."); return; }
            if (session.SetupState != MapSetupState.GeometryReady && session.SetupState != MapSetupState.ScaleCalibrated) { SetStatus("Confirm valid geometry before calibrating scale."); return; }
            if (knownDistanceInput == null || !float.TryParse(knownDistanceInput.text, NumberStyles.Float, CultureInfo.InvariantCulture, out var distance)) { SetStatus("Enter a valid real-world distance in metres."); return; }
            var sourceValidation = annotationController == null ? new ValidationResult() : annotationController.ValidateCurrent();
            if (annotationController == null || !sourceValidation.IsValid) { SetStatus("Source geometry is invalid: " + string.Join(" ", sourceValidation.errors.ToArray())); return; }
            var project = session.ActiveProject; var floor = project.floors[0]; var image = project.sourceImage;
            var calculation = ScaleCalibrationService.Calculate(scaleCalibrationController.FirstPoint, scaleCalibrationController.SecondPoint, distance, image.originalWidthPixels, image.originalHeightPixels);
            if (!calculation.IsValid) { SetStatus(calculation.Error); return; }
            var next = new ScaleCalibrationData { firstPixel = scaleCalibrationController.FirstPoint, secondPixel = scaleCalibrationController.SecondPoint, knownDistanceMetres = distance, pixelDistance = calculation.PixelDistance, metresPerPixel = calculation.MetresPerPixel, isValid = true, calibratedUtc = DateTime.UtcNow.ToString("o"), validationMessage = "Valid" };
            var oldScale = project.scaleCalibration; var oldRooms = floor.rooms; var oldWalls = floor.walls; var oldDoors = floor.doors;
            project.scaleCalibration = next;
            if (project.geographicCalibration != null) { project.geographicCalibration.isValid = false; project.geographicCalibration.validationMessage = "Scale changed; geographic calibration required."; }
            geoCalibrationController?.SyncLocalPositions();
            if (!PixelToLocalGeometryConverter.TryRegenerate(floor, next, image.originalWidthPixels, image.originalHeightPixels, new MapGenerationDefaults(), out var conversionResult)) { project.scaleCalibration = oldScale; SetStatus("Could not generate metre geometry: " + string.Join(" ", conversionResult.errors.ToArray())); return; }
            try
            {
                repository.SaveProject(project); session.SetSetupState(MapSetupState.ScaleCalibrated); session.MarkSaved();
                scaleCalibrationController.SetActive(false); SetScalePanelVisible(false); UpdateScaleInfo();
                SetStatus("Scale calibrated successfully. Scale: " + next.metresPerPixel.ToString("0.0000", CultureInfo.InvariantCulture) + " m/px");
            }
            catch (Exception exception)
            {
                project.scaleCalibration = oldScale; floor.rooms = oldRooms; floor.walls = oldWalls; floor.doors = oldDoors;
                Debug.LogException(exception); SetStatus("Could not save scale calibration.");
            }
        }

        private void HandleAnnotationsCommitted()
        {
            var project = session == null ? null : session.ActiveProject;
            if (project == null || project.floors == null || project.floors.Count == 0) return;
            var floor = project.floors[0]; var scale = project.scaleCalibration;
            if (scale == null || !scale.isValid) { session.SetSetupState(MapSetupState.GeometryReady); return; }
            if (PixelToLocalGeometryConverter.TryRegenerate(floor, scale, project.sourceImage.originalWidthPixels, project.sourceImage.originalHeightPixels, new MapGenerationDefaults(), out var result))
            { try { repository.SaveProject(project); session.SetSetupState(MapSetupState.ScaleCalibrated); session.MarkSaved(); SetStatus("Derived metre geometry regenerated."); } catch (Exception exception) { Debug.LogException(exception); SetStatus("Could not save regenerated geometry."); } }
            else { floor.rooms.Clear(); floor.walls.Clear(); floor.doors.Clear(); session.SetSetupState(MapSetupState.GeometryReady); try { repository.SaveProject(project); session.MarkSaved(); } catch (Exception exception) { Debug.LogException(exception); } SetStatus("Scale requires valid source geometry: " + string.Join(" ", result.errors.ToArray())); }
        }

        public void BeginGeoCalibration()
        {
            if (session == null || session.ActiveProject == null || (session.SetupState != MapSetupState.ScaleCalibrated && session.SetupState != MapSetupState.GeoCalibrated)) { SetStatus("Complete scale calibration before geographic calibration."); return; }
            if (geoCalibrationController == null) { SetStatus("Geographic calibration is not configured."); return; }
            annotationEditorPanel?.SetActive(false); SetScalePanelVisible(false); geoCalibrationController.SyncLocalPositions(); geoCalibrationController.SetActive(true); SetGeoPanelVisible(true); UpdateGeoInfo(); SetStatus("Select a map point, enter its latitude/longitude, then add the anchor.");
        }

        public void CancelGeoCalibration() { geoCalibrationController?.SetActive(false); SetGeoPanelVisible(false); SetStatus("Geographic calibration cancelled."); }

        public void AddGeoAnchor()
        {
            if (geoCalibrationController == null || latitudeInput == null || longitudeInput == null || !double.TryParse(latitudeInput.text, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) || !double.TryParse(longitudeInput.text, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)) { SetStatus("Enter valid latitude and longitude values."); return; }
            if (!geoCalibrationController.AddAnchor(latitude, longitude, anchorLabelInput == null ? "" : anchorLabelInput.text, out var error)) { SetStatus(error); return; }
            geoCalibrationController.SetActive(true); try { repository.SaveProject(session.ActiveProject); session.MarkSaved(); } catch (Exception exception) { Debug.LogException(exception); SetStatus("Could not save geographic anchor."); return; } UpdateGeoInfo(); SetStatus("Geographic anchor added.");
        }

        public void DeleteLastGeoAnchor()
        { if (geoCalibrationController != null && geoCalibrationController.DeleteLastAnchor()) { try { repository.SaveProject(session.ActiveProject); session.MarkSaved(); } catch (Exception exception) { Debug.LogException(exception); } UpdateGeoInfo(); SetStatus("Last geographic anchor removed."); } }
        public void EditLastGeoAnchor()
        {
            if (geoCalibrationController == null || latitudeInput == null || longitudeInput == null) { SetStatus("Geographic calibration is not configured."); return; }
            if (!double.TryParse(latitudeInput.text, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) || !double.TryParse(longitudeInput.text, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)) { SetStatus("Enter valid anchor data."); return; }
            if (!geoCalibrationController.EditLastAnchor(latitude, longitude, anchorLabelInput == null ? "" : anchorLabelInput.text, out var error)) { SetStatus(error); return; }
            try { repository.SaveProject(session.ActiveProject); session.MarkSaved(); } catch (Exception exception) { Debug.LogException(exception); } UpdateGeoInfo(); SetStatus("Last geographic anchor edited.");
        }

        public void ApplyGeoCalibration()
        {
            if (geoCalibrationController == null || geoCalibrationController.Anchors == null) { SetStatus("Geographic calibration is not configured."); return; }
            var anchors = geoCalibrationController.Anchors; var count = anchors.Count; if (count == 0) { SetStatus("Add at least one geographic anchor."); return; }
            var method = count == 1 ? GeoCalibrationMethod.OneAnchorHeading : count == 2 ? GeoCalibrationMethod.TwoAnchors : GeoCalibrationMethod.MultiAnchorRigid;
            var heading = 0d; if (count == 1 && (headingInput == null || !double.TryParse(headingInput.text, NumberStyles.Float, CultureInfo.InvariantCulture, out heading))) { SetStatus("Enter a valid map heading in degrees."); return; }
            var solved = GeoCalibrationService.Solve(anchors, method, heading); if (!solved.IsValid) { SetStatus(solved.Error); return; }
            GeoCalibrationService.ApplyTo(session.ActiveProject.geographicCalibration, solved);
            try { repository.SaveProject(session.ActiveProject); session.SetSetupState(MapSetupState.GeoCalibrated); session.MarkSaved(); geoCalibrationController.SetActive(false); SetGeoPanelVisible(false); UpdateGeoInfo(); UpdatePredictions(); SetStatus("Geographic calibration successful. Quality: " + solved.Quality + ", RMS: " + solved.RmsErrorMetres.ToString("0.00", CultureInfo.InvariantCulture) + " m"); } catch (Exception exception) { session.ActiveProject.geographicCalibration.isValid = false; Debug.LogException(exception); SetStatus("Could not save geographic calibration."); }
        }

        private void BindGeoButtons()
        { if (calibrateGeoButton != null) calibrateGeoButton.onClick.AddListener(BeginGeoCalibration); if (addGeoAnchorButton != null) addGeoAnchorButton.onClick.AddListener(AddGeoAnchor); if (editGeoAnchorButton != null) editGeoAnchorButton.onClick.AddListener(EditLastGeoAnchor); if (deleteGeoAnchorButton != null) deleteGeoAnchorButton.onClick.AddListener(DeleteLastGeoAnchor); if (applyGeoButton != null) applyGeoButton.onClick.AddListener(ApplyGeoCalibration); if (cancelGeoButton != null) cancelGeoButton.onClick.AddListener(CancelGeoCalibration); }
        private void SetGeoPanelVisible(bool visible) { if (geoCalibrationPanel != null) geoCalibrationPanel.SetActive(visible); }
        private void UpdateGeoInfo()
        { if (geoInfoText == null || geoCalibrationController == null) return; var anchors = geoCalibrationController.Anchors; var count = anchors == null ? 0 : anchors.Count; var geo = session == null || session.ActiveProject == null ? null : session.ActiveProject.geographicCalibration; geoInfoText.text = "Anchors: " + count + "\n" + (geo != null && geo.isValid ? "Heading: " + geo.mapHeadingDegrees.ToString("0.00", CultureInfo.InvariantCulture) + "°\nRMS: " + geo.rmsErrorMetres.ToString("0.00", CultureInfo.InvariantCulture) + " m\nScale mismatch: " + geo.scaleMismatchPercent.ToString("0.00", CultureInfo.InvariantCulture) + "%\nQuality: " + geo.quality : "Not calibrated"); }
        private void UpdatePredictions()
        { if (predictionText == null || session == null || session.ActiveProject == null || !session.ActiveProject.geographicCalibration.isValid) return; var predictions = GeoPredictionService.PredictFloor(session.ActiveProject.floors[0], new GeoCoordinateTransformer(session.ActiveProject.geographicCalibration)); var text = "Derived coordinate predictions\n"; foreach (var prediction in predictions) text += prediction.entityId + " / " + prediction.pointId + ": " + prediction.coordinate.latitude.ToString("F6", CultureInfo.InvariantCulture) + ", " + prediction.coordinate.longitude.ToString("F6", CultureInfo.InvariantCulture) + "\n"; predictionText.text = text; }

        private void BindScaleButtons()
        {
            if (calibrateScaleButton != null) calibrateScaleButton.onClick.AddListener(BeginScaleCalibration);
            if (applyScaleButton != null) applyScaleButton.onClick.AddListener(ApplyScaleCalibration);
            if (cancelScaleButton != null) cancelScaleButton.onClick.AddListener(CancelScaleCalibration);
        }

        private void SetScalePanelVisible(bool visible) { if (scaleCalibrationPanel != null) scaleCalibrationPanel.SetActive(visible); }
        private void UpdateScaleInfo()
        {
            if (scaleInfoText == null || session == null || session.ActiveProject == null) return;
            var scale = session.ActiveProject.scaleCalibration;
            if (scale != null && scale.isValid) scaleInfoText.text = "Scale: " + scale.metresPerPixel.ToString("0.0000", CultureInfo.InvariantCulture) + " m/px\nPixel distance: " + scale.pixelDistance.ToString("0.0", CultureInfo.InvariantCulture) + " px\nReal distance: " + scale.knownDistanceMetres.ToString("0.00", CultureInfo.InvariantCulture) + " m";
            else scaleInfoText.text = "Scale: not calibrated";
        }

        private static MapSetupState DetermineState(MapProjectData project)
        {
            if (project == null || project.sourceImage == null || string.IsNullOrWhiteSpace(project.sourceImage.pathReference)) return MapSetupState.NoMap;
            if (project.geographicCalibration != null && project.geographicCalibration.isValid) return MapSetupState.GeoCalibrated;
            if (project.scaleCalibration != null && project.scaleCalibration.isValid && project.floors != null && project.floors.Count > 0 && PixelToLocalGeometryConverter.ValidateDerived(project.floors[0]).IsValid) return MapSetupState.ScaleCalibrated;
            if (project.floors != null && project.floors.Count > 0 && project.floors[0].sourceAnnotations != null && project.floors[0].sourceAnnotations.rooms != null && project.floors[0].sourceAnnotations.rooms.Count > 0) return MapSetupState.GeometryReady;
            return MapSetupState.ImageImported;
        }
        private void BindAnnotationButtons()
        {
            if (annotationController == null) return;
            if (panButton != null) panButton.onClick.AddListener(() => annotationController.SetTool(AnnotationTool.PanView));
            if (roomButton != null) roomButton.onClick.AddListener(() => annotationController.SetTool(AnnotationTool.AddRoom));
            if (wallButton != null) wallButton.onClick.AddListener(() => annotationController.SetTool(AnnotationTool.AddWall));
            if (doorButton != null) doorButton.onClick.AddListener(() => annotationController.SetTool(AnnotationTool.AddDoor));
            if (selectButton != null) selectButton.onClick.AddListener(() => annotationController.SetTool(AnnotationTool.SelectEdit));
            if (undoButton != null) undoButton.onClick.AddListener(annotationController.Undo); if (redoButton != null) redoButton.onClick.AddListener(annotationController.Redo); if (deleteButton != null) deleteButton.onClick.AddListener(annotationController.DeleteSelected); if (finishButton != null) finishButton.onClick.AddListener(annotationController.FinishDraft); if (cancelButton != null) cancelButton.onClick.AddListener(annotationController.CancelDraft); if (confirmGeometryButton != null) confirmGeometryButton.onClick.AddListener(ConfirmGeometry); if (renameButton != null) renameButton.onClick.AddListener(() => annotationController.RenameSelectedRoom(roomNameInput == null ? "" : roomNameInput.text));
        }
        private void HandleAnalysisProgress(FloorPlanAnalysisStage stage)
        {
            switch (stage)
            {
                case FloorPlanAnalysisStage.PreparingImage: SetStatus("Preparing image..."); break;
                case FloorPlanAnalysisStage.DetectingWalls: SetStatus("Detecting walls..."); break;
                case FloorPlanAnalysisStage.DetectingRoomRegions: SetStatus("Detecting room regions..."); break;
                case FloorPlanAnalysisStage.GeneratingMapGeometry: SetStatus("Generating map geometry..."); break;
                case FloorPlanAnalysisStage.Complete: SetStatus("Analysis complete."); break;
            }
        }
        private void HandleAnalysisState(bool analyzing) { if (!analyzing && statusText != null && statusText.text.EndsWith("...", StringComparison.Ordinal)) SetStatus("Analysis stopped."); }
        private void SetStatus(string message) { if (statusText != null) statusText.text = message ?? "Unknown image import error."; StatusChanged?.Invoke(message); Debug.Log("ARNav: " + message); }
        private void OnDestroy() { if (analysisController != null) { analysisController.DiscardTransientState(); analysisController.ProgressChanged -= HandleAnalysisProgress; analysisController.AnalysisStateChanged -= HandleAnalysisState; } if (imageImporter != null) imageImporter.ResultRecovered -= HandleImportResult; if (importButton != null) importButton.onClick.RemoveListener(ImportFloorPlan); if (fitButton != null) fitButton.onClick.RemoveListener(FitView); if (preview3DButton != null) preview3DButton.onClick.RemoveListener(Open3DPreview); if (seedTestButton != null) seedTestButton.onClick.RemoveListener(CreateSeedTestProject); if (seedNavigationButton != null) seedNavigationButton.onClick.RemoveListener(OpenSeedNavigation); if (analyzeButton != null) analyzeButton.onClick.RemoveListener(AnalyzeFloorPlan); if (showOriginalButton != null) showOriginalButton.onClick.RemoveListener(ToggleOriginalImage); if (editMapButton != null) editMapButton.onClick.RemoveListener(ToggleEditorPanel); }
    }
}
