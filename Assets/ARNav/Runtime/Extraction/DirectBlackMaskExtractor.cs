using System;
using System.Collections.Generic;
using ARNav.Core.Data;
using ARNav.Core.Geometry;
using UnityEngine;

namespace ARNav.Runtime.Extraction
{
    /// <summary>Deterministic black candidates followed by a compact structural-stroke filter.</summary>
    public sealed class DirectBlackMaskExtractor : IFloorPlanExtractor
    {
        public const byte BlackThreshold = 150;

        public FloorPlanExtractionResult Extract(FloorPlanImageData image, FloorPlanCropRegion region)
        {
            var result = new FloorPlanExtractionResult { analyzedRegion = region };
            if (image == null || !region.IsValid(image.width, image.height))
            { result.message = "Invalid floorplan image."; return result; }
            var width = image.width; var height = image.height;
            var mask = new byte[width * height];
            var boundaryEvidence = new byte[mask.Length];
            var histogram = new int[256];
            for (var i = 0; i < image.luminance.Length; i++) histogram[image.luminance[i]]++;
            var tenth = Percentile(histogram, image.luminance.Length / 10);
            var background = Percentile(histogram, image.luminance.Length * 9 / 10);
            // Only faded, low-contrast monochrome scans need a lifted threshold.
            // Ordinary white-page drawings keep the documented black threshold.
            var threshold = background < 235 && tenth > 100 ? Math.Min(205, Math.Max(BlackThreshold, background - 30)) : BlackThreshold;
            result.annotations.wallThreshold = threshold;
            result.diagnostics.grayscaleThreshold = threshold;
            image.ReportStage(FloorPlanAnalysisStage.DetectingWalls);
            for (var y = 0; y < height; y++)
            {
                image.ThrowIfCancellationRequested();
                var sourceRow = (height - 1 - y) * width;
                var targetRow = y * width;
                for (var x = 0; x < width; x++)
                {
                    var index = sourceRow + x;
                    var value = image.luminance[index];
                    if (image.colorPixels != null)
                    {
                        var c = image.colorPixels[index];
                        value = (byte)Math.Min(255, Math.Max(0, (int)(c.r * .2126f + c.g * .7152f + c.b * .0722f + .5f)));
                    }
                    if (value <= Math.Min(240, background - 12)) boundaryEvidence[targetRow + x] = 1;
                    if (value <= threshold)
                    {
                        if (image.colorPixels != null)
                        {
                            var c = image.colorPixels[index];
                            var highest = Math.Max(c.r, Math.Max(c.g, c.b));
                            var lowest = Math.Min(c.r, Math.Min(c.g, c.b));
                            if (highest - lowest > 65 && value > 60) continue;
                        }
                        mask[targetRow + x] = 1;
                    }
                }
            }
            result.diagnostics.binaryBlackMask = mask;
            var initialWalls = StructuralBlackMaskFilter.Process(mask, width, height, out var typicalWidth);
            var walls = StructuralTopologyRefiner.Process(initialWalls, mask, width, height, typicalWidth, out var rejected);
            result.diagnostics.rejectedObjectMask = rejected;
            result.diagnostics.textExclusionMask = rejected;
            result.annotations.rejectedObjectRuns = BuildRuns(rejected, width, height);
            result.annotations.initialBlackPixelCount = CountPixels(mask);
            result.annotations.textExclusionPixelCount = (int)CountPixels(rejected);
            result.diagnostics.architecturalWallMask = walls;
            result.diagnostics.estimatedWallThicknessPixels = typicalWidth;
            result.annotations.wallPathDiagnostics.estimatedWidthPixels = typicalWidth;
            result.annotations.wallRuns = BuildRuns(walls, width, height);
            long wallPixels = 0;
            foreach (var run in result.annotations.wallRuns) wallPixels += (long)(run.maxX - run.minX) * (long)(run.maxY - run.minY);
            result.annotations.architecturalWallPixelCount = wallPixels;
            if (wallPixels == 0) { result.message = "No supported black wall bands were found. Architectural walls must be black."; return result; }

            image.ReportStage(FloorPlanAnalysisStage.DetectingRoomRegions);
            FloorRegionAnalyzer.Analyze(walls, FilterBoundaryEvidence(boundaryEvidence, width, height, Math.Max(8, typicalWidth * 3)),
                width, height, typicalWidth, result, image.ThrowIfCancellationRequested);
            result.annotations.exteriorRuns = BuildRuns(result.diagnostics.exteriorMask, width, height);
            result.annotations.voidRuns = BuildRuns(result.diagnostics.voidMask, width, height);
            result.annotations.openingRuns = BuildRuns(result.diagnostics.openingMask, width, height);
            if (result.annotations.rooms.Count == 0)
            {
                result.message = "Black strokes were found, but no enclosed floor region could be derived.";
                return result;
            }
            result.succeeded = true;
            result.confidence = .7f;
            result.message = "Architectural wall networks and enclosed floor regions detected; review ambiguous objects and open boundaries.";
            return result;
        }

        internal static List<PixelRasterRun> BuildRuns(byte[] mask, int width, int height)
        {
            var runs = new List<PixelRasterRun>();
            var previous = new Dictionary<long, PixelRasterRun>();
            for (var y = 0; y < height; y++)
            {
                var current = new Dictionary<long, PixelRasterRun>();
                for (var x = 0; x < width;)
                {
                    while (x < width && mask[y * width + x] == 0) x++;
                    if (x >= width) break;
                    var start = x;
                    while (x < width && mask[y * width + x] != 0) x++;
                    var key = ((long)start << 32) | (uint)x;
                    if (previous.TryGetValue(key, out var prior) && prior.maxY == y) prior.maxY = y + 1;
                    else { prior = new PixelRasterRun { minX = start, maxX = x, minY = y, maxY = y + 1 }; runs.Add(prior); }
                    current[key] = prior;
                }
                previous = current;
            }
            return runs;
        }

        private static long CountPixels(byte[] mask)
        { long count = 0; foreach (var value in mask) if (value != 0) count++; return count; }

        private static int Percentile(int[] histogram, int rank)
        { var count = 0; for (var i = 0; i < histogram.Length; i++) { count += histogram[i]; if (count > rank) return i; } return 255; }

        private static byte[] FilterBoundaryEvidence(byte[] candidate, int width, int height, int minimumRun)
        {
            var supported = new byte[candidate.Length];
            for (var y = 0; y < height; y++) for (var x = 0; x < width;)
            {
                if (candidate[y * width + x] == 0) { x++; continue; }
                var start = x; while (x < width && candidate[y * width + x] != 0) x++;
                if (x - start >= minimumRun) for (var xx = start; xx < x; xx++) supported[y * width + xx] = 1;
            }
            for (var x = 0; x < width; x++) for (var y = 0; y < height;)
            {
                if (candidate[y * width + x] == 0) { y++; continue; }
                var start = y; while (y < height && candidate[y * width + x] != 0) y++;
                if (y - start >= minimumRun) for (var yy = start; yy < y; yy++) supported[yy * width + x] = 1;
            }
            return supported;
        }
    }
}
