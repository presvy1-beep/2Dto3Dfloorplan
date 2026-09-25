using System;
using System.Collections.Generic;
using ARNav.Core.Data;
using UnityEngine;

namespace ARNav.Runtime.Generation
{
    /// <summary>
    /// Derives a render-friendly wall footprint from the lossless raster wall runs.
    /// Thin stroke components are thinned to a connected medial skeleton and rebuilt
    /// at a stable metric width. Area-like components keep their source footprint.
    /// </summary>
    public static class WallGeometryNormalizer
    {
        public sealed class Diagnostics
        {
            public long rawPixels;
            public long normalizedPixels;
            public int rawComponents;
            public int removedComponents;
            public int skeletonizedComponents;
            public int estimatedThicknessPixels;
            public int outputThicknessPixels;
            public int rawRunCount;
            public int normalizedRunCount;
            public float areaChangePercent;
        }

        private sealed class Component
        {
            public readonly List<int> pixels = new List<int>();
            public int minX = int.MaxValue, minY = int.MaxValue, maxX, maxY, perimeter;
            public int Width { get { return maxX - minX + 1; } }
            public int Height { get { return maxY - minY + 1; } }
            public int Length { get { return Math.Max(Width, Height); } }
            public float Thickness { get { return perimeter == 0 ? 0 : pixels.Count * 2f / perimeter; } }
        }

        private struct ThicknessSample
        {
            public float value, weight;
            public ThicknessSample(float value, float weight) { this.value = value; this.weight = weight; }
        }

