using System.IO;
using ARNav.Runtime.Extraction;
using NUnit.Framework;
using UnityEngine;

namespace ARNav.Tests.EditMode
{
    public sealed class RealBlackMaskRegressionTests
    {
        [Test]
        public void LandscapeRetainsBedroomBathroomAndExteriorBands()
        {
            var result = Extract("f1.jpeg"); var mask = result.diagnostics.architecturalWallMask;
            Assert.Greater(Coverage(mask, 640, 475, 274, 602, 274, 2), .75f, "Bedroom divider is absent.");
            Assert.Greater(Coverage(mask, 640, 423, 70, 423, 196, 3), .75f, "Bathroom partition is absent.");
            Assert.Greater(Coverage(mask, 640, 150, 63, 410, 63, 3), .9f, "Top exterior wall is fragmented.");
            Assert.AreEqual(0, mask[205 * 640 + 480], "Bathroom doorway was closed.");
        }

        [Test]
        public void PortraitRetainsRoomNetworkAndExcludesTitleBlock()
        {
            var result = Extract("fp2.jpeg"); var mask = result.diagnostics.architecturalWallMask;
            Assert.Greater(Coverage(mask, 477, 68, 207, 325, 207, 3), .65f, "Main interior partition is absent.");
            Assert.Greater(Coverage(mask, 477, 325, 76, 325, 200, 3), .65f, "Right exterior wall is absent.");
            Assert.AreEqual(0, mask[200 * 477 + 400], "Title block became a wall.");
        }

        private static FloorPlanExtractionResult Extract(string name)
        {
            var path = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "current-input", name);
            if (!File.Exists(path)) path = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Downloads", name);
            Assert.IsTrue(File.Exists(path), "Real-plan fixture is not installed: " + path);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.IsTrue(texture.LoadImage(File.ReadAllBytes(path)));
                var colors = texture.GetPixels32();
                var result = new DirectBlackMaskExtractor().Extract(
                    new FloorPlanImageData(texture.width, texture.height, new byte[colors.Length], colors),
                    FloorPlanCropRegion.Full(texture.width, texture.height));
                Assert.IsTrue(result.succeeded, result.message);
                return result;
            }
            finally { Object.DestroyImmediate(texture); }
        }

        private static float Coverage(byte[] mask, int width, int x0, int y0, int x1, int y1, int radius)
        {
            var steps = System.Math.Max(System.Math.Abs(x1 - x0), System.Math.Abs(y1 - y0));
            var hits = 0;
            for (var step = 0; step <= steps; step++)
            {
                var x = x0 + (x1 - x0) * step / System.Math.Max(1, steps);
                var y = y0 + (y1 - y0) * step / System.Math.Max(1, steps);
                var found = false;
                for (var dy = -radius; dy <= radius && !found; dy++) for (var dx = -radius; dx <= radius; dx++)
                    if (mask[(y + dy) * width + x + dx] != 0) { found = true; break; }
                if (found) hits++;
            }
            return hits / (float)(steps + 1);
        }
    }
}
