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
            result.diagnostics.binaryBlackMask = mask;
            var walls = StructuralBlackMaskFilter.Process(mask, width, height, out var typicalWidth);
            result.diagnostics.architecturalWallMask = walls;
            result.diagnostics.estimatedWallThicknessPixels = typicalWidth;
            result.annotations.wallPathDiagnostics.estimatedWidthPixels = typicalWidth;
            result.annotations.wallRuns = BuildRuns(walls, width, height);
            result.annotations.wallThreshold = BlackThreshold;
            long wallPixels = 0;
            foreach (var run in result.annotations.wallRuns) wallPixels += (long)(run.maxX - run.minX) * (long)(run.maxY - run.minY);
            result.annotations.architecturalWallPixelCount = wallPixels;
            if (wallPixels == 0) { result.message = "No supported black wall bands were found. Architectural walls must be black."; return result; }

            // Derive floor scanlines from supported wall spans. JPEGs can have a
            // pale exterior segment while the surrounding rows still establish
            // the building bay; bridge only between valid wall-supported rows.
            // Existing narrower spans remain intact for L-shaped footprints.
            image.ReportStage(FloorPlanAnalysisStage.DetectingRoomRegions);
            var floor = new byte[mask.Length];
            var minX = width; var maxX = 0; var minY = height; var maxY = 0;
            var leftEdge = new int[height]; var rightEdge = new int[height];
            var minimumSpan = Math.Max(8, Math.Min(width, height) / 4);
            for (var y = 0; y < height; y++)
            {
                image.ThrowIfCancellationRequested();
                var left = width; var right = -1;
                for (var x = 0; x < width; x++) if (walls[y * width + x] != 0) { left = Math.Min(left, x); right = x; }
                leftEdge[y] = right - left >= minimumSpan ? left : width;
                rightEdge[y] = right - left >= minimumSpan ? right : -1;
            }
            var maximumBridge = Math.Max(4, Math.Min(width, height) / 2);
            for (var y = 0; y < height; y++)
            {
                image.ThrowIfCancellationRequested();
                var left = leftEdge[y]; var right = rightEdge[y];
                if (right < left)
                {
                    var above = y - 1; var below = y + 1;
                    while (above >= 0 && y - above <= maximumBridge && rightEdge[above] < leftEdge[above]) above--;
                    while (below < height && below - y <= maximumBridge && rightEdge[below] < leftEdge[below]) below++;
                    if (above < 0 || below >= height || y - above > maximumBridge || below - y > maximumBridge) continue;
                    left = Math.Min(leftEdge[above], leftEdge[below]);
                    right = Math.Max(rightEdge[above], rightEdge[below]);
                }
                minX = Math.Min(minX, left); maxX = Math.Max(maxX, right + 1);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y + 1);
                for (var x = left; x <= right; x++) if (walls[y * width + x] == 0) floor[y * width + x] = 1;
            }
            // Long horizontal perimeter bands can bound a room even when a
            // pale vertical wall disappears from the black mask. Fill between
            // the top and bottom structural hits in each supported column.
            // This also keeps the outside corner of an L-shaped plan empty.
            var topEdge = new int[width]; var bottomEdge = new int[width];
            for (var x = 0; x < width; x++)
            {
                if ((x & 63) == 0) image.ThrowIfCancellationRequested();
                var top = height; var bottom = -1;
                for (var y = 0; y < height; y++) if (walls[y * width + x] != 0)
                { top = Math.Min(top, y); bottom = y; }
                topEdge[x] = bottom - top >= minimumSpan ? top : height;
                bottomEdge[x] = bottom - top >= minimumSpan ? bottom : -1;
            }
            for (var x = 0; x < width; x++)
            {
                if ((x & 63) == 0) image.ThrowIfCancellationRequested();
                var top = topEdge[x]; var bottom = bottomEdge[x];
                if (bottom < top)
                {
                    var left = x - 1; var right = x + 1;
                    while (left >= 0 && x - left <= maximumBridge && bottomEdge[left] < topEdge[left]) left--;
                    while (right < width && right - x <= maximumBridge && bottomEdge[right] < topEdge[right]) right++;
                    if (left < 0 || right >= width || x - left > maximumBridge || right - x > maximumBridge) continue;
                    top = Math.Min(topEdge[left], topEdge[right]);
                    bottom = Math.Max(bottomEdge[left], bottomEdge[right]);
                }
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x + 1);
                minY = Math.Min(minY, top); maxY = Math.Max(maxY, bottom + 1);
                for (var y = top; y <= bottom; y++) if (walls[y * width + x] == 0) floor[y * width + x] = 1;
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
            result.message = "Supported black wall bands extruded. Estimated scale — dimensions are approximate; dark furniture and text can still be misclassified.";
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
