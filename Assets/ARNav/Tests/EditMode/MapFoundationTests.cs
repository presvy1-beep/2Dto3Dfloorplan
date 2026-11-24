using System;
using System.IO;
using NUnit.Framework;
using ARNav.Core.Data;
using ARNav.Core.Serialization;
using ARNav.Core.Validation;
using ARNav.Core.Geometry;
using ARNav.Core.Annotation;
using ARNav.Core.Calibration;
using ARNav.Core.Geo;
using ARNav.Runtime.Import;
using ARNav.Runtime.Location;
using ARNav.Core.Location;
using ARNav.Core.Generation;
using ARNav.Core.Navigation;
using ARNav.Runtime.Generation;
using ARNav.Runtime.Navigation;
using ARNav.Runtime.App;
using UnityEngine;

namespace ARNav.Tests.EditMode
{
    public class MapFoundationTests
    {
        private string testRoot;

        [SetUp]
        public void SetUp() { testRoot = Path.Combine(Path.GetTempPath(), "arnav-tests-" + Guid.NewGuid().ToString("N")); }

        [TearDown]
        public void TearDown() { if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true); }

        [Test]
        public void TestMapIsValidAndContainsExpectedStructure()
        {
            var map = TestMapFactory.CreateTinyThreeRoomMap();
            var result = MapValidator.Validate(map);
            Assert.IsTrue(result.IsValid, string.Join(" ", result.errors.ToArray()));
            Assert.AreEqual(3, map.floors[0].rooms.Count);
            Assert.AreEqual(2, map.floors[0].doors.Count);
        }

        [Test]
        public void JsonRoundTripPreservesCoreData()
        {
            var original = TestMapFactory.CreateTinyThreeRoomMap();
            var json = MapJsonSerializer.Serialize(original);
            TestContext.WriteLine(json);
            var loaded = MapJsonSerializer.Deserialize(json);
            Assert.That(json, Does.Contain("\"schemaVersion\": 1"));
            Assert.AreEqual(original.schemaVersion, loaded.schemaVersion);
            Assert.AreEqual(original.projectId, loaded.projectId);
            Assert.AreEqual(original.sourceImage.originalWidthPixels, loaded.sourceImage.originalWidthPixels);
            Assert.AreEqual(original.floors[0].rooms[0].polygon.vertices[2].x, loaded.floors[0].rooms[0].polygon.vertices[2].x);
            Assert.AreEqual(original.floors[0].doors[0].parentWallId, loaded.floors[0].doors[0].parentWallId);
            Assert.AreEqual(original.floors[0].navigation.edges[1].distanceMetres, loaded.floors[0].navigation.edges[1].distanceMetres);
        }

        [Test]
        public void LocalRepositorySavesLoadsListsAndDeletesProject()
        {
            var repository = new LocalFileMapRepository(testRoot);
            var map = TestMapFactory.CreateTinyThreeRoomMap();
            repository.CreateProject(map);
            Assert.That(File.Exists(Path.Combine(testRoot, "ARNav", "Maps", map.projectId, "map.json")), Is.True);
            CollectionAssert.Contains(repository.ListProjects(), map.projectId);
            var loaded = repository.LoadProject(map.projectId);
            Assert.AreEqual(map.floors[0].navigation.nodes.Count, loaded.floors[0].navigation.nodes.Count);
            repository.DeleteProject(map.projectId);
            CollectionAssert.DoesNotContain(repository.ListProjects(), map.projectId);
        }

        [Test]
        public void ValidatorReportsBrokenReferences()
        {
            var map = TestMapFactory.CreateTinyThreeRoomMap();
            map.floors[0].doors[0].parentWallId = "missing-wall";
            var result = MapValidator.Validate(map);
            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("missing wall", string.Join(" ", result.errors.ToArray()));
        }

        [Test]
        public void ImageStorageCopiesPngAndMetadataSurvivesReload()
        {
            var repository = new LocalFileMapRepository(testRoot);
            var map = TestMapFactory.CreateTinyThreeRoomMap(); repository.CreateProject(map);
            var source = Path.Combine(testRoot, "source.png"); WriteTestImage(source, false);
            var storage = new MapImageStorageService(repository);
            var stored = storage.ImportAndStore(map, source);
            map.sourceImage.pathReference = stored.relativePath; map.sourceImage.originalWidthPixels = stored.width; map.sourceImage.originalHeightPixels = stored.height; map.sourceImage.format = stored.format;
            repository.SaveProject(map); UnityEngine.Object.DestroyImmediate(stored.texture);
            var loaded = repository.LoadProject(map.projectId);
            Assert.AreEqual("floorplan.png", loaded.sourceImage.pathReference);
            Assert.AreEqual(100, loaded.sourceImage.originalWidthPixels); Assert.AreEqual(50, loaded.sourceImage.originalHeightPixels);
            Assert.IsTrue(File.Exists(repository.GetProjectImagePath(map.projectId, "floorplan.png")));
        }

        [Test]
        public void ImageStorageReplacementLeavesOnlyCurrentImage()
        {
            var repository = new LocalFileMapRepository(testRoot); var map = TestMapFactory.CreateTinyThreeRoomMap(); repository.CreateProject(map);
            var png = Path.Combine(testRoot, "one.png"); var jpg = Path.Combine(testRoot, "two.jpg"); WriteTestImage(png, false); WriteTestImage(jpg, true);
            var storage = new MapImageStorageService(repository); var first = storage.ImportAndStore(map, png); UnityEngine.Object.DestroyImmediate(first.texture);
            map.sourceImage.pathReference = first.relativePath; map.sourceImage.originalWidthPixels = first.width; map.sourceImage.originalHeightPixels = first.height; repository.SaveProject(map);
            var second = storage.ImportAndStore(map, jpg); var oldPath = map.sourceImage.pathReference; map.sourceImage.pathReference = second.relativePath; map.sourceImage.originalWidthPixels = second.width; map.sourceImage.originalHeightPixels = second.height; repository.SaveProject(map); storage.DeleteIfObsolete(map, oldPath, second.relativePath); UnityEngine.Object.DestroyImmediate(second.texture);
            Assert.IsFalse(File.Exists(repository.GetProjectImagePath(map.projectId, "floorplan.png"))); Assert.IsTrue(File.Exists(repository.GetProjectImagePath(map.projectId, "floorplan.jpg")));
        }

        [Test]
        public void SamePathChangedBytesAreRehashedAndExactSnapshotIsStored()
        {
            var repository = new LocalFileMapRepository(testRoot); var map = TestMapFactory.CreateTinyThreeRoomMap(); repository.CreateProject(map);
            var source = Path.Combine(testRoot, "Test1.png"); WriteTestImage(source, false);
            var storage = new MapImageStorageService(repository); var first = storage.ImportAndStore(map, source); map.sourceImage.pathReference = first.relativePath; var hashA = first.sha256; UnityEngine.Object.DestroyImmediate(first.texture);
            WriteTestImage(source, true);
            var second = storage.ImportAndStore(map, source); var hashB = second.sha256;
            Assert.AreNotEqual(hashA, hashB);
            Assert.AreEqual(new FileInfo(source).Length, second.sourceByteLength);
            Assert.AreEqual(hashB, MapImageStorageService.ComputeSha256(second.absolutePath));
            UnityEngine.Object.DestroyImmediate(second.texture);
        }

        [Test]
        public void ImageStorageRejectsUnsupportedFileType()
        {
            var repository = new LocalFileMapRepository(testRoot); var map = TestMapFactory.CreateTinyThreeRoomMap(); repository.CreateProject(map);
            var source = Path.Combine(testRoot, "source.gif"); File.WriteAllBytes(source, new byte[] { 1, 2, 3 });
            Assert.Throws<NotSupportedException>(() => new MapImageStorageService(repository).ImportAndStore(map, source));
        }

        [Test]
        public void GridAStarRoutesAroundSolidBarrierAndSimplifiesOnlyClearSegments()
        {
            var grid = new NavigationGrid(12, 9, 1, new LocalPoint(0, 0), "s", "h");
            for (var y = 0; y < 9; y++) for (var x = 0; x < 12; x++) grid.SetWalkable(x, y, true);
            for (var y = 1; y < 8; y++) grid.SetWalkable(5, y, false);
            Assert.IsTrue(grid.TryFindPath(2, 4, 9, 4, out var path, out var explored, out var raw));
            Assert.Greater(raw, 8); Assert.Greater(explored, 0); Assert.Greater(path.Count, 2);
            for (var i = 0; i < path.Count - 1; i++) { Assert.IsTrue(grid.TryWorldToCell(path[i], out var ax, out var ay)); Assert.IsTrue(grid.TryWorldToCell(path[i + 1], out var bx, out var by)); Assert.IsTrue(grid.HasLineOfSight(ax, ay, bx, by)); }
        }

        [Test]
        public void GridAStarRejectsDisconnectedMapAndDiagonalCornerCut()
        {
            var grid = new NavigationGrid(4, 4, 1, new LocalPoint(0, 0), "s", "h");
            grid.SetWalkable(0, 0, true); grid.SetWalkable(1, 1, true); grid.SetWalkable(1, 0, false); grid.SetWalkable(0, 1, false);
            Assert.IsFalse(grid.TryFindPath(0, 0, 1, 1, out _, out _, out _));
            grid.SetWalkable(1, 0, true); grid.SetWalkable(0, 1, true); grid.SetWalkable(2, 1, true); grid.SetWalkable(2, 2, true); grid.SetWalkable(3, 2, true);
            Assert.IsFalse(grid.TryFindPath(0, 0, 3, 3, out _, out _, out _));
        }

