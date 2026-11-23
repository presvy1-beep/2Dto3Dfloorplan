using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using ARNav.Core.Data;
using ARNav.Core.Navigation;
using ARNav.Core.Geometry;
using ARNav.Core.Location;
using ARNav.Core.Validation;
using ARNav.Core.Generation;
using ARNav.Runtime.App;
using ARNav.Runtime.Generation;
using ARNav.Runtime.Import;
using ARNav.Runtime.Location;
using UnityEngine;
using UnityEngine.UI;

namespace ARNav.Runtime.Navigation
{
    /// <summary>Coordinates generated geometry, runtime NavMesh, destinations, routes, and transient waypoints.</summary>
    public sealed class NavigationController : MonoBehaviour
    {
        [SerializeField] private ProceduralBuildingGenerator generator;
        [SerializeField] private RuntimeNavMeshBuilder navMeshBuilder;
        [SerializeField] private Dropdown destinationDropdown;
        [SerializeField] private InputField startXInput, startZInput;
        [SerializeField] private Button startRouteButton, clearRouteButton, rebuildButton, fitButton, backButton;
        [SerializeField] private Button developmentRouteButton;
        [SerializeField] private Text statusText;
        [SerializeField] private Camera previewCamera;
        [SerializeField] private UnityLocationProvider unityLocationProvider;
        [SerializeField] private MockLocationProvider mockLocationProvider;
        [SerializeField] private LocationHud locationHud;
        [SerializeField] private Button enableLocationButton;
        [SerializeField] private float waypointHeightOffsetMetres = 0.08f;
        private MapProjectData project;
        private FloorData floor;
        private readonly MapSession session = new MapSession();
        private readonly WaypointProgressTracker progress = new WaypointProgressTracker();
        private NavigationRoute route;
        private NavigationGrid navigationGrid;
        private GameObject navigationRoot, roomAnchorsRoot, routeRoot, networkRoot;
        private Material networkMaterial;
        private AndroidDocumentBridge documentBridge;
        private bool exportAvailable;
        private bool liveStartAvailable;
        private LocalPoint liveStart;
        private ILocationProvider locationProvider;
        private UserMapPositionService positionService;
        private double lastRerouteUnixSeconds;
        private string selectedStartRoomId;
        private Text minimalGpsReadout;
        [SerializeField] private float defaultViewAngle = 0.5f;
        [SerializeField] private float defaultViewZoom = 0.5f;
        private CameraViewState cameraView;
        private string cameraProjectId;
        private bool rebuildInProgress;
        private float mapReflectionZ;
        private int startupViewStabilizationFrames;
        private int lastDisplayGeneration;
        public MapSetupState SetupState { get { return session.SetupState; } }
        public NavigationRoute ActiveRoute { get { return route; } }
        public bool HasActiveRoute { get { return route != null && route.valid; } }
        public int RoomAnchorCount { get { return roomAnchorsRoot == null ? 0 : roomAnchorsRoot.transform.childCount; } }
        public bool ValidateActiveRoute(out string error)
        {
            error = null;
            if (route == null || floor == null) { error = "No active route."; return false; }
            var canonicalCorners = new List<LocalPoint>(route.rawCorners.Count); foreach (var point in route.rawCorners) canonicalCorners.Add(new LocalPoint(point.x, 2f * mapReflectionZ - point.z));
            if (RouteGeometryValidator.CrossesSolidWall(canonicalCorners, floor, out error)) return false;
            if (RouteGeometryValidator.EntersNonWalkableRegion(canonicalCorners, floor, out error)) return false;
            return true;
        }
        public RuntimeNavMeshState NavMeshState { get { return navMeshBuilder == null ? RuntimeNavMeshState.NotBuilt : navMeshBuilder.State; } }
        public bool CanExportCurrentModel { get { return exportAvailable && generator != null && project != null && project.sourceImage != null && generator.GeneratedRoot != null && generator.GeneratedProjectId == project.projectId && generator.GeneratedSourceSha256 == project.sourceImage.sha256; } }
        public event Action<bool> ExportAvailabilityChanged;
        public event Action<string> StatusChanged;

        private void Awake()
        {
            startupViewStabilizationFrames = 8;
        }

        private void Start()
        {
            if (startRouteButton != null) startRouteButton.onClick.RemoveListener(StartRoute); if (clearRouteButton != null) clearRouteButton.onClick.RemoveListener(ClearRoute); if (rebuildButton != null) rebuildButton.onClick.RemoveListener(Rebuild); if (fitButton != null) fitButton.onClick.RemoveListener(FitBuilding); if (backButton != null) backButton.onClick.RemoveListener(BackToSetup); if (enableLocationButton != null) enableLocationButton.onClick.RemoveListener(EnableLocation); if (developmentRouteButton != null) developmentRouteButton.onClick.RemoveListener(StartDevelopmentRoute);
            if (startRouteButton != null) startRouteButton.onClick.AddListener(StartRoute); if (clearRouteButton != null) clearRouteButton.onClick.AddListener(ClearRoute); if (rebuildButton != null) rebuildButton.onClick.AddListener(Rebuild); if (fitButton != null) fitButton.onClick.AddListener(FitBuilding); if (backButton != null) backButton.onClick.AddListener(BackToSetup); if (enableLocationButton != null) enableLocationButton.onClick.AddListener(EnableLocation); if (developmentRouteButton != null) { developmentRouteButton.onClick.AddListener(StartDevelopmentRoute); developmentRouteButton.gameObject.SetActive(Debug.isDebugBuild || Application.isEditor); }
            var host = MapSessionHost.Instance; if (host == null || host.Session == null || host.Session.ActiveProject == null || string.IsNullOrWhiteSpace(host.ActiveProjectId)) { SetStatus("No active map project. Return to setup and open a project first."); return; }
            project = host.Session.ActiveProject; floor = project.floors == null || project.floors.Count == 0 ? null : project.floors[0]; ResetCameraViewForProject(); InitializeLocation(); Rebuild();
        }

        private void LateUpdate()
        {
            if (startupViewStabilizationFrames <= 0 || !DisplayOrientationState.IsStable || generator == null || generator.GeneratedRoot == null || previewCamera == null) return;
            startupViewStabilizationFrames--;
            Debug.Log("[ARNav][Display] Apply deterministic preview frame=" + startupViewStabilizationFrames + " screen=" + Screen.width + "x" + Screen.height + " safeArea=" + Screen.safeArea + " cameraAspect=" + previewCamera.aspect + " cameraEuler=" + previewCamera.transform.eulerAngles + " rootScale=" + generator.GeneratedRoot.transform.localScale);
            ApplyCameraView();
            lastDisplayGeneration = DisplayOrientationState.Generation;
        }

