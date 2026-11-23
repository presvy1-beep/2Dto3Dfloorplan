using System;
using System.Collections.Generic;
using ARNav.Core.Data;
using ARNav.Core.Geometry;
using UnityEngine;

namespace ARNav.Runtime.Extraction
{
    /// <summary>Deterministic color-only conversion. Every dark pixel is wall material, including dark annotations.</summary>
    public sealed class DirectBlackMaskExtractor : IFloorPlanExtractor
    {
        public const byte BlackThreshold = 96;

        public FloorPlanExtractionResult Extract(FloorPlanImageData image, FloorPlanCropRegion region)
        {
            var result = new FloorPlanExtractionResult { analyzedRegion = region };
            if (image == null || !region.IsValid(image.width, image.height))
            { result.message = "Invalid floorplan image."; return result; }
            var width = image.width; var height = image.height;
            var mask = new byte[width * height];
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
                    if (value <= BlackThreshold) mask[targetRow + x] = 1;
                }
            }
            result.annotations.wallRuns = BuildRuns(mask, width, height);
            result.annotations.wallThreshold = BlackThreshold;
            long wallPixels = 0;
            foreach (var run in result.annotations.wallRuns) wallPixels += (long)(run.maxX - run.minX) * (long)(run.maxY - run.minY);
            result.annotations.architecturalWallPixelCount = wallPixels;
            if (wallPixels == 0) { result.message = "No black pixels were found. Architectural walls must be black."; return result; }

            // The outermost dark pixel on each occupied row defines a scanline of
            // floor occupancy. This follows L-shaped outlines without a full-page
            // rectangle. Wall pixels are omitted; door gaps remain open in the wall mask.
            image.ReportStage(FloorPlanAnalysisStage.DetectingRoomRegions);
            var floor = new byte[mask.Length];
            var minX = width; var maxX = 0; var minY = height; var maxY = 0;
            for (var y = 0; y < height; y++)
            {
                image.ThrowIfCancellationRequested();
                var left = width; var right = -1;
                for (var x = 0; x < width; x++) if (mask[y * width + x] != 0) { left = Math.Min(left, x); right = x; }
                if (right - left < 2) continue;
                minX = Math.Min(minX, left); maxX = Math.Max(maxX, right + 1);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y + 1);
                for (var x = left; x <= right; x++) if (mask[y * width + x] == 0) floor[y * width + x] = 1;
            }
            var floorRuns = BuildRuns(floor, width, height);
            if (floorRuns.Count == 0) { result.message = "Black strokes were found, but no floor region could be derived."; return result; }
            var room = new RoomAnnotationData
            {
                id = "direct_floor_1", displayName = "Estimated floor", category = "Detected",
                regionType = "Room", walkable = true, destinationSelectable = false,
                detectionConfidence = .5f, preferRasterGeometry = true, rasterRuns = floorRuns
            };
            room.vertices.Add(new PixelPoint(minX, minY)); room.vertices.Add(new PixelPoint(maxX, minY));
            room.vertices.Add(new PixelPoint(maxX, maxY)); room.vertices.Add(new PixelPoint(minX, maxY));
            result.annotations.rooms.Add(room);
            result.succeeded = true;
            result.confidence = .5f;
            result.message = "Black pixels extruded as walls. Estimated scale — dimensions are approximate; dark furniture and text may also be extruded.";
            return result;
        }

        private static List<PixelRasterRun> BuildRuns(byte[] mask, int width, int height)
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
    }
}
