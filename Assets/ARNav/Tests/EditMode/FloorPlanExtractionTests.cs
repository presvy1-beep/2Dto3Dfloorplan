using ARNav.Core.Data;
using ARNav.Core.Generation;
using ARNav.Core.Geometry;
using ARNav.Runtime.Extraction;
using ARNav.Runtime.Generation;
using NUnit.Framework;
using System;

namespace ARNav.Tests.EditMode
{
    public sealed class FloorPlanExtractionTests
    {
        [Test]
        public void CleanGridProducesRoomsAndWallsInSourcePixels()
        {
            const int width = 1100, height = 400;
            var pixels = new byte[width * height];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = 255;
            for (var y = 0; y < height; y++) { pixels[y * width] = 0; pixels[y * width + 400] = 0; pixels[y * width + 700] = 0; pixels[y * width + 1099] = 0; }
            for (var x = 0; x < width; x++) { pixels[x] = 0; pixels[(height - 1) * width + x] = 0; }
            var result = new SimpleFloorPlanExtractor().Extract(new FloorPlanImageData(width, height, pixels), FloorPlanCropRegion.Full(width, height));
            Assert.IsTrue(result.succeeded, result.message);
            Assert.AreEqual(3, result.annotations.rooms.Count);
            Assert.GreaterOrEqual(result.annotations.walls.Count, 5);
            Assert.AreEqual(0, result.annotations.rooms[0].vertices[0].x);
            Assert.AreEqual("Room", result.annotations.rooms[0].regionType);
            Assert.IsTrue(result.annotations.rooms[0].walkable);
        }

        [Test]
        public void PartialPartitionDoesNotBecomeAFullSpanGridWall()
        {
            const int width = 240, height = 200;
            var pixels = new byte[width * height];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = 255;
            for (var x = 20; x <= 220; x++) { pixels[20 * width + x] = 0; pixels[180 * width + x] = 0; }
            for (var y = 20; y <= 180; y++) { pixels[y * width + 20] = 0; pixels[y * width + 220] = 0; }
            for (var y = 20; y <= 130; y++) pixels[y * width + 120] = 0;

            var result = new SimpleFloorPlanExtractor().Extract(new FloorPlanImageData(width, height, pixels), FloorPlanCropRegion.Full(width, height));
            Assert.IsTrue(result.succeeded, result.message);
            Assert.IsFalse(result.annotations.walls.Exists(w =>
                Math.Abs(w.start.x - 120) < 3 && Math.Abs(w.end.x - 120) < 3 &&
                Math.Abs(w.end.y - w.start.y) > 140),
                "The 50-pixel opening below the partition must not be filled by the clean-grid shortcut.");
        }

        [Test]
        public void DocumentSidebarRulesAreExcludedFromBuildingGeometry()
        {
            const int width = 300, height = 400;
            var pixels = new byte[width * height];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = 255;
            foreach (var x in new[] { 3, 18, 210, 225, 240, 299 })
                for (var y = 0; y < height; y++) pixels[y * width + x] = 0;
            for (var x = 40; x <= 200; x++) { pixels[30 * width + x] = 0; pixels[370 * width + x] = 0; }
            for (var y = 30; y <= 370; y++) { pixels[y * width + 40] = 0; pixels[y * width + 200] = 0; }
            for (var y = 30; y <= 190; y++) pixels[y * width + 110] = 0;

            var result = new SimpleFloorPlanExtractor().Extract(new FloorPlanImageData(width, height, pixels), FloorPlanCropRegion.Full(width, height));
            Assert.IsTrue(result.succeeded, result.message);
            Assert.GreaterOrEqual(result.analyzedRegion.x, 21);
            Assert.LessOrEqual(result.analyzedRegion.x + result.analyzedRegion.width, 215);
            Assert.IsFalse(result.annotations.wallRuns.Exists(run => run.minX >= 220),
                "The title block must not be serialized as a building wall.");
        }