        private void OnEnable() { DisplayOrientationState.Changed += OnDisplayChanged; }
        private void OnDisable() { DisplayOrientationState.Changed -= OnDisplayChanged; }
        private void OnDisplayChanged()
        {
            if (!DisplayOrientationState.IsStable || generator == null || generator.GeneratedRoot == null || previewCamera == null) return;
            if (lastDisplayGeneration == DisplayOrientationState.Generation) return;
            lastDisplayGeneration = DisplayOrientationState.Generation;
            ApplyCameraView();
            Debug.Log("[ARNav][Display] Reframed after display change size=" + Screen.width + "x" + Screen.height + " safeArea=" + Screen.safeArea + " cameraAspect=" + previewCamera.aspect);
        }
        public void Rebuild()
        {
            if (rebuildInProgress) { SetStatus("NavMesh build already in progress."); return; }
            SetExportAvailability(false);
            ClearRoute(); if (project == null || floor == null || generator == null || (NavigationFeatureConfiguration.NavigationFeaturesEnabled && navMeshBuilder == null)) { SetStatus("3D generation is not configured for the active map."); return; }
            if (project.sourceImage == null || string.IsNullOrWhiteSpace(project.sourceImage.sha256)) { SetStatus("Current image identity is missing; import and analyze the current image again."); return; }
            if (!generator.Generate(project, out var generationError)) { SetStatus(generationError); return; }
            mapReflectionZ = generator.GeneratedBounds.center.z;
            if (!NavigationFeatureConfiguration.NavigationFeaturesEnabled)
            {
                if (navMeshBuilder != null) navMeshBuilder.Clear();
                navigationGrid = null;
                if (navigationRoot != null) DestroyObject(navigationRoot);
                navigationRoot = roomAnchorsRoot = routeRoot = networkRoot = null; route = null;
                session.Open(project, MapSetupState.MapGenerated);
                ApplyCameraView();
                SetExportAvailability(true);
                SetStatus("3D structure ready.");
                return;
            }
            // Build navigation from a single union occupancy surface rather than the
            // overlapping semantic room floors used for visual reconstruction.
            if (NavigationFloorBuilder.AddTo(generator.GeneratedRoot, floor, 0.06f, out var floorResult, out var floorError) == null)
            { SetStatus("Navigation floor generation failed: " + floorError); generator.ClearGeneratedBuilding(); return; }
            Debug.Log("[ARNav][Navigation] Navigation floor: cells=" + floorResult.cells + " vertices=" + floorResult.vertices + " triangles=" + floorResult.triangles + " bounds=" + floorResult.bounds);
            rebuildInProgress = true;
            StartCoroutine(BuildNavigationAfterMeshCooking());
        }
        private IEnumerator BuildNavigationAfterMeshCooking()
        {
            yield return null; yield return null; yield return new WaitForSeconds(0.5f);
            if (project == null || floor == null || navMeshBuilder == null || generator == null || generator.GeneratedRoot == null) { rebuildInProgress = false; yield break; }
            var navMeshReady = navMeshBuilder.Build(generator.GeneratedRoot, generator.GeneratedBounds, out var navError);
            if (navMeshReady) navMeshReady = navMeshBuilder.VerifyRegistered(out navError);

            // Grid A* is intentionally independent of NavMesh generation. A sparse or
            // empty Unity NavMesh must not prevent the current map's grid, waypoints,
            // or valid grid routes from being initialized.
            session.Open(project, MapSetupState.NavigationReady);
            floor.mapSessionId = session.SessionId;
            floor.sourceSha256 = project.sourceImage == null ? null : project.sourceImage.sha256;
            if (MapSessionHost.Instance != null && MapSessionHost.Instance.Session != null)
                MapSessionHost.Instance.Session.Open(project, MapSetupState.NavigationReady);
            CreateNavigationRoot();
            PopulateDestinations();
            ApplyCameraView();
            BuildNavigationGrid();
            CreateRoomAnchors();
            BuildAutomaticWaypointNetwork();
            if (navMeshReady) SetStatus("Navigation ready for " + project.projectName + ".");
            else
            {
                var message = string.IsNullOrEmpty(navError) ? "Unity NavMesh is unavailable." : navError;
                Debug.LogWarning("[ARNav][Navigation] " + message + " Grid A* remains active for the current occupancy map.");
                SetStatus("Grid navigation ready; Unity NavMesh unavailable.");
            }
            rebuildInProgress = false;
        }
        public void StartRoute()
        {
            if (!NavigationFeatureConfiguration.NavigationFeaturesEnabled) { SetStatus("Navigation features are disabled in this build."); return; }
            if (navMeshBuilder == null || navMeshBuilder.State != RuntimeNavMeshState.Ready) { SetStatus("NavMesh is not ready."); return; }
            var destinations = DestinationRooms();
            if (destinationDropdown == null || destinationDropdown.value < 0 || destinationDropdown.value >= destinations.Count) { SetStatus("Select a destination room."); return; }
            var startCanonical = liveStartAvailable ? liveStart : ReadManualStart(); var start = new LocalPoint(startCanonical.x, 2f * mapReflectionZ - startCanonical.z); var room = destinations[destinationDropdown.value];
            var anchor = roomAnchorsRoot == null ? null : roomAnchorsRoot.transform.Find("RoomNavAnchor_" + room.id);
            if (anchor == null) { SetStatus("Destination room anchor is missing."); return; }
            var anchorMetadata = anchor.GetComponent<GeneratedRoomNavAnchor>();
            if (anchorMetadata == null) { ClearRoute(); SetStatus("Destination room anchor is missing."); return; }
            if (!ResolveAnchorRoutePoint(anchorMetadata)) { ClearRoute(); SetStatus("Destination room has no nearby NavMesh point."); return; }
            if (!NavigationRouteService.TryCalculateBetween(start, floor, room.id, new LocalPoint(anchorMetadata.navMeshPosition.x, anchorMetadata.navMeshPosition.z), navMeshBuilder.Settings, out route, out var error)) { ClearRoute(); SetStatus(error); return; }
            if (!ValidateRoute(route, "destination route")) { ClearRoute(); return; }
            CreateRouteObjects(route); progress.Reset(route); Debug.Log("[ARNav][Navigation] Active route committed: valid=" + (route != null && route.valid) + " corners=" + route.rawCorners.Count); SetStatus("Route to " + room.displayName + " ready: " + route.totalDistanceMetres.ToString("0.0", CultureInfo.InvariantCulture) + " m.");
        }
        public void StartDevelopmentRoute()
        {
            if (!NavigationFeatureConfiguration.NavigationFeaturesEnabled) return;
            if (!(Debug.isDebugBuild || Application.isEditor) || destinationDropdown == null || floor == null) return;
            var destinations = DestinationRooms();
            if (destinations.Count < 2 || roomAnchorsRoot == null) return;
            // Development check tries real sampled room endpoints, just as two room
            // selections do; the raw polygon points are not valid routing coordinates.
            NavigationRoute fallbackRoute = null; string fallbackStartRoom = null; string fallbackDestinationRoom = null;
            for (var startIndex = 0; startIndex < destinations.Count; startIndex++)
            {
                var startTransform = roomAnchorsRoot.transform.Find("RoomNavAnchor_" + destinations[startIndex].id);
                var startAnchor = startTransform == null ? null : startTransform.GetComponent<GeneratedRoomNavAnchor>();
                if (startAnchor == null || !ResolveAnchorRoutePoint(startAnchor)) continue;
                for (var destinationIndex = destinations.Count - 1; destinationIndex >= 0; destinationIndex--)
                {
                    if (destinationIndex == startIndex) continue;
                    var destinationTransform = roomAnchorsRoot.transform.Find("RoomNavAnchor_" + destinations[destinationIndex].id);
                    var destinationAnchor = destinationTransform == null ? null : destinationTransform.GetComponent<GeneratedRoomNavAnchor>();
                    if (destinationAnchor == null || !ResolveAnchorRoutePoint(destinationAnchor)) continue;
                    var directCanonical = new[]
                    {
                        new LocalPoint(startAnchor.navMeshPosition.x, 2f * mapReflectionZ - startAnchor.navMeshPosition.z),
                        new LocalPoint(destinationAnchor.navMeshPosition.x, 2f * mapReflectionZ - destinationAnchor.navMeshPosition.z)
                    };
                    if (!NavigationRouteService.TryCalculateBetween(new LocalPoint(startAnchor.navMeshPosition.x, startAnchor.navMeshPosition.z), floor, destinationAnchor.roomId, new LocalPoint(destinationAnchor.navMeshPosition.x, destinationAnchor.navMeshPosition.z), navMeshBuilder.Settings, out var candidate, out _)) continue;
                    var directCrossesWall = RouteGeometryValidator.CrossesSolidWall(directCanonical, floor, out _);
                    if (fallbackRoute == null) { fallbackRoute = candidate; fallbackStartRoom = destinations[startIndex].id; fallbackDestinationRoom = destinations[destinationIndex].id; }
                    if (!directCrossesWall || candidate.rawCorners.Count <= 2) continue;
                    selectedStartRoomId = destinations[startIndex].id;
                    if (!ValidateRoute(candidate, "development selected route")) continue;
                    CommitDevelopmentRoute(candidate, startIndex, destinationIndex, destinations, true); return;
                }
            }
            if (fallbackRoute != null)
            {
                // A complete route is still useful for the smoke run, but explicitly
                // record that this geometry did not provide the stricter detour pair.
                selectedStartRoomId = fallbackStartRoom;
                var startIndex = destinations.FindIndex(r => r.id == fallbackStartRoom); var destinationIndex = destinations.FindIndex(r => r.id == fallbackDestinationRoom);
                if (ValidateRoute(fallbackRoute, "development selected route")) { CommitDevelopmentRoute(fallbackRoute, startIndex, destinationIndex, destinations, false); return; }
            }
            SetStatus("No complete development route exists between the generated room anchors.");
        }

