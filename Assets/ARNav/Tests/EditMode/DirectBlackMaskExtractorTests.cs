using System;
using ARNav.Runtime.Extraction;
using NUnit.Framework;
using UnityEngine;

namespace ARNav.Tests.EditMode
{
    public sealed class DirectBlackMaskExtractorTests
    {
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        [TestCase(4)] [TestCase(5)]
        public void GeometryPreservesTheExactBlackMask(int fixture)
        {
            var source = Fixture(fixture);
            var result = Analyze(source);
            Assert.IsTrue(result.succeeded, result.message);
            var reconstructed = new byte[source.Length];
            foreach (var run in result.annotations.wallRuns)
                for (var y = (int)run.minY; y < run.maxY; y++)
                    for (var x = (int)run.minX; x < run.maxX; x++) reconstructed[y * 64 + x] = 1;
            CollectionAssert.AreEqual(source, reconstructed, "Merged wall runs changed black geometry.");
            Assert.Greater(result.annotations.rooms[0].rasterRuns.Count, 0);
            if (fixture == 2) Assert.AreEqual(0, reconstructed[10 * 64 + 32], "Door gap closed.");
            if (fixture == 3) Assert.IsFalse(result.annotations.rooms[0].rasterRuns.Exists(run =>
                run.minY <= 45 && run.maxY > 45 && run.minX <= 45 && run.maxX > 45), "L-shaped exterior void became floor.");
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
            Assert.IsTrue(AnalyzeImage(image).annotations.wallRuns.Exists(run => run.minX <= 30 && run.maxX > 30 && run.minY <= 30 && run.maxY > 30));
            image.luminance[(63 - 30) * 64 + 30] = DirectBlackMaskExtractor.BlackThreshold + 1;
            Assert.IsFalse(AnalyzeImage(image).annotations.wallRuns.Exists(run => run.minX <= 30 && run.maxX > 30 && run.minY <= 30 && run.maxY > 30));
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
            if (variant == 4) Line(15, 45, 44, 16);
            if (variant == 5) Line(18, 20, 25, 20);
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