        [Test]
        public void NavigationGridLabelsComponentsUsingNoCornerCutConnectivity()
        {
            var grid = new NavigationGrid(4, 4, 1, new LocalPoint(0, 0), "s", "h");
            grid.SetWalkable(0, 0, true); grid.SetWalkable(1, 0, true); grid.SetWalkable(0, 1, true); grid.SetWalkable(1, 1, true);
            grid.SetWalkable(3, 3, true);
            Assert.AreEqual(2, grid.BuildConnectedComponents());
            Assert.AreEqual(grid.GetComponentId(0, 0), grid.GetComponentId(1, 1));
            Assert.AreNotEqual(grid.GetComponentId(1, 1), grid.GetComponentId(3, 3));
        }

        [Test]
        public void AutomaticWaypointNetworkBuildsAStarSpanningTreeAndKeepsPathsWalkable()
        {
            var grid = new NavigationGrid(12, 9, 1, new LocalPoint(0, 0), "session", "hash");
            for (var y = 0; y < 9; y++) for (var x = 0; x < 12; x++) grid.SetWalkable(x, y, true);
            for (var y = 0; y < 9; y++) if (y != 7) grid.SetWalkable(5, y, false);
            grid.BuildConnectedComponents();
            var component = grid.GetComponentId(2, 4);
            var nodes = new[]
            {
                new WaypointGridNode { roomId = "A", cellX = 2, cellY = 4, componentId = component },
                new WaypointGridNode { roomId = "B", cellX = 8, cellY = 3, componentId = component },
                new WaypointGridNode { roomId = "C", cellX = 9, cellY = 7, componentId = component }
            };
            var result = WaypointNetworkService.Build(grid, nodes);
            Assert.AreEqual(2, result.edges.Count);
            Assert.AreEqual(0, result.sameComponentPathFailures);
            var hasObstacleDetour = false;
            foreach (var edge in result.edges)
            {
                hasObstacleDetour |= edge.path.Count > 2;
                for (var i = 0; i < edge.path.Count - 1; i++)
                {
                    Assert.IsTrue(grid.TryWorldToCell(edge.path[i], out var ax, out var ay));
                    Assert.IsTrue(grid.TryWorldToCell(edge.path[i + 1], out var bx, out var by));
                    Assert.IsTrue(grid.HasLineOfSight(ax, ay, bx, by));
                }
            }
            Assert.IsTrue(hasObstacleDetour, "at least one network edge must route around the wall to its opening");
        }

        [Test]
        public void AutomaticWaypointNetworkDoesNotJoinDisconnectedComponents()
        {
            var grid = new NavigationGrid(9, 5, 1, new LocalPoint(0, 0), "session", "hash");
            for (var y = 0; y < 5; y++) for (var x = 0; x < 9; x++) grid.SetWalkable(x, y, true);
            for (var y = 0; y < 5; y++) grid.SetWalkable(4, y, false);
            grid.BuildConnectedComponents();
            var left = grid.GetComponentId(1, 2); var right = grid.GetComponentId(6, 2);
            var nodes = new[]
            {
                new WaypointGridNode { roomId = "A", cellX = 1, cellY = 2, componentId = left },
                new WaypointGridNode { roomId = "B", cellX = 3, cellY = 2, componentId = left },
                new WaypointGridNode { roomId = "C", cellX = 6, cellY = 2, componentId = right }
            };
            var result = WaypointNetworkService.Build(grid, nodes);
            Assert.AreEqual(2, result.componentCount);
            Assert.AreEqual(1, result.edges.Count);
            Assert.AreEqual("A", result.edges[0].fromRoomId);
            Assert.AreEqual("B", result.edges[0].toRoomId);
        }

        [Test]
        public void RoomHoleSurvivesJsonAndIsExcludedFromRoomRouting()
        {
            var room = new RoomData { id = "donut" };
            room.polygon.vertices.Add(new LocalPoint(0, 0)); room.polygon.vertices.Add(new LocalPoint(10, 0)); room.polygon.vertices.Add(new LocalPoint(10, 10)); room.polygon.vertices.Add(new LocalPoint(0, 10));
            var hole = new LocalPolygon(); hole.vertices.Add(new LocalPoint(4, 4)); hole.vertices.Add(new LocalPoint(6, 4)); hole.vertices.Add(new LocalPoint(6, 6)); hole.vertices.Add(new LocalPoint(4, 6)); room.holes.Add(hole);
            Assert.IsTrue(RoomNavigationPointService.Contains(room, new LocalPoint(2, 2)));
            Assert.IsFalse(RoomNavigationPointService.Contains(room, new LocalPoint(5, 5)));
            var project = TestMapFactory.CreateTinyThreeRoomMap(); project.floors[0].rooms[0].holes.Add(hole);
            var loaded = MapJsonSerializer.Deserialize(MapJsonSerializer.Serialize(project));
            Assert.AreEqual(1, loaded.floors[0].rooms[0].holes.Count);
            Assert.AreEqual(4, loaded.floors[0].rooms[0].holes[0].vertices.Count);
        }

        [Test]
        public void ViewerCoordinateMathPreservesUpperLeftPixelsThroughPanAndZoom()
        {
            var imageWidth = 1920f; var imageHeight = 1080f; var fit = FloorPlanCoordinateMapper.CalculateFitScale(imageWidth, imageHeight, 1000, 700); var pan = new ViewerPoint(73, -41); var scale = fit * 3f;
            var points = new[] { new PixelPoint(0, 0), new PixelPoint(960, 540), new PixelPoint(1920, 1080) };
            foreach (var point in points)
            {
                var local = FloorPlanCoordinateMapper.ImagePixelToViewportLocal(point, imageWidth, imageHeight, scale, pan);
                Assert.IsTrue(FloorPlanCoordinateMapper.TryViewportLocalToImagePixel(local, imageWidth, imageHeight, scale, pan, out var roundTrip));
                Assert.That(roundTrip.x, Is.EqualTo(point.x).Within(0.01f)); Assert.That(roundTrip.y, Is.EqualTo(point.y).Within(0.01f));
            }
            var upperLeft = FloorPlanCoordinateMapper.ImagePixelToViewportLocal(new PixelPoint(0, 0), imageWidth, imageHeight, scale, pan);
            Assert.That(upperLeft.x, Is.EqualTo(pan.x - imageWidth * scale * 0.5f).Within(0.01f)); Assert.That(upperLeft.y, Is.EqualTo(pan.y + imageHeight * scale * 0.5f).Within(0.01f));
        }

        [Test]
        public void AndroidPickerResponseParserHandlesSuccessCancelAndError()
        {
            var success = AndroidImagePickerResultParser.Parse("{\"status\":\"success\",\"path\":\"/cache/arnav-picker.png\",\"displayName\":\"plan.png\",\"mimeType\":\"image/png\"}");
            Assert.IsTrue(success.succeeded); Assert.IsTrue(success.temporary); Assert.AreEqual("plan.png", success.displayName);
            var cancelled = AndroidImagePickerResultParser.Parse("{\"status\":\"cancelled\"}"); Assert.IsTrue(cancelled.cancelled); Assert.IsFalse(cancelled.succeeded);
            var error = AndroidImagePickerResultParser.Parse("{\"status\":\"error\",\"error\":\"Could not read the selected document.\"}"); Assert.IsFalse(error.succeeded); StringAssert.Contains("Could not read", error.error);
        }

