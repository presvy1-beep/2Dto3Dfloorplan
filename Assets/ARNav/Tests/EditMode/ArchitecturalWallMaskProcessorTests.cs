using System;
using System.Collections.Generic;
using ARNav.Core.Data;
using ARNav.Core.Geometry;
using ARNav.Runtime.Extraction;
using ARNav.Runtime.MapEditor;
using ARNav.Runtime.Generation;
using NUnit.Framework;
using UnityEngine;

namespace ARNav.Tests.EditMode
{
    public sealed class ArchitecturalWallMaskProcessorTests
    {
        [Test]
        public void RoomNumberGlyphClusterIsExcludedAndWallFootprintSurvivesJsonRoundTrip()
        {
            const int width = 220, height = 180;
            var luminance = new byte[width * height]; var colors = new Color32[luminance.Length];
            for (var i = 0; i < luminance.Length; i++) { luminance[i] = 255; colors[i] = new Color32(255, 255, 255, 255); }
            DrawRect(luminance, width, height, 22, 20, 198, 158, 5);
            DrawText(luminance, width, height, "101", 70, 76, 3);

            var annotations = new FloorPlanAnnotationData();
            ArchitecturalWallMaskProcessor.Process(new FloorPlanImageData(width, height, luminance, colors), FloorPlanCropRegion.Full(width, height), annotations);
            var before = new bool[width * height]; foreach (var run in annotations.wallRuns) for (var y = (int)run.minY; y < (int)run.maxY; y++) for (var x = (int)run.minX; x < (int)run.maxX; x++) before[y * width + x] = true;
            Assert.Greater(annotations.textComponentCount, 0);
            Assert.Greater(annotations.textExclusionPixelCount, 0);
            Assert.IsTrue(before[20 * width + 100], "The wall line passing adjacent to the number must remain.");
            Assert.IsFalse(before[76 * width + 72], "A glyph pixel from room number 101 must not enter architectural wall runs.");
            Assert.AreEqual(annotations.architecturalWallPixelCount, Count(before));

            var json = JsonUtility.ToJson(annotations);
            var reloaded = JsonUtility.FromJson<FloorPlanAnnotationData>(json);
            var after = new bool[width * height]; foreach (var run in reloaded.wallRuns) for (var y = (int)run.minY; y < (int)run.maxY; y++) for (var x = (int)run.minX; x < (int)run.maxX; x++) after[y * width + x] = true;
            Assert.AreEqual(1f, IoU(before, after), "RLE wall mask JSON round-trip must be lossless.");
            Assert.AreEqual(annotations.wallThreshold, reloaded.wallThreshold);
        }

        [Test]
        public void WallLineTouchingTextIsProtectedWhileDetachedGlyphsAreSuppressed()
        {
            const int width = 220, height = 180; var pixels = new byte[width * height]; var colors = new Color32[pixels.Length];
            for (var i = 0; i < pixels.Length; i++) { pixels[i] = 255; colors[i] = new Color32(255, 255, 255, 255); }
            DrawRect(pixels, width, height, 20, 20, 200, 160, 5); DrawText(pixels, width, height, "204", 70, 25, 3);
            var data = new FloorPlanAnnotationData(); ArchitecturalWallMaskProcessor.Process(new FloorPlanImageData(width, height, pixels, colors), FloorPlanCropRegion.Full(width, height), data);
            var mask = ToMask(data, width, height);
            Assert.IsTrue(mask[20 * width + 52], "The continuous architectural wall must survive the touching annotation.");
            Assert.IsFalse(mask[28 * width + 82], "Glyph pixels away from the wall line must be excluded.");
        }

        [Test]
        public void ShortStructuralStrokeIsNotDeletedAsTextNoise()
        {
            const int width = 160, height = 140; var pixels = new byte[width * height]; var colors = new Color32[pixels.Length];
            for (var i = 0; i < pixels.Length; i++) { pixels[i] = 255; colors[i] = new Color32(255, 255, 255, 255); }
            DrawRect(pixels, width, height, 15, 15, 145, 125, 4);
            for (var x = 72; x < 85; x++) for (var y = 64; y < 69; y++) pixels[y * width + x] = 0;
            var data = new FloorPlanAnnotationData(); ArchitecturalWallMaskProcessor.Process(new FloorPlanImageData(width, height, pixels, colors), FloorPlanCropRegion.Full(width, height), data);
            Assert.IsTrue(ToMask(data, width, height)[66 * width + 78], "A detached but non-glyph-like short wall stroke should remain.");
        }

