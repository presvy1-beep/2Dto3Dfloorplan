using System;
using System.Collections.Generic;
using ARNav.Core.Data;
using ARNav.Core.Geometry;
using UnityEngine;

namespace ARNav.Runtime.Extraction
{
    /// <summary>
    /// Converts mostly monochrome drawings to compact wall-mask runs. It removes
    /// disconnected glyph-sized component clusters; it deliberately never erases
    /// pixels from a large connected wall component.
    /// </summary>
    public static class ArchitecturalWallMaskProcessor
    {
        private sealed class Component
        {
            public readonly List<int> pixels = new List<int>();
            public int minX = int.MaxValue, minY = int.MaxValue, maxX, maxY;
            public bool textCandidate;
            public float CenterX { get { return (minX + maxX) * .5f; } }
            public float CenterY { get { return (minY + maxY) * .5f; } }
            public int Width { get { return maxX - minX + 1; } }
            public int Height { get { return maxY - minY + 1; } }
        }

        public static void Process(FloorPlanImageData image, FloorPlanCropRegion region, FloorPlanAnnotationData output, FloorPlanExtractionDiagnostics diagnosticMasks = null)
        {
            if (image == null || output == null || !region.IsValid(image.width, image.height)) return;
            var threshold = OtsuThreshold(image, region);
            var mask = new bool[image.width * image.height];
            long initialBlack = 0;
            Color32 bg; var hasColoredBackground = TryFindColoredBorderBackground(image, region, out bg);
            for (var y = region.y; y < region.y + region.height; y++) for (var x = region.x; x < region.x + region.width; x++)
            {
                var color = image.GetColor(x, y);
                var background = hasColoredBackground && ColorDistanceSquared(color, bg) <= 38 * 38 * 3;
                if (!background && image.Get(x, y) <= threshold) { mask[y * image.width + x] = true; initialBlack++; }
            }

            if (diagnosticMasks != null)
            {
                diagnosticMasks.binaryBlackMask = new byte[mask.Length];
                for (var i = 0; i < mask.Length; i++) if (mask[i]) diagnosticMasks.binaryBlackMask[i] = 255;
            }
            var minDimension = Math.Min(region.width, region.height);
            // Long continuous line evidence protects wall strokes. Removing these
            // pixels before component analysis also separates glyph tails that touch
            // a wall, while a small dilation retains the source wall thickness.
            var protectedLines = FindArchitecturalLinePixels(mask, image.width, image.height, region, minDimension);
            var radius = Mathf.Clamp(Mathf.RoundToInt(minDimension / 500f), 1, 4);
            var protectedMask = Dilate(protectedLines, image.width, image.height, region, radius);
            var residual = new bool[mask.Length]; for (var i = 0; i < mask.Length; i++) residual[i] = mask[i] && !protectedMask[i];

            var labels = new int[residual.Length]; for (var i = 0; i < labels.Length; i++) labels[i] = -1;
            var components = new List<Component>(); var queue = new int[mask.Length];
            for (var y = region.y; y < region.y + region.height; y++) for (var x = region.x; x < region.x + region.width; x++)
            {
                var seed = y * image.width + x; if (!residual[seed] || labels[seed] >= 0) continue;
                var id = components.Count; var component = new Component(); components.Add(component); var head = 0; var tail = 0; queue[tail++] = seed; labels[seed] = id;
                while (head < tail)
                {
                    var current = queue[head++]; component.pixels.Add(current); var px = current % image.width; var py = current / image.width;
                    component.minX = Math.Min(component.minX, px); component.maxX = Math.Max(component.maxX, px); component.minY = Math.Min(component.minY, py); component.maxY = Math.Max(component.maxY, py);
                    for (var oy = -1; oy <= 1; oy++) for (var ox = -1; ox <= 1; ox++)
                    {
                        if (ox == 0 && oy == 0) continue; var nx = px + ox; var ny = py + oy;
                        if (nx < region.x || ny < region.y || nx >= region.x + region.width || ny >= region.y + region.height) continue;
                        var ni = ny * image.width + nx; if (!residual[ni] || labels[ni] >= 0) continue; labels[ni] = id; queue[tail++] = ni;
                    }
                }
            }

            var glyphMaxDimension = Mathf.Max(18, Mathf.RoundToInt(minDimension * .055f));
            // A single raster glyph can contain many black pixels (especially at
            // display resolutions); cap by compact bounds, not an unrealistically
            // small absolute pixel budget. A short structural stroke still survives
            // because isolated non-glyph candidates are removed only below the
            // separate tiny-component threshold below.
            var glyphMaxArea = Mathf.Max(120, Mathf.RoundToInt(image.width * image.height * .003f));
            foreach (var component in components)
                component.textCandidate = component.Width <= glyphMaxDimension && component.Height <= glyphMaxDimension && component.pixels.Count <= glyphMaxArea;

            // A word/number is a spatial group of similarly sized glyph components.
            // Small isolated glyphs are also excluded; longer/large wall fragments and
            // the primary connected wall network remain untouched.
            var removeComponent = new bool[components.Count]; var visited = new bool[components.Count]; var textComponents = 0; var removedPixels = 0;
            for (var i = 0; i < components.Count; i++)
            {
                if (visited[i] || !components[i].textCandidate) continue;
                var cluster = new List<int>(); var pending = new Queue<int>(); pending.Enqueue(i); visited[i] = true;
                while (pending.Count > 0)
                {
                    var current = pending.Dequeue(); cluster.Add(current); var a = components[current];
                    for (var j = 0; j < components.Count; j++)
                    {
                        if (visited[j] || !components[j].textCandidate) continue; var b = components[j];
                        var gapX = Math.Max(0, Math.Max(a.minX - b.maxX, b.minX - a.maxX) - 1);
                        var verticalOverlap = Math.Max(0, Math.Min(a.maxY, b.maxY) - Math.Max(a.minY, b.minY) + 1);
                        var aligned = verticalOverlap >= Math.Min(a.Height, b.Height) * .35f;
                        var near = gapX <= Math.Max(a.Height, b.Height) * 1.6f && Mathf.Abs(a.CenterY - b.CenterY) <= Math.Max(a.Height, b.Height) * .7f;
                        if (near && aligned) { visited[j] = true; pending.Enqueue(j); }
                    }
                }
                var groupPixels = 0; foreach (var c in cluster) groupPixels += components[c].pixels.Count;
                // Remove groups of two or more glyph-like marks, plus very small
                // isolated marks. Preserve a short structural stroke when it is large
                // enough or part of a larger connected wall component.
                var remove = cluster.Count >= 2 || groupPixels <= Mathf.Max(20, image.width * image.height / 40000);
                if (!remove) continue;
                foreach (var c in cluster) { removeComponent[c] = true; textComponents++; removedPixels += components[c].pixels.Count; }
            }

            if (diagnosticMasks != null) diagnosticMasks.textExclusionMask = new byte[mask.Length];
            for (var i = 0; i < components.Count; i++) if (removeComponent[i]) foreach (var pixel in components[i].pixels)
            {
                mask[pixel] = false;
                if (diagnosticMasks != null) diagnosticMasks.textExclusionMask[pixel] = 255;
            }
            if (diagnosticMasks != null)
            {
                diagnosticMasks.architecturalWallMask = new byte[mask.Length];
                for (var i = 0; i < mask.Length; i++) if (mask[i]) diagnosticMasks.architecturalWallMask[i] = 255;
            }
            output.wallRuns = BuildRuns(mask, image.width, image.height, region);
            // wallRuns remains the lossless cleaned-mask representation. The ordered
            // graph paths are a derived field generated here and therefore survive the
            // extraction save/reload before BUILD 3D sees the project.
            output.wallPaths = WallPathExtractor.Extract(output.wallRuns, image.width, image.height, out var pathDiagnostics);
            output.wallPathDiagnostics = pathDiagnostics;
            output.wallThreshold = threshold;
            output.textComponentCount = textComponents;
            output.textExclusionPixelCount = removedPixels;
            output.wallContinuityRepairCount = pathDiagnostics.joinedGaps;
            output.initialBlackPixelCount = initialBlack;
            long kept = 0; foreach (var run in output.wallRuns) kept += (long)(run.maxX - run.minX) * (long)(run.maxY - run.minY);
            output.architecturalWallPixelCount = kept;
        }

