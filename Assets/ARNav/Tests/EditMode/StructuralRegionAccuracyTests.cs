using System;
using ARNav.Core.Data;
using ARNav.Core.Serialization;
using ARNav.Runtime.Extraction;
using NUnit.Framework;

namespace ARNav.Tests.EditMode
{
    public sealed class StructuralRegionAccuracyTests
    {
        private const int Size = 96;

        [Test]
        public void ConnectedRoomsRetainDoorAndRejectIsolatedFurniture()
        {
            var wall = new byte[Size * Size]; var image = new byte[wall.Length];
            Rect(wall, 8, 8, 87, 87, 3);
            for (var y = 8; y <= 87; y++) if (y < 43 || y > 51) for (var x = 46; x <= 48; x++) wall[y * Size + x] = 1;
            Array.Copy(wall, image, wall.Length);
            Rect(image, 20, 20, 35, 32, 2); // bed/table outline
            Line(image, 2, 3, 72, 3, 1); // dimension stroke
            var result = Extract(image);
            Assert.IsTrue(result.succeeded, result.message);
            Assert.GreaterOrEqual(result.annotations.rooms.Count, 2, "Doorway did not separate the two rooms for segmentation.");
            Assert.AreEqual(0, result.diagnostics.architecturalWallMask[26 * Size + 20], "Furniture entered the 3D wall source.");
            Assert.AreEqual(1, result.diagnostics.rejectedObjectMask[26 * Size + 20]);
            Assert.AreEqual(0, result.diagnostics.architecturalWallMask[47 * Size + 47], "Door opening was closed in physical geometry.");
            Assert.AreEqual(1, result.diagnostics.openingMask[47 * Size + 47]);
            Assert.AreEqual(1, result.diagnostics.exteriorMask[4 * Size + 4]);
            Assert.AreEqual(0, result.diagnostics.floorMask[4 * Size + 4]);
            var detected = result.diagnostics.architecturalWallMask;
            var tp = 0; var fp = 0; var fn = 0;
            for (var p = 0; p < wall.Length; p++)
            { if (detected[p] != 0 && wall[p] != 0) tp++; else if (detected[p] != 0) fp++; else if (wall[p] != 0) fn++; }
            var precision = tp / (float)(tp + fp); var recall = tp / (float)(tp + fn); var iou = tp / (float)(tp + fp + fn);
            Assert.Greater(precision, .58f, "exact-pixel wall precision=" + precision);
            Assert.Greater(recall, .85f, "wall recall=" + recall);
            Assert.Greater(iou, .58f, "exact-pixel wall IoU=" + iou);
            var floorTp = 0; var floorFp = 0; var floorFn = 0;
            for (var y = 0; y < Size; y++) for (var x = 0; x < Size; x++)
            {
                var p = y * Size + x;
                var expected = x >= 11 && x < 87 && y >= 11 && y < 87 && wall[p] == 0;
                var actual = result.diagnostics.floorMask[p] != 0;
                if (expected && actual) floorTp++;
                else if (actual) floorFp++;
                else if (expected) floorFn++;
            }
            Assert.Greater(floorTp / (float)(floorTp + floorFp), .95f, "floor precision");
            Assert.Greater(floorTp / (float)(floorTp + floorFn), .85f, "floor recall");
            Assert.Greater(floorTp / (float)(floorTp + floorFp + floorFn), .85f, "floor IoU");
        }

        [Test]
        public void LShapedFootprintKeepsExteriorCornerEmptyAndRepairsPinholes()
        {
            var image = new byte[Size * Size];
            Rect(image, 8, 8, 87, 87, 3);
            for (var y = 48; y <= 90; y++) for (var x = 48; x <= 90; x++) image[y * Size + x] = 0;
            Line(image, 48, 48, 87, 48, 3); Line(image, 48, 48, 48, 87, 3);
            image[9 * Size + 30] = 0; // one-pixel raster defect
            var result = Extract(image);
            Assert.IsTrue(result.succeeded, result.message);
            Assert.AreEqual(1, result.diagnostics.architecturalWallMask[9 * Size + 30]);
            Assert.AreEqual(1, result.diagnostics.floorMask[30 * Size + 30]);
            Assert.AreEqual(0, result.diagnostics.floorMask[70 * Size + 70]);
        }

        [Test]
        public void LongNarrowHallIsClassifiedAsCorridor()
        {
            var image = new byte[Size * Size];
            Rect(image, 5, 20, 90, 37, 3);
            var result = Extract(image);
            Assert.IsTrue(result.succeeded, result.message);
            Assert.AreEqual("Corridor", result.annotations.rooms[0].regionType);
            Assert.AreEqual(1, result.diagnostics.floorMask[29 * Size + 45]);
            Assert.AreEqual(0, result.diagnostics.floorMask[50 * Size + 45]);
        }