        public void AuditRoomConnectivity()
        {
            if (!(Debug.isDebugBuild || Application.isEditor) || floor == null || roomAnchorsRoot == null) return;
            var anchors = new List<GeneratedRoomNavAnchor>();
            for (var i = 0; i < roomAnchorsRoot.transform.childCount; i++)
            { var anchor = roomAnchorsRoot.transform.GetChild(i).GetComponent<GeneratedRoomNavAnchor>(); if (anchor != null) anchors.Add(anchor); }
            var groups = new List<List<string>>(); var unseen = new HashSet<string>(); foreach (var anchor in anchors) unseen.Add(anchor.roomId);
            while (unseen.Count > 0)
            {
                var seed = unseen.GetEnumerator(); seed.MoveNext(); var group = new List<string> { seed.Current }; unseen.Remove(seed.Current);
                for (var cursor = 0; cursor < group.Count; cursor++)
                    foreach (var candidate in anchors)
                    {
                        if (!unseen.Contains(candidate.roomId)) continue;
                        var from = FindAnchor(anchors, group[cursor]);
                        if (from == null || !from.hasNavMeshPosition || !candidate.hasNavMeshPosition) continue;
                        if (!NavigationRouteService.TryCalculateBetween(new LocalPoint(from.navMeshPosition.x, from.navMeshPosition.z), floor, candidate.roomId, new LocalPoint(candidate.navMeshPosition.x, candidate.navMeshPosition.z), navMeshBuilder.Settings, out var check, out _)) continue;
                        if (!ValidateRouteReadOnly(check)) continue;
                        unseen.Remove(candidate.roomId); group.Add(candidate.roomId);
                    }
                groups.Add(group);
            }
            foreach (var group in groups) Debug.Log("[ARNav][Connectivity] component=" + string.Join("/", group.ToArray()));
        }

        private static GeneratedRoomNavAnchor FindAnchor(List<GeneratedRoomNavAnchor> anchors, string roomId)
        { foreach (var anchor in anchors) if (anchor.roomId == roomId) return anchor; return null; }
        private bool ValidateRouteReadOnly(NavigationRoute candidate)
        {
            if (candidate == null || !candidate.valid) return false;
            var canonical = new List<LocalPoint>(candidate.rawCorners.Count); foreach (var point in candidate.rawCorners) canonical.Add(new LocalPoint(point.x, 2f * mapReflectionZ - point.z));
            return !RouteGeometryValidator.CrossesSolidWall(canonical, floor, out _) && !RouteGeometryValidator.EntersNonWalkableRegion(canonical, floor, out _);
        }

        private void CommitDevelopmentRoute(NavigationRoute candidate, int startIndex, int destinationIndex, List<RoomData> destinations, bool detourEvidence)
        {
            route = candidate; CreateRouteObjects(route); progress.Reset(route); destinationDropdown.value = destinationIndex;
            if (detourEvidence) Debug.Log("[ARNav][Smoke] Test1 detour evidence: A=" + destinations[startIndex].id + " B=" + destinations[destinationIndex].id + " directSegmentWallIntersection=1 pathStatus=" + candidate.status + " corners=" + candidate.rawCorners.Count + " routeWallIntersections=0");
            else Debug.LogWarning("[ARNav][Smoke] Complete Test1 path exists, but no direct-wall-crossing detour pair was found; A=" + destinations[startIndex].id + " B=" + destinations[destinationIndex].id + " corners=" + candidate.rawCorners.Count + ".");
            SetStatus("Development route selected: " + destinations[startIndex].id + " to " + destinations[destinationIndex].id + ".");
        }

        /// <summary>Development-only deterministic progression check using the real route tracker.</summary>
        public IEnumerator RunDevelopmentProgressionForSmoke()
        {
            if (!(Debug.isDebugBuild || Application.isEditor) || route == null || !route.valid) yield break;
            for (var i = 0; i < route.waypoints.Count; i++)
            {
                UpdateUserPosition(route.waypoints[i], 0, true);
                yield return new WaitForSeconds(0.05f);
            }
            Debug.Log(progress.DestinationReached ? "[ARNav][Smoke] Waypoints reached" : "[ARNav][Smoke] FAIL: waypoint progression did not reach destination");
        }
        public void ClearRoute() { if (routeRoot != null) DestroyObject(routeRoot); routeRoot = null; route = null; progress.Reset(null); selectedStartRoomId = null; }

