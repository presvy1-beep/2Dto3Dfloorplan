using System;
using System.Collections.Generic;

namespace ARNav.Runtime.Extraction
{
    /// <summary>Small, deterministic raster filter for connected architectural strokes.</summary>
    internal static class StructuralBlackMaskFilter
    {
        private sealed class Component
        {
            public int area, minX, minY, maxX, maxY, longestHorizontal, longestVertical, longestDiagonal;
            public bool keep, oblique;
            public int LongSide { get { return Math.Max(maxX - minX + 1, maxY - minY + 1); } }
            public int ShortSide { get { return Math.Min(maxX - minX + 1, maxY - minY + 1); } }
        }

        public static byte[] Process(byte[] candidate, int width, int height, out int typicalWidth)
        {
            if (candidate == null || candidate.Length != width * height) throw new ArgumentException("Invalid black mask.");
            var minimum = Math.Min(width, height);
            var joined = (byte[])candidate.Clone();
            // Only a single missing pixel in a band of at least two rows/columns
            // is repaired. Door gaps and separated parallel strokes stay open.
            for (var y = 1; y < height - 1; y++) for (var x = 1; x < width - 1; x++)
            {
                var p = y * width + x;
                if (candidate[p] != 0) continue;
                var horizontal = candidate[p - 1] != 0 && candidate[p + 1] != 0 &&
                    ((candidate[p - width - 1] != 0 && candidate[p - width + 1] != 0) ||
                     (candidate[p + width - 1] != 0 && candidate[p + width + 1] != 0));
                var vertical = candidate[p - width] != 0 && candidate[p + width] != 0 &&
                    ((candidate[p - width - 1] != 0 && candidate[p + width - 1] != 0) ||
                     (candidate[p - width + 1] != 0 && candidate[p + width + 1] != 0));
                if (horizontal || vertical) joined[p] = 1;
            }
            typicalWidth = EstimateWidth(joined, width, height);

            // Solid strokes have 2x2 patches. Hatched printed walls can have no
            // such patch, so also accept a locally dense 5x5 black band. An
            // isolated one-pixel annotation contributes at most five pixels.
            var core = new byte[joined.Length];
            var prefix = new int[(width + 1) * (height + 1)];
            for (var y = 0; y < height; y++)
            {
                var row = 0;
                for (var x = 0; x < width; x++)
                { row += joined[y * width + x]; prefix[(y + 1) * (width + 1) + x + 1] = prefix[y * (width + 1) + x + 1] + row; }
            }
            for (var y = 0; y < height - 1; y++) for (var x = 0; x < width - 1; x++)
            {
                var p = y * width + x;
                if (joined[p] == 0 || joined[p + 1] == 0 || joined[p + width] == 0 || joined[p + width + 1] == 0) continue;
                core[p] = core[p + 1] = core[p + width] = core[p + width + 1] = 1;
            }
            for (var y = 2; y < height - 2; y++) for (var x = 2; x < width - 2; x++)
            {
                var p = y * width + x;
                if (joined[p] == 0) continue;
                var densityStride = width + 1; var x0 = x - 2; var x1 = x + 3; var y0 = y - 2; var y1 = y + 3;
                var density = prefix[y1 * densityStride + x1] - prefix[y0 * densityStride + x1] - prefix[y1 * densityStride + x0] + prefix[y0 * densityStride + x0];
                if (density >= 8) core[p] = 1;
            }
            var labels = new int[core.Length]; var queue = new int[core.Length]; var components = new List<Component> { null };
            for (var start = 0; start < core.Length; start++)
            {
                if (core[start] == 0 || labels[start] != 0) continue;
                var id = components.Count; var head = 0; var tail = 0;
                var part = new Component { minX = width, minY = height, maxX = -1, maxY = -1 };
                labels[start] = id; queue[tail++] = start;
                while (head < tail)
                {
                    var p = queue[head++]; var x = p % width; var y = p / width;
                    part.area++; part.minX = Math.Min(part.minX, x); part.maxX = Math.Max(part.maxX, x);
                    part.minY = Math.Min(part.minY, y); part.maxY = Math.Max(part.maxY, y);
                    for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        var nx = x + dx; var ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                        var q = ny * width + nx;
                        if (core[q] == 0 || labels[q] != 0) continue;
                        labels[q] = id; queue[tail++] = q;
                    }
                }
                components.Add(part);
            }
            for (var y = 0; y < height; y++) for (var x = 0; x < width;)
            {
                var id = labels[y * width + x]; if (id == 0) { x++; continue; }
                var start = x; while (x < width && labels[y * width + x] == id) x++;
                components[id].longestHorizontal = Math.Max(components[id].longestHorizontal, x - start);
            }
            for (var x = 0; x < width; x++) for (var y = 0; y < height;)
            {
                var id = labels[y * width + x]; if (id == 0) { y++; continue; }
                var start = y; while (y < height && labels[y * width + x] == id) y++;
                components[id].longestVertical = Math.Max(components[id].longestVertical, y - start);
            }
            for (var x = 0; x < width; x++)
            { MeasureDiagonal(labels, components, width, height, x, 0, 1); MeasureDiagonal(labels, components, width, height, x, height - 1, -1); }
            for (var y = 1; y < height; y++) MeasureDiagonal(labels, components, width, height, 0, y, 1);
            for (var y = 0; y < height - 1; y++) MeasureDiagonal(labels, components, width, height, 0, y, -1);
            for (var i = 1; i < components.Count; i++)
            {
                var part = components[i];
                var longBand = Math.Max(Math.Max(part.longestHorizontal, part.longestVertical), part.longestDiagonal) >= Math.Max(12, minimum / 5);
                var intersectingBands = part.longestHorizontal >= Math.Max(8, minimum / 14) &&
                    part.longestVertical >= Math.Max(8, minimum / 14);
                var obliqueBand = part.LongSide >= Math.Max(20, minimum / 4) &&
                    part.LongSide >= part.ShortSide * 3 / 2;
                part.oblique = obliqueBand;
                if (part.area >= Math.Max(24, minimum / 10) && (longBand || intersectingBands || obliqueBand)) part.keep = true;
            }
            var proximity = Math.Max(2, minimum / 160);
            for (var i = 1; i < components.Count; i++)
            {
                var part = components[i];
                if (part.keep || part.LongSide < Math.Max(8, minimum / 25) || part.LongSide < part.ShortSide * 2) continue;
                part.keep = TouchesKeptPixels(labels, components, i, part, width, height, proximity);
            }
            var kept = new byte[core.Length];
            for (var p = 0; p < core.Length; p++) if (labels[p] != 0 && components[labels[p]].keep) kept[p] = 1;