        private static List<PixelRasterRun> BuildRuns(bool[] mask, int width, int height, FloorPlanCropRegion region)
        {
            var rectangles = new List<PixelRasterRun>(); var active = new Dictionary<string, PixelRasterRun>();
            for (var y = region.y; y < region.y + region.height; y++)
            {
                var current = new Dictionary<string, PixelRasterRun>(); var x = region.x;
                while (x < region.x + region.width)
                {
                    while (x < region.x + region.width && !mask[y * width + x]) x++;
                    if (x >= region.x + region.width) break; var start = x;
                    while (x + 1 < region.x + region.width && mask[y * width + x + 1]) x++;
                    var end = x; var key = start.ToString() + ":" + end.ToString(); PixelRasterRun run;
                    if (active.TryGetValue(key, out var prior) && Mathf.Abs(prior.maxY - y) < .01f) { prior.maxY = y + 1; run = prior; }
                    else { run = new PixelRasterRun { minX = start, maxX = end + 1, minY = y, maxY = y + 1 }; rectangles.Add(run); }
                    current[key] = run; x++;
                }
                active = current;
            }
            return rectangles;
        }

        private static bool[] FindArchitecturalLinePixels(bool[] mask, int width, int height, FloorPlanCropRegion region, int minDimension)
        {
            var protectedPixels = new bool[mask.Length]; var minLength = Mathf.Max(24, Mathf.RoundToInt(minDimension * .06f));
            var directions = new[] { new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1), new Vector2Int(1, -1) };
            foreach (var direction in directions)
            {
                for (var y = region.y; y < region.y + region.height; y++) for (var x = region.x; x < region.x + region.width; x++)
                {
                    var index = y * width + x; if (!mask[index]) continue;
                    var px = x - direction.x; var py = y - direction.y;
                    if (px >= region.x && py >= region.y && px < region.x + region.width && py < region.y + region.height && mask[py * width + px]) continue;
                    var length = 0; var nx = x; var ny = y;
                    while (nx >= region.x && ny >= region.y && nx < region.x + region.width && ny < region.y + region.height && mask[ny * width + nx]) { length++; nx += direction.x; ny += direction.y; }
                    if (length < minLength) continue;
                    nx = x; ny = y; while (nx >= region.x && ny >= region.y && nx < region.x + region.width && ny < region.y + region.height && mask[ny * width + nx]) { protectedPixels[ny * width + nx] = true; nx += direction.x; ny += direction.y; }
                }
            }
            return protectedPixels;
        }