        public static List<LocalRasterRun> Normalize(IList<LocalRasterRun> sourceRuns, int width, int height,
            float metresPerPixel, float minimumWallThicknessMetres, out Diagnostics diagnostics)
        {
            diagnostics = new Diagnostics();
            var output = new List<LocalRasterRun>();
            if (sourceRuns == null || sourceRuns.Count == 0 || width <= 0 || height <= 0 ||
                !(metresPerPixel > 0) || float.IsInfinity(metresPerPixel) || float.IsNaN(metresPerPixel)) return output;

            var count = checked((long)width * height);
            if (count > 32000000) throw new InvalidOperationException("Wall mask exceeds the normalization limit.");
            var mask = new bool[(int)count];
            foreach (var run in sourceRuns)
            {
                if (run == null) continue;
                var x0 = Mathf.Clamp(Mathf.FloorToInt(run.minX / metresPerPixel + 0.0001f), 0, width);
                var x1 = Mathf.Clamp(Mathf.CeilToInt(run.maxX / metresPerPixel - 0.0001f), 0, width);
                // Local Z has a lower-left origin; raster Y has an upper-left origin.
                var y0 = Mathf.Clamp(height - Mathf.CeilToInt(run.maxZ / metresPerPixel - 0.0001f), 0, height);
                var y1 = Mathf.Clamp(height - Mathf.FloorToInt(run.minZ / metresPerPixel + 0.0001f), 0, height);
                for (var y = y0; y < y1; y++) for (var x = x0; x < x1; x++) mask[y * width + x] = true;
            }

            for (var i = 0; i < mask.Length; i++) if (mask[i]) diagnostics.rawPixels++;
            diagnostics.rawRunCount = sourceRuns.Count;
            if (diagnostics.rawPixels == 0) return output;

            var components = FindComponents(mask, width, height);
            diagnostics.rawComponents = components.Count;
            var thicknesses = new List<ThicknessSample>();
            foreach (var component in components)
            {
                // Exclude dots, small glyph strokes, and very large filled regions from
                // the robust thickness estimate. Component area/perimeter is stable for
                // long strokes and does not let a single wide junction dominate.
                if (component.Length >= 8 && component.Length <= Math.Min(width, height) * .15f &&
                    component.Thickness >= 1f && component.Thickness <= Math.Min(width, height) * .06f)
                    thicknesses.Add(new ThicknessSample(component.Thickness, component.Length));
            }
            thicknesses.Sort((a, b) => a.value.CompareTo(b.value));
            var totalWeight = 0f; foreach (var sample in thicknesses) totalWeight += sample.weight;
            var accumulatedWeight = 0f; var robustThickness = 1f;
            foreach (var sample in thicknesses) { accumulatedWeight += sample.weight; if (accumulatedWeight >= totalWeight * .5f) { robustThickness = sample.value; break; } }
            var estimated = thicknesses.Count == 0 ? 1 : Mathf.Max(1, Mathf.RoundToInt(robustThickness));
            var minimum = Mathf.Max(0.08f, minimumWallThicknessMetres);
            // Guard the exact 0.15 / 0.01 case from floating point rounding to 15.000001.
            var target = Mathf.Max(estimated, Mathf.CeilToInt(minimum / metresPerPixel - .001f));
            target = Mathf.Clamp(target, 2, Mathf.Max(2, Mathf.Min(width, height) / 6));
            diagnostics.estimatedThicknessPixels = estimated;
            var strokeRadius = Mathf.Max(1, Mathf.CeilToInt((target - 1) * .5f));
            diagnostics.outputThicknessPixels = strokeRadius * 2 + 1;

            var structural = new bool[components.Count];
            var structuralLength = Mathf.Max(target * 4f, Mathf.Min(width, height) * .06f);
            for (var i = 0; i < components.Count; i++)
                structural[i] = components[i].Length >= structuralLength ||
                                components[i].Thickness >= target * .78f;

            var normalized = new bool[mask.Length];
            for (var componentIndex = 0; componentIndex < components.Count; componentIndex++)
            {
                var component = components[componentIndex];
                var shortSpan = Math.Min(component.Width, component.Height);
                var longSpan = Math.Max(component.Width, component.Height);
                var curvedCandidate = component.Length >= target * 3f && shortSpan >= target * 1.5f &&
                                      longSpan / (float)shortSpan >= 1.35f && longSpan / (float)shortSpan <= 3.5f;
                var insignificant = component.Length <= Mathf.Max(2, estimated * 1.5f) &&
                                    component.pixels.Count <= Mathf.Max(5, estimated * estimated * .4f);
                if (insignificant) { diagnostics.removedComponents++; continue; }

                var lineLike = component.Thickness < target * .78f && component.Length >= Mathf.Max(6, target * 1.5f);
                if (!structural[componentIndex] && !curvedCandidate && component.Thickness < Mathf.Max(3f, estimated * 1.8f) &&
                    component.Length < structuralLength &&
                    !NearStructuralComponent(components, structural, componentIndex, target * 2f))
                {
                    // Detached, thin marks shorter than a small architectural stub are
                    // usually furniture, isolated labels, or raster specks. Keep short
                    // thick strokes and fragments that have strong spatial evidence of
                    // continuing a nearby wall network.
                    diagnostics.removedComponents++;
                    continue;
                }
                if (!lineLike)
                {
                    foreach (var pixel in component.pixels) normalized[pixel] = true;
                    continue;
                }

                diagnostics.skeletonizedComponents++;
                var skeleton = ThinComponent(mask, width, component);
                PruneShortJunctionBranches(skeleton, width, component, Mathf.Max(3, Mathf.RoundToInt(target * 1.25f)));
                DilateComponent(skeleton, normalized, width, height, component, strokeRadius);
            }

            diagnostics.normalizedPixels = Count(normalized);
            diagnostics.areaChangePercent = diagnostics.rawPixels == 0 ? 0 :
                (diagnostics.normalizedPixels - diagnostics.rawPixels) * 100f / diagnostics.rawPixels;
            output = BuildRuns(normalized, width, height, metresPerPixel);
            diagnostics.normalizedRunCount = output.Count;
            return output;
        }

        private static bool NearStructuralComponent(List<Component> components, bool[] structural, int current, float maximumGap)
        {
            var component = components[current]; var maximumSquared = maximumGap * maximumGap;
            for (var i = 0; i < components.Count; i++)
            {
                if (i == current || !structural[i]) continue;
                var other = components[i];
                var dx = Math.Max(0, Math.Max(component.minX - other.maxX, other.minX - component.maxX));
                var dy = Math.Max(0, Math.Max(component.minY - other.maxY, other.minY - component.maxY));
                if (dx * dx + dy * dy <= maximumSquared) return true;
            }
            return false;
        }

