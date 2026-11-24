using System;
using ARNav.Runtime.Extraction;
using ARNav.Core.Navigation;
using ARNav.Core.Data;
using ARNav.Core.Geometry;
using ARNav.Runtime.Generation;
using NUnit.Framework;
using UnityEngine;

namespace ARNav.Tests.EditMode
{
    public sealed class DirectBlackMaskExtractorTests
    {
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)] [TestCase(8)] [TestCase(9)] [TestCase(10)]
        public void StructuralGeometryPreservesWallsAndRejectsUnsupportedStrokes(int fixture)
        {
            var source = Fixture(fixture);
            var result = Analyze(source);
            Assert.IsTrue(result.succeeded, result.message);
            var reconstructed = new byte[source.Length];
            foreach (var run in result.annotations.wallRuns)
                for (var y = (int)run.minY; y < run.maxY; y++)
                    for (var x = (int)run.minX; x < run.maxX; x++) reconstructed[y * 64 + x] = 1;
            Assert.AreEqual(1, reconstructed[11 * 64 + 20], "Main exterior wall was lost.");
            Assert.AreEqual(1, reconstructed[40 * 64 + 11], "Left exterior wall was lost.");
            Assert.Greater(result.annotations.rooms[0].rasterRuns.Count, 0);
            if (fixture == 1) Assert.AreEqual(1, reconstructed[30 * 64 + 31], "Interior partition was lost.");
            if (fixture == 2) Assert.AreEqual(0, reconstructed[11 * 64 + 32], "Door gap closed.");
            if (fixture == 3) Assert.IsFalse(result.annotations.rooms[0].rasterRuns.Exists(run =>
                run.minY <= 45 && run.maxY > 45 && run.minX <= 45 && run.maxX > 45), "L-shaped exterior void became floor.");
            if (fixture == 4) Assert.AreEqual(1, reconstructed[30 * 64 + 30], "Diagonal wall was lost.");
            if (fixture == 5) Assert.AreEqual(0, reconstructed[20 * 64 + 21], "Thin annotation became a wall.");
            if (fixture == 6) Assert.AreEqual(1, reconstructed[11 * 64 + 32], "One-pixel wall interruption stayed open.");
            if (fixture == 7) Assert.AreEqual(0, reconstructed[25 * 64 + 32], "Parallel walls were merged.");
            if (fixture == 8) Assert.AreEqual(0, reconstructed[2 * 64 + 32], "Dimension line became a wall.");
            if (fixture == 9)
            {
                Assert.AreEqual(0, reconstructed[25 * 64 + 11], "The pale/open exterior segment was turned into a wall.");
                Assert.IsTrue(result.annotations.rooms[0].rasterRuns.Exists(run =>
                    run.minY <= 25 && run.maxY > 25 && run.minX <= 20 && run.maxX > 20), "Floor did not continue through an interrupted exterior row.");
            }
            if (fixture == 10)
            {
                Assert.AreEqual(0, reconstructed[52 * 64 + 30], "The pale bottom boundary was turned into a wall.");
                Assert.IsTrue(result.annotations.rooms[0].rasterRuns.Exists(run =>
                    run.minY <= 52 && run.maxY > 52 && run.minX <= 30 && run.maxX > 30), "Floor did not cross a pale bottom boundary section.");
            }
            if (fixture == 0)
            {
                var thickness = 0;
                for (var y = 7; y <= 16; y++) thickness += reconstructed[y * 64 + 20];
                Assert.GreaterOrEqual(thickness, 4, "Structural wall remained a thin thread.");
            }
        }

        [Test]
        public void RepeatedImportsAndAtoBtoAHaveIdenticalGeometry()
        {
            var a = Fixture(3); var b = Fixture(4);
            var baseline = Signature(Analyze(a));
            for (var i = 0; i < 20; i++) Assert.AreEqual(baseline, Signature(Analyze(a)), "Run " + i);
            Analyze(b);
            Assert.AreEqual(baseline, Signature(Analyze(a)), "A→B→A changed geometry.");
        }

        [Test]
        public void ThresholdIsStableAndDocumented()
        {
            var source = Fixture(0);
            var image = ToImage(source);
            image.luminance[(63 - 30) * 64 + 30] = DirectBlackMaskExtractor.BlackThreshold;
            Assert.AreEqual(1, AnalyzeImage(image).diagnostics.binaryBlackMask[30 * 64 + 30]);
            image.luminance[(63 - 30) * 64 + 30] = DirectBlackMaskExtractor.BlackThreshold + 1;
            Assert.AreEqual(0, AnalyzeImage(image).diagnostics.binaryBlackMask[30 * 64 + 30]);
        }

        [Test]
        public void BundledMaterialsUseSupportedUrpShaders()
        {
            foreach (var name in new[] { "ARNavFloor", "ARNavWall" })
            {
                var material = Resources.Load<Material>("Materials/" + name);
                Assert.IsNotNull(material, name + " is absent from Resources.");
                Assert.IsNotNull(material.shader);
                StringAssert.Contains("Universal Render Pipeline/Unlit", material.shader.name);
                Assert.IsTrue(material.shader.isSupported);
                Assert.IsTrue(material.HasProperty("_BaseMap"));
            }
        }

        [Test]
        public void TopViewShowsSourceImageUpAtScreenTop()
        {
            var rotation = CameraFramingUtility.SourceAlignedTopRotation();
            Assert.Greater(Vector3.Dot(rotation * Vector3.forward, Vector3.down), .999f);
            Assert.Greater(Vector3.Dot(rotation * Vector3.up, Vector3.forward), .999f);
            Assert.Greater(Vector3.Dot(rotation * Vector3.right, Vector3.right), .999f);
        }

        [Test]
        public void FilteredRasterBuildsOneFullHeightWallMeshAcrossRebuilds()
        {
            var extraction = Analyze(Fixture(1));
            var project = MapProjectData.Create("filtered-raster-test", "Filtered raster test");
            project.sourceImage.originalWidthPixels = 64; project.sourceImage.originalHeightPixels = 64;
            project.scaleCalibration.isValid = true; project.scaleCalibration.isEstimated = true;
            project.scaleCalibration.metresPerPixel = .03f;
            var floor = new FloorData { id = "floor-1", name = "Ground", sourceAnnotations = extraction.annotations };
            project.floors.Add(floor);
            Assert.IsTrue(PixelToLocalGeometryConverter.TryRegenerate(floor, project.scaleCalibration, 64, 64,
                new MapGenerationDefaults(), out var validation), string.Join(" ", validation.errors.ToArray()));
            var owner = new GameObject("FilteredRasterGenerationTest");
            var generator = owner.AddComponent<ProceduralBuildingGenerator>();
            try
            {
                for (var rebuild = 0; rebuild < 2; rebuild++)
                {
                    Assert.IsTrue(generator.Generate(project, out var error), error);
                    Assert.AreEqual(1f, generator.GeneratedRoot.transform.localScale.z, .0001f);
                    Assert.AreEqual(2.7f, generator.GeneratedBounds.size.y, .0001f);
                    var wallCount = 0;
                    foreach (var renderer in generator.GeneratedRoot.GetComponentsInChildren<MeshRenderer>())
                        if (renderer.name == "RasterWallMaskFallback") wallCount++;
                    Assert.AreEqual(1, wallCount, "Rebuild duplicated or omitted the wall mesh.");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        private static FloorPlanExtractionResult Analyze(byte[] mask) { return AnalyzeImage(ToImage(mask)); }
        private static FloorPlanExtractionResult AnalyzeImage(FloorPlanImageData image)
        { return new DirectBlackMaskExtractor().Extract(image, FloorPlanCropRegion.Full(64, 64)); }
        private static FloorPlanImageData ToImage(byte[] mask)
        {
            var pixels = new byte[mask.Length];
            for (var y = 0; y < 64; y++) for (var x = 0; x < 64; x++) pixels[(63 - y) * 64 + x] = mask[y * 64 + x] != 0 ? (byte)0 : (byte)255;
            return new FloorPlanImageData(64, 64, pixels);
        }
        private static byte[] Fixture(int variant)
        {
            var pixels = new byte[64 * 64];
            void Line(int x0, int y0, int x1, int y1)
            {
                var dx = Math.Abs(x1 - x0); var dy = Math.Abs(y1 - y0);
                for (var n = 0; n <= Math.Max(dx, dy); n++)
                { var x = x0 + (x1 - x0) * n / Math.Max(1, Math.Max(dx, dy)); var y = y0 + (y1 - y0) * n / Math.Max(1, Math.Max(dx, dy)); pixels[y * 64 + x] = 1; }
            }
            for (var thick = 0; thick < 3; thick++)
            {
                Line(10, 10 + thick, 53, 10 + thick); Line(10, 51 + thick, 53, 51 + thick);
                Line(10 + thick, 10, 10 + thick, 53); Line(51 + thick, 10, 51 + thick, 53);
            }
            if (variant == 1) for (var x = 30; x <= 32; x++) Line(x, 10, x, 53);
            if (variant == 2) for (var y = 10; y <= 12; y++) for (var x = 28; x <= 36; x++) pixels[y * 64 + x] = 0;
            if (variant == 3)
            {
                for (var y = 30; y <= 53; y++) for (var x = 34; x <= 53; x++) pixels[y * 64 + x] = 0;
                for (var thick = 0; thick < 3; thick++)
                { Line(34 + thick, 30, 34 + thick, 53); Line(34, 30 + thick, 53, 30 + thick); }
            }
            if (variant == 4) for (var offset = -1; offset <= 1; offset++) Line(15 + offset, 45, 44 + offset, 16);
            if (variant == 5) Line(18, 20, 25, 20);
            if (variant == 6) for (var y = 10; y <= 12; y++) pixels[y * 64 + 32] = 0;
            if (variant == 7) for (var y = 20; y <= 30; y++) if (y <= 22 || y >= 28)
                for (var x = 17; x <= 47; x++) pixels[y * 64 + x] = 1;
            if (variant == 8) Line(3, 2, 60, 2);
            if (variant == 9) for (var y = 18; y <= 36; y++) for (var x = 10; x <= 12; x++) pixels[y * 64 + x] = 0;
            if (variant == 10) for (var y = 51; y <= 53; y++) for (var x = 20; x <= 40; x++) pixels[y * 64 + x] = 0;
            return pixels;
        }
        private static string Signature(FloorPlanExtractionResult result)
        {
            var text = new System.Text.StringBuilder();
            foreach (var run in result.annotations.wallRuns) text.Append(run.minX).Append(',').Append(run.minY).Append(',').Append(run.maxX).Append(',').Append(run.maxY).Append(';');
            text.Append('|');
            foreach (var run in result.annotations.rooms[0].rasterRuns) text.Append(run.minX).Append(',').Append(run.minY).Append(',').Append(run.maxX).Append(',').Append(run.maxY).Append(';');
            return text.ToString();
        }
    }
}
