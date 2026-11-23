using System;
using ARNav.Runtime.Extraction;
using NUnit.Framework;
using UnityEngine;

namespace ARNav.Tests.EditMode
{
    public sealed class OpenCvFloorPlanExtractorTests
    {
        [Test]
        public void NativeMasksPreserveDoorGapAndExcludeFurniture()
        {
            const int width = 48, height = 40;
            var bytes = new byte[32 + 2 * width * height];
            Write(bytes, 0, 0x4f435650); Write(bytes, 4, 1);
            Write(bytes, 8, 2); Write(bytes, 12, 3);
            Write(bytes, 16, 44); Write(bytes, 20, 34); Write(bytes, 24, 4);
            for (var y = 6; y <= 32; y++)
                for (var x = 5; x <= 42; x++)
                {
                    var exterior = x == 5 || x == 42 || y == 6 || y == 32;
                    var partition = x == 24 && y != 18 && y != 19;
                    if (exterior || partition) bytes[32 + y * width + x] = 255;
                    else bytes[32 + width * height + y * width + x] = 255;
                }
            var result = OpenCvFloorPlanExtractor.ParseNativeResult(bytes, width, height);
            Assert.That(result.succeeded, Is.True, result.message);
            Assert.That(result.annotations.rooms.Count, Is.EqualTo(1));
            Assert.That(result.annotations.wallPathDiagnostics.estimatedWidthPixels, Is.EqualTo(4));
            Assert.That(result.annotations.wallPaths, Is.Empty);
            Assert.That(result.annotations.wallRuns.Exists(run => run.minX <= 24 && run.maxX > 24 && run.minY <= 18 && run.maxY > 18), Is.False, "Door gap should stay open.");
            Assert.That(result.annotations.wallRuns.Exists(run => run.minX <= 12 && run.maxX > 12 && run.minY <= 12 && run.maxY > 12), Is.False, "Furniture should not be generated from the clean native mask.");
            Assert.That(result.annotations.rooms[0].rasterRuns.Count, Is.GreaterThan(0));
        }

        [Test]
        public void InvalidNativeResultFailsWithoutGeometry()
        {
            var result = OpenCvFloorPlanExtractor.ParseNativeResult(new byte[17], 48, 40);
            Assert.That(result.succeeded, Is.False);
            Assert.That(result.annotations.wallRuns, Is.Empty);
        }

        [Test]
        public void UnityBottomRowsBecomeOpenCvTopRows()
        {
            var pixels = new[]
            {
                new Color32(5, 20, 10, 255), new Color32(25, 10, 5, 255),
                new Color32(30, 40, 35, 255), new Color32(60, 50, 55, 255)
            };
            var image = new FloorPlanImageData(2, 2, new byte[4], pixels);
            Assert.That(OpenCvFloorPlanExtractor.PrepareTopLeftBlackness(image), Is.EqualTo(new byte[] { 40, 60, 20, 25 }));
        }

        private static void Write(byte[] bytes, int offset, int value)
        {
            Array.Copy(BitConverter.GetBytes(value), 0, bytes, offset, 4);
        }
    }
}