        private static List<Component> FindComponents(bool[] mask, int width, int height)
        {
            var result = new List<Component>(); var labels = new bool[mask.Length]; var queue = new int[mask.Length];
            for (var seed = 0; seed < mask.Length; seed++)
            {
                if (!mask[seed] || labels[seed]) continue;
                var component = new Component(); result.Add(component); var head = 0; var tail = 0;
                queue[tail++] = seed; labels[seed] = true;
                while (head < tail)
                {
                    var index = queue[head++]; var x = index % width; var y = index / width;
                    component.pixels.Add(index); component.minX = Math.Min(component.minX, x); component.maxX = Math.Max(component.maxX, x);
                    component.minY = Math.Min(component.minY, y); component.maxY = Math.Max(component.maxY, y);
                    if (x == 0 || !mask[index - 1]) component.perimeter++;
                    if (x == width - 1 || !mask[index + 1]) component.perimeter++;
                    if (y == 0 || !mask[index - width]) component.perimeter++;
                    if (y == height - 1 || !mask[index + width]) component.perimeter++;
                    for (var oy = -1; oy <= 1; oy++) for (var ox = -1; ox <= 1; ox++)
                    {
                        if (ox == 0 && oy == 0) continue; var nx = x + ox; var ny = y + oy;
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                        var next = ny * width + nx; if (mask[next] && !labels[next]) { labels[next] = true; queue[tail++] = next; }
                    }
                }
            }
            return result;
        }

        // Zhang-Suen thinning preserves endpoints, corners, loops, and junctions while
        // reducing a stroke to a one-cell medial graph. It operates only on line-like
        // connected components; thick regions bypass it.
        private static bool[] ThinComponent(bool[] source, int width, Component component)
        {
            var height = source.Length / width; var localWidth = component.Width + 2; var localHeight = component.Height + 2;
            var local = new bool[localWidth * localHeight];
            foreach (var pixel in component.pixels)
            {
                var x = pixel % width - component.minX + 1; var y = pixel / width - component.minY + 1;
                local[y * localWidth + x] = true;
            }
            var remove = new List<int>(); var changed = true;
            while (changed)
            {
                changed = false;
                for (var phase = 0; phase < 2; phase++)
                {
                    remove.Clear();
                    for (var y = 1; y < localHeight - 1; y++) for (var x = 1; x < localWidth - 1; x++)
                    {
                        var i = y * localWidth + x; if (!local[i]) continue;
                        var p2 = local[i - localWidth]; var p3 = local[i - localWidth + 1]; var p4 = local[i + 1]; var p5 = local[i + localWidth + 1];
                        var p6 = local[i + localWidth]; var p7 = local[i + localWidth - 1]; var p8 = local[i - 1]; var p9 = local[i - localWidth - 1];
                        var neighbors = (p2?1:0)+(p3?1:0)+(p4?1:0)+(p5?1:0)+(p6?1:0)+(p7?1:0)+(p8?1:0)+(p9?1:0);
                        if (neighbors < 2 || neighbors > 6) continue;
                        var transitions = (!p2 && p3?1:0)+(!p3 && p4?1:0)+(!p4 && p5?1:0)+(!p5 && p6?1:0)+(!p6 && p7?1:0)+(!p7 && p8?1:0)+(!p8 && p9?1:0)+(!p9 && p2?1:0);
                        if (transitions != 1) continue;
                        var cornerSafe = phase == 0 ? !(p2 && p4 && p6) && !(p4 && p6 && p8) : !(p2 && p4 && p8) && !(p2 && p6 && p8);
                        if (cornerSafe) remove.Add(i);
                    }
                    if (remove.Count > 0) { foreach (var i in remove) local[i] = false; changed = true; }
                }
            }
            var result = new bool[source.Length];
            for (var y = 1; y < localHeight - 1; y++) for (var x = 1; x < localWidth - 1; x++) if (local[y * localWidth + x])
                result[(component.minY + y - 1) * width + component.minX + x - 1] = true;
            return result;
        }