        [Test]
        public void ComplexRasterWallMaskIsSplitBelowUnityUiVertexLimit()
        {
            const int test2RunCount = 16460;
            var chunkCount = FloorPlanRasterRunGraphic.ChunkCountForRunCount(test2RunCount);
            Assert.AreEqual(2, chunkCount);
            Assert.LessOrEqual(FloorPlanRasterRunGraphic.MaxRunsPerChunk * 4, 65000);
            Assert.AreEqual(0, FloorPlanRasterRunGraphic.ChunkCountForRunCount(0));
        }

        [Test]
        public void WallMaskMesherPreservesLFootprintAndOmitsBottomAndInternalFaces()
        {
            var runs = new List<LocalRasterRun>
            {
                new LocalRasterRun { minX = 0, maxX = 2, minZ = 0, maxZ = 1 },
                new LocalRasterRun { minX = 0, maxX = 1, minZ = 1, maxZ = 2 }
            };
            var mesh = ArchitecturalWallMaskMeshBuilder.Build(runs, 0, 2.7f);
            try
            {
                Assert.NotNull(mesh);
                Assert.AreEqual(16, mesh.triangles.Length / 3, "The L top is represented by two rectangles and its perimeter by six merged side spans, with no bottom/internal walls.");
                Assert.Less(mesh.vertexCount, 32, "Coplanar top vertices shared by adjacent merged rectangles should be welded.");
                Assert.AreEqual(0f, mesh.bounds.min.y, .0001f);
                Assert.AreEqual(2.7f, mesh.bounds.max.y, .0001f);
                Assert.AreEqual(2f, mesh.bounds.size.x, .0001f);
                Assert.AreEqual(2f, mesh.bounds.size.z, .0001f);
                Assert.IsTrue(MeshContainsTopPoint(mesh, new Vector2(.5f, .5f)));
                Assert.IsTrue(MeshContainsTopPoint(mesh, new Vector2(.5f, 1.5f)));
                Assert.IsFalse(MeshContainsTopPoint(mesh, new Vector2(1.5f, 1.5f)), "The concave notch must remain empty.");

                var vertices = mesh.vertices; var triangles = mesh.triangles; var topArea = 0f;
                for (var i = 0; i < triangles.Length; i += 3)
                {
                    var a = vertices[triangles[i]]; var b = vertices[triangles[i + 1]]; var c = vertices[triangles[i + 2]];
                    var normalY = Vector3.Cross(b - a, c - a).y;
                    if (normalY > 0) topArea += normalY * .5f;
                }
                Assert.AreEqual(3f, topArea, .0001f, "Top footprint area must equal the source raster union.");
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void RepeatedAdjacentWallScanlinesBecomeOneContinuousExtrusion()
        {
            var runs = new List<LocalRasterRun>();
            for (var row = 0; row < 12; row++)
                runs.Add(new LocalRasterRun { minX = 2f, maxX = 5f, minZ = row, maxZ = row + 1f });

            var mesh = ArchitecturalWallMaskMeshBuilder.Build(runs, 0f, 2.7f);
            try
            {
                Assert.NotNull(mesh);
                Assert.AreEqual(10, mesh.triangles.Length / 3, "A rectangular 12-row wall should have one top quad and four exposed side quads, not one prism per row.");
                Assert.AreEqual(20, mesh.vertexCount, "Each of the five planar surfaces should have four hard-normal vertices; scanline seams must not duplicate geometry.");
                Assert.AreEqual(36f, TopTriangleArea(mesh), .0001f, "The extruded footprint must retain the exact 3 x 12 source area.");
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void WallMaskMesherPointsEveryExternalSideNormalOutward()
        {
            var mesh = ArchitecturalWallMaskMeshBuilder.Build(new List<LocalRasterRun>
            {
                new LocalRasterRun { minX = 0, maxX = 1, minZ = 0, maxZ = 1 }
            }, 0, 2.7f);
            try
            {
                var vertices = mesh.vertices; var triangles = mesh.triangles;
                var sides = new bool[4];
                for (var i = 0; i < triangles.Length; i += 3)
                {
                    var a = vertices[triangles[i]]; var b = vertices[triangles[i + 1]]; var c = vertices[triangles[i + 2]];
                    var normal = Vector3.Cross(b - a, c - a).normalized;
                    var center = (a + b + c) / 3f;
                    if (normal.x > .99f) { Assert.That(center.x, Is.EqualTo(1f).Within(.0001f)); sides[0] = true; }
                    else if (normal.x < -.99f) { Assert.That(center.x, Is.EqualTo(0f).Within(.0001f)); sides[1] = true; }
                    else if (normal.z > .99f) { Assert.That(center.z, Is.EqualTo(1f).Within(.0001f)); sides[2] = true; }
                    else if (normal.z < -.99f) { Assert.That(center.z, Is.EqualTo(0f).Within(.0001f)); sides[3] = true; }
                }
                CollectionAssert.AreEqual(new[] { true, true, true, true }, sides);
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void WallNormalizerTurnsStairStepDiagonalIntoContinuousConsistentStroke()
        {
            const int width = 140, height = 120; const float mpp = .01f;
            var mask = new bool[width * height];
            for (var i = 0; i < 70; i++) Set(mask, width, height, 15 + i, 20 + i / 2, true);
            var runs = ToLocalRuns(mask, width, height, mpp);
            var normalized = WallGeometryNormalizer.Normalize(runs, width, height, mpp, .15f, out var stats);
            var after = FromLocalRuns(normalized, width, height, mpp);
            Assert.AreEqual(1, stats.skeletonizedComponents);
            Assert.That(stats.outputThicknessPixels, Is.InRange(15, 17), "The robust raster estimate may report a fractionally wider diagonal stroke.");
            Assert.Greater(stats.normalizedPixels, stats.rawPixels * 5);
            Assert.AreEqual(1, CountComponents(after, width, height));
            Assert.IsTrue(after[37 * width + 50], "The diagonal centerline should remain covered by the rebuilt wall stroke.");
        }

        [Test]
        public void WallNormalizerPreservesDoorSizedEndpointOpeningAndRepairsTinyAccidentalGap()
        {
            const int width = 180, height = 100; const float mpp = .01f;
            var doorMask = new bool[width * height];
            FillRect(doorMask, width, height, 15, 15, 55, 20);
            FillRect(doorMask, width, height, 115, 15, 155, 20);
            var doorOutput = FromLocalRuns(WallGeometryNormalizer.Normalize(ToLocalRuns(doorMask, width, height, mpp), width, height, mpp, .15f, out _), width, height, mpp);
            Assert.IsFalse(doorOutput[17 * width + 85], "A 60-pixel doorway must remain open after normalization.");

            var gapMask = new bool[width * height];
            FillRect(gapMask, width, height, 15, 55, 75, 60);
            FillRect(gapMask, width, height, 78, 55, 145, 60);
            var gapOutput = FromLocalRuns(WallGeometryNormalizer.Normalize(ToLocalRuns(gapMask, width, height, mpp), width, height, mpp, .15f, out _), width, height, mpp);
            Assert.IsTrue(gapOutput[57 * width + 76], "The 2-pixel accidental gap should be covered by the normalized stroke.");
        }

        [Test]
        public void WallNormalizerKeepsCurvesCornersAndJunctionsAsSolidFootprints()
        {
            const int width = 120, height = 100; const float mpp = .01f;
            var arc = new bool[width * height];
            for (var angle = 0; angle <= 180; angle++)
            {
                var radians = angle * Mathf.Deg2Rad;
                var x = 60 + Mathf.RoundToInt(28 * Mathf.Cos(radians)); var y = 50 - Mathf.RoundToInt(28 * Mathf.Sin(radians));
                for (var oy = -1; oy <= 1; oy++) for (var ox = -1; ox <= 1; ox++) Set(arc, width, height, x + ox, y + oy, true);
            }
            var arcOutput = FromLocalRuns(WallGeometryNormalizer.Normalize(ToLocalRuns(arc, width, height, mpp), width, height, mpp, .15f, out _), width, height, mpp);
            Assert.AreEqual(1, CountComponents(arcOutput, width, height), "The rasterized curve must remain one connected wall.");

            var junction = new bool[width * height];
            FillRect(junction, width, height, 10, 46, 110, 51);
            FillRect(junction, width, height, 58, 10, 63, 87);
            var junctionOutput = FromLocalRuns(WallGeometryNormalizer.Normalize(ToLocalRuns(junction, width, height, mpp), width, height, mpp, .15f, out _), width, height, mpp);
            Assert.AreEqual(1, CountComponents(junctionOutput, width, height), "The T-junction must be one connected wall mass.");
            Assert.IsTrue(junctionOutput[48 * width + 60]);
        }

        [Test]
        public void WallNormalizerDropsOnlyTinyNoiseAndPreservesThickLShapeAndJsonRuns()
        {
            const int width = 100, height = 90; const float mpp = .01f;
            var source = new bool[width * height];
            FillRect(source, width, height, 15, 15, 75, 20);
            FillRect(source, width, height, 15, 20, 20, 70);
            Set(source, width, height, 90, 5, true);
            var rawRuns = ToLocalRuns(source, width, height, mpp);
            var roundTrip = FromLocalRuns(rawRuns, width, height, mpp);
            Assert.AreEqual(1f, IoU(source, roundTrip), .0001f, "The serialized source runs remain a lossless representation.");

            var normalized = WallGeometryNormalizer.Normalize(rawRuns, width, height, mpp, .15f, out var stats);
            var after = FromLocalRuns(normalized, width, height, mpp);
            Assert.AreEqual(1, stats.removedComponents, "The detached one-pixel artifact should be suppressed.");
            Assert.IsFalse(after[5 * width + 90]);
            Assert.IsTrue(after[16 * width + 16]);
            Assert.IsTrue(after[60 * width + 16]);
            Assert.AreEqual(1, CountComponents(after, width, height));
            Assert.Less(stats.areaChangePercent, 300f, "Area growth should be controlled and attributable to the minimum wall thickness.");
        }

        [Test]
        public void WallNormalizerMakesVariableThinRasterWidthsVisuallyConsistent()
        {
            const int width = 150, height = 90; const float mpp = .01f;
            var source = new bool[width * height];
            for (var x = 12; x < 138; x++)
            {
                var thickness = x % 9 < 3 ? 2 : x % 9 < 6 ? 4 : 6;
                for (var y = 40 - thickness / 2; y < 40 + (thickness + 1) / 2; y++) Set(source, width, height, x, y, true);
            }
            var runs = ToLocalRuns(source, width, height, mpp);
            var normalized = FromLocalRuns(WallGeometryNormalizer.Normalize(runs, width, height, mpp, .15f, out var stats), width, height, mpp);
            Assert.AreEqual(1, stats.skeletonizedComponents);
            var widths = new List<int>();
            for (var x = 25; x < 125; x++)
            {
                var low = height; var high = -1;
                for (var y = 0; y < height; y++) if (normalized[y * width + x]) { low = Mathf.Min(low, y); high = Mathf.Max(high, y); }
                if (high >= low) widths.Add(high - low + 1);
            }
            Assert.LessOrEqual(Max(widths) - Min(widths), 3, "Compression width fluctuations should collapse to a stable stroke width.");
        }

        [Test]
        public void WallNormalizerRemovesDetachedShortFurnitureStrokeButKeepsWallContinuation()
        {
            const int width = 180, height = 130; const float mpp = .01f;
            var source = new bool[width * height];
            FillRect(source, width, height, 10, 15, 160, 19);
            // A short parallel stub beside the wall is credible continuation evidence.
            FillRect(source, width, height, 12, 28, 32, 31);
            // A small detached mark deep inside the open area looks like a fixture glyph.
            FillRect(source, width, height, 75, 80, 98, 81);
            var normalized = FromLocalRuns(WallGeometryNormalizer.Normalize(ToLocalRuns(source, width, height, mpp), width, height, mpp, .15f, out var stats), width, height, mpp);
            Assert.IsTrue(normalized[29 * width + 20], "A short stroke beside a long architectural run must survive.");
            Assert.IsFalse(normalized[80 * width + 85], "A thin detached short mark inside a room must not become a wall.");
            Assert.GreaterOrEqual(stats.removedComponents, 1);
        }

        [Test]
        public void WallPathsTraceStraightDiagonalAndCornerAsOrderedSimplifiedPaths()
        {
            const int width=160,height=140;var straight=new bool[width*height];FillRect(straight,width,height,10,50,145,54);
            var paths=ExtractPaths(straight,width,height,out var stats);
            var head="";for(var i=0;i<Math.Min(12,paths[0].points.Count);i++)head+=paths[0].points[i].x+":"+paths[0].points[i].y+",";
            Assert.AreEqual(1,paths.Count);Assert.LessOrEqual(paths[0].points.Count,2,"closed="+paths[0].closed+" estimatedWidth="+stats.estimatedWidthPixels+" rawPoints="+stats.pointsBeforeSimplification+" outputPoints="+stats.pointsAfterSimplification+" head="+head);Assert.AreEqual(2,stats.pointsAfterSimplification);Assert.Greater(stats.pointsBeforeSimplification,100);

            var diagonal=new bool[width*height];for(var i=0;i<90;i++)Set(diagonal,width,height,20+i,15+i/2,true);
            paths=ExtractPaths(diagonal,width,height,out stats);
            Assert.AreEqual(1,paths.Count);Assert.LessOrEqual(paths[0].points.Count,4,"Stair steps should collapse to an ordered diagonal path.");

            var corner=new bool[width*height];FillRect(corner,width,height,20,20,105,24);FillRect(corner,width,height,101,20,105,110);
            paths=ExtractPaths(corner,width,height,out stats);
            Assert.AreEqual(1,paths.Count);Assert.GreaterOrEqual(paths[0].points.Count,3,"Douglas-Peucker must retain the L corner.");
        }

        [Test]
        public void WallPathsPreserveTJunctionTopologyAndClosedLoops()
        {
            const int width=150,height=130;var tee=new bool[width*height];FillRect(tee,width,height,15,62,135,66);FillRect(tee,width,height,72,15,76,64);
            var paths=ExtractPaths(tee,width,height,out var stats);
            Assert.AreEqual(3,paths.Count);Assert.AreEqual(3,stats.endpoints);Assert.GreaterOrEqual(stats.junctions,1);
            foreach(var path in paths)Assert.IsTrue(path.points.Count>=2);

            var loop=new bool[width*height];for(var angle=0;angle<360;angle++){var r=angle*Mathf.Deg2Rad;Set(loop,width,height,75+Mathf.RoundToInt(32*Mathf.Cos(r)),65+Mathf.RoundToInt(32*Mathf.Sin(r)),true);}
            paths=ExtractPaths(loop,width,height,out stats);
            Assert.AreEqual(1,paths.Count);Assert.IsTrue(paths[0].closed);Assert.GreaterOrEqual(paths[0].points.Count,5);
        }

        [Test]
        public void WallPathsJoinOnlyTinyFacingCollinearGapsAndSerializeInOrder()
        {
            const int width=180,height=100;var shortGap=new bool[width*height];FillRect(shortGap,width,height,10,45,70,49);FillRect(shortGap,width,height,73,45,155,49);
            var paths=ExtractPaths(shortGap,width,height,out var stats);
            var endpointText="";foreach(var p in paths)endpointText+=" ["+p.points[0].x+","+p.points[0].y+" -> "+p.points[p.points.Count-1].x+","+p.points[p.points.Count-1].y+"]";
            Assert.AreEqual(1,stats.joinedGaps,"widthPx="+stats.estimatedWidthPixels+" endpoints="+stats.endpoints+" paths="+stats.initialPaths+" final="+paths.Count+endpointText);Assert.AreEqual(1,paths.Count);

            var doorway=new bool[width*height];FillRect(doorway,width,height,10,45,65,49);FillRect(doorway,width,height,100,45,155,49);
            paths=ExtractPaths(doorway,width,height,out stats);
            Assert.AreEqual(0,stats.joinedGaps);Assert.AreEqual(2,paths.Count,"A doorway-sized gap must remain disconnected.");

            var annotations=new FloorPlanAnnotationData{wallRuns=ToPixelRuns(shortGap,width,height),wallPaths=paths};
            var reload=JsonUtility.FromJson<FloorPlanAnnotationData>(JsonUtility.ToJson(annotations));
            Assert.AreEqual(annotations.wallRuns.Count,reload.wallRuns.Count,"Raw wallRuns remain serialized.");
            Assert.AreEqual(2,reload.wallPaths.Count);Assert.Greater(reload.wallPaths[0].points.Count,1);
            Assert.AreNotEqual(reload.wallPaths[0].points[0].x,reload.wallPaths[0].points[reload.wallPaths[0].points.Count-1].x,"Path points must have a stable order along the stroke.");
        }

        private static List<WallPathData> ExtractPaths(bool[] mask,int width,int height,out WallPathDiagnostics stats)
        {return WallPathExtractor.Extract(ToPixelRuns(mask,width,height),width,height,out stats);}
        private static List<PixelRasterRun> ToPixelRuns(bool[] mask,int width,int height)
        {
            var runs=new List<PixelRasterRun>();for(var y=0;y<height;y++){var x=0;while(x<width){while(x<width&&!mask[y*width+x])x++;if(x>=width)break;var start=x;while(x+1<width&&mask[y*width+x+1])x++;runs.Add(new PixelRasterRun{minX=start,maxX=x+1,minY=y,maxY=y+1});x++;}}return runs;
        }

        private static bool[] ToMask(FloorPlanAnnotationData annotations, int width, int height)
        {
            var mask = new bool[width * height]; foreach (var run in annotations.wallRuns) for (var y = (int)run.minY; y < (int)run.maxY; y++) for (var x = (int)run.minX; x < (int)run.maxX; x++) mask[y * width + x] = true; return mask;
        }
        private static int Count(bool[] mask) { var result = 0; foreach (var bit in mask) if (bit) result++; return result; }
        private static int CountComponents(bool[] mask, int width, int height)
        {
            var seen = new bool[mask.Length]; var queue = new int[mask.Length]; var components = 0;
            for (var start = 0; start < mask.Length; start++) if (mask[start] && !seen[start])
            {
                components++; var head = 0; var tail = 0; queue[tail++] = start; seen[start] = true;
                while (head < tail) { var i = queue[head++]; var x = i % width; var y = i / width; for (var oy = -1; oy <= 1; oy++) for (var ox = -1; ox <= 1; ox++) { var nx = x + ox; var ny = y + oy; if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue; var j = ny * width + nx; if (mask[j] && !seen[j]) { seen[j] = true; queue[tail++] = j; } } }
            }
            return components;
        }
        private static List<LocalRasterRun> ToLocalRuns(bool[] mask, int width, int height, float mpp)
        {
            var runs = new List<LocalRasterRun>();
            for (var y = 0; y < height; y++) { var x = 0; while (x < width) { while (x < width && !mask[y * width + x]) x++; if (x == width) break; var start = x; while (x + 1 < width && mask[y * width + x + 1]) x++; runs.Add(new LocalRasterRun { minX = start * mpp, maxX = (x + 1) * mpp, minZ = (height - y - 1) * mpp, maxZ = (height - y) * mpp }); x++; } }
            return runs;
        }
        private static bool[] FromLocalRuns(IList<LocalRasterRun> runs, int width, int height, float mpp)
        {
            var mask = new bool[width * height]; foreach (var run in runs) { var x0 = Mathf.Clamp(Mathf.FloorToInt(run.minX / mpp + .0001f), 0, width); var x1 = Mathf.Clamp(Mathf.CeilToInt(run.maxX / mpp - .0001f), 0, width); var y0 = Mathf.Clamp(height - Mathf.CeilToInt(run.maxZ / mpp - .0001f), 0, height); var y1 = Mathf.Clamp(height - Mathf.FloorToInt(run.minZ / mpp + .0001f), 0, height); for (var y = y0; y < y1; y++) for (var x = x0; x < x1; x++) mask[y * width + x] = true; } return mask;
        }
        private static void FillRect(bool[] mask, int width, int height, int x0, int y0, int x1, int y1) { for (var y = y0; y < y1; y++) for (var x = x0; x < x1; x++) Set(mask, width, height, x, y, true); }
        private static void Set(bool[] mask, int width, int height, int x, int y, bool value) { if (x >= 0 && y >= 0 && x < width && y < height) mask[y * width + x] = value; }
        private static int Min(List<int> values) { var value = int.MaxValue; foreach (var item in values) value = Math.Min(value, item); return value; }
        private static int Max(List<int> values) { var value = int.MinValue; foreach (var item in values) value = Math.Max(value, item); return value; }
        private static bool MeshContainsTopPoint(Mesh mesh, Vector2 point)
        {
            var vertices = mesh.vertices; var triangles = mesh.triangles;
            for (var i = 0; i < triangles.Length; i += 3)
            {
                var a = vertices[triangles[i]]; var b = vertices[triangles[i + 1]]; var c = vertices[triangles[i + 2]];
                if (Mathf.Abs(a.y - 2.7f) > .0001f || Mathf.Abs(b.y - 2.7f) > .0001f || Mathf.Abs(c.y - 2.7f) > .0001f) continue;
                var d1 = Cross(new Vector2(b.x - a.x, b.z - a.z), point - new Vector2(a.x, a.z));
                var d2 = Cross(new Vector2(c.x - b.x, c.z - b.z), point - new Vector2(b.x, b.z));
                var d3 = Cross(new Vector2(a.x - c.x, a.z - c.z), point - new Vector2(c.x, c.z));
                if ((d1 >= 0 && d2 >= 0 && d3 >= 0) || (d1 <= 0 && d2 <= 0 && d3 <= 0)) return true;
            }
            return false;
        }
        private static float TopTriangleArea(Mesh mesh)
        {
            var vertices = mesh.vertices; var triangles = mesh.triangles; var area = 0f;
            for (var i = 0; i < triangles.Length; i += 3)
            {
                var a = vertices[triangles[i]]; var b = vertices[triangles[i + 1]]; var c = vertices[triangles[i + 2]];
                if (Mathf.Abs(a.y - mesh.bounds.max.y) < .0001f && Mathf.Abs(b.y - mesh.bounds.max.y) < .0001f && Mathf.Abs(c.y - mesh.bounds.max.y) < .0001f)
                    area += Vector3.Cross(b - a, c - a).magnitude * .5f;
            }
            return area;
        }
        private static float Cross(Vector2 a, Vector2 b) { return a.x * b.y - a.y * b.x; }
        private static float IoU(bool[] a, bool[] b) { var intersection = 0; var union = 0; for (var i = 0; i < a.Length; i++) { if (a[i] && b[i]) intersection++; if (a[i] || b[i]) union++; } return union == 0 ? 1 : intersection / (float)union; }
        private static void DrawRect(byte[] pixels, int width, int height, int x0, int y0, int x1, int y1, int thickness)
        { for (var t = 0; t < thickness; t++) { for (var x = x0; x <= x1; x++) { Set(pixels, width, height, x, y0 + t); Set(pixels, width, height, x, y1 - t); } for (var y = y0; y <= y1; y++) { Set(pixels, width, height, x0 + t, y); Set(pixels, width, height, x1 - t, y); } } }
        private static void DrawText(byte[] pixels, int width, int height, string text, int x, int y, int scale)
        {
            var glyphs = new Dictionary<char, string> { ['0'] = "111101101101111", ['1'] = "010110010010111", ['2'] = "111001111100111", ['4'] = "101101111001001", ['H'] = "101101111101101", ['R'] = "110101110101101", ['O'] = "111101101101111", ['M'] = "100111111101101" };
            foreach (var ch in text)
            {
                if (!glyphs.TryGetValue(ch, out var bitmap)) { x += 4 * scale; continue; }
                for (var i = 0; i < bitmap.Length; i++) if (bitmap[i] == '1') for (var sy = 0; sy < scale; sy++) for (var sx = 0; sx < scale; sx++) Set(pixels, width, height, x + (i % 3) * scale + sx, y + (i / 3) * scale + sy);
                x += 4 * scale;
            }
        }
        private static void Set(byte[] pixels, int width, int height, int x, int y) { if (x >= 0 && y >= 0 && x < width && y < height) pixels[y * width + x] = 0; }
    }
}