            // A supported wall also has a sustained straight stroke. Restrict
            // each retained network to the neighbourhood of horizontal,
            // vertical, or 45-degree runs; irregular vegetation protrusions
            // and curved fixture outlines then stop becoming tall strands.
            var straight = new byte[joined.Length];
            var minimumLine = Math.Max(14, minimum / 20);
            for (var y = 0; y < height; y++) MarkLine(joined, straight, width, height, 0, y, 1, 0, minimumLine, typicalWidth);
            for (var x = 0; x < width; x++) MarkLine(joined, straight, width, height, x, 0, 0, 1, minimumLine, typicalWidth);
            for (var x = 0; x < width; x++) { MarkLine(joined, straight, width, height, x, 0, 1, 1, minimumLine, typicalWidth); MarkLine(joined, straight, width, height, x, height - 1, 1, -1, minimumLine, typicalWidth); }
            for (var y = 1; y < height; y++) MarkLine(joined, straight, width, height, 0, y, 1, 1, minimumLine, typicalWidth);
            for (var y = 0; y < height - 1; y++) MarkLine(joined, straight, width, height, 0, y, 1, -1, minimumLine, typicalWidth);
            // Raster walls at common non-45-degree angles otherwise lose their
            // middle sections because only cardinal/diagonal support is present.
            var obliqueMask = new byte[joined.Length];
            for (var p = 0; p < joined.Length; p++)
                if (labels[p] != 0 && components[labels[p]].oblique) obliqueMask[p] = joined[p];
            MarkSlopedLines(obliqueMask, straight, width, height, 2, 1, minimumLine / 2, typicalWidth);
            MarkSlopedLines(obliqueMask, straight, width, height, 1, 2, minimumLine / 2, typicalWidth);
            var supportRadius = Math.Max(2, Math.Min(6, typicalWidth));
            Array.Clear(prefix, 0, prefix.Length);
            var stride = width + 1;
            for (var y = 0; y < height; y++)
            {
                var row = 0;
                for (var x = 0; x < width; x++)
                { row += straight[y * width + x]; prefix[(y + 1) * stride + x + 1] = prefix[y * stride + x + 1] + row; }
            }
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            {
                var p = y * width + x;
                if (kept[p] == 0) continue;
                var left = Math.Max(0, x - supportRadius); var right = Math.Min(width, x + supportRadius + 1);
                var top = Math.Max(0, y - supportRadius); var bottom = Math.Min(height, y + supportRadius + 1);
                var count = prefix[bottom * stride + right] - prefix[top * stride + right] -
                    prefix[bottom * stride + left] + prefix[top * stride + left];
                if (count == 0) kept[p] = 0;
            }

            // One-pixel expansion gives surviving two-pixel bands a practical
            // minimum width without enlarging discarded annotations. Do not fill
            // a pixel between opposing parallel bands, so narrow voids survive.
            var output = (byte[])kept.Clone();
            for (var y = 1; y < height - 1; y++) for (var x = 1; x < width - 1; x++)
            {
                var p = y * width + x;
                if (kept[p] != 0) continue;
                if ((kept[p - 1] != 0 && kept[p + 1] != 0) || (kept[p - width] != 0 && kept[p + width] != 0)) continue;
                for (var dy = -1; dy <= 1 && output[p] == 0; dy++) for (var dx = -1; dx <= 1; dx++)
                    if (kept[p + dy * width + dx] != 0) { output[p] = 1; break; }
            }
            return output;
        }