        // Raster text, hatch marks, and furniture symbols often touch the main wall
        // network and therefore share its connected-component label. Remove only
        // short skeleton branches that start at a free endpoint and terminate at a
        // junction. Paths that join two endpoints (real short walls) are preserved.
        private static void PruneShortJunctionBranches(bool[] skeleton, int width, Component component, int maximumLength)
        {
            var height = skeleton.Length / width;
            var changed = true;
            var guard = 0;
            while (changed && guard++ < maximumLength)
            {
                changed = false;
                var endpoints = new List<int>();
                var minY = Math.Max(0, component.minY - 1); var maxY = Math.Min(height - 1, component.maxY + 1);
                var minX = Math.Max(0, component.minX - 1); var maxX = Math.Min(width - 1, component.maxX + 1);
                for (var y = minY; y <= maxY; y++) for (var x = minX; x <= maxX; x++)
                {
                    var pixel = y * width + x;
                    if (skeleton[pixel] && NeighborCount(skeleton, width, height, pixel) == 1) endpoints.Add(pixel);
                }

                foreach (var endpoint in endpoints)
                {
                    if (!skeleton[endpoint]) continue;
                    var path = new List<int> { endpoint };
                    var previous = -1;
                    var current = endpoint;
                    var reachedJunction = false;
                    while (path.Count <= maximumLength)
                    {
                        var next = FindNextNeighbor(skeleton, width, height, current, previous);
                        if (next < 0) break;
                        previous = current;
                        current = next;
                        var degree = NeighborCount(skeleton, width, height, current);
                        if (degree >= 3) { reachedJunction = true; break; }
                        if (degree != 2) break;
                        path.Add(current);
                    }
                    if (!reachedJunction || path.Count > maximumLength) continue;
                    // Keep the junction pixel; remove the endpoint-to-junction spur.
                    foreach (var pixel in path) skeleton[pixel] = false;
                    changed = true;
                }
            }
        }

        private static int NeighborCount(bool[] mask, int width, int height, int index)
        {
            var count = 0; var x = index % width; var y = index / width;
            for (var oy = -1; oy <= 1; oy++) for (var ox = -1; ox <= 1; ox++)
            {
                if (ox == 0 && oy == 0) continue;
                var nx = x + ox; var ny = y + oy;
                if (nx >= 0 && ny >= 0 && nx < width && ny < height && mask[ny * width + nx]) count++;
            }
            return count;
        }

        private static int FindNextNeighbor(bool[] mask, int width, int height, int index, int previous)
        {
            var x = index % width; var y = index / width;
            for (var oy = -1; oy <= 1; oy++) for (var ox = -1; ox <= 1; ox++)
            {
                if (ox == 0 && oy == 0) continue;
                var nx = x + ox; var ny = y + oy;
                if (nx >= 0 && ny >= 0 && nx < width && ny < height)
                {
                    var neighbor = ny * width + nx;
                    if (neighbor != previous && mask[neighbor]) return neighbor;
                }
            }
            return -1;
        }

        private static void DilateComponent(bool[] skeleton, bool[] output, int width, int height, Component component, int radius)
        {
            foreach (var pixel in component.pixels)
            {
                if (!skeleton[pixel]) continue;
                var sx = pixel % width; var sy = pixel / width;
                for (var oy = -radius; oy <= radius; oy++) for (var ox = -radius; ox <= radius; ox++)
                {
                    if (ox * ox + oy * oy > radius * radius) continue;
                    var nx = sx + ox; var ny = sy + oy;
                    if (nx >= 0 && ny >= 0 && nx < width && ny < height) output[ny * width + nx] = true;
                }
            }
        }

        private static List<LocalRasterRun> BuildRuns(bool[] mask, int width, int height, float mpp)
        {
            var result = new List<LocalRasterRun>(); var active = new Dictionary<string, LocalRasterRun>();
            for (var y = 0; y < height; y++)
            {
                var current = new Dictionary<string, LocalRasterRun>(); var x = 0;
                while (x < width)
                {
                    while (x < width && !mask[y * width + x]) x++;
                    if (x >= width) break; var start = x;
                    while (x + 1 < width && mask[y * width + x + 1]) x++;
                    var end = x + 1; var key = start + ":" + end;
                    if (active.TryGetValue(key, out var previous) && Mathf.Abs(previous.minZ - (height - y) * mpp) < mpp * .1f)
                    { previous.minZ = (height - y - 1) * mpp; current[key] = previous; }
                    else
                    {
                        var run = new LocalRasterRun { minX = start * mpp, maxX = end * mpp, minZ = (height - y - 1) * mpp, maxZ = (height - y) * mpp };
                        result.Add(run); current[key] = run;
                    }
                    x++;
                }
                active = current;
            }
            return result;
        }

        private static long Count(bool[] mask) { long count = 0; foreach (var value in mask) if (value) count++; return count; }
    }
}
