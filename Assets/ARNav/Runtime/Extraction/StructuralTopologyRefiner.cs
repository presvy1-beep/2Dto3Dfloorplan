using System;
using System.Collections.Generic;

namespace ARNav.Runtime.Extraction
{
    // Classify whole stroke networks after the thickness filter. A small closed
    // furniture outline can contain long straight runs, but it has neither the
    // scale nor the connections of the architectural network.
    internal static class StructuralTopologyRefiner
    {
        private sealed class Part
        {
            public int area, left, right, top, bottom;
            public bool keep;
            public int Width { get { return right - left + 1; } }
            public int Height { get { return bottom - top + 1; } }
        }

        public static byte[] Process(byte[] strokes, byte[] candidate, int width, int height, int thickness, out byte[] rejected)
        {
            var labels = new int[strokes.Length];
            var queue = new int[strokes.Length];
            var parts = new List<Part> { null };
            for (var start = 0; start < strokes.Length; start++)
            {
                if (strokes[start] == 0 || labels[start] != 0) continue;
                var id = parts.Count;
                var part = new Part { left = width, top = height, right = -1, bottom = -1 };
                var head = 0; var tail = 0; queue[tail++] = start; labels[start] = id;
                while (head < tail)
                {
                    var p = queue[head++]; var x = p % width; var y = p / width;
                    part.area++; part.left = Math.Min(part.left, x); part.right = Math.Max(part.right, x);
                    part.top = Math.Min(part.top, y); part.bottom = Math.Max(part.bottom, y);
                    for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++)
                    {
                        var xx = x + dx; var yy = y + dy;
                        if (xx < 0 || yy < 0 || xx >= width || yy >= height) continue;
                        var q = yy * width + xx;
                        if (strokes[q] != 0 && labels[q] == 0) { labels[q] = id; queue[tail++] = q; }
                    }
                }
                parts.Add(part);
            }
            if (parts.Count == 1) { rejected = (byte[])candidate.Clone(); return new byte[strokes.Length]; }
            var primary = 1;
            for (var i = 2; i < parts.Count; i++)
                if (parts[i].area > parts[primary].area) primary = i;
            parts[primary].keep = true;
            var main = parts[primary];
            var scale = Math.Max(main.Width, main.Height);
            var gap = Math.Max(3, thickness * 3);
            for (var i = 1; i < parts.Count; i++)
            {
                if (i == primary) continue;
                var part = parts[i];
                var longSide = Math.Max(part.Width, part.Height);
                var shortSide = Math.Min(part.Width, part.Height);
                var distanceX = Math.Max(0, Math.Max(main.left - part.right, part.left - main.right));
                var distanceY = Math.Max(0, Math.Max(main.top - part.bottom, part.top - main.bottom));
                var nearNetwork = distanceX <= gap && distanceY <= gap;
                var elongated = longSide >= Math.Max(9, shortSide * 3);
                var substantial = longSide >= Math.Min(60, scale * .25f) && part.area >= main.area * .10f;
                // Detached furniture rings have comparable width and height and
                // enclose a large blank centre. A short partition near a wall
                // remains eligible even when a door breaks the connection.
                part.keep = substantial || (nearNetwork && elongated && longSide >= Math.Max(7, thickness * 4));
            }
            var output = new byte[strokes.Length]; rejected = new byte[strokes.Length];
            for (var p = 0; p < strokes.Length; p++)
            {
                var id = labels[p];
                if (id != 0 && parts[id].keep) output[p] = 1;
                else if (candidate[p] != 0 || strokes[p] != 0) rejected[p] = 1;
            }
            for (var i = 1; i < parts.Count; i++)
                if (parts[i].keep) PruneDenseBlob(output, rejected, candidate, labels, i, parts[i], width, height, thickness);
            // Pixels from candidate components rejected by the stroke filter are
            // diagnostic only; they can never enter the serialized wall runs.
            return output;
        }