        [Test]
        public void ExteriorDoorGapKeepsInteriorFloorWithoutClosingWallOpening()
        {
            const int width = 250, height = 200;
            var pixels = new byte[width * height];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = 255;
            for (var x = 25; x <= 225; x++) { pixels[20 * width + x] = 0; pixels[180 * width + x] = 0; }
            for (var y = 20; y <= 180; y++)
            {
                if (y < 80 || y > 120) pixels[y * width + 25] = 0;
                pixels[y * width + 225] = 0;
            }

            var result = new SimpleFloorPlanExtractor().Extract(new FloorPlanImageData(width, height, pixels), FloorPlanCropRegion.Full(width, height));
            Assert.IsTrue(result.succeeded, result.message);
            Assert.IsTrue(result.annotations.rooms.Exists(room => room.walkable && room.rasterRuns.Count > 0),
                "An exterior door must not make the whole interior an unwalkable void.");
            Assert.IsFalse(result.annotations.wallRuns.Exists(run =>
                run.minX <= 25 && run.maxX > 25 && run.minY <= 100 && run.maxY > 100),
                "The classification bridge must not close the physical wall opening.");
        }

        [Test]
        public void OpenExteriorUsesClearlyLabeledEstimatedFloor()
        {
            const int width = 280, height = 220;
            var pixels = new byte[width * height];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = 255;
            for (var x = 30; x <= 250; x++) { pixels[25 * width + x] = 0; pixels[195 * width + x] = 0; }
            for (var y = 25; y <= 195; y++)
            {
                if (y < 70 || y > 150) pixels[y * width + 30] = 0;
                if (y < 70 || y > 150) pixels[y * width + 250] = 0;
            }

            var result = new SimpleFloorPlanExtractor().Extract(new FloorPlanImageData(width, height, pixels), FloorPlanCropRegion.Full(width, height));
            Assert.IsTrue(result.succeeded, result.message);
            var floor = result.annotations.rooms.Find(room => room.regionType == "EstimatedInterior");
            Assert.IsNotNull(floor, "Open boundaries still need a reviewable floor preview.");
            Assert.IsTrue(floor.walkable);
            Assert.IsTrue(floor.preferRasterGeometry);
            Assert.Less(floor.detectionConfidence, .5f);
            Assert.IsTrue(result.message.Contains("estimated"));
            Assert.IsFalse(floor.rasterRuns.Exists(run => run.minX < 30 || run.maxX > 251));
            var project = MapProjectData.Create("estimated", "Estimated Floor Test");
            var derivedFloor = new FloorData { id = "ground", sourceAnnotations = result.annotations };
            Assert.IsTrue(PixelToLocalGeometryConverter.TryRegenerate(derivedFloor,
                new ScaleCalibrationData { isValid = true, isEstimated = true, metresPerPixel = .02f },
                width, height, new MapGenerationDefaults(), out var validation), string.Join("; ", validation.errors));
            project.floors.Add(derivedFloor);
            var plan = BuildingGenerationPlanner.Build(project);
            Assert.IsTrue(plan.IsValid, string.Join("; ", plan.errors));
            var roomPlan = plan.floors[0].rooms.Find(room => room.source.id == "estimated_interior_1");
            Assert.IsNotNull(roomPlan);
            Assert.IsTrue(roomPlan.rasterFallback);
            var mesh = RuntimeMeshFactory.CreateFloorMesh(roomPlan, 0, 1);
            try { Assert.IsNotNull(mesh); Assert.Greater(mesh.vertexCount, 0); }
            finally { if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void CropCoordinatesRemainOriginalImageCoordinates()
        {
            const int width = 300, height = 200;
            var pixels = new byte[width * height];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = 255;
            for (var y = 20; y <= 180; y++) { pixels[y * width + 50] = 0; pixels[y * width + 250] = 0; }
            for (var x = 50; x <= 250; x++) { pixels[20 * width + x] = 0; pixels[180 * width + x] = 0; }
            var result = new SimpleFloorPlanExtractor().Extract(new FloorPlanImageData(width, height, pixels), new FloorPlanCropRegion(50, 20, 201, 161));
            Assert.IsTrue(result.succeeded, result.message);
            Assert.AreEqual(50, result.annotations.rooms[0].vertices[0].x);
            Assert.AreEqual(20, result.annotations.rooms[0].vertices[0].y);
        }

        [Test]
        public void RegionMetadataIsJsonCompatible()
        {
            var room = new RoomAnnotationData { id = "r", regionType = "Void", walkable = false, destinationSelectable = false };
            var json = UnityEngine.JsonUtility.ToJson(room);
            var loaded = UnityEngine.JsonUtility.FromJson<RoomAnnotationData>(json);
            Assert.AreEqual("Void", loaded.regionType);
            Assert.IsFalse(loaded.walkable);
            Assert.IsFalse(loaded.destinationSelectable);
        }

        [Test]
        public void LocalizedColoredPlanProducesOriginalPixelGeometry()
        {
            const int width = 240, height = 240; var pixels = new byte[width * height]; for (var i = 0; i < pixels.Length; i++) pixels[i] = 255;
            for (var x = 25; x < 115; x++) { pixels[35 * width + x] = 80; pixels[125 * width + x] = 80; }
            for (var y = 35; y < 126; y++) { pixels[y * width + 25] = 80; pixels[y * width + 115] = 80; }
            for (var i = 0; i < 70; i++) pixels[(150 + i) * width + 145 + i] = 100;
            var result = new SimpleFloorPlanExtractor().Extract(new FloorPlanImageData(width, height, pixels), FloorPlanCropRegion.Full(width, height));
            Assert.IsTrue(result.succeeded, result.message); Assert.Greater(result.annotations.walls.Count, 0); Assert.IsTrue(result.annotations.walls.Exists(w => Math.Abs(w.start.x - 25) < 2 || Math.Abs(w.end.x - 25) < 2));
        }

        [Test]
        public void LocalizedRasterPreservesConcaveLShapedRoomOutline()
        {
            const int width = 300, height = 360; var pixels = new byte[width * height]; for (var i = 0; i < pixels.Length; i++) pixels[i] = 255;
            var polygon = new[] { new UnityEngine.Vector2Int(40, 40), new UnityEngine.Vector2Int(260, 40), new UnityEngine.Vector2Int(260, 100), new UnityEngine.Vector2Int(130, 100), new UnityEngine.Vector2Int(130, 260), new UnityEngine.Vector2Int(40, 260) };
            for (var i = 0; i < polygon.Length; i++) DrawThickLine(pixels, width, height, polygon[i], polygon[(i + 1) % polygon.Length]);
            var result = new SimpleFloorPlanExtractor().Extract(new FloorPlanImageData(width, height, pixels), FloorPlanCropRegion.Full(width, height));
            Assert.IsTrue(result.succeeded, result.message);
            var room = result.annotations.rooms.Find(r => r.walkable && r.vertices.Count >= 5);
            Assert.IsNotNull(room, "Expected a non-rectangular connected free-space contour, not a bounding box.");
        }

        [Test]
        public void ColouredPageBackgroundIsNotMisclassifiedAsArchitecturalWall()
        {
            const int width = 260, height = 220;
            var luminance = new byte[width * height]; var colors = new UnityEngine.Color32[width * height];
            var page = new UnityEngine.Color32(71, 112, 76, 255); var floor = new UnityEngine.Color32(255, 255, 255, 255); var wall = new UnityEngine.Color32(5, 5, 5, 255);
            for (var i = 0; i < luminance.Length; i++) { luminance[i] = 95; colors[i] = page; }
            Fill(24, 24, 236, 196, floor);
            DrawColorLine(24, 24, 236, 24, wall, 5); DrawColorLine(236, 24, 236, 196, wall, 5); DrawColorLine(236, 196, 24, 196, wall, 5); DrawColorLine(24, 196, 24, 24, wall, 5);
            DrawColorLine(120, 24, 120, 86, wall, 4); DrawColorLine(120, 104, 120, 196, wall, 4);
            DrawColorLine(120, 112, 178, 112, wall, 4); DrawColorLine(196, 112, 236, 112, wall, 4);
            var result = new SimpleFloorPlanExtractor { CaptureDiagnosticMasks = true }.Extract(new FloorPlanImageData(width, height, luminance, colors), FloorPlanCropRegion.Full(width, height));
            Assert.IsTrue(result.succeeded, result.message);
            Assert.IsTrue(result.annotations.rooms.Exists(r => r.walkable && r.rasterRuns.Count > 0), "The raster interior should survive even when room openings join its topology.");
            Assert.Greater(result.diagnostics.roomMaskCoverage, .75f);
            Assert.Less(result.diagnostics.roomMaskFalseFill, .2f);
            Assert.Greater(result.diagnostics.estimatedWallThicknessPixels, 0);
            Assert.Greater(result.diagnostics.openingMask.Length, 0);
            Assert.Greater(result.annotations.doors.Count, 0, "A plausible opening should remain represented after background removal.");

            var roundTrip = UnityEngine.JsonUtility.FromJson<FloorPlanAnnotationData>(UnityEngine.JsonUtility.ToJson(result.annotations));
            Assert.IsNotNull(roundTrip.rooms[0].rasterRuns);
            Assert.AreEqual(result.annotations.rooms[0].rasterRuns.Count, roundTrip.rooms[0].rasterRuns.Count);
            var repeated = new SimpleFloorPlanExtractor().Extract(new FloorPlanImageData(width, height, luminance, colors), FloorPlanCropRegion.Full(width, height));
            Assert.AreEqual(UnityEngine.JsonUtility.ToJson(result.annotations), UnityEngine.JsonUtility.ToJson(repeated.annotations), "The same decoded pixels must produce deterministic JSON ordering/content.");

            void Fill(int x0, int y0, int x1, int y1, UnityEngine.Color32 color)
            { for (var y = y0; y <= y1; y++) for (var x = x0; x <= x1; x++) { colors[y * width + x] = color; luminance[y * width + x] = color.r == 255 ? (byte)255 : (byte)5; } }
            void DrawColorLine(int x0, int y0, int x1, int y1, UnityEngine.Color32 color, int thickness)
            {
                var dx = Math.Abs(x1 - x0); var sx = x0 < x1 ? 1 : -1; var dy = -Math.Abs(y1 - y0); var sy = y0 < y1 ? 1 : -1; var error = dx + dy;
                while (true) { for (var oy = -thickness / 2; oy <= thickness / 2; oy++) for (var ox = -thickness / 2; ox <= thickness / 2; ox++) if (x0 + ox >= 0 && y0 + oy >= 0 && x0 + ox < width && y0 + oy < height) { var i = (y0 + oy) * width + x0 + ox; colors[i] = color; luminance[i] = color.r; } if (x0 == x1 && y0 == y1) break; var e2 = 2 * error; if (e2 >= dy) { error += dy; x0 += sx; } if (e2 <= dx) { error += dx; y0 += sy; } }
            }
        }

        [Test]
        public void RasterRoundTripPreservesAnIrregularUShapeAndHoleMetadata()
        {
            const int width = 280, height = 280; var pixels = new byte[width * height]; for (var i = 0; i < pixels.Length; i++) pixels[i] = 255;
            var polygon = new[] { new UnityEngine.Vector2Int(35, 35), new UnityEngine.Vector2Int(245, 35), new UnityEngine.Vector2Int(245, 245), new UnityEngine.Vector2Int(185, 245), new UnityEngine.Vector2Int(185, 95), new UnityEngine.Vector2Int(95, 95), new UnityEngine.Vector2Int(95, 245), new UnityEngine.Vector2Int(35, 245) };
            for (var i = 0; i < polygon.Length; i++) DrawThickLine(pixels, width, height, polygon[i], polygon[(i + 1) % polygon.Length]);
            var result = new SimpleFloorPlanExtractor().Extract(new FloorPlanImageData(width, height, pixels), FloorPlanCropRegion.Full(width, height));
            var room = result.annotations.rooms.Find(r => r.walkable && r.vertices.Count >= 6);
            Assert.IsNotNull(room, "U-shaped occupancy must not be replaced by its bounding rectangle.");
            var loaded = UnityEngine.JsonUtility.FromJson<RoomAnnotationData>(UnityEngine.JsonUtility.ToJson(room));
            Assert.AreEqual(room.vertices.Count, loaded.vertices.Count);
            Assert.AreEqual(room.holes.Count, loaded.holes.Count);
        }

        private static void DrawThickLine(byte[] pixels, int width, int height, UnityEngine.Vector2Int a, UnityEngine.Vector2Int b)
        {
            var dx = Math.Abs(b.x - a.x); var sx = a.x < b.x ? 1 : -1; var dy = -Math.Abs(b.y - a.y); var sy = a.y < b.y ? 1 : -1; var error = dx + dy; var x = a.x; var y = a.y;
            while (true)
            {
                for (var oy = -2; oy <= 2; oy++) for (var ox = -2; ox <= 2; ox++) if (x + ox >= 0 && y + oy >= 0 && x + ox < width && y + oy < height) pixels[(y + oy) * width + x + ox] = 20;
                if (x == b.x && y == b.y) break;
                var twice = 2 * error; if (twice >= dy) { error += dy; x += sx; } if (twice <= dx) { error += dx; y += sy; }
            }
        }
    }
}