        private static int EstimateWidth(byte[] mask, int width, int height)
        {
            var minimum = Math.Min(width, height); var longRun = Math.Max(12, minimum / 12);
            var maximum = Math.Max(4, minimum / 25); var samples = new List<int>();
            for (var y = 0; y < height; y++) for (var x = 0; x < width;)
            {
                if (mask[y * width + x] == 0) { x++; continue; }
                var start = x; while (x < width && mask[y * width + x] != 0) x++;
                if (x - start < longRun) continue;
                var middle = (start + x) / 2; var top = y; var bottom = y;
                while (top > 0 && mask[(top - 1) * width + middle] != 0) top--;
                while (bottom + 1 < height && mask[(bottom + 1) * width + middle] != 0) bottom++;
                var stroke = bottom - top + 1;
                if (stroke >= 2 && stroke <= maximum) samples.Add(stroke);
            }
            for (var x = 0; x < width; x++) for (var y = 0; y < height;)
            {
                if (mask[y * width + x] == 0) { y++; continue; }
                var start = y; while (y < height && mask[y * width + x] != 0) y++;
                if (y - start < longRun) continue;
                var middle = (start + y) / 2; var left = x; var right = x;
                while (left > 0 && mask[middle * width + left - 1] != 0) left--;
                while (right + 1 < width && mask[middle * width + right + 1] != 0) right++;
                var stroke = right - left + 1;
                if (stroke >= 2 && stroke <= maximum) samples.Add(stroke);
            }
            if (samples.Count == 0) return 2;
            samples.Sort(); return samples[samples.Count / 2];
        }

        private static void MeasureDiagonal(int[] labels, List<Component> components, int width, int height, int startX, int startY, int stepY)
        {
            var x = startX; var y = startY;
            while (x < width && y >= 0 && y < height)
            {
                var id = labels[y * width + x];
                if (id == 0) { x++; y += stepY; continue; }
                var length = 0;
                while (x < width && y >= 0 && y < height && labels[y * width + x] == id)
                { length++; x++; y += stepY; }
                components[id].longestDiagonal = Math.Max(components[id].longestDiagonal, length);
            }
        }

        private static bool TouchesKeptPixels(int[] labels, List<Component> components, int id, Component part, int width, int height, int radius)
        {
            for (var y = part.minY; y <= part.maxY; y++) for (var x = part.minX; x <= part.maxX; x++)
            {
                if (labels[y * width + x] != id) continue;
                for (var dy = -radius; dy <= radius; dy++)
                {
                    var yy = y + dy; if (yy < 0 || yy >= height) continue;
                    for (var dx = -radius; dx <= radius; dx++)
                    {
                        var xx = x + dx; if (xx < 0 || xx >= width) continue;
                        var neighbor = labels[yy * width + xx];
                        if (neighbor != 0 && neighbor != id && components[neighbor].keep) return true;
                    }
                }
            }
            return false;
        }

        private static void MarkLine(byte[] mask, byte[] support, int width, int height, int startX, int startY, int dx, int dy, int minimum, int typicalWidth)
        {
            var x = startX; var y = startY;
            while (x >= 0 && x < width && y >= 0 && y < height)
            {
                if (mask[y * width + x] == 0) { x += dx; y += dy; continue; }
                var firstX = x; var firstY = y; var length = 0;
                while (x >= 0 && x < width && y >= 0 && y < height && mask[y * width + x] != 0)
                { length++; x += dx; y += dy; }
                if (length < minimum) continue;
                var middleX = firstX + length / 2 * dx; var middleY = firstY + length / 2 * dy;
                var perpendicularX = -dy; var perpendicularY = dx;
                var thickness = 1;
                for (var sign = -1; sign <= 1; sign += 2)
                    for (var step = 1; step <= typicalWidth * 3; step++)
                    {
                        var px = middleX + perpendicularX * step * sign; var py = middleY + perpendicularY * step * sign;
                        if (px < 0 || py < 0 || px >= width || py >= height || mask[py * width + px] == 0) break;
                        thickness++;
                    }
                if (thickness > Math.Max(12, typicalWidth * 3)) continue;
                for (var n = 0; n < length; n++) support[(firstY + n * dy) * width + firstX + n * dx] = 1;
            }
        }

        private static void MarkSlopedLines(byte[] mask, byte[] support, int width, int height, int dx, int dy, int minimum, int typicalWidth)
        {
            for (var x = 0; x < width; x++)
            {
                MarkLine(mask, support, width, height, x, 0, dx, dy, minimum, typicalWidth);
                MarkLine(mask, support, width, height, x, height - 1, dx, -dy, minimum, typicalWidth);
            }
            for (var y = 1; y < height; y++)
            {
                for (var x = 0; x < dx; x++)
                {
                    MarkLine(mask, support, width, height, x, y, dx, dy, minimum, typicalWidth);
                    MarkLine(mask, support, width, height, x, y, dx, -dy, minimum, typicalWidth);
                }
            }
        }
    }
}