        [Test]
        public void ImageFormatDetectionUsesBytesAndSupportsJpegAliasesAndPng()
        {
            Assert.AreEqual("png", MapImageStorageService.DetectImageFormat(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }));
            Assert.AreEqual("jpg", MapImageStorageService.DetectImageFormat(new byte[] { 0xff, 0xd8, 0xff, 0x00 }));
            Assert.Throws<NotSupportedException>(() => MapImageStorageService.DetectImageFormat(new byte[] { 1, 2, 3 }));
        }

        [Test]
        public void CurrentRuntimeConfigurationPausesNavigationWithoutRemovingItsImplementation()
        {
            Assert.IsFalse(NavigationFeatureConfiguration.NavigationFeaturesEnabled);
            // NavigationGrid and A* remain directly testable while disabled in the app runtime.
            var grid = new NavigationGrid(3, 1, 1, new LocalPoint(0, 0), "session", "hash");
            for (var x = 0; x < 3; x++) grid.SetWalkable(x, 0, true);
            grid.BuildConnectedComponents();
            Assert.IsTrue(grid.TryFindPath(0, 0, 2, 0, out var path, out _, out _));
            Assert.AreEqual(2, path.Count);
        }

        [Test]
        public void DefaultWallHeightIsModestlyReducedAndCentralized()
        {
            var defaults = new MapGenerationDefaults();
            Assert.That(defaults.defaultWallHeightMetres, Is.EqualTo(2.7f).Within(0.0001f));
            Assert.That(defaults.defaultBaseThicknessMetres, Is.EqualTo(0.025f).Within(0.0001f));
            Assert.That(defaults.defaultWallThicknessMetres, Is.EqualTo(0.15f).Within(0.0001f));
        }

        [Test]
        public void PresentationMaterialsAndBaseDimensionsAreCentralized()
        {
            Assert.That(WallPresentationConfiguration.WallColor, Is.EqualTo(Color.white));
            Assert.That(WallPresentationConfiguration.BaseColor.r, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(WallPresentationConfiguration.BaseColor.g, Is.EqualTo(0.937255f).Within(0.0001f));
            var mesh = RuntimeMeshFactory.CreatePresentationBaseMesh(new Bounds(new Vector3(2f, 0f, 3f), new Vector3(4f, 0f, 6f)), 0.35f, 0.025f);
            try
            {
                Assert.IsNotNull(mesh);
                Assert.That(mesh.bounds.min.y, Is.EqualTo(0.325f).Within(0.0001f));
                Assert.That(mesh.bounds.max.y, Is.EqualTo(0.35f).Within(0.0001f));
            }
            finally { if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void RuntimeGlbExporterWritesArchitecturalMeshesAndExcludesNavigationNodes()
        {
            var root = new GameObject("GeneratedBuilding");
            var mesh = new Mesh { name = "TestFloor" };
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            try
            {
                mesh.vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 0, 1) };
                mesh.triangles = new[] { 0, 1, 2 }; mesh.RecalculateNormals(); mesh.RecalculateBounds();
                var arch = new GameObject("FloorMesh"); arch.transform.SetParent(root.transform, false); arch.AddComponent<MeshFilter>().sharedMesh = mesh; arch.AddComponent<MeshRenderer>().sharedMaterial = material;
                var nav = new GameObject("NavigationRuntime"); nav.transform.SetParent(root.transform, false);
                var debug = new GameObject("NetworkPath_Debug"); debug.transform.SetParent(nav.transform, false); debug.AddComponent<MeshFilter>().sharedMesh = mesh; debug.AddComponent<MeshRenderer>().sharedMaterial = material;

                Assert.IsTrue(RuntimeGlbExporter.TryExport(root, out var result, out var error), error);
                Assert.AreEqual(1, result.meshCount); Assert.AreEqual(3, result.vertexCount); Assert.AreEqual(1, result.triangleCount);
                Assert.AreEqual(0x46546c67u, BitConverter.ToUInt32(result.bytes, 0));
                Assert.AreEqual(2u, BitConverter.ToUInt32(result.bytes, 4));
                Assert.AreEqual(result.bytes.Length, BitConverter.ToUInt32(result.bytes, 8));
                var jsonLength = (int)BitConverter.ToUInt32(result.bytes, 12);
                var json = System.Text.Encoding.UTF8.GetString(result.bytes, 20, jsonLength);
                Assert.That(json.TrimEnd(' '), Does.EndWith("}"));
                Assert.That(json.TrimEnd(' ').IndexOf('\0'), Is.EqualTo(-1), "GLB JSON chunk padding must be spaces, never NUL bytes.");
                StringAssert.Contains("FloorMesh", json); StringAssert.Contains("POSITION", json); StringAssert.DoesNotContain("NetworkPath_Debug", json);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(mesh); UnityEngine.Object.DestroyImmediate(material); }
        }

        [Test]
        public void PixelAnnotationsRoundTripAndReferencesSurvive()
        {
            var map = TestMapFactory.CreateTinyThreeRoomMap(); var annotations = map.floors[0].sourceAnnotations;
            annotations.rooms.Add(new RoomAnnotationData { id = "room_pixel_1", displayName = "Room 101", vertices = new System.Collections.Generic.List<PixelPoint> { new PixelPoint(1, 2), new PixelPoint(20, 2), new PixelPoint(20, 20) } });
            annotations.walls.Add(new WallAnnotationData { id = "wall_pixel_1", start = new PixelPoint(20, 2), end = new PixelPoint(20, 20) });
            annotations.doors.Add(new DoorAnnotationData { id = "door_pixel_1", wallId = "wall_pixel_1", startT = 0.2f, endT = 0.5f });
            var loaded = MapJsonSerializer.Deserialize(MapJsonSerializer.Serialize(map)); var result = AnnotationValidator.Validate(loaded.floors[0].sourceAnnotations);
            Assert.IsTrue(result.IsValid, string.Join(" ", result.errors.ToArray())); Assert.AreEqual("room_pixel_1", loaded.floors[0].sourceAnnotations.rooms[0].id); Assert.AreEqual("wall_pixel_1", loaded.floors[0].sourceAnnotations.doors[0].wallId); Assert.AreEqual(20, loaded.floors[0].sourceAnnotations.rooms[0].vertices[1].x);
            Assert.IsTrue(AnnotationOperations.RenameRoom(loaded.floors[0].sourceAnnotations, "room_pixel_1", "Robotics Lab")); Assert.AreEqual("room_pixel_1", loaded.floors[0].sourceAnnotations.rooms[0].id); Assert.AreEqual("Robotics Lab", loaded.floors[0].sourceAnnotations.rooms[0].displayName);
        }

        [Test]
        public void AnnotationValidatorRejectsBadPolygonsWallsAndDoors()
        {
            var data = new FloorPlanAnnotationData(); data.rooms.Add(new RoomAnnotationData { id = "too-few", vertices = new System.Collections.Generic.List<PixelPoint> { new PixelPoint(0, 0), new PixelPoint(1, 1) } }); data.rooms.Add(new RoomAnnotationData { id = "zero", vertices = new System.Collections.Generic.List<PixelPoint> { new PixelPoint(0, 0), new PixelPoint(1, 1), new PixelPoint(2, 2) } }); data.rooms.Add(new RoomAnnotationData { id = "cross", vertices = new System.Collections.Generic.List<PixelPoint> { new PixelPoint(0, 0), new PixelPoint(10, 10), new PixelPoint(0, 10), new PixelPoint(10, 0) } }); data.walls.Add(new WallAnnotationData { id = "short", start = new PixelPoint(0, 0), end = new PixelPoint(1, 0) }); data.doors.Add(new DoorAnnotationData { id = "bad-door", wallId = "missing", startT = 0.8f, endT = 0.2f });
            var result = AnnotationValidator.Validate(data); Assert.IsFalse(result.IsValid); var messages = string.Join(" ", result.errors.ToArray()); StringAssert.Contains("at least 3", messages); StringAssert.Contains("zero area", messages); StringAssert.Contains("self-intersects", messages); StringAssert.Contains("too short", messages); StringAssert.Contains("missing wall", messages);
        }

        [Test]
        public void DeletingWallCascadesDoorsAndHistorySupportsUndoRedo()
        {
            var data = new FloorPlanAnnotationData(); data.walls.Add(new WallAnnotationData { id = "wall", start = new PixelPoint(0, 0), end = new PixelPoint(10, 0) }); data.doors.Add(new DoorAnnotationData { id = "door", wallId = "wall", startT = 0.2f, endT = 0.4f }); AnnotationOperations.DeleteWallAndDoors(data, "wall"); Assert.AreEqual(0, data.walls.Count); Assert.AreEqual(0, data.doors.Count);
            var history = new AnnotationHistory(); var before = new FloorPlanAnnotationData(); var after = new FloorPlanAnnotationData(); after.rooms.Add(new RoomAnnotationData { id = "room", displayName = "Room 1", vertices = new System.Collections.Generic.List<PixelPoint> { new PixelPoint(0, 0), new PixelPoint(4, 0), new PixelPoint(4, 4) } }); history.Record(before, after); var undone = history.Undo(after); Assert.AreEqual(0, undone.rooms.Count); var redone = history.Redo(undone); Assert.AreEqual("room", redone.rooms[0].id);
        }

        [Test]
        public void ScaleCalculationUsesKnownDistanceOverPixelDistance()
        {
            var result = ScaleCalibrationService.Calculate(new PixelPoint(0, 0), new PixelPoint(500, 0), 10f, 1000, 500);
            Assert.IsTrue(result.IsValid, result.Error); Assert.That(result.PixelDistance, Is.EqualTo(500f).Within(0.0001f)); Assert.That(result.MetresPerPixel, Is.EqualTo(0.02f).Within(0.000001f));
        }

        [Test]
        public void ScaleCalculationRejectsInvalidInputs()
        {
            Assert.IsFalse(ScaleCalibrationService.Calculate(new PixelPoint(1, 1), new PixelPoint(1, 1), 10, 100, 100).IsValid);
            Assert.IsFalse(ScaleCalibrationService.Calculate(new PixelPoint(1, 1), new PixelPoint(2, 1), 0, 100, 100).IsValid);
            Assert.IsFalse(ScaleCalibrationService.Calculate(new PixelPoint(-1, 1), new PixelPoint(2, 1), 1, 100, 100).IsValid);
        }

        [Test]
        public void PixelToLocalConversionPreservesConventionAndSource()
        {
            var floor = new FloorData { id = "floor_0" };
            floor.sourceAnnotations.rooms.Add(new RoomAnnotationData { id = "room", displayName = "Lab", vertices = new System.Collections.Generic.List<PixelPoint> { new PixelPoint(100, 400), new PixelPoint(300, 400), new PixelPoint(300, 200), new PixelPoint(100, 200) } });
            floor.sourceAnnotations.walls.Add(new WallAnnotationData { id = "wall", start = new PixelPoint(0, 0), end = new PixelPoint(500, 0) });
            floor.sourceAnnotations.doors.Add(new DoorAnnotationData { id = "door", wallId = "wall", startT = 0.2f, endT = 0.4f });
            var original = floor.sourceAnnotations.rooms[0].vertices[0];
            var scale = new ScaleCalibrationData { firstPixel = new PixelPoint(0, 0), secondPixel = new PixelPoint(500, 0), knownDistanceMetres = 10, metresPerPixel = 0.02f, isValid = true };
            Assert.IsTrue(PixelToLocalGeometryConverter.TryRegenerate(floor, scale, 1000, 500, new MapGenerationDefaults(), out var result), string.Join(" ", result.errors.ToArray()));
            Assert.That(floor.rooms[0].polygon.vertices[0].x, Is.EqualTo(2f).Within(0.0001f)); Assert.That(floor.rooms[0].polygon.vertices[0].z, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(floor.walls[0].start.x, Is.EqualTo(0f).Within(0.0001f)); Assert.That(floor.walls[0].start.z, Is.EqualTo(10f).Within(0.0001f));
            Assert.That(floor.doors[0].distanceAlongWallMetres, Is.EqualTo(3f).Within(0.0001f)); Assert.That(floor.doors[0].widthMetres, Is.EqualTo(2f).Within(0.0001f));
            Assert.AreEqual(original.x, floor.sourceAnnotations.rooms[0].vertices[0].x); Assert.AreEqual(original.y, floor.sourceAnnotations.rooms[0].vertices[0].y);
            var roundTrip = PixelToLocalGeometryConverter.ToPixel(floor.rooms[0].polygon.vertices[0], 0.02f, 500); Assert.That(roundTrip.x, Is.EqualTo(original.x).Within(0.0001f)); Assert.That(roundTrip.y, Is.EqualTo(original.y).Within(0.0001f));
        }

        [Test]
        public void AuthoritativeWallRasterBypassesOutOfBoundsLegacyWallGuesses()
        {
            var floor = new FloorData { id = "floor_mask" };
            floor.sourceAnnotations.rooms.Add(new RoomAnnotationData { id = "room", vertices = new System.Collections.Generic.List<PixelPoint> { new PixelPoint(10, 10), new PixelPoint(90, 10), new PixelPoint(90, 90), new PixelPoint(10, 90) } });
            floor.sourceAnnotations.wallRuns.Add(new PixelRasterRun { minX = 12, minY = 12, maxX = 20, maxY = 18 });
            floor.sourceAnnotations.wallPaths.Add(new WallPathData { id = 0, widthPixels = 2.5f, points = new System.Collections.Generic.List<PixelPoint> { new PixelPoint(12, 15), new PixelPoint(50, 15) } });
            floor.sourceAnnotations.walls.Add(new WallAnnotationData { id = "legacy-outside", start = new PixelPoint(-4, 4), end = new PixelPoint(50, 4) });
            floor.sourceAnnotations.doors.Add(new DoorAnnotationData { id = "legacy-door", wallId = "missing", startT = 0.2f, endT = 0.4f });
            var scale = new ScaleCalibrationData { isValid = true, metresPerPixel = 0.01f, pixelDistance = 100, knownDistanceMetres = 1 };
            Assert.IsTrue(PixelToLocalGeometryConverter.TryRegenerate(floor, scale, 100, 100, new MapGenerationDefaults(), out var result), string.Join(" ", result.errors.ToArray()));
            Assert.AreEqual(0, floor.walls.Count, "Raster wall mask is authoritative; invalid legacy centerlines should not block 3D conversion.");
            Assert.AreEqual(0, floor.doors.Count);
            Assert.AreEqual(1, floor.architecturalWallRuns.Count);
            Assert.AreEqual(1, floor.architecturalWallPaths.Count);
            Assert.That(floor.architecturalWallPaths[0].widthMetres,
                Is.EqualTo(WallPathGeometryConfiguration.MinimumWallThicknessMeters).Within(0.0001f),
                "The shared minimum prevents thin raster strokes from becoming 2.7 m needles.");
        }

        [Test]
        public void PixelToLocalExplicitlyLocksBottomLeftOriginConvention()
        {
            const float scale = 0.02f; const int height = 500;
            AssertLocal(PixelToLocalGeometryConverter.ToLocal(new PixelPoint(0, 500), scale, height), 0, 0);
            AssertLocal(PixelToLocalGeometryConverter.ToLocal(new PixelPoint(0, 0), scale, height), 0, 10);
            AssertLocal(PixelToLocalGeometryConverter.ToLocal(new PixelPoint(1000, 500), scale, height), 20, 0);
            AssertLocal(PixelToLocalGeometryConverter.ToLocal(new PixelPoint(1000, 0), scale, height), 20, 10);
            AssertLocal(PixelToLocalGeometryConverter.ToLocal(new PixelPoint(100, 400), scale, height), 2, 2);
        }

        [Test]
        public void RecalibrationRegeneratesDimensionsAndPreservesIdsAndNames()
        {
            var floor = new FloorData { id = "floor_0" }; floor.sourceAnnotations.rooms.Add(new RoomAnnotationData { id = "room_stable", displayName = "Named Room", vertices = new System.Collections.Generic.List<PixelPoint> { new PixelPoint(0, 0), new PixelPoint(100, 0), new PixelPoint(100, 100) } });
            var first = new ScaleCalibrationData { metresPerPixel = 0.01f, isValid = true }; var second = new ScaleCalibrationData { metresPerPixel = 0.02f, isValid = true };
            Assert.IsTrue(PixelToLocalGeometryConverter.TryRegenerate(floor, first, 200, 200, null, out _)); var firstWidth = floor.rooms[0].polygon.vertices[1].x;
            Assert.IsTrue(PixelToLocalGeometryConverter.TryRegenerate(floor, second, 200, 200, null, out _)); Assert.That(floor.rooms[0].polygon.vertices[1].x, Is.EqualTo(firstWidth * 2).Within(0.0001f)); Assert.AreEqual("room_stable", floor.rooms[0].id); Assert.AreEqual("Named Room", floor.rooms[0].displayName);
        }

        [Test]
        public void ScaleAndDerivedGeometrySurviveRepositoryRoundTrip()
        {
            var repository = new LocalFileMapRepository(testRoot); var map = MapProjectData.Create("scale-map", "Scale Map"); var floor = new FloorData { id = "floor_0" }; floor.buildingBoundary.vertices.Add(new LocalPoint(0, 0)); floor.buildingBoundary.vertices.Add(new LocalPoint(10, 0)); floor.buildingBoundary.vertices.Add(new LocalPoint(10, 10)); floor.buildingBoundary.vertices.Add(new LocalPoint(0, 10)); floor.sourceAnnotations.rooms.Add(new RoomAnnotationData { id = "room", displayName = "Room", vertices = new System.Collections.Generic.List<PixelPoint> { new PixelPoint(0, 0), new PixelPoint(100, 0), new PixelPoint(100, 100) } }); map.floors.Add(floor); repository.CreateProject(map);
            map.scaleCalibration = new ScaleCalibrationData { firstPixel = new PixelPoint(0, 0), secondPixel = new PixelPoint(100, 0), knownDistanceMetres = 2, pixelDistance = 100, metresPerPixel = 0.02f, isValid = true }; Assert.IsTrue(PixelToLocalGeometryConverter.TryRegenerate(floor, map.scaleCalibration, 200, 200, null, out _)); repository.SaveProject(map);
            var loaded = repository.LoadProject(map.projectId); Assert.IsTrue(loaded.scaleCalibration.isValid); Assert.That(loaded.scaleCalibration.metresPerPixel, Is.EqualTo(0.02f).Within(0.000001f)); Assert.AreEqual("room", loaded.floors[0].rooms[0].id); Assert.That(loaded.floors[0].rooms[0].polygon.vertices[1].x, Is.EqualTo(2f).Within(0.0001f));
        }

        [Test]
        public void OldProjectWithoutScaleCalibrationStillLoads()
        {
            var loaded = MapJsonSerializer.Deserialize("{\"schemaVersion\":1,\"projectId\":\"old\",\"floors\":[{\"id\":\"floor_0\",\"sourceAnnotations\":{\"rooms\":[],\"walls\":[],\"doors\":[]}}]}");
            Assert.IsNotNull(loaded); Assert.IsNotNull(loaded.scaleCalibration); Assert.IsFalse(loaded.scaleCalibration.isValid); Assert.IsNotNull(loaded.floors[0].sourceAnnotations);
        }

        [Test]
        public void Wgs84ProjectionReferenceAndSmallOffsetsRoundTrip()
        {
            var reference = new GeoCoordinate(45.5, -73.57); var origin = GeoProjectionService.ToEastNorth(reference, reference); Assert.That(origin.east, Is.EqualTo(0).Within(0.000001)); Assert.That(origin.north, Is.EqualTo(0).Within(0.000001));
            var offset = new EastNorthPoint(12.5, 23.25); var coordinate = GeoProjectionService.ToGeoCoordinate(offset, reference); var roundTrip = GeoProjectionService.ToEastNorth(coordinate, reference); Assert.That(roundTrip.east, Is.EqualTo(offset.east).Within(0.000001)); Assert.That(roundTrip.north, Is.EqualTo(offset.north).Within(0.000001)); Assert.IsTrue(GeoProjectionService.IsValid(coordinate));
            Assert.IsFalse(GeoProjectionService.IsValid(new GeoCoordinate(91, 0))); Assert.IsFalse(GeoProjectionService.IsValid(new GeoCoordinate(0, 181)));
        }

        [Test]
        public void HeadingConventionMapsLocalAxesToEastNorth()
        {
            var zeroX = GeoCalibrationService.Transform(new EastNorthPoint(10, 0), 0); var zeroZ = GeoCalibrationService.Transform(new EastNorthPoint(0, 10), 0); Assert.That(zeroX.east, Is.EqualTo(10).Within(0.000001)); Assert.That(zeroX.north, Is.EqualTo(0).Within(0.000001)); Assert.That(zeroZ.east, Is.EqualTo(0).Within(0.000001)); Assert.That(zeroZ.north, Is.EqualTo(10).Within(0.000001));
            var ninetyZ = GeoCalibrationService.Transform(new EastNorthPoint(0, 10), 90); var ninetyX = GeoCalibrationService.Transform(new EastNorthPoint(10, 0), 90); Assert.That(ninetyZ.east, Is.EqualTo(10).Within(0.000001)); Assert.That(ninetyZ.north, Is.EqualTo(0).Within(0.000001)); Assert.That(ninetyX.east, Is.EqualTo(0).Within(0.000001)); Assert.That(ninetyX.north, Is.EqualTo(-10).Within(0.000001));
        }

        [Test]
        public void OneAnchorHeadingTransformsAndInvertsLocalPoint()
        {
            var reference = new GeoCoordinate(45, -73); var anchor = new GeoAnchorData { id = "a", localPosition = new LocalPoint(4, 6), coordinate = reference }; var solved = GeoCalibrationService.Solve(new[] { anchor }, GeoCalibrationMethod.OneAnchorHeading, 32); Assert.IsTrue(solved.IsValid, solved.Error);
            var data = ToData(solved); var transformer = new GeoCoordinateTransformer(data); var point = new LocalPoint(12, 8); var geo = transformer.LocalToGeo(point); var back = transformer.GeoToLocal(geo); Assert.That(back.x, Is.EqualTo(point.x).Within(0.00001)); Assert.That(back.z, Is.EqualTo(point.z).Within(0.00001));
        }

        [Test]
        public void TwoAnchorSolverRecoversHeadingAndTranslation()
        {
            var reference = new GeoCoordinate(45, -73); var anchors = SyntheticAnchors(reference, 30, 5, 7, new LocalPoint(0, 0), new LocalPoint(10, 0)); var solved = GeoCalibrationService.Solve(anchors, GeoCalibrationMethod.TwoAnchors); Assert.IsTrue(solved.IsValid, solved.Error); Assert.That(solved.HeadingDegrees, Is.EqualTo(30).Within(0.0001)); Assert.That(solved.TranslationEastMetres, Is.EqualTo(0).Within(0.0001)); Assert.That(solved.TranslationNorthMetres, Is.EqualTo(0).Within(0.0001)); Assert.That(solved.RmsErrorMetres, Is.LessThan(0.00001));
        }

        [Test]
        public void TwoAnchorScaleMismatchIsDiagnosticOnly()
        {
            var reference = new GeoCoordinate(45, -73); var a = new GeoAnchorData { id = "a", localPosition = new LocalPoint(0, 0), coordinate = reference }; var bGeo = GeoProjectionService.ToGeoCoordinate(new EastNorthPoint(22, 0), reference); var b = new GeoAnchorData { id = "b", localPosition = new LocalPoint(20, 0), coordinate = bGeo }; var solved = GeoCalibrationService.Solve(new[] { a, b }, GeoCalibrationMethod.TwoAnchors); Assert.IsTrue(solved.IsValid); Assert.That(solved.ImpliedScaleRatio, Is.EqualTo(1.1).Within(0.0001)); Assert.That(solved.ScaleMismatchPercent, Is.EqualTo(10).Within(0.01));
        }

        [Test]
        public void MultiAnchorRigidFitReportsResidualsAndPreservesFixedScale()
        {
            var reference = new GeoCoordinate(45, -73); var anchors = SyntheticAnchors(reference, 30, 5, 7, new LocalPoint(0, 0), new LocalPoint(10, 0), new LocalPoint(10, 8), new LocalPoint(0, 8)); var solved = GeoCalibrationService.Solve(anchors, GeoCalibrationMethod.MultiAnchorRigid); Assert.IsTrue(solved.IsValid, solved.Error); Assert.That(solved.HeadingDegrees, Is.EqualTo(30).Within(0.0001)); Assert.That(solved.TranslationEastMetres, Is.EqualTo(0).Within(0.0001)); Assert.That(solved.TranslationNorthMetres, Is.EqualTo(0).Within(0.0001)); Assert.AreEqual(4, solved.Residuals.Count); Assert.That(solved.ImpliedScaleRatio, Is.EqualTo(1).Within(0.0001));
        }

        [Test]
        public void GeoPredictionsCoverRoomWallDoorAndBoundary()
        {
            var reference = new GeoCoordinate(45, -73); var anchor = new GeoAnchorData { id = "a", localPosition = new LocalPoint(0, 0), coordinate = reference }; var data = ToData(GeoCalibrationService.Solve(new[] { anchor }, GeoCalibrationMethod.OneAnchorHeading, 0)); var floor = new FloorData { id = "floor" }; floor.buildingBoundary.vertices.Add(new LocalPoint(0, 0)); floor.buildingBoundary.vertices.Add(new LocalPoint(10, 0)); floor.buildingBoundary.vertices.Add(new LocalPoint(10, 10)); floor.sourceAnnotations = new FloorPlanAnnotationData(); floor.rooms.Add(new RoomData { id = "room", displayName = "Room" }); floor.rooms[0].polygon.vertices.Add(new LocalPoint(0, 0)); floor.rooms[0].polygon.vertices.Add(new LocalPoint(4, 0)); floor.rooms[0].polygon.vertices.Add(new LocalPoint(4, 4)); floor.walls.Add(new WallData { id = "wall", start = new LocalPoint(0, 0), end = new LocalPoint(0, 4) }); floor.doors.Add(new DoorData { id = "door", parentWallId = "wall", distanceAlongWallMetres = 1, widthMetres = 1 }); var predictions = GeoPredictionService.PredictFloor(floor, new GeoCoordinateTransformer(data)); Assert.IsTrue(predictions.Exists(p => p.entityId == "room" && p.pointId == "vertex_1")); Assert.IsTrue(predictions.Exists(p => p.entityId == "wall" && p.pointId == "end")); Assert.IsTrue(predictions.Exists(p => p.entityId == "door" && p.pointId == "centre")); Assert.IsTrue(predictions.Exists(p => p.entityId == "boundary"));
        }

        [Test]
        public void GeographicAnchorsAndTransformSurviveJsonRoundTrip()
        {
            var map = MapProjectData.Create("geo-map", "Geo Map"); var floor = new FloorData { id = "floor_0" }; floor.buildingBoundary.vertices.Add(new LocalPoint(0, 0)); floor.buildingBoundary.vertices.Add(new LocalPoint(10, 0)); floor.buildingBoundary.vertices.Add(new LocalPoint(10, 10)); map.floors.Add(floor); var coordinate = new GeoCoordinate(45.5, -73.5); map.geographicCalibration.anchors.Add(new GeoAnchorData { id = "anchor", localPosition = new LocalPoint(2, 3), sourcePixel = new PixelPoint(20, 30), hasSourcePixel = true, coordinate = coordinate, label = "Corner" }); var solved = GeoCalibrationService.Solve(map.geographicCalibration.anchors, GeoCalibrationMethod.OneAnchorHeading, 32); GeoCalibrationService.ApplyTo(map.geographicCalibration, solved); var loaded = MapJsonSerializer.Deserialize(MapJsonSerializer.Serialize(map)); Assert.IsTrue(loaded.geographicCalibration.isValid); Assert.AreEqual("OneAnchorHeading", loaded.geographicCalibration.method); Assert.That(loaded.geographicCalibration.mapHeadingDegrees, Is.EqualTo(32).Within(0.000001)); Assert.AreEqual("anchor", loaded.geographicCalibration.anchors[0].id); Assert.IsTrue(loaded.geographicCalibration.anchors[0].hasSourcePixel); var before = new GeoCoordinateTransformer(map.geographicCalibration).LocalToGeo(new LocalPoint(8, 6)); var after = new GeoCoordinateTransformer(loaded.geographicCalibration).LocalToGeo(new LocalPoint(8, 6)); Assert.That(after.latitude, Is.EqualTo(before.latitude).Within(0.000000001)); Assert.That(after.longitude, Is.EqualTo(before.longitude).Within(0.000000001));
        }

        [Test]
        public void MultiAnchorOutlierGetsLargerResidualAndDegradesQuality()
        {
            var reference = new GeoCoordinate(45, -73); var anchors = SyntheticAnchors(reference, 20, 3, 4, new LocalPoint(0, 0), new LocalPoint(10, 0), new LocalPoint(10, 8), new LocalPoint(0, 8)); var bad = GeoProjectionService.ToEastNorth(anchors[3].coordinate, reference); anchors[3].coordinate = GeoProjectionService.ToGeoCoordinate(new EastNorthPoint(bad.east + 5, bad.north), reference); var solved = GeoCalibrationService.Solve(anchors, GeoCalibrationMethod.MultiAnchorRigid); Assert.IsTrue(solved.IsValid); Assert.That(solved.Residuals["anchor_3"], Is.GreaterThan(solved.Residuals["anchor_0"])); Assert.That(solved.RmsErrorMetres, Is.GreaterThan(0.5)); Assert.That(solved.Quality, Is.Not.EqualTo(GeoCalibrationQuality.Excellent));
        }

        [Test]
        public void LocationQualityAndStalenessAreCentralized()
        {
            var settings = new LocationQualitySettings(); Assert.AreEqual(LocationQuality.Excellent, LocationQualityEvaluator.Evaluate(2, settings)); Assert.AreEqual(LocationQuality.Good, LocationQualityEvaluator.Evaluate(5, settings)); Assert.AreEqual(LocationQuality.Fair, LocationQualityEvaluator.Evaluate(10, settings)); Assert.AreEqual(LocationQuality.Poor, LocationQualityEvaluator.Evaluate(20, settings)); Assert.AreEqual(LocationQuality.Unusable, LocationQualityEvaluator.Evaluate(40, settings)); var reading = new LocationReading { timestampUnixSeconds = 100, receivedUnixSeconds = 100, horizontalAccuracyMetres = 2, isValid = true }; Assert.IsTrue(LocationQualityEvaluator.IsStale(reading, 120, settings, out var age)); Assert.AreEqual(20, age, 0.001);
        }

        [Test]
        public void RegionLocatorSupportsConcavePolygonsAndBoundaryDistance()
        {
            var polygon = new LocalPolygon(); polygon.vertices.Add(new LocalPoint(0, 0)); polygon.vertices.Add(new LocalPoint(10, 0)); polygon.vertices.Add(new LocalPoint(10, 10)); polygon.vertices.Add(new LocalPoint(5, 5)); polygon.vertices.Add(new LocalPoint(0, 10)); Assert.IsTrue(RegionLocator.Contains(new LocalPoint(2, 2), polygon)); Assert.IsFalse(RegionLocator.Contains(new LocalPoint(5, 8), polygon)); Assert.That(RegionLocator.DistanceToBoundary(new LocalPoint(5, 2), polygon), Is.EqualTo(2).Within(0.0001)); Assert.IsTrue(RegionLocator.Contains(new LocalPoint(0, 5), polygon));
        }

        [Test]
        public void UserMapPositionUsesExistingGeoInverseAndUncertainty()
        {
            var project = MapProjectData.Create("position", "Position"); var floor = new FloorData { id = "floor" }; floor.buildingBoundary.vertices.Add(new LocalPoint(0, 0)); floor.buildingBoundary.vertices.Add(new LocalPoint(20, 0)); floor.buildingBoundary.vertices.Add(new LocalPoint(20, 10)); floor.buildingBoundary.vertices.Add(new LocalPoint(0, 10)); var room = new RoomData { id = "room_a", displayName = "Room A" }; room.polygon.vertices.Add(new LocalPoint(0, 0)); room.polygon.vertices.Add(new LocalPoint(10, 0)); room.polygon.vertices.Add(new LocalPoint(10, 10)); room.polygon.vertices.Add(new LocalPoint(0, 10)); floor.rooms.Add(room); project.floors.Add(floor); project.geographicCalibration = new GeoCalibrationData { isValid = true, geographicOrigin = new GeoCoordinate(45, -73), mapHeadingDegrees = 0, translationEastMetres = 0, translationNorthMetres = 0 }; var transformer = new GeoCoordinateTransformer(project.geographicCalibration); var service = new UserMapPositionService(); var reading = GeoReading(transformer, new LocalPoint(5, 5), 2); var inside = service.Evaluate(project, reading, reading.timestampUnixSeconds); Assert.AreEqual(BuildingPositionStatus.Inside, inside.buildingStatus); Assert.AreEqual(RoomPositionStatus.InsideRoom, inside.roomStatus); var near = GeoReading(transformer, new LocalPoint(19, 5), 2); var uncertain = service.Evaluate(project, near, near.timestampUnixSeconds); Assert.AreEqual(BuildingPositionStatus.Uncertain, uncertain.buildingStatus);
        }

        [Test]
        public void UserMapPositionRejectsStaleAndInvalidCalibration()
        {
            var project = MapProjectData.Create("position", "Position"); project.floors.Add(new FloorData { id = "floor" }); var reading = new LocationReading { latitude = 45, longitude = -73, altitudeMetres = 2, horizontalAccuracyMetres = 2, verticalAccuracyMetres = 3, timestampUnixSeconds = 1, receivedUnixSeconds = 1, isValid = true }; var service = new UserMapPositionService(); var stale = service.Evaluate(project, reading, 100); Assert.IsFalse(stale.hasValidLocation); Assert.That(stale.diagnostic, Does.Contain("Stale")); reading.timestampUnixSeconds = 100; reading.receivedUnixSeconds = 100; var invalid = service.Evaluate(project, reading, 100); Assert.IsFalse(invalid.hasValidLocation); Assert.That(invalid.diagnostic, Does.Contain("geographic calibration"));
        }

        [Test]
        public void MockLocationProviderPublishesInjectedReading()
        {
            var objectUnderTest = new GameObject("MockLocationProviderTest"); var provider = objectUnderTest.AddComponent<MockLocationProvider>(); var receivedCount = 0; LocationReading received = null; provider.ReadingReceived += value => { received = value; receivedCount++; }; provider.StartProvider(); var reading = new LocationReading { latitude = 1, longitude = 2, altitudeMetres = 3, horizontalAccuracyMetres = 4, verticalAccuracyMetres = 5, timestampUnixSeconds = 10, isValid = true }; provider.Inject(reading); Assert.AreSame(reading, received); Assert.AreEqual(3, provider.LastReading.altitudeMetres); var next = new LocationReading { latitude = 1.1, longitude = 2.1, altitudeMetres = 3.2, horizontalAccuracyMetres = 4, verticalAccuracyMetres = 5, timestampUnixSeconds = 11, isValid = true }; provider.Inject(next); Assert.AreEqual(2, receivedCount); Assert.AreSame(next, received); UnityEngine.Object.DestroyImmediate(objectUnderTest);
        }

        [Test]
        public void RouteWaypointSamplerPreservesTurnsEndpointsAndSpacing()
        {
            var corners = new[] { new LocalPoint(0, 0), new LocalPoint(5, 0), new LocalPoint(5, 4) }; var points = RouteWaypointSampler.Sample(corners, 2); Assert.AreEqual(new LocalPoint(0, 0).x, points[0].x); Assert.IsTrue(points.Exists(p => Math.Abs(p.x - 5) < 0.0001 && Math.Abs(p.z) < 0.0001)); Assert.That(points[points.Count - 1].z, Is.EqualTo(4).Within(0.0001)); Assert.That(RouteWaypointSampler.TotalDistance(corners), Is.EqualTo(9).Within(0.0001));
        }

        [Test]
        public void RouteWaypointSamplerHandlesZeroLengthSegmentsAndPolylineDistance()
        {
            var points = RouteWaypointSampler.Sample(new[] { new LocalPoint(1, 1), new LocalPoint(1, 1), new LocalPoint(4, 1) }, 1); Assert.AreEqual(4, points.Count); Assert.That(RouteWaypointSampler.DistanceToPolyline(new LocalPoint(2, 3), points), Is.EqualTo(2).Within(0.0001));
        }

        [Test]
        public void RouteGeometryValidatorRejectsSolidWallCrossing()
        {
            var floor = new FloorData(); floor.walls.Add(new WallData { id = "w", start = new LocalPoint(2, 0), end = new LocalPoint(2, 4) });
            Assert.IsTrue(RouteGeometryValidator.CrossesSolidWall(new[] { new LocalPoint(0, 2), new LocalPoint(4, 2) }, floor, out _));
        }

        [Test]
        public void RouteGeometryValidatorAllowsDoorOpening()
        {
            var floor = new FloorData(); floor.walls.Add(new WallData { id = "w", start = new LocalPoint(2, 0), end = new LocalPoint(2, 4) }); floor.doors.Add(new DoorData { id = "d", parentWallId = "w", distanceAlongWallMetres = 1.5f, widthMetres = 1 });
            Assert.IsFalse(RouteGeometryValidator.CrossesSolidWall(new[] { new LocalPoint(0, 2), new LocalPoint(4, 2) }, floor, out _));
        }

        [Test]
        public void RouteGeometryValidatorRejectsVoidPoint()
        {
            var floor = new FloorData(); var room = new RoomData { id = "void", walkable = false }; room.polygon.vertices.AddRange(new[] { new LocalPoint(0, 0), new LocalPoint(4, 0), new LocalPoint(4, 4), new LocalPoint(0, 4) }); floor.rooms.Add(room);
            Assert.IsTrue(RouteGeometryValidator.EntersNonWalkableRegion(new[] { new LocalPoint(2, 2) }, floor, out _));
        }

        [Test]
        public void RouteGeometryValidatorRejectsSegmentThroughVoid()
        {
            var floor = new FloorData(); var room = new RoomData { id = "void", walkable = false }; room.polygon.vertices.AddRange(new[] { new LocalPoint(1, 1), new LocalPoint(3, 1), new LocalPoint(3, 3), new LocalPoint(1, 3) }); floor.rooms.Add(room);
            Assert.IsTrue(RouteGeometryValidator.EntersNonWalkableRegion(new[] { new LocalPoint(0, 2), new LocalPoint(4, 2) }, floor, out _));
        }

        [Test]
        public void RouteGeometryValidatorIgnoresEnclosingBackground()
        {
            var floor = new FloorData(); var background = new RoomData { id = "background", walkable = false }; background.polygon.vertices.AddRange(new[] { new LocalPoint(-10, -10), new LocalPoint(10, -10), new LocalPoint(10, 10), new LocalPoint(-10, 10) }); floor.rooms.Add(background);
            for (var i = 0; i < 4; i++) { var room = new RoomData { id = "r" + i }; var x = i * 2f; room.polygon.vertices.AddRange(new[] { new LocalPoint(x, 0), new LocalPoint(x + 1, 0), new LocalPoint(x + 1, 1), new LocalPoint(x, 1) }); floor.rooms.Add(room); }
            Assert.IsFalse(RouteGeometryValidator.EntersNonWalkableRegion(new[] { new LocalPoint(0, .5f), new LocalPoint(1, .5f) }, floor, out _));
        }

        [Test]
        public void RoomNavigationPointServiceFindsInteriorPointForConcaveRoom()
        {
            var room = new RoomData { id = "concave" }; room.polygon.vertices.Add(new LocalPoint(0, 0)); room.polygon.vertices.Add(new LocalPoint(10, 0)); room.polygon.vertices.Add(new LocalPoint(10, 10)); room.polygon.vertices.Add(new LocalPoint(5, 5)); room.polygon.vertices.Add(new LocalPoint(0, 10)); Assert.IsTrue(RoomNavigationPointService.TryFindInteriorPoint(room, out var point, out var error), error); Assert.IsTrue(RegionLocator.Contains(point, room.polygon));
        }

        [Test]
        public void RoomWaypointCandidatePrefersAnInteriorCentroidIndependentOfNavMesh()
        {
            var room = new RoomData { id = "room" };
            room.polygon.vertices.AddRange(new[] { new LocalPoint(0, 0), new LocalPoint(8, 0), new LocalPoint(8, 4), new LocalPoint(0, 4) });
            Assert.IsTrue(RoomNavigationPointService.TryFindInteriorPoint(room, out var point, out var error), error);
            Assert.IsTrue(RegionLocator.Contains(point, room.polygon));
            Assert.AreEqual(4f, point.x, .001f);
            Assert.AreEqual(2f, point.z, .001f);
        }

        [Test]
        public void CameraViewStateComposesAngleAndZoomWithoutResettingEitherValue()
        {
            var state = new CameraViewState(0, 1); state.SetZoom(1.2f); state.SetAngle(45); Assert.AreEqual(1.2f, state.zoom, .0001f); Assert.AreEqual(45, state.angle, .0001f);
            state.SetZoom(1.7f); Assert.AreEqual(45, state.angle, .0001f); state.SetAngle(70); Assert.AreEqual(1.7f, state.zoom, .0001f);
        }

        [Test]
        public void CameraTopViewFitDistanceUsesFullMapBoundsAndComposedPivot()
        {
            var bounds = new Bounds(new Vector3(12, 0, -7), new Vector3(20, 2, 10));
            var distance = CameraFramingUtility.CalculateFitDistance(bounds, 60, 16f / 9f);
            Assert.Greater(distance, bounds.extents.magnitude);
            var state = new CameraViewState(.5f, .5f); state.SetPivot(bounds.center); state.SetAngle(0);
            Assert.AreEqual(bounds.center, state.pivot);
            Assert.AreEqual(.5f, state.zoom, .0001f);
        }

        [Test]
        public void RoomDisplayCandidateIsInsidePolygonAndOutsideWallFootprints()
        {
            var room = new RoomData { id = "concave" };
            room.polygon.vertices.AddRange(new[] { new LocalPoint(0, 0), new LocalPoint(8, 0), new LocalPoint(8, 8), new LocalPoint(4, 4), new LocalPoint(0, 8) });
            var floor = new FloorData(); floor.rooms.Add(room);
            floor.walls.Add(new WallData { id = "edge", start = new LocalPoint(0, 0), end = new LocalPoint(8, 0), thicknessMetres = .2f });
            Assert.IsTrue(RoomNavigationPointService.TryFindWalkableInteriorPoint(room, floor, .1f, out var point, out var error), error);
            Assert.IsTrue(RegionLocator.Contains(point, room.polygon));
            Assert.IsTrue(RouteGeometryValidator.IsPointClearOfSolidWalls(point, floor, 0, out _));
        }

        [Test]
        public void WallFootprintClearanceMeasuresFiniteRectangleAndNotInfiniteCenterline()
        {
            var floor = new FloorData(); floor.walls.Add(new WallData { id = "finite", start = new LocalPoint(0, 0), end = new LocalPoint(2, 0), thicknessMetres = .2f });
            Assert.AreEqual(0f, RouteGeometryValidator.DistanceToSolidWallFootprints(new LocalPoint(1, 0), floor), .0001f);
            Assert.AreEqual((float)Math.Sqrt(.05f), RouteGeometryValidator.DistanceToSolidWallFootprints(new LocalPoint(-.2f, .2f), floor), .0001f);
        }

        [Test]
        public void RoomCandidatesStayInsideConcaveRoomAndWallFootprintUsesThickness()
        {
            var room = new RoomData { id = "concave" }; room.polygon.vertices.AddRange(new[] { new LocalPoint(0, 0), new LocalPoint(10, 0), new LocalPoint(10, 10), new LocalPoint(5, 5), new LocalPoint(0, 10) });
            var candidates = RoomNavigationPointService.GetInteriorCandidates(room); Assert.IsTrue(candidates.Count >= 80); foreach (var candidate in candidates) if (RegionLocator.Contains(candidate, room.polygon)) Assert.IsTrue(RegionLocator.Contains(candidate, room.polygon));
            var wall = new WallData { id = "thick", start = new LocalPoint(5, 0), end = new LocalPoint(5, 10), thicknessMetres = 1f }; Assert.IsTrue(RouteGeometryValidator.SegmentIntersectsWallFootprint(new LocalPoint(4.6f, 2), new LocalPoint(5.4f, 2), wall, out _, out _));
        }

        [Test]
        public void WaypointProgressUsesHorizontalDistanceAndReachesDestination()
        {
            var route = new NavigationRoute { valid = true }; route.waypoints.Add(new LocalPoint(0, 0)); route.waypoints.Add(new LocalPoint(2, 0)); var tracker = new WaypointProgressTracker(); tracker.Reset(route); Assert.IsTrue(tracker.Update(new LocalPoint(0.5f, 0), route, 1, true, 1)); Assert.AreEqual(1, tracker.CurrentIndex); Assert.IsTrue(tracker.Update(new LocalPoint(2, 0), route, 1, true, 1)); Assert.IsTrue(tracker.DestinationReached);
        }

        [Test]
        public void WaypointProgressRejectsPoorAccuracyUnlessManual()
        {
            var route = new NavigationRoute { valid = true }; route.waypoints.Add(new LocalPoint(0, 0)); var tracker = new WaypointProgressTracker(); tracker.Reset(route); Assert.IsFalse(tracker.Update(new LocalPoint(0, 0), route, 10, false, 1)); Assert.IsTrue(tracker.Update(new LocalPoint(0, 0), route, 10, true, 1));
        }

        [Test]
        public void RuntimeGeneratedBuildingBuildsAndClearsNavMesh()
        {
            var buildingObject = new GameObject("GenerationSmokeTest"); var generator = buildingObject.AddComponent<ProceduralBuildingGenerator>(); var navObject = new GameObject("NavMeshSmokeTest"); var navBuilder = navObject.AddComponent<RuntimeNavMeshBuilder>();
            try
            {
                Assert.IsTrue(generator.Generate(TestMapFactory.CreateTinyThreeRoomMap(), out var generationError), generationError); Assert.AreEqual(1f, generator.GeneratedRoot.transform.localScale.z, .0001f); Assert.AreEqual(generator.GeneratedBounds.center.z, generator.GeneratedRoot.transform.TransformPoint(new Vector3(0, 0, generator.GeneratedBounds.center.z)).z, .0001f); Assert.IsTrue(navBuilder.Build(generator.GeneratedRoot, generator.GeneratedBounds, out var navError), navError); Assert.AreEqual(RuntimeNavMeshState.Ready, navBuilder.State); navBuilder.Clear(); Assert.AreEqual(RuntimeNavMeshState.NotBuilt, navBuilder.State); generator.ClearGeneratedBuilding(); Assert.IsNull(generator.GeneratedRoot);
            }
            finally { UnityEngine.Object.DestroyImmediate(navObject); UnityEngine.Object.DestroyImmediate(buildingObject); }
        }

        [Test]
        public void RuntimeNavMeshRouteUsesGeneratedWalkableGeometry()
        {
            var buildingObject = new GameObject("RouteSmokeBuilding"); var generator = buildingObject.AddComponent<ProceduralBuildingGenerator>(); var navObject = new GameObject("RouteSmokeNavMesh"); var navBuilder = navObject.AddComponent<RuntimeNavMeshBuilder>();
            try
            {
                var map = TestMapFactory.CreateTinyThreeRoomMap(); Assert.IsTrue(generator.Generate(map, out var generationError), generationError); Assert.IsTrue(navBuilder.Build(generator.GeneratedRoot, generator.GeneratedBounds, out var navError), navError); Assert.IsTrue(NavigationRouteService.TryCalculate(new LocalPoint(2, 2), map.floors[0], "room_b", navBuilder.Settings, out var route, out var routeError), routeError); Assert.IsTrue(route.valid); Assert.GreaterOrEqual(route.rawCorners.Count, 2); Assert.Greater(route.totalDistanceMetres, 5);
            }
            finally { UnityEngine.Object.DestroyImmediate(navObject); UnityEngine.Object.DestroyImmediate(buildingObject); }
        }

        [Test]
        public void MapSessionKeepsExplicitlyOpenedProjectIdentity()
        {
            var session = new MapSession(); var projectA = MapProjectData.Create("project-a", "A"); var projectB = MapProjectData.Create("project-b", "B"); session.Open(projectA); Assert.AreEqual("project-a", session.CurrentProjectId); session.Open(projectB); Assert.AreEqual("project-b", session.CurrentProjectId); Assert.AreSame(projectB, session.ActiveProject);
        }

        [Test]
        public void PolygonTriangulatorSupportsConcavePolygonAndBothWindings()
        {
            var polygon = new[] { new LocalPoint(0, 0), new LocalPoint(4, 0), new LocalPoint(4, 4), new LocalPoint(2, 2), new LocalPoint(0, 4) };
            Assert.IsTrue(PolygonTriangulator.TryTriangulate(polygon, out var ccw, out var error), error); Assert.AreEqual(9, ccw.Count);
            Array.Reverse(polygon); Assert.IsTrue(PolygonTriangulator.TryTriangulate(polygon, out var cw, out var reverseError), reverseError); Assert.AreEqual(9, cw.Count);
        }

        [Test]
        public void PolygonTriangulatorRejectsDegenerateAndSelfIntersectingPolygons()
        {
            Assert.IsFalse(PolygonTriangulator.TryTriangulate(new[] { new LocalPoint(0, 0), new LocalPoint(1, 0), new LocalPoint(2, 0) }, out _, out _));
            Assert.IsFalse(PolygonTriangulator.TryTriangulate(new[] { new LocalPoint(0, 0), new LocalPoint(4, 4), new LocalPoint(0, 4), new LocalPoint(4, 0) }, out _, out _));
        }

        [Test]
        public void PolygonTriangulatorRejectsSelfTouchingVerticesAndExplainsFailure()
        {
            var touching = new[] { new LocalPoint(0, 0), new LocalPoint(3, 0), new LocalPoint(3, 3), new LocalPoint(1.5f, 0), new LocalPoint(0, 3) };
            Assert.IsFalse(PolygonTriangulator.IsSimple(touching));
            var result = PolygonTriangulator.Triangulate(touching);
            Assert.IsFalse(result.success); Assert.AreEqual(TriangulationFailureReason.TouchingNonAdjacentEdges, result.failureReason);
            Assert.GreaterOrEqual(result.failingEdgeA, 0); Assert.Greater(result.failingEdgeB, result.failingEdgeA);
        }

        [Test]
        public void PolygonTriangulatorSafelyRemovesCollinearBoundaryVertices()
        {
            var polygon = new[] { new LocalPoint(0, 0), new LocalPoint(1, 0), new LocalPoint(2, 0), new LocalPoint(2, 2), new LocalPoint(0, 2) };
            Assert.IsTrue(PolygonTriangulator.TryTriangulate(polygon, out var triangles, out var error), error);
            Assert.AreEqual(6, triangles.Count);
        }

        [Test]
        public void InvalidRoomWithRasterRunsUsesRasterFallbackAndRemainsInPlan()
        {
            var map = TestMapFactory.CreateTinyThreeRoomMap(); var bad = new RoomData { id = "raster-room" };
            bad.polygon.vertices.AddRange(new[] { new LocalPoint(20, 20), new LocalPoint(24, 24), new LocalPoint(20, 24), new LocalPoint(24, 20) });
            bad.rasterRuns.Add(new LocalRasterRun { minX = 20, minZ = 20, maxX = 22, maxZ = 21 }); bad.rasterRuns.Add(new LocalRasterRun { minX = 20, minZ = 21, maxX = 21, maxZ = 23 });
            map.floors[0].rooms.Add(bad); var plan = BuildingGenerationPlanner.Build(map);
            Assert.IsTrue(plan.IsValid, string.Join(" ", plan.errors.ToArray()));
            var fallback = plan.floors[0].rooms.Find(r => r.source.id == "raster-room"); Assert.NotNull(fallback); Assert.IsTrue(fallback.rasterFallback);
            Assert.IsTrue(RoomNavigationPointService.Contains(bad, new LocalPoint(20.5f, 22f)));
            Assert.IsFalse(RoomNavigationPointService.Contains(bad, new LocalPoint(23.5f, 23.5f)));
            var mesh = RuntimeMeshFactory.CreateFloorMesh(fallback, 0, 1);
            Assert.AreEqual(8, mesh.vertexCount); Assert.That(mesh.bounds.max.x, Is.EqualTo(22).Within(.001f)); Assert.That(mesh.bounds.max.z, Is.EqualTo(23).Within(.001f));
            UnityEngine.Object.DestroyImmediate(mesh);
        }

        [Test]
        public void WallOpeningPlannerCreatesSidesHeaderAndMultipleDoors()
        {
            var wall = new WallData { id = "wall", start = new LocalPoint(0, 0), end = new LocalPoint(10, 0), heightMetres = 3, thicknessMetres = 0.2f };
            var doors = new[] { new DoorData { id = "door_a", parentWallId = "wall", distanceAlongWallMetres = 3, widthMetres = 2 }, new DoorData { id = "door_b", parentWallId = "wall", distanceAlongWallMetres = 8, widthMetres = 1 } };
            Assert.IsTrue(WallOpeningPlanner.TryPlan(wall, doors, new MapGenerationDefaults(), out var plan, out var error), error); Assert.AreEqual(5, plan.pieces.Count); Assert.AreEqual(2, plan.doors.Count); Assert.AreEqual(WallPieceType.Header, plan.pieces[1].type); Assert.That(plan.doors[0].widthMetres, Is.EqualTo(2).Within(0.0001));
        }

        [Test]
        public void WallOpeningPlannerRejectsOverlappingDoors()
        {
            var wall = new WallData { id = "wall", start = new LocalPoint(0, 0), end = new LocalPoint(10, 0), heightMetres = 3, thicknessMetres = 0.2f };
            var doors = new[] { new DoorData { id = "a", parentWallId = "wall", distanceAlongWallMetres = 4, widthMetres = 4 }, new DoorData { id = "b", parentWallId = "wall", distanceAlongWallMetres = 5, widthMetres = 4 } };
            Assert.IsFalse(WallOpeningPlanner.TryPlan(wall, doors, new MapGenerationDefaults(), out _, out var error)); StringAssert.Contains("overlap", error);
        }

        [Test]
        public void BuildingGenerationPlanPreservesRoomsWallsDoorsAndElevation()
        {
            var map = TestMapFactory.CreateTinyThreeRoomMap(); var plan = BuildingGenerationPlanner.Build(map); Assert.IsTrue(plan.IsValid, string.Join(" ", plan.errors.ToArray())); Assert.AreEqual(1, plan.floors.Count); Assert.AreEqual(3, plan.floors[0].rooms.Count); Assert.AreEqual(2, plan.floors[0].walls.Count); Assert.AreEqual("room_a", plan.floors[0].rooms[0].source.id); Assert.AreEqual("wall_a_corridor", plan.floors[0].walls[0].wallId); Assert.AreEqual(3, plan.floors[0].walls[0].pieces.Count);
        }

        [Test]
        public void BuildingGenerationPlanExcludesNonWalkableFloorRegions()
        {
            var map = TestMapFactory.CreateTinyThreeRoomMap();
            var voidRoom = new RoomData { id = "void-region", walkable = false, destinationSelectable = false };
            voidRoom.polygon.vertices.AddRange(new[] { new LocalPoint(-20, -20), new LocalPoint(20, -20), new LocalPoint(20, 20), new LocalPoint(-20, 20) });
            map.floors[0].rooms.Add(voidRoom);
            var plan = BuildingGenerationPlanner.Build(map);
            Assert.IsTrue(plan.IsValid, string.Join(" ", plan.errors.ToArray()));
            Assert.IsFalse(plan.floors[0].rooms.Exists(room => room.source.id == "void-region"));
        }

        [Test]
        public void BuildingGenerationPlanSkipsInvalidRoomWithoutFailingOtherGeometry()
        {
            var map = TestMapFactory.CreateTinyThreeRoomMap();
            var invalid = new RoomData { id = "bow-tie", displayName = "Bad semantic patch", walkable = true };
            invalid.polygon.vertices.AddRange(new[] { new LocalPoint(30, 30), new LocalPoint(34, 34), new LocalPoint(30, 34), new LocalPoint(34, 30) });
            map.floors[0].rooms.Add(invalid);
            var plan = BuildingGenerationPlanner.Build(map);
            Assert.IsTrue(plan.IsValid, string.Join(" ", plan.errors.ToArray()));
            Assert.AreEqual(3, plan.floors[0].rooms.Count);
            Assert.AreEqual(2, plan.floors[0].walls.Count);
            Assert.AreEqual(1, plan.warnings.Count);
            StringAssert.Contains("bow-tie", plan.warnings[0]);
        }

        [Test]
        public void NavigationFloorBuilderCreatesNonOverlappingUpwardWalkableMesh()
        {
            var map = TestMapFactory.CreateTinyThreeRoomMap();
            Assert.IsTrue(NavigationFloorBuilder.TryBuild(map.floors[0], 0.1f, out var result, out var error), error);
            Assert.Greater(result.vertices, 0); Assert.Greater(result.triangles, 0);
            var normals = result.mesh.normals; Assert.Greater(normals.Length, 0);
            for (var i = 0; i < normals.Length; i++) Assert.Greater(normals[i].y, 0.9f);
            UnityEngine.Object.DestroyImmediate(result.mesh);
        }

        private static LocationReading GeoReading(GeoCoordinateTransformer transformer, LocalPoint point, double accuracy)
        { var coordinate = transformer.LocalToGeo(point); return new LocationReading { latitude = coordinate.latitude, longitude = coordinate.longitude, altitudeMetres = 2, horizontalAccuracyMetres = accuracy, verticalAccuracyMetres = 3, timestampUnixSeconds = 100, receivedUnixSeconds = 100, isValid = true }; }

        private static GeoAnchorData[] SyntheticAnchors(GeoCoordinate reference, double heading, double east, double north, params LocalPoint[] points)
        {
            var result = new GeoAnchorData[points.Length]; for (var i = 0; i < points.Length; i++) { var en = GeoCalibrationService.Transform(new EastNorthPoint(points[i].x, points[i].z), heading); en = new EastNorthPoint(en.east + east, en.north + north); result[i] = new GeoAnchorData { id = "anchor_" + i, localPosition = points[i], coordinate = GeoProjectionService.ToGeoCoordinate(en, reference) }; } return result;
        }

        private static GeoCalibrationData ToData(GeoCalibrationSolveResult result)
        { return new GeoCalibrationData { geographicOrigin = result.ReferenceCoordinate, mapHeadingDegrees = result.HeadingDegrees, translationEastMetres = result.TranslationEastMetres, translationNorthMetres = result.TranslationNorthMetres, isValid = result.IsValid }; }

        private static void WriteTestImage(string path, bool jpeg)
        {
            var texture = new Texture2D(100, 50, TextureFormat.RGBA32, false);
            for (var y = 0; y < 50; y++) for (var x = 0; x < 100; x++) texture.SetPixel(x, y, new Color(x / 99f, y / 49f, 0.25f, 1));
            File.WriteAllBytes(path, jpeg ? texture.EncodeToJPG(90) : texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
        }

        private static void AssertLocal(LocalPoint point, float x, float z)
        { Assert.That(point.x, Is.EqualTo(x).Within(0.0001f)); Assert.That(point.z, Is.EqualTo(z).Within(0.0001f)); }
    }
}