        private bool ValidateAgainstNavigationGrid(NavigationRoute candidate)
        {
            if (navigationGrid == null || navigationGrid.sessionId != session.SessionId || navigationGrid.sourceSha256 != project.sourceImage.sha256) return false;
            for (var i = 0; i < candidate.rawCorners.Count - 1; i++)
            {
                var a = candidate.rawCorners[i]; var b = candidate.rawCorners[i + 1];
                if (!navigationGrid.TryWorldToCell(new LocalPoint(a.x, 2f * mapReflectionZ - a.z), out var ax, out var ay) || !navigationGrid.TryWorldToCell(new LocalPoint(b.x, 2f * mapReflectionZ - b.z), out var bx, out var by) || !navigationGrid.HasLineOfSight(ax, ay, bx, by))
                { Debug.LogWarning("[ARNav][RouteValidation] NavMesh route segment " + i + " intersects blocked/out-of-grid occupancy cells; falling back to GridAStar."); return false; }
            }
            return true;
        }
        private bool ResolveAnchorRoutePoint(GeneratedRoomNavAnchor anchor)
        {
            if (anchor == null) return false;
            if (anchor.sessionId != session.SessionId || anchor.sourceSha256 != (project.sourceImage == null ? null : project.sourceImage.sha256)) { anchor.hasNavMeshPosition = false; anchor.hasGridPosition = false; Debug.LogWarning("[ARNav][TouchRoute] stale waypoint session/hash: " + anchor.roomId); return false; }
            var room = RoomNavigationPointService.FindRoom(floor, anchor.roomId); if (room == null) return false;
            if (anchor.hasNavMeshPosition && UnityEngine.AI.NavMesh.SamplePosition(anchor.navMeshPosition, out var cached, .08f, UnityEngine.AI.NavMesh.AllAreas) && RegionLocator.Contains(new LocalPoint(cached.position.x, 2f * mapReflectionZ - cached.position.z), room.polygon) && RouteGeometryValidator.IsPointClearOfSolidWalls(new LocalPoint(cached.position.x, 2f * mapReflectionZ - cached.position.z), floor, 0f, out _)) { anchor.navMeshPosition = cached.position; return true; }
            if (!TrySampleRoomPoint(room, out var sampled, out var reason)) { anchor.hasNavMeshPosition = false; Debug.LogWarning("[ARNav][TouchRoute] no same-room NavMesh point: room=" + room.id + " reason=" + reason); return false; }
            anchor.navMeshPosition = sampled.position; anchor.navMeshSampleDistance = Vector3.Distance(anchor.displayPosition, sampled.position); anchor.hasNavMeshPosition = true; return true;
        }
        public void SetViewAngle(float value)
        { EnsureCameraState(); cameraView.SetAngle(Mathf.Clamp01(value)); ApplyCameraView(); }
        public void SetViewZoom(float value)
        { EnsureCameraState(); cameraView.SetZoom(Mathf.Clamp01(value)); ApplyCameraView(); }
        public void UpdateUserPosition(LocalPoint localPosition, double horizontalAccuracyMetres, bool manual = false)
        { if (route == null) return; var settings = navMeshBuilder == null ? new NavigationSettings() : navMeshBuilder.Settings; var advanced = progress.Update(localPosition, route, horizontalAccuracyMetres, manual, settings.waypointArrivalRadiusMetres); if (advanced) SetStatus(progress.DestinationReached ? "Destination reached: " + route.destinationRoomName : "Waypoint " + progress.CurrentIndex + " of " + route.waypoints.Count + "."); }
        public void EnableLocation() { locationProvider?.StartProvider(); }
        public void ExportCurrent3D()
        {
            if (!CanExportCurrentModel) { SetStatus("Generate a current 3D model before exporting GLB."); return; }
            if (!RuntimeGlbExporter.TryExport(generator.GeneratedRoot, out var exported, out var error)) { SetStatus("3D export failed: " + error); return; }
            if (documentBridge == null) documentBridge = gameObject.GetComponent<AndroidDocumentBridge>() ?? gameObject.AddComponent<AndroidDocumentBridge>();
            var sourceName = project.sourceImage.fileName;
            if (string.IsNullOrWhiteSpace(sourceName)) sourceName = "FloorPlan";
            else sourceName = System.IO.Path.GetFileNameWithoutExtension(sourceName);
            foreach (var invalid in System.IO.Path.GetInvalidFileNameChars()) sourceName = sourceName.Replace(invalid, '_');
            var fileName = "ARNav_" + sourceName + ".glb";
            Debug.Log("[ARNav][Export] Generated GLB bytes=" + exported.bytes.Length + " meshes=" + exported.meshCount + " vertices=" + exported.vertexCount + " triangles=" + exported.triangleCount + " project=" + generator.GeneratedProjectId + " sha256=" + generator.GeneratedSourceSha256);
            SetStatus("Choose where to save the GLB model...");
            if (!documentBridge.BeginGlbSave(exported.bytes, fileName, OnGlbSaveCompleted, out error)) SetStatus("GLB export failed: " + error);
        }

        private void OnGlbSaveCompleted(bool success, string destination, long bytes)
        {
            if (success) { Debug.Log("[ARNav][Export] GLB saved bytes=" + bytes + " destination=" + destination); SetStatus("3D model saved (" + bytes + " bytes)."); }
            else { Debug.LogWarning("[ARNav][Export] Save failed: " + destination); SetStatus(destination == "Export cancelled." ? "GLB export cancelled." : "GLB export failed: " + destination); }
        }

        public void CopyCurrentJson()
        {
            if (project == null) { SetStatus("No map JSON is available."); return; }
            var json = ARNav.Core.Serialization.MapJsonSerializer.Serialize(project);
            SetStatus(AndroidDocumentBridge.TryCopyTextAndVerify(json, out var readBack, out var error)
                ? "Map JSON copied (" + readBack.Length + " characters)."
                : "Copy JSON failed: " + (error ?? "clipboard did not match") + " Use Export JSON.");
        }

        public void ExportCurrentJson()
        {
            if (project == null) { SetStatus("No map JSON is available."); return; }
            if (documentBridge == null) documentBridge = gameObject.GetComponent<AndroidDocumentBridge>() ?? gameObject.AddComponent<AndroidDocumentBridge>();
            var json = ARNav.Core.Serialization.MapJsonSerializer.Serialize(project);
            SetStatus("Choose where to save the map JSON...");
            if (!documentBridge.BeginJsonSave(json, "FloorPlan_" + project.projectId + ".json", OnJsonSaveCompleted, out var error)) SetStatus("Export JSON failed: " + error);
        }

        private void OnJsonSaveCompleted(bool success, string destination, long bytes)
        { SetStatus(success ? "Map JSON saved (" + bytes + " bytes)." : destination == "Export cancelled." ? "JSON export cancelled." : "Export JSON failed: " + destination); }

