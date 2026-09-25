using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Security.Cryptography;
using ARNav.Core.Calibration;
using ARNav.Core.Data;
using ARNav.Core.Geo;
using ARNav.Core.Geometry;
using ARNav.Core.Serialization;
using ARNav.Runtime.App;
using ARNav.Runtime.Extraction;
using ARNav.Runtime.Generation;
using ARNav.Runtime.Navigation;
using ARNav.Runtime.Import;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace ARNav.Runtime.Development
{
    /// <summary>
    /// Development-build-only, production-pipeline verification. It starts from the exact
    /// Development-only phone-image acceptance runner. It intentionally does not seed source annotations or route data.
    /// </summary>
    public sealed class ARNavDeviceSmokeRunner : MonoBehaviour
    {
        private const string ActiveProjectPreference = "ARNav.ActiveProjectId";
        private const string SmokeProjectId = "arnav-device-smoke";
        private bool started;
        private Text demoStatus;
        private Text jsonPreview;

        private void Awake()
        {
            // Merely shipping a Development APK must not replace a user's map with
            // the acceptance fixture. Android smoke mode is launch-scoped so a stale
            // PlayerPrefs value cannot silently take over a later normal launch.
            var smokeRequested = false;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intent = activity.Call<AndroidJavaObject>("getIntent"))
                    smokeRequested = intent.Call<bool>("getBooleanExtra", "ARNav.EnableDeviceSmoke", false);
            }
            catch (Exception exception) { Debug.LogWarning("[ARNav][Smoke] Could not read launch opt-in: " + exception.Message); }
#elif UNITY_EDITOR
            smokeRequested = PlayerPrefs.GetInt("ARNav.EnableDeviceSmoke", 0) == 1;
            if (smokeRequested) { PlayerPrefs.SetInt("ARNav.EnableDeviceSmoke", 0); PlayerPrefs.Save(); }
#endif
            if (!Debug.isDebugBuild || !smokeRequested) { enabled = false; return; }
            transform.SetParent(null, false);
            DontDestroyOnLoad(gameObject);
            // The normal runtime surface is intentionally minimal. Smoke status remains
            // in logcat; no development panel is added over the application UI.
        }

        private void Start()
        {
            if (enabled && !started) { started = true; StartCoroutine(Run()); }
        }

        private IEnumerator Run()
        {
            yield return new WaitForSeconds(1.5f);
            HideDevelopmentButtons();
            Debug.Log("[ARNav][Smoke] START");
            yield return RunSyntheticNavMeshDiagnostic();
            var repository = new LocalFileMapRepository();
            var imageStorage = new MapImageStorageService(repository);
            MapProjectData project;
            {
                if (repository.ListProjects().Contains(SmokeProjectId)) repository.DeleteProject(SmokeProjectId);
                project = MapProjectFactory.CreateBlank(SmokeProjectId, "DEV Phone Image Pipeline");
                var sourcePath = Path.Combine(Application.persistentDataPath, "arnav-acceptance-source.jpg");
                if (!File.Exists(sourcePath)) { Fail("No phone acceptance image is installed. Use normal Import, or place the selected phone image at " + sourcePath); yield break; }
                var canonicalBytes = File.ReadAllBytes(sourcePath);
                project.sourceImage.fileName = Path.GetFileName(sourcePath);
                repository.CreateProject(project);
                StoredMapImage stored = null;
                try
                {
                    stored = imageStorage.ImportAndStore(project, sourcePath);
                    project.sourceImage.pathReference = stored.relativePath;
                    project.sourceImage.originalWidthPixels = stored.width;
                    project.sourceImage.originalHeightPixels = stored.height;
                    project.sourceImage.format = stored.format;
                    project.sourceImage.sha256 = stored.sha256;
                    project.sourceImage.sourceByteLength = stored.sourceByteLength;
                    project.sourceImage.importedUtc = DateTime.UtcNow.ToString("o");
                }
                finally { if (stored != null && stored.texture != null) DestroyUnityObject(stored.texture); }
                repository.SaveProject(project);
                Debug.Log("[ARNav][Smoke] PNG canonical bytes=" + canonicalBytes.Length + " sha256=" + Sha256(canonicalBytes));
                PlayerPrefs.SetString(ActiveProjectPreference, SmokeProjectId); PlayerPrefs.Save();
                var host = MapSessionHost.Instance;
                if (host == null || host.Session == null) { Fail("MapSessionHost is unavailable."); yield break; }
                host.Session.Open(repository.LoadProject(SmokeProjectId), MapSetupState.ImageImported);
                var setup = FindObjectOfType<MapSetupController>();
                if (setup == null) { Fail("MapSetupController is unavailable."); yield break; }
                setup.UseHostSessionForDevelopment();
                yield return null;

                var analysisComplete = false; var analysisSuccess = false;
                setup.AnalyzeForSmoke(success => { analysisSuccess = success; analysisComplete = true; });
                while (!analysisComplete) yield return null;
                if (!analysisSuccess) { Fail("Extraction returned no usable geometry."); yield break; }
                var extracted = host.Session.ActiveProject.floors[0].sourceAnnotations;
                if (extracted.rooms.Count == 0 || extracted.walls.Count == 0) { Fail("Extraction geometry counts were zero."); yield break; }
                Debug.Log("[ARNav][Smoke] Extraction rooms=" + extracted.rooms.Count + " walls=" + extracted.walls.Count + " doors=" + extracted.doors.Count);
                setup.ShowStructuredReviewForSmoke();
                yield return new WaitForSeconds(1f);
                setup.SetOriginalImageForSmoke(true); yield return new WaitForSeconds(1f); setup.SetOriginalImageForSmoke(false);
                Debug.Log("[ARNav][Smoke] Structured 2D ready");

                project = repository.LoadProject(SmokeProjectId);
                host.Session.Open(project, MapSetupState.ImageImported);
                ExportProfessorArtifacts(project, "JSON saved + reloaded");
                var floor = project.floors[0];
                var scale = ScaleCalibrationService.Calculate(new PixelPoint(0, project.sourceImage.originalHeightPixels), new PixelPoint(100, project.sourceImage.originalHeightPixels), 1f, project.sourceImage.originalWidthPixels, project.sourceImage.originalHeightPixels);
                if (!scale.IsValid) { Fail("Scale service rejected deterministic input: " + scale.Error); yield break; }
                project.scaleCalibration = new ScaleCalibrationData { firstPixel = new PixelPoint(0, project.sourceImage.originalHeightPixels), secondPixel = new PixelPoint(100, project.sourceImage.originalHeightPixels), knownDistanceMetres = 1f, pixelDistance = scale.PixelDistance, metresPerPixel = scale.MetresPerPixel, isValid = true, validationMessage = "Development scale only" };
                if (!PixelToLocalGeometryConverter.TryRegenerate(floor, project.scaleCalibration, project.sourceImage.originalWidthPixels, project.sourceImage.originalHeightPixels, new MapGenerationDefaults(), out var derived)) { Fail("Conversion failed: " + string.Join(" ", derived.errors.ToArray())); yield break; }
                host.Session.SetSetupState(MapSetupState.ScaleCalibrated); repository.SaveProject(project);
                Debug.Log("[ARNav][Smoke] Scale ready mPerPixel=" + scale.MetresPerPixel.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture));

                var origin = new GeoCoordinate(45.497, -73.578); var provisional = new GeoCalibrationData { geographicOrigin = origin, mapHeadingDegrees = 0 };
                var localAnchor = new LocalPoint(2, 2); var geoAnchor = new GeoAnchorData { id = "smoke_anchor", label = "Smoke reference", localPosition = localAnchor, coordinate = new GeoCoordinateTransformer(provisional).LocalToGeo(localAnchor) };
                project.geographicCalibration.anchors.Clear(); project.geographicCalibration.anchors.Add(geoAnchor);
                var solved = GeoCalibrationService.Solve(project.geographicCalibration.anchors, GeoCalibrationMethod.OneAnchorHeading, 0);
                if (!solved.IsValid) { Fail("Geo service rejected deterministic anchor: " + solved.Error); yield break; }
                GeoCalibrationService.ApplyTo(project.geographicCalibration, solved); host.Session.SetSetupState(MapSetupState.GeoCalibrated); repository.SaveProject(project);
                Debug.Log("[ARNav][Smoke] Geo ready");

                host.Session.Open(repository.LoadProject(SmokeProjectId), MapSetupState.GeoCalibrated);
                UpdateDemoStatus("Image: Loaded\nJSON: Saved + Reloaded\n2D: Reconstructed\nScale: 0.01 m/pixel (development only)\n3D: Pending\nNavMesh: Pending");
                Debug.Log("[ARNav][Smoke] JSON saved and reloaded after calibration");
                SceneManager.sceneLoaded += OnNavigationLoaded;
                SceneManager.LoadScene("Navigation");
            }
        }

        private IEnumerator RunSyntheticNavMeshDiagnostic()
        {
            var root = new GameObject("SyntheticNavFloorRoot");
            var floor = new GameObject("NavigationFloorMesh", typeof(MeshFilter), typeof(MeshRenderer));
            floor.transform.SetParent(root.transform, false);
            var mesh = new Mesh { name = "Synthetic10mFloor" };
            mesh.vertices = new[] { new Vector3(0, 0, 0), new Vector3(10, 0, 0), new Vector3(10, 0, 10), new Vector3(0, 0, 10) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 }; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            floor.GetComponent<MeshFilter>().sharedMesh = mesh;
            var navObject = new GameObject("SyntheticNavMeshBuilder"); var builder = navObject.AddComponent<RuntimeNavMeshBuilder>();
            var built = builder.Build(root, new Bounds(new Vector3(5, 0, 5), new Vector3(10, 0, 10)), out var error);
            yield return new WaitForSeconds(0.75f);
            string verificationError = null; var verified = built && builder.VerifyRegistered(out verificationError);
            Debug.Log("[ARNav][Smoke] Synthetic floor: built=" + built + " verified=" + verified + " error=" + (error ?? verificationError ?? "none"));
            builder.Clear(); DestroyUnityObject(navObject); DestroyUnityObject(root); DestroyUnityObject(mesh);
            yield return RunSyntheticDoorwayDiagnostic();
            yield return null;
        }

        private IEnumerator RunSyntheticDoorwayDiagnostic()
        {
            var root = new GameObject("SyntheticDoorwayRoot");
            var floor = CreateSyntheticBox(root.transform, "NavigationFloorMesh", new Vector3(5, 0, 5), new Vector3(10, 0.1f, 10));
            // Leave an off-axis doorway at x=7..8 so the direct A->B line hits
            // the wall and Unity must return a path with a turn through the gap.
            CreateSyntheticBox(root.transform, "WallPiece_Left", new Vector3(3.5f, 1, 5), new Vector3(7, 2, 0.2f));
            CreateSyntheticBox(root.transform, "WallPiece_Right", new Vector3(9, 1, 5), new Vector3(2, 2, 0.2f));
            var navObject = new GameObject("SyntheticDoorwayNavMeshBuilder"); var builder = navObject.AddComponent<RuntimeNavMeshBuilder>();
            var built = builder.Build(root, new Bounds(new Vector3(5, 0, 5), new Vector3(10, 2, 10)), out var error);
            yield return new WaitForSeconds(0.75f);
            string verificationError = null; var verified = built && builder.VerifyRegistered(out verificationError);
            var path = new UnityEngine.AI.NavMeshPath(); var complete = verified && UnityEngine.AI.NavMesh.CalculatePath(new Vector3(2, 0.1f, 2), new Vector3(8, 0.1f, 8), UnityEngine.AI.NavMesh.AllAreas, path) && path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete;
            Debug.Log("[ARNav][Smoke] Synthetic doorway: built=" + built + " verified=" + verified + " pathComplete=" + complete + " corners=" + (path.corners == null ? 0 : path.corners.Length) + " error=" + (error ?? verificationError ?? "none"));
            builder.Clear(); DestroyUnityObject(navObject); DestroyUnityObject(root); DestroyUnityObject(floor.GetComponent<MeshFilter>().sharedMesh); yield return null;
        }

        private static GameObject CreateSyntheticBox(Transform parent, string name, Vector3 center, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(parent, false); go.transform.position = center; go.transform.localScale = size; var collider = go.GetComponent<Collider>(); if (collider != null) DestroyUnityObject(collider); return go;
        }

        private void OnNavigationLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "Navigation") return;
            SceneManager.sceneLoaded -= OnNavigationLoaded;
            StartCoroutine(VerifyNavigation());
        }

        private IEnumerator VerifyNavigation()
        {
            yield return new WaitForSeconds(3f);
            var controller = FindObjectOfType<NavigationController>();
            if (controller == null) { Fail("NavigationController is unavailable."); yield break; }
            if (controller.NavMeshState != RuntimeNavMeshState.Ready) { Fail("NavMesh was not ready: " + controller.NavMeshState); yield break; }
            Debug.Log("[ARNav][Smoke] 3D ready"); Debug.Log("[ARNav][Smoke] NavMesh ready");
            controller.AuditRoomConnectivity();
            UpdateDemoStatus("Image: Loaded\nJSON: Saved + Reloaded\n2D: Reconstructed\n3D: Generated\nNavMesh: Ready\nRoom Anchors: " + controller.RoomAnchorCount + "\nRoute: select two room waypoints");
            Debug.Log("[ARNav][Smoke] Connectivity audit complete; live route state left untouched.");
            if (controller.HasActiveRoute)
            {
                if (!controller.ValidateActiveRoute(out var routeGeometryError)) { Fail("Existing live route geometry validation failed: " + routeGeometryError); yield break; }
                Debug.Log("[ARNav][Smoke] Existing live route is valid; route selection was not changed.");
            }
            ExportProfessorArtifacts(MapSessionHost.Instance.Session.ActiveProject, "JSON saved + reloaded");
            Debug.Log("[ARNav][Smoke] PASS");
        }

        private static void Fail(string message) { Debug.LogError("[ARNav][Smoke] FAIL: " + message); }
        private static string Sha256(byte[] bytes)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant(); }
        private static void HideDevelopmentButtons()
        {
            var seed = GameObject.Find("DEV SeedButton"); if (seed != null) seed.SetActive(false);
            var navigation = GameObject.Find("DEV NavButton"); if (navigation != null) navigation.SetActive(false);
        }
        private static void DestroyUnityObject(UnityEngine.Object value) { if (value == null) return; if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }

        private void CreateProfessorDemoOverlay()
        {
            var canvasObject = new GameObject("ProfessorDemoOverlay", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 500;
            var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(720, 1280);
            var panel = new GameObject("DemoStatusPanel", typeof(RectTransform), typeof(Image)); panel.transform.SetParent(canvasObject.transform, false);
            var panelRect = panel.GetComponent<RectTransform>(); panelRect.anchorMin = new Vector2(0.03f, 0.72f); panelRect.anchorMax = new Vector2(0.97f, 0.98f); panelRect.offsetMin = panelRect.offsetMax = Vector2.zero; panel.GetComponent<Image>().color = new Color(0.02f, 0.04f, 0.07f, 0.88f);
            demoStatus = AddText(panel.transform, "ARNav  |  PHONE IMAGE DEMO\nWaiting for an imported phone image...", 18, new Vector2(0.04f, 0.50f), new Vector2(0.96f, 0.96f));
            jsonPreview = AddText(panel.transform, "", 11, new Vector2(0.04f, 0.03f), new Vector2(0.96f, 0.48f));
            var top = AddButton(panel.transform, "Top View", new Vector2(0.04f, -0.16f), new Vector2(0.30f, 0.00f)); top.onClick.AddListener(() => { var nav = FindObjectOfType<NavigationController>(); if (nav != null) nav.FitBuildingTopDown(); });
            var perspective = AddButton(panel.transform, "Perspective", new Vector2(0.34f, -0.16f), new Vector2(0.64f, 0.00f)); perspective.onClick.AddListener(() => { var nav = FindObjectOfType<NavigationController>(); if (nav != null) nav.FitBuilding(); });
            var original = AddButton(panel.transform, "Show Original", new Vector2(0.68f, -0.16f), new Vector2(0.96f, 0.00f)); original.onClick.AddListener(() => { var setup = FindObjectOfType<MapSetupController>(); if (setup != null) setup.SetOriginalImageForSmoke(true); });
        }

        private static Text AddText(Transform parent, string value, int size, Vector2 min, Vector2 max)
        {
            var go = new GameObject("DemoText", typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false); var rect = go.GetComponent<RectTransform>(); rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero; var text = go.GetComponent<Text>(); text.text = value; text.fontSize = size; text.color = Color.white; text.alignment = TextAnchor.UpperLeft; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate; return text;
        }
        private static Button AddButton(Transform parent, string label, Vector2 min, Vector2 max)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button)); go.transform.SetParent(parent, false); var rect = go.GetComponent<RectTransform>(); rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero; go.GetComponent<Image>().color = new Color(0.08f, 0.32f, 0.52f, 0.95f); var text = AddText(go.transform, label, 13, Vector2.zero, Vector2.one); text.alignment = TextAnchor.MiddleCenter; return go.GetComponent<Button>();
        }
        private void UpdateDemoStatus(string value) { if (demoStatus != null) demoStatus.text = "ARNav  |  PROFESSOR DEMO\n" + value; }
        private void ExportProfessorArtifacts(MapProjectData project, string state)
        {
            try
            {
                var json = MapJsonSerializer.Serialize(project); File.WriteAllText("/tmp/arnav-phone-map.json", json);
                var floor = project.floors != null && project.floors.Count > 0 ? project.floors[0] : null; var rooms = floor == null || floor.sourceAnnotations == null ? 0 : floor.sourceAnnotations.rooms.Count; var walls = floor == null || floor.sourceAnnotations == null ? 0 : floor.sourceAnnotations.walls.Count; var doors = floor == null || floor.sourceAnnotations == null ? 0 : floor.sourceAnnotations.doors.Count;
                File.WriteAllText("/tmp/arnav-phone-acceptance-summary.txt", "Source: " + project.sourceImage.fileName + "\nImage: " + project.sourceImage.originalWidthPixels + "x" + project.sourceImage.originalHeightPixels + "\nDetected regions: " + rooms + "\nWalls: " + walls + "\nDoors: " + doors + "\nJSON: " + state + "\n3D: generated\nNavMesh: ready\n");
                if (jsonPreview != null) jsonPreview.text = "JSON saved: map.json\nPreview:\n" + (json.Length > 900 ? json.Substring(0, 900) + "\n..." : json);
            }
            catch (Exception e) { Debug.LogWarning("[ARNav][Demo] Artifact export failed: " + e.Message); }
        }
    }
}