        [Test]
        public void TouchingFurnitureAndOverprintedTextDoNotBreakPerimeter()
        {
            var image = new byte[Size * Size];
            Rect(image, 8, 8, 87, 87, 3);
            Rect(image, 10, 25, 27, 36, 1); // cabinet attached to inner face
            Line(image, 6, 45, 15, 45, 1); // dark label crossing the left wall
            var result = Extract(image);
            Assert.IsTrue(result.succeeded, result.message);
            Assert.AreEqual(1, result.diagnostics.architecturalWallMask[45 * Size + 9]);
            Assert.AreEqual(1, result.diagnostics.architecturalWallMask[30 * Size + 9]);
            Assert.AreEqual(0, result.diagnostics.architecturalWallMask[30 * Size + 27], "Attached cabinet became a wall.");
        }

        [Test]
        public void SealedInteriorCourtyardIsNonWalkable()
        {
            var image = new byte[Size * Size];
            Rect(image, 8, 8, 87, 87, 3);
            Rect(image, 30, 30, 61, 61, 3);
            var result = Extract(image);
            Assert.IsTrue(result.succeeded, result.message);
            Assert.AreEqual(1, result.diagnostics.voidMask[45 * Size + 45]);
            Assert.AreEqual(0, result.diagnostics.floorMask[45 * Size + 45]);
            Assert.AreEqual(1, result.diagnostics.floorMask[20 * Size + 20]);
        }

        [Test]
        public void ShallowAngledPartitionKeepsItsMiddleSection()
        {
            var image = new byte[Size * Size];
            Rect(image, 8, 8, 87, 87, 3);
            for (var x = 18; x <= 74; x++) for (var t = 0; t < 3; t++) image[(24 + (x - 18) / 2 + t) * Size + x] = 1;
            var result = Extract(image);
            Assert.IsTrue(result.succeeded, result.message);
            Assert.AreEqual(1, result.diagnostics.architecturalWallMask[38 * Size + 46]);
        }

        [Test]
        public void DetectionLayersSurviveJsonAndOlderProjectsStillDeserialize()
        {
            var image = new byte[Size * Size];
            Rect(image, 8, 8, 87, 87, 3);
            Rect(image, 30, 30, 61, 61, 3);
            Line(image, 48, 8, 48, 87, 3);
            for (var y = 43; y <= 51; y++) for (var x = 48; x <= 50; x++) image[y * Size + x] = 0;
            Rect(image, 18, 20, 27, 29, 2);
            var result = Extract(image);
            var project = MapProjectData.Create("detected-layers", "Detected layers");
            project.floors.Add(new FloorData
            {
                id = "floor-1",
                sourceAnnotations = result.annotations
            });

            var restored = MapJsonSerializer.Deserialize(MapJsonSerializer.Serialize(project));
            var annotations = restored.floors[0].sourceAnnotations;
            Assert.Greater(annotations.wallRuns.Count, 0);
            Assert.Greater(annotations.exteriorRuns.Count, 0);
            Assert.Greater(annotations.voidRuns.Count, 0);
            Assert.Greater(annotations.openingRuns.Count, 0);
            Assert.Greater(annotations.rejectedObjectRuns.Count, 0);
            Assert.AreEqual(annotations.wallRuns[0].maxX, restored.floors[0].sourceAnnotations.wallRuns[0].maxX);

            var older = MapJsonSerializer.Deserialize("{\"schemaVersion\":1,\"projectId\":\"old\",\"floors\":[{\"id\":\"floor_0\",\"sourceAnnotations\":{\"rooms\":[],\"walls\":[],\"doors\":[]}}]}");
            Assert.IsNotNull(older.floors[0].sourceAnnotations.wallRuns);
            Assert.IsNotNull(older.floors[0].sourceAnnotations.exteriorRuns);
            Assert.IsNotNull(older.floors[0].sourceAnnotations.voidRuns);
        }

        private static FloorPlanExtractionResult Extract(byte[] mask)
        {
            var luminance = new byte[mask.Length];
            for (var y = 0; y < Size; y++) for (var x = 0; x < Size; x++)
                luminance[(Size - 1 - y) * Size + x] = mask[y * Size + x] == 0 ? (byte)255 : (byte)0;
            return new DirectBlackMaskExtractor().Extract(new FloorPlanImageData(Size, Size, luminance), FloorPlanCropRegion.Full(Size, Size));
        }
        private static void Line(byte[] mask, int x0, int y0, int x1, int y1, int width)
        {
            var steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
            for (var n = 0; n <= steps; n++) for (var t = 0; t < width; t++)
            { var x = x0 + (x1 - x0) * n / Math.Max(1, steps); var y = y0 + (y1 - y0) * n / Math.Max(1, steps); mask[(y + t) * Size + x + ((x0 == x1) ? t : 0)] = 1; }
        }
        private static void Rect(byte[] mask, int x0, int y0, int x1, int y1, int width)
        { Line(mask, x0, y0, x1, y0, width); Line(mask, x0, y1, x1, y1, width); Line(mask, x0, y0, x0, y1, width); Line(mask, x1, y0, x1, y1, width); }
    }
}