        private void SetExportAvailability(bool available)
        { exportAvailable = available; ExportAvailabilityChanged?.Invoke(CanExportCurrentModel); }
        public void SetGpsReadout(Text readout) { minimalGpsReadout = readout; }
        private void InitializeLocation()
        { if (project == null) return; positionService = new UserMapPositionService(); locationProvider = Application.isEditor ? (ILocationProvider)mockLocationProvider : unityLocationProvider; if (locationProvider == null) return; locationProvider.ReadingReceived += OnLocationReading; locationProvider.StatusChanged += OnLocationStatus; locationHud?.SetStatus("Off"); }
        private void OnLocationReading(LocationReading reading)
        { var result = positionService.Evaluate(project, reading, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0); locationHud?.SetReading(reading, result); if (minimalGpsReadout != null && reading != null) { var c = CultureInfo.InvariantCulture; minimalGpsReadout.text = "Lat: " + reading.latitude.ToString("F6", c) + "\nLong: " + reading.longitude.ToString("F6", c) + "\nAlt: " + reading.altitudeMetres.ToString("0.0", c); } var settings = navMeshBuilder == null ? new NavigationSettings() : navMeshBuilder.Settings; liveStartAvailable = result.hasValidLocation && result.quality >= LocationQuality.Excellent && result.quality <= LocationQuality.Fair && reading.horizontalAccuracyMetres <= settings.gpsMaximumAccuracyMetres; if (liveStartAvailable) { liveStart = result.localPosition; if (route == null) SetStatus("Usable live map position received."); else { var offRoute = RouteWaypointSampler.DistanceToPolyline(result.localPosition, route.waypoints) > settings.offRouteThresholdMetres; if (offRoute) TryReroute(result.localPosition, settings); UpdateUserPosition(result.localPosition, reading.horizontalAccuracyMetres); } } }
        private void TryReroute(LocalPoint start, NavigationSettings settings)
        { var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); var moved = RouteWaypointSampler.TotalDistance(new[] { route.startLocal, start }); if (now - lastRerouteUnixSeconds < 5 || moved < settings.rerouteMinimumMovementMetres) { SetStatus("Live position is off the current route."); return; } if (NavigationRouteService.TryCalculate(start, floor, route.destinationRoomId, settings, out var replacement, out _) && ValidateRoute(replacement, "rerouted route")) { ClearRoute(); route = replacement; CreateRouteObjects(route); progress.Reset(route); lastRerouteUnixSeconds = now; SetStatus("Route recalculated from current position."); } else SetStatus("Live position is off the route; destination remains unreachable from here."); }
        private void OnLocationStatus(LocationProviderStatus status, string message) { locationHud?.SetStatus(status == LocationProviderStatus.Running ? "Running — waiting for fix" : status.ToString()); }
        public void FitBuilding()
        { ApplyCameraView(); }
        public void FitBuildingTopDown()
        { if (previewCamera == null || generator == null || generator.GeneratedRoot == null) return; EnsureCameraState(); cameraView.SetAngle(0f); cameraView.SetZoom(0.5f); cameraView.SetPivot(GetGeneratedBounds().center); ApplyCameraView(); }
        public void BackToSetup() { UnityEngine.SceneManagement.SceneManager.LoadScene("MapSetup"); }
        private void CreateNavigationRoot() { if (navigationRoot != null) DestroyObject(navigationRoot); if (networkMaterial != null) DestroyObject(networkMaterial); networkMaterial = null; networkRoot = null; navigationRoot = new GameObject("NavigationRuntime"); navigationRoot.transform.SetParent(generator.GeneratedRoot.transform, false); roomAnchorsRoot = new GameObject("RoomAnchors"); roomAnchorsRoot.transform.SetParent(navigationRoot.transform, false); }
        private void ResetCameraViewForProject()
        { cameraProjectId = project == null ? null : project.projectId; cameraView = new CameraViewState(defaultViewAngle, defaultViewZoom); }
        private void EnsureCameraState()
        { if (cameraView == null || cameraProjectId != (project == null ? null : project.projectId)) ResetCameraViewForProject(); }
        private void ApplyCameraView()
        {
            if (generator == null || generator.GeneratedRoot == null || previewCamera == null) return; EnsureCameraState(); var bounds = GetGeneratedBounds();
            var target = bounds.center; cameraView.SetPivot(target); var pitch = Mathf.Lerp(90f, 25f, Mathf.Clamp01(cameraView.angle));
            var fit = CameraFramingUtility.CalculateFitDistance(bounds, previewCamera.fieldOfView, Mathf.Max(0.01f, previewCamera.aspect));
            var distance = fit * Mathf.Pow(2f, (0.5f - Mathf.Clamp01(cameraView.zoom)) * 2f);
            previewCamera.orthographic = false;
            if (cameraView.angle <= 0.0001f)
            {
                previewCamera.transform.position = target + Vector3.up * distance;
                previewCamera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            }
            else
            {
                var orbit = Quaternion.Euler(pitch, 35f, 0f);
                previewCamera.transform.position = target + orbit * Vector3.back * distance;
                previewCamera.transform.rotation = Quaternion.LookRotation((target - previewCamera.transform.position).normalized, Vector3.up);
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var visibleRenderers = generator.GeneratedRoot.GetComponentsInChildren<Renderer>(true);
            Debug.Log("[ARNav][View] mapBounds=" + bounds + " camera=" + previewCamera.name + " enabled=" + previewCamera.enabled +
                      " pos=" + previewCamera.transform.position + " rot=" + previewCamera.transform.rotation.eulerAngles +
                      " fov=" + previewCamera.fieldOfView + " aspect=" + previewCamera.aspect + " distance=" + distance +
                      " renderers=" + visibleRenderers.Length);
            foreach (var renderer in visibleRenderers)
                if (renderer != null) Debug.Log("[ARNav][View] renderer=" + renderer.name + " enabled=" + renderer.enabled +
                    " active=" + renderer.gameObject.activeInHierarchy + " layer=" + renderer.gameObject.layer +
                    " bounds=" + renderer.bounds + " shader=" + (renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null ? "<null>" : renderer.sharedMaterial.shader.name));
#endif
        }
        private Bounds GetGeneratedBounds()
        {
            var bounds = generator.GeneratedBounds; var hasBounds = bounds.size.sqrMagnitude > 0.000001f;
            var renderers = generator.GeneratedRoot.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers) { if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; } else bounds.Encapsulate(renderer.bounds); }
            return bounds;
        }
        private bool ValidateRoute(NavigationRoute candidate, string label)
        {
            if (candidate == null || !candidate.valid) { SetStatus(label + " is invalid."); return false; }
            var canonicalCorners = new List<LocalPoint>(candidate.rawCorners.Count); foreach (var point in candidate.rawCorners) canonicalCorners.Add(new LocalPoint(point.x, 2f * mapReflectionZ - point.z));
            if (RouteGeometryValidator.CrossesSolidWall(canonicalCorners, floor, out var wallError)) { Debug.LogError("[ARNav][RouteValidation] " + label + " INVALID: " + wallError); SetStatus("Route blocked by wall."); return false; }
            if (RouteGeometryValidator.EntersNonWalkableRegion(canonicalCorners, floor, out var regionError)) { Debug.LogError("[ARNav][RouteValidation] " + label + " INVALID: " + regionError); SetStatus("Route enters non-walkable space."); return false; }
            Debug.Log("[ARNav][RouteValidation] " + label + " valid: corners=" + candidate.rawCorners.Count + " wallIntersections=0"); return true;
        }
        private List<RoomData> DestinationRooms()
        { var rooms = new List<RoomData>(); if (floor != null && floor.rooms != null) foreach (var room in floor.rooms) if (room != null && room.walkable && room.destinationSelectable) rooms.Add(room); return rooms; }
        private void PopulateDestinations()
        { if (destinationDropdown == null) return; destinationDropdown.ClearOptions(); var options = new List<string>(); foreach (var room in DestinationRooms()) options.Add(string.IsNullOrWhiteSpace(room.displayName) ? room.id : room.displayName); destinationDropdown.AddOptions(options); }
        private void CreateRoomAnchors()
        {
            var rooms = DestinationRooms(); var count = 0; var routable = 0; var missing = new List<string>();
            foreach (var room in rooms)
            {
                var clearance = 0.005f;
                if (!RoomNavigationPointService.TryFindWalkableInteriorPoint(room, floor, clearance, out var candidate, out var reason)) { missing.Add(room.id + "=no valid non-wall polygon interior: " + reason); Debug.LogWarning("[ARNav][Navigation] Semantic room omitted because no actual non-wall interior point was found: " + room.id + " " + reason); continue; }
                var pointInside = RoomNavigationPointService.Contains(room, candidate); var overlapsWall = !RouteGeometryValidator.IsPointClearOfSolidWalls(candidate, floor, 0f, out var wallId);
                if (!pointInside || overlapsWall) { missing.Add(room.id + "=candidate failed containment/wall assertion"); Debug.LogError("[ARNav][WaypointAssert] Reject " + room.id + " inside=" + pointInside + " wallOverlap=" + overlapsWall + " wall=" + wallId); continue; }
                var display = new Vector3(candidate.x, floor.elevationMetres, 2f * mapReflectionZ - candidate.z);
                var anchor = new GameObject("RoomNavAnchor_" + room.id); anchor.transform.SetParent(roomAnchorsRoot.transform, false); anchor.transform.position = display;
                var metadata = anchor.AddComponent<GeneratedRoomNavAnchor>(); metadata.waypointId = floor.id + ":" + room.id; metadata.floorId = floor.id; metadata.roomId = room.id; metadata.roomNumber = room.roomNumber; metadata.displayName = room.displayName; metadata.sessionId = session.SessionId; metadata.sourceSha256 = project.sourceImage == null ? null : project.sourceImage.sha256; metadata.displayPosition = display;
                var gridCellX = -1; var gridCellY = -1;
                if (navigationGrid != null && navigationGrid.sessionId == session.SessionId && navigationGrid.sourceSha256 == project.sourceImage.sha256 && navigationGrid.TryFindWalkable(candidate, p => RoomNavigationPointService.Contains(room, p), 80, out gridCellX, out gridCellY))
                { var gridPoint = navigationGrid.CellCenter(gridCellX, gridCellY); metadata.gridPosition = new Vector3(gridPoint.x, floor.elevationMetres, 2f * mapReflectionZ - gridPoint.z); metadata.hasGridPosition = true; metadata.gridComponentId = navigationGrid.GetComponentId(gridCellX, gridCellY); }
                if (TrySampleRoomPoint(room, out var hit, out reason)) { metadata.navMeshPosition = hit.position; metadata.hasNavMeshPosition = true; metadata.navMeshSampleDistance = Vector3.Distance(display, hit.position); routable++; }
                else missing.Add(room.id + "=" + reason);
                var wallGap = RouteGeometryValidator.DistanceToSolidWallFootprints(candidate, floor); var markerHalfSize = Mathf.Max(0.001f, Mathf.Min(0.12f, wallGap * 0.65f));
                anchor.AddComponent<SphereCollider>().radius = Mathf.Clamp(markerHalfSize * 1.5f, .025f, .10f); CreateMarker(anchor, Color.magenta, markerHalfSize); count++;
                Debug.Log("[ARNav][WaypointAssert] id=" + metadata.waypointId + " room=" + room.id + " displayXZ=(" + display.x.ToString("0.00", CultureInfo.InvariantCulture) + "," + display.z.ToString("0.00", CultureInfo.InvariantCulture) + ") pointInPolygon=" + pointInside + " wallOverlap=" + overlapsWall + " wallGap=" + wallGap.ToString("0.00", CultureInfo.InvariantCulture) + " gridCell=" + (metadata.hasGridPosition ? gridCellX + "," + gridCellY : "none") + " gridComponent=" + metadata.gridComponentId + " routeSample=" + metadata.hasNavMeshPosition + " sampleDistance=" + metadata.navMeshSampleDistance.ToString("0.00", CultureInfo.InvariantCulture) + (metadata.hasNavMeshPosition ? "" : " reason=" + reason));
            }
            Debug.Log("[ARNav][Navigation] Room coverage: semantic=" + rooms.Count + " visibleWaypoints=" + count + " routingPositions=" + routable + " withoutRoutingPosition=" + (count - routable) + " malformed=" + (rooms.Count - count) + " details=" + string.Join(",", missing.ToArray()));
            var gridRoutable = 0; foreach (Transform child in roomAnchorsRoot.transform) { var a = child.GetComponent<GeneratedRoomNavAnchor>(); if (a != null && a.hasGridPosition) gridRoutable++; }
            Debug.Log("[ARNav][NavigationGrid] dimensions=" + (navigationGrid == null ? "none" : navigationGrid.width + "x" + navigationGrid.height) + " cellSize=" + (navigationGrid == null ? 0 : navigationGrid.cellSize).ToString("0.000") + " clearance=0.015m components=" + (navigationGrid == null ? 0 : navigationGrid.ComponentCount) + " roomRoutingPositions=" + gridRoutable + "/" + count + " session=" + (navigationGrid == null ? "none" : navigationGrid.sessionId) + " sha256=" + (project.sourceImage == null ? "none" : project.sourceImage.sha256));
        }

        private void BuildAutomaticWaypointNetwork()
        {
            if (navigationGrid == null || navigationGrid.sessionId != session.SessionId || navigationGrid.sourceSha256 != project.sourceImage.sha256)
            { Debug.LogWarning("[ARNav][WaypointNetwork] skipped: no current-session occupancy grid."); return; }
            var nodes = new List<WaypointGridNode>();
            if (roomAnchorsRoot != null) foreach (Transform child in roomAnchorsRoot.transform)
            {
                var anchor = child.GetComponent<GeneratedRoomNavAnchor>();
                if (anchor == null || !anchor.hasGridPosition || anchor.sessionId != session.SessionId || anchor.sourceSha256 != project.sourceImage.sha256 || anchor.gridComponentId < 0) continue;
                var canonical = new LocalPoint(anchor.gridPosition.x, 2f * mapReflectionZ - anchor.gridPosition.z);
                if (!navigationGrid.TryWorldToCell(canonical, out var x, out var y) || !navigationGrid.IsWalkable(x, y) || navigationGrid.GetComponentId(x, y) != anchor.gridComponentId) continue;
                nodes.Add(new WaypointGridNode { roomId = anchor.roomId, cellX = x, cellY = y, componentId = anchor.gridComponentId });
            }
            var result = WaypointNetworkService.Build(navigationGrid, nodes);
            networkRoot = new GameObject("AutomaticWaypointNetwork"); networkRoot.transform.SetParent(navigationRoot.transform, false);
            if (networkMaterial != null) DestroyObject(networkMaterial);
            networkMaterial = CreateLineMaterial(new Color(0.62f, 0.08f, 0.9f));
            var drawn = 0;
            foreach (var edge in result.edges)
            {
                var blocked = false;
                for (var i = 0; i < edge.path.Count - 1; i++)
                {
                    if (!navigationGrid.TryWorldToCell(edge.path[i], out var ax, out var ay) || !navigationGrid.TryWorldToCell(edge.path[i + 1], out var bx, out var by) || !navigationGrid.HasLineOfSight(ax, ay, bx, by)) { blocked = true; break; }
                }
                string wallError = null;
                var intersectsWall = !blocked && RouteGeometryValidator.CrossesSolidWall(edge.path, floor, out wallError);
                if (blocked || intersectsWall)
                { Debug.LogError("[ARNav][WaypointNetwork] rejected " + edge.fromRoomId + "->" + edge.toRoomId + " component=" + edge.componentId + " reason=" + (blocked ? "blocked occupancy segment" : wallError)); continue; }
                if (RouteGeometryValidator.EntersNonWalkableRegion(edge.path, floor, out var regionError))
                { Debug.LogError("[ARNav][WaypointNetwork] rejected " + edge.fromRoomId + "->" + edge.toRoomId + " component=" + edge.componentId + " reason=" + regionError); continue; }
                var lineObject = new GameObject("NetworkPath_" + edge.fromRoomId + "_" + edge.toRoomId);
                lineObject.transform.SetParent(networkRoot.transform, false);
                var line = lineObject.AddComponent<LineRenderer>(); line.useWorldSpace = true; line.enabled = true; line.positionCount = edge.path.Count;
                line.startWidth = 0.055f; line.endWidth = 0.055f; line.numCapVertices = 2; line.numCornerVertices = 2; line.sharedMaterial = networkMaterial; line.startColor = networkMaterial.color; line.endColor = networkMaterial.color;
                for (var i = 0; i < edge.path.Count; i++) line.SetPosition(i, new Vector3(edge.path[i].x, floor.elevationMetres + waypointHeightOffsetMetres, 2f * mapReflectionZ - edge.path[i].z));
                drawn++;
                Debug.Log("[ARNav][WaypointNetworkEdge] " + edge.fromRoomId + "->" + edge.toRoomId + " component=" + edge.componentId + " backend=GridAStar rawCells=" + edge.rawCellCount + " points=" + edge.path.Count + " wallIntersections=0 enabled=" + line.enabled);
            }
            Debug.Log("[ARNav][WaypointNetwork] waypoints=" + nodes.Count + " components=" + result.componentCount + " edges=" + drawn + " sameComponentPathFailures=" + result.sameComponentPathFailures + " session=" + session.SessionId + " sha256=" + project.sourceImage.sha256);
        }

        private void BuildNavigationGrid()
        {
            navigationGrid = null;
            if (floor == null || project == null || floor.rooms == null || floor.rooms.Count == 0) return;
            const float cell = 0.05f, clearance = 0.015f;
            var wallThicknesses = new List<float>(); if (floor.walls != null) foreach (var wall in floor.walls) if (wall != null && wall.thicknessMetres > 0) wallThicknesses.Add(wall.thicknessMetres);
            var openingWidths = new List<float>(); if (floor.doors != null) foreach (var door in floor.doors) if (door != null && door.widthMetres > 0) openingWidths.Add(door.widthMetres);
            wallThicknesses.Sort(); openingWidths.Sort();
            var typicalWall = wallThicknesses.Count == 0 ? 0f : wallThicknesses[wallThicknesses.Count / 2];
            var typicalOpening = openingWidths.Count == 0 ? 0f : openingWidths[openingWidths.Count / 2];
            Debug.Log("[ARNav][NavigationGridTuning] estimatedWallThickness=" + typicalWall.ToString("0.000", CultureInfo.InvariantCulture) + "m typicalOpening=" + typicalOpening.ToString("0.000", CultureInfo.InvariantCulture) + "m clearance=" + clearance.ToString("0.000", CultureInfo.InvariantCulture) + "m effectiveOpening=" + Mathf.Max(0f, typicalOpening - 2f * clearance).ToString("0.000", CultureInfo.InvariantCulture) + "m");
            var minX = float.PositiveInfinity; var minZ = float.PositiveInfinity; var maxX = float.NegativeInfinity; var maxZ = float.NegativeInfinity;
            foreach (var room in floor.rooms) if (room != null && room.walkable)
            {
                if (room.rasterRuns != null && room.rasterRuns.Count > 0) foreach (var run in room.rasterRuns) { minX = Mathf.Min(minX, run.minX); minZ = Mathf.Min(minZ, run.minZ); maxX = Mathf.Max(maxX, run.maxX); maxZ = Mathf.Max(maxZ, run.maxZ); }
                else if (room.polygon != null && room.polygon.vertices != null) foreach (var v in room.polygon.vertices) { minX = Mathf.Min(minX, v.x); minZ = Mathf.Min(minZ, v.z); maxX = Mathf.Max(maxX, v.x); maxZ = Mathf.Max(maxZ, v.z); }
            }
            if (float.IsNaN(minX) || float.IsInfinity(minX) || float.IsNaN(minZ) || float.IsInfinity(minZ) || maxX <= minX || maxZ <= minZ) return;
            var origin = new LocalPoint(minX, minZ); var width = Mathf.CeilToInt((maxX - minX) / cell); var height = Mathf.CeilToInt((maxZ - minZ) / cell);
            if ((long)width * height > 500000) { Debug.LogError("[ARNav][NavigationGrid] Grid cap exceeded; refusing unsafe allocation."); return; }
            var grid = new NavigationGrid(width, height, cell, origin, session.SessionId, project.sourceImage == null ? null : project.sourceImage.sha256);
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            {
                var p = grid.CellCenter(x, y); var roomWalkable = false;
                foreach (var room in floor.rooms) if (room != null && room.walkable && RoomNavigationPointService.Contains(room, p)) { roomWalkable = true; break; }
                if (!roomWalkable) continue;
                var inVoid = false; foreach (var room in floor.rooms) if (room != null && !room.walkable && RegionLocator.Contains(p, room.polygon)) { inVoid = true; break; }
                if (inVoid || IntersectsWall(p, floor, clearance)) continue;
                grid.SetWalkable(x, y, true);
            }
            grid.BuildConnectedComponents();
            navigationGrid = grid;
        }

        private static bool IntersectsWall(LocalPoint point, FloorData mapFloor, float clearance)
        {
            if (mapFloor.walls == null) return false;
            foreach (var wall in mapFloor.walls)
            {
                var dx = wall.end.x - wall.start.x; var dz = wall.end.z - wall.start.z; var lenSq = dx * dx + dz * dz; if (lenSq < 1e-8f) continue;
                var t = Mathf.Clamp01(((point.x - wall.start.x) * dx + (point.z - wall.start.z) * dz) / lenSq);
                var wallLength = Mathf.Sqrt(lenSq); var along = t * wallLength; var opening = false;
                if (mapFloor.doors != null) foreach (var door in mapFloor.doors) if (door != null && door.parentWallId == wall.id && along >= door.distanceAlongWallMetres - door.widthMetres * 0.5f - clearance && along <= door.distanceAlongWallMetres + door.widthMetres * 0.5f + clearance) { opening = true; break; }
                if (opening) continue;
                var px = wall.start.x + t * dx; var pz = wall.start.z + t * dz; var radius = Mathf.Max(0.01f, wall.thicknessMetres * 0.5f) + clearance;
                if ((point.x - px) * (point.x - px) + (point.z - pz) * (point.z - pz) <= radius * radius) return true;
            }
            return false;
        }
        private bool TrySampleRoomPoint(RoomData room, out UnityEngine.AI.NavMeshHit hit, out string reason)
        {
            reason = "no interior candidate"; var candidates = RoomNavigationPointService.GetInteriorCandidates(room); var bestScore = float.MinValue; var found = false; var best = default(UnityEngine.AI.NavMeshHit); var sampledAny = false; var insideAny = false; var wallRejected = false;
            if (room == null || room.polygon == null || room.polygon.vertices == null || room.polygon.vertices.Count < 3 || !PolygonTriangulator.TryTriangulate(room.polygon.vertices, out _, out _)) { hit = default(UnityEngine.AI.NavMeshHit); reason = "malformed room polygon"; return false; }
            var radii = new[] { .12f, .25f, .4f, .6f };
            foreach (var point in candidates) if (RegionLocator.Contains(point, room.polygon)) foreach (var radius in radii) if (UnityEngine.AI.NavMesh.SamplePosition(new Vector3(point.x, floor.elevationMetres, 2f * mapReflectionZ - point.z), out var sampled, radius, UnityEngine.AI.NavMesh.AllAreas))
            {
                sampledAny = true; var canonical = new LocalPoint(sampled.position.x, 2f * mapReflectionZ - sampled.position.z); if (!RegionLocator.Contains(canonical, room.polygon)) continue; insideAny = true;
                var clearance = RoomNavigationPointService.DistanceToBoundary(canonical, room);
                var wallClearance = navMeshBuilder == null ? .3f : navMeshBuilder.Settings.agentRadiusMetres;
                if (clearance < wallClearance || !RouteGeometryValidator.IsPointClearOfSolidWalls(canonical, floor, wallClearance, out _)) { wallRejected = true; continue; }
                if (clearance > bestScore) { bestScore = clearance; best = sampled; found = true; }
            }
            hit = best; if (!found) reason = wallRejected && insideAny ? "wall blockers cover usable room area or leave less than agent clearance" : insideAny ? "room polygon aligns but no NavMesh sample within 0.6m (coverage/agent erosion)" : sampledAny ? "NavMesh samples fall outside room polygon (coordinate/region mismatch)" : "no NavMesh surface sampled within room interior candidates at radii 0.12/0.25/0.4/0.6m"; return found;
        }
        private void CreateRouteObjects(NavigationRoute activeRoute)
        {
            if (routeRoot != null) DestroyObject(routeRoot);
            routeRoot = new GameObject("ActiveRoute"); routeRoot.transform.SetParent(navigationRoot.transform, false); var line = routeRoot.AddComponent<LineRenderer>(); line.useWorldSpace = true; line.enabled = true; line.positionCount = activeRoute.rawCorners.Count; line.startWidth = 0.06f; line.endWidth = 0.06f; line.material = CreateLineMaterial(new Color(0.62f, 0.08f, 0.9f)); line.startColor = new Color(0.62f, 0.08f, 0.9f); line.endColor = new Color(0.62f, 0.08f, 0.9f);
            for (var i = 0; i < activeRoute.rawCorners.Count; i++) { var point = activeRoute.rawCorners[i]; line.SetPosition(i, new Vector3(point.x, floor.elevationMetres + waypointHeightOffsetMetres, point.z)); }
            Debug.Log("[ARNav][TouchRoute] LineRenderer committed enabled=" + line.enabled + " worldSpace=" + line.useWorldSpace + " positions=" + line.positionCount + " corners=" + activeRoute.rawCorners.Count);
            var settings = navMeshBuilder.Settings; for (var i = 0; i < activeRoute.waypoints.Count; i++) { var point = activeRoute.waypoints[i]; var waypoint = new GameObject(i == activeRoute.waypoints.Count - 1 ? "Destination" : "Waypoint_" + i.ToString("000")); waypoint.transform.SetParent(routeRoot.transform, false); waypoint.transform.position = new Vector3(point.x, floor.elevationMetres + waypointHeightOffsetMetres, point.z); var metadata = waypoint.AddComponent<NavigationWaypoint>(); metadata.index = i; metadata.routeId = activeRoute.destinationRoomId; metadata.roomId = i == activeRoute.waypoints.Count - 1 ? activeRoute.destinationRoomId : selectedStartRoomId; var owner = RoomNavigationPointService.FindRoom(floor, metadata.roomId); metadata.roomNumber = owner == null ? null : owner.roomNumber; metadata.arrivalRadiusMetres = i == activeRoute.waypoints.Count - 1 ? settings.destinationArrivalRadiusMetres : settings.waypointArrivalRadiusMetres; metadata.isDestination = i == activeRoute.waypoints.Count - 1; }
        }
        // Logical waypoint/anchor parents remain empty; this child is visual-only and has no collider.
        private static void CreateMarker(GameObject parent, Color color, float size)
        {
            var marker = new GameObject("VisualMarker", typeof(MeshFilter), typeof(MeshRenderer)); marker.transform.SetParent(parent.transform, false); marker.transform.localPosition = new Vector3(0, 0.12f, 0);
            var mesh = new Mesh { name = "NavigationMarkerMesh" }; mesh.vertices = new[] { new Vector3(-size, 0, -size), new Vector3(size, 0, -size), new Vector3(size, 0, size), new Vector3(-size, 0, size) }; mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 }; mesh.RecalculateNormals(); marker.GetComponent<MeshFilter>().sharedMesh = mesh; marker.GetComponent<MeshRenderer>().sharedMaterial = CreateLineMaterial(color);
        }
        private static void CreateAnchorDebugMarker(GameObject parent) { var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere); marker.name = "DebugAnchorMarker"; marker.transform.SetParent(parent.transform, false); marker.transform.localPosition = new Vector3(0, 0.12f, 0); marker.transform.localScale = Vector3.one * 0.16f; var renderer = marker.GetComponent<Renderer>(); if (renderer != null) { var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Universal Render Pipeline/Lit"); if (shader != null) { var material = new Material(shader); material.color = Color.yellow; renderer.sharedMaterial = material; } } var collider = marker.GetComponent<Collider>(); if (collider != null) DestroyObject(collider); }
        private static Material CreateLineMaterial(Color color) { var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default"); if (shader == null) throw new InvalidOperationException("No runtime-compatible line shader is available."); var material = new Material(shader); material.color = color; return material; }
        private static Material CreateLineMaterial() { return CreateLineMaterial(Color.cyan); }
        private LocalPoint ReadManualStart() { if (startXInput != null && startZInput != null && float.TryParse(startXInput.text, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && float.TryParse(startZInput.text, NumberStyles.Float, CultureInfo.InvariantCulture, out var z)) return new LocalPoint(x, z); return new LocalPoint(0, 0); }
        private void SetStatus(string message) { if (statusText != null) statusText.text = message; StatusChanged?.Invoke(message); Debug.Log("ARNav: " + message); }
        private static void DestroyObject(UnityEngine.Object value) { if (value == null) return; if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
        private void OnDestroy() { if (locationProvider != null) { locationProvider.ReadingReceived -= OnLocationReading; locationProvider.StatusChanged -= OnLocationStatus; locationProvider.StopProvider(); } ClearRoute(); if (networkMaterial != null) DestroyObject(networkMaterial); if (navMeshBuilder != null) navMeshBuilder.Clear(); if (generator != null) generator.ClearGeneratedBuilding(); if (navigationRoot != null) DestroyObject(navigationRoot); if (startRouteButton != null) startRouteButton.onClick.RemoveListener(StartRoute); if (clearRouteButton != null) clearRouteButton.onClick.RemoveListener(ClearRoute); if (rebuildButton != null) rebuildButton.onClick.RemoveListener(Rebuild); if (fitButton != null) fitButton.onClick.RemoveListener(FitBuilding); if (backButton != null) backButton.onClick.RemoveListener(BackToSetup); if (enableLocationButton != null) enableLocationButton.onClick.RemoveListener(EnableLocation); if (developmentRouteButton != null) developmentRouteButton.onClick.RemoveListener(StartDevelopmentRoute); }
    }
}