        private static void PruneDenseBlob(byte[] output, byte[] rejected, byte[] candidate, int[] labels,
            int id, Part part, int imageWidth, int imageHeight, int thickness)
        {
            if (part.Width < thickness * 4 || part.Height < thickness * 4 ||
                part.area < part.Width * part.Height / 4) return;
            var radius = Math.Max(6, thickness * 2);
            var left = Math.Max(0, part.left - radius); var right = Math.Min(imageWidth - 1, part.right + radius);
            var top = Math.Max(0, part.top - radius); var bottom = Math.Min(imageHeight - 1, part.bottom + radius);
            var width = right - left + 1; var height = bottom - top + 1; var stride = width + 1;
            var prefix = new int[stride * (height + 1)];
            for (var y = 0; y < height; y++)
            {
                var row = 0;
                for (var x = 0; x < width; x++)
                { row += candidate[(top + y) * imageWidth + left + x]; prefix[(y + 1) * stride + x + 1] = prefix[y * stride + x + 1] + row; }
            }
            var density = new byte[width * height]; var preservedLine = new byte[density.Length];
            for (var y = part.top; y <= part.bottom; y++) for (var x = part.left; x <= part.right; x++)
            {
                var p = y * imageWidth + x; if (labels[p] != id) continue;
                var lx = x - left; var ly = y - top;
                var x0 = Math.Max(0, lx - radius); var x1 = Math.Min(width, lx + radius + 1);
                var y0 = Math.Max(0, ly - radius); var y1 = Math.Min(height, ly + radius + 1);
                var count = prefix[y1 * stride + x1] - prefix[y0 * stride + x1] - prefix[y1 * stride + x0] + prefix[y0 * stride + x0];
                density[ly * width + lx] = (byte)(count * 100 / ((x1 - x0) * (y1 - y0)));
            }
            var minimumLine = Math.Max(24, thickness * 8);
            for (var y = part.top; y <= part.bottom; y++) for (var x = part.left; x <= part.right;)
            {
                if (labels[y * imageWidth + x] != id || output[y * imageWidth + x] == 0) { x++; continue; }
                var start = x; while (x <= part.right && labels[y * imageWidth + x] == id && output[y * imageWidth + x] != 0) x++;
                if (x - start >= minimumLine) for (var xx = start; xx < x; xx++) preservedLine[(y - top) * width + xx - left] = 1;
            }
            for (var x = part.left; x <= part.right; x++) for (var y = part.top; y <= part.bottom;)
            {
                if (labels[y * imageWidth + x] != id || output[y * imageWidth + x] == 0) { y++; continue; }
                var start = y; while (y <= part.bottom && labels[y * imageWidth + x] == id && output[y * imageWidth + x] != 0) y++;
                if (y - start >= minimumLine) for (var yy = start; yy < y; yy++) preservedLine[(yy - top) * width + x - left] = 1;
            }
            var seen = new byte[density.Length]; var queue = new int[density.Length];
            for (var start = 0; start < density.Length; start++)
            {
                if (density[start] < 72 || seen[start] != 0) continue;
                var head = 0; var tail = 0; var minX = width; var maxX = -1; var minY = height; var maxY = -1;
                queue[tail++] = start; seen[start] = 1;
                while (head < tail)
                {
                    var q = queue[head++]; var x = q % width; var y = q / width;
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                    for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++)
                    {
                        var xx = x + dx; var yy = y + dy;
                        if (xx < 0 || yy < 0 || xx >= width || yy >= height) continue;
                        var z = yy * width + xx;
                        if (seen[z] == 0 && density[z] >= 30) { seen[z] = 1; queue[tail++] = z; }
                    }
                }
                if (tail < 64 || maxX - minX + 1 < Math.Max(16, thickness * 4) ||
                    maxY - minY + 1 < Math.Max(16, thickness * 4)) continue;
                for (var n = 0; n < tail; n++)
                {
                    var q = queue[n]; if (preservedLine[q] != 0) continue;
                    var p = (top + q / width) * imageWidth + left + q % width;
                    if (output[p] != 0) { output[p] = 0; rejected[p] = 1; }
                }
            }
        }
    }
}