        private static bool[] Dilate(bool[] source, int width, int height, FloorPlanCropRegion region, int radius)
        {
            var output = new bool[source.Length];
            for (var y = region.y; y < region.y + region.height; y++) for (var x = region.x; x < region.x + region.width; x++) if (source[y * width + x])
                for (var oy = -radius; oy <= radius; oy++) for (var ox = -radius; ox <= radius; ox++)
                { var nx = x + ox; var ny = y + oy; if (nx >= region.x && ny >= region.y && nx < region.x + region.width && ny < region.y + region.height) output[ny * width + nx] = true; }
            return output;
        }

        private static int OtsuThreshold(FloorPlanImageData image, FloorPlanCropRegion region)
        {
            var histogram = new long[256]; long total = 0, sum = 0;
            for (var y = region.y; y < region.y + region.height; y++) for (var x = region.x; x < region.x + region.width; x++) { var v = image.Get(x, y); histogram[v]++; total++; sum += v; }
            long backgroundCount = 0, backgroundSum = 0; double bestVariance = -1; var threshold = 160;
            for (var t = 0; t < 255; t++)
            {
                backgroundCount += histogram[t]; if (backgroundCount == 0) continue; var foregroundCount = total - backgroundCount; if (foregroundCount == 0) break;
                backgroundSum += t * histogram[t]; var m0 = backgroundSum / (double)backgroundCount; var m1 = (sum - backgroundSum) / (double)foregroundCount;
                var variance = backgroundCount * (double)foregroundCount * (m0 - m1) * (m0 - m1);
                if (variance > bestVariance) { bestVariance = variance; threshold = t; }
            }
            return Mathf.Clamp(threshold, 80, 220);
        }

        private static bool TryFindColoredBorderBackground(FloorPlanImageData image, FloorPlanCropRegion region, out Color32 color)
        {
            color = new Color32(); if (image.colorPixels == null) return false;
            var bins = new Dictionary<int, int>(); var samples = new List<Color32>(); var stride = Mathf.Max(1, Mathf.Min(region.width, region.height) / 500);
            for (var x = region.x; x < region.x + region.width; x += stride) { samples.Add(image.GetColor(x, region.y)); samples.Add(image.GetColor(x, region.y + region.height - 1)); }
            for (var y = region.y; y < region.y + region.height; y += stride) { samples.Add(image.GetColor(region.x, y)); samples.Add(image.GetColor(region.x + region.width - 1, y)); }
            foreach (var p in samples) { var key = ((p.r >> 4) << 8) | ((p.g >> 4) << 4) | (p.b >> 4); bins.TryGetValue(key, out var count); bins[key] = count + 1; }
            var best = -1; var bestCount = 0; foreach (var pair in bins) if (pair.Value > bestCount) { best = pair.Key; bestCount = pair.Value; }
            if (best < 0 || bestCount / (float)samples.Count < .22f) return false;
            var r = 0; var g = 0; var b = 0; var n = 0;
            foreach (var p in samples) if ((((p.r >> 4) << 8) | ((p.g >> 4) << 4) | (p.b >> 4)) == best) { r += p.r; g += p.g; b += p.b; n++; }
            color = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255);
            return Mathf.Max(color.r, Mathf.Max(color.g, color.b)) - Mathf.Min(color.r, Mathf.Min(color.g, color.b)) >= 28;
        }

        private static int ColorDistanceSquared(Color32 a, Color32 b) { var r = a.r - b.r; var g = a.g - b.g; var bl = a.b - b.b; return r * r + g * g + bl * bl; }
    }
}
