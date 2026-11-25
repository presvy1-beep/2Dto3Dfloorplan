using System;
using System.Collections.Generic;
using ARNav.Core.Data;
using ARNav.Core.Geometry;

namespace ARNav.Runtime.Extraction
{
    // Virtual closures separate regions without changing the physical wall mask.
    internal static class FloorRegionAnalyzer
    {
        private sealed class Region
        {
            public int id, area, left, right, top, bottom;
            public bool exterior;
        }

        public static void Analyze(byte[] walls, byte[] boundaryEvidence, int width, int height, int thickness,
            FloorPlanExtractionResult result, Action checkCancellation)
        {
            var footprint = BuildFootprint(walls, boundaryEvidence, width, height, thickness, checkCancellation);
            var closure = new byte[walls.Length];
            // Wide balcony and hall entrances can exceed a dozen wall widths.
            // These pixels are segmentation barriers only; the wall mask remains open.
            var maxOpening = Math.Max(5, Math.Min(width, height) / 3);
            var interiorOpening = Math.Max(10, thickness * 8);
            var continuation = Math.Max(4, thickness * 2);
            for (var y = 1; y < height - 1; y++)
            {
                checkCancellation();
                for (var x = 2; x < width - 2; x++)
                {
                    var p = y * width + x;
                    if (walls[p] == 0 || walls[p + 1] != 0) continue;
                    var end = x + 1;
                    while (end < width && walls[y * width + end] == 0 && end - x <= maxOpening) end++;
                    if (end + 2 >= width || end - x > maxOpening || end == x + 1) continue;
                    if (x < continuation || end + continuation >= width ||
                        !HasContinuation(walls, p, -1, continuation) ||
                        !HasContinuation(walls, y * width + end, 1, continuation)) continue;
                    if (end - x > interiorOpening)
                    {
                        var middle = (x + end) / 2; var offset = thickness * 2 + 2;
                        var above = y - offset; var below = y + offset;
                        if ((above >= 0 && footprint[above * width + middle] != 0) &&
                            (below < height && footprint[below * width + middle] != 0)) continue;
                    }
                    for (var xx = x + 1; xx < end; xx++) closure[y * width + xx] = 1;
                }
            }
            for (var x = 1; x < width - 1; x++)
            {
                checkCancellation();
                for (var y = 2; y < height - 2; y++)
                {
                    var p = y * width + x;
                    if (walls[p] == 0 || walls[p + width] != 0) continue;
                    var end = y + 1;
                    while (end < height && walls[end * width + x] == 0 && end - y <= maxOpening) end++;
                    if (end + 2 >= height || end - y > maxOpening || end == y + 1) continue;
                    if (y < continuation || end + continuation >= height ||
                        !HasContinuation(walls, p, -width, continuation) ||
                        !HasContinuation(walls, end * width + x, width, continuation)) continue;
                    if (end - y > interiorOpening)
                    {
                        var middle = (y + end) / 2; var offset = thickness * 2 + 2;
                        var left = x - offset; var right = x + offset;
                        if ((left >= 0 && footprint[middle * width + left] != 0) &&
                            (right < width && footprint[middle * width + right] != 0)) continue;
                    }
                    for (var yy = y + 1; yy < end; yy++) closure[yy * width + x] = 1;
                }
            }
            var labels = new int[walls.Length]; var queue = new int[walls.Length];
            var regions = new List<Region> { null };
            for (var start = 0; start < walls.Length; start++)
            {
                if (footprint[start] == 0 || walls[start] != 0 || closure[start] != 0 || labels[start] != 0) continue;
                var region = new Region { id = regions.Count, left = width, top = height, right = -1, bottom = -1 };
                var head = 0; var tail = 0; queue[tail++] = start; labels[start] = region.id;
                while (head < tail)
                {
                    var p = queue[head++]; var x = p % width; var y = p / width;
                    region.area++; region.left = Math.Min(region.left, x); region.right = Math.Max(region.right, x);
                    region.top = Math.Min(region.top, y); region.bottom = Math.Max(region.bottom, y);
                    if (x == 0 || y == 0 || x == width - 1 || y == height - 1) region.exterior = true;
                    if (x > 0) Visit(p - 1, region.id, walls, closure, footprint, labels, queue, ref tail);
                    if (x + 1 < width) Visit(p + 1, region.id, walls, closure, footprint, labels, queue, ref tail);
                    if (y > 0) Visit(p - width, region.id, walls, closure, footprint, labels, queue, ref tail);
                    if (y + 1 < height) Visit(p + width, region.id, walls, closure, footprint, labels, queue, ref tail);
                }
                regions.Add(region);
                checkCancellation();
            }
            var largest = 0;
            for (var i = 1; i < regions.Count; i++)
                if (!regions[i].exterior && (largest == 0 || regions[i].area > regions[largest].area)) largest = i;
            var connected = new bool[regions.Count];
            for (var p = 0; p < closure.Length; p++)
            {
                if (closure[p] == 0) continue;
                var x = p % width; var y = p / width;
                var a = 0; var b = 0;
                if (x > 0) Pair(labels[p - 1], ref a, ref b);
                if (x + 1 < width) Pair(labels[p + 1], ref a, ref b);
                if (y > 0) Pair(labels[p - width], ref a, ref b);
                if (y + 1 < height) Pair(labels[p + width], ref a, ref b);
                if (a != 0 && b != 0 && a != b && !regions[a].exterior && !regions[b].exterior)
                { connected[a] = true; connected[b] = true; }
            }
            var voidRegion = new bool[regions.Count];
            var minimumRegionArea = Math.Max(64, Math.Max(thickness * thickness * 8, walls.Length / 2000));
            if (largest != 0) for (var i = 1; i < regions.Count; i++)
            {
                var r = regions[i]; var main = regions[largest];
                voidRegion[i] = i != largest && !r.exterior && !connected[i] &&
                    r.area < main.area * .45f &&
                    r.left > main.left + thickness && r.right < main.right - thickness &&
                    r.top > main.top + thickness && r.bottom < main.bottom - thickness;
            }
            var exterior = new byte[walls.Length]; var floor = new byte[walls.Length];
            var voids = new byte[walls.Length]; var openings = new byte[walls.Length];
            var regionMask = new byte[walls.Length];
            for (var p = 0; p < labels.Length; p++)
            {
                var id = labels[p];
                if (footprint[p] == 0) { exterior[p] = 1; continue; }
                if (id == 0) continue;
                if (regions[id].exterior) exterior[p] = 1;
                else if (voidRegion[id]) voids[p] = 1;
                else if (regions[id].area >= minimumRegionArea)
                { floor[p] = 1; regionMask[p] = (byte)Math.Min(255, id); }
                else voids[p] = 1;
            }
            var openingOwner = new int[walls.Length];
            var seenClosure = new byte[walls.Length];
            for (var start = 0; start < closure.Length; start++)
            {
                if (closure[start] == 0 || footprint[start] == 0 || seenClosure[start] != 0) continue;
                var head = 0; var tail = 0; queue[tail++] = start; seenClosure[start] = 1;
                var interior = 0; var exteriorNeighbor = false;
                while (head < tail)
                {
                    var p = queue[head++]; var x = p % width; var y = p / width;
                    if (x > 0) ClosureNeighbor(p - 1, closure, footprint, seenClosure, labels, regions, voidRegion, queue, ref tail, ref interior, ref exteriorNeighbor);
                    if (x + 1 < width) ClosureNeighbor(p + 1, closure, footprint, seenClosure, labels, regions, voidRegion, queue, ref tail, ref interior, ref exteriorNeighbor);
                    if (y > 0) ClosureNeighbor(p - width, closure, footprint, seenClosure, labels, regions, voidRegion, queue, ref tail, ref interior, ref exteriorNeighbor);
                    if (y + 1 < height) ClosureNeighbor(p + width, closure, footprint, seenClosure, labels, regions, voidRegion, queue, ref tail, ref interior, ref exteriorNeighbor);
                }
                for (var i = 0; i < tail; i++)
                {
                    var p = queue[i];
                    if (interior != 0 && regions[interior].area >= minimumRegionArea)
                    {
                        floor[p] = 1; openings[p] = 1; openingOwner[p] = interior; regionMask[p] = (byte)Math.Min(255, interior);
                        var region = regions[interior]; var x = p % width; var y = p / width;
                        region.left = Math.Min(region.left, x); region.right = Math.Max(region.right, x);
                        region.top = Math.Min(region.top, y); region.bottom = Math.Max(region.bottom, y);
                    }
                    else if (exteriorNeighbor) exterior[p] = 1;
                }
            }
            result.diagnostics.exteriorMask = exterior;
            result.diagnostics.interiorMask = floor;
            result.diagnostics.floorMask = floor;
            result.diagnostics.voidMask = voids;
            result.diagnostics.openingMask = openings;
            result.diagnostics.virtualClosureMask = closure;
            result.diagnostics.regionLabels = regionMask;
            var regionNumber = 0;
            foreach (var region in regions)
            {
                if (region == null || region.exterior || voidRegion[region.id] || region.area < minimumRegionArea) continue;
                var rw = region.right - region.left + 1; var rh = region.bottom - region.top + 1;
                var corridor = Math.Max(rw, rh) >= Math.Min(rw, rh) * 3 && region.area >= thickness * thickness * 6;
                var room = new RoomAnnotationData
                {
                    id = "direct_region_" + (++regionNumber), displayName = corridor ? "Corridor " + regionNumber : "Room " + regionNumber,
                    category = "Detected", regionType = corridor ? "Corridor" : "Room", walkable = true,
                    destinationSelectable = false, detectionConfidence = corridor ? .65f : .7f,
                    preferRasterGeometry = true, rasterRuns = BuildRegionRuns(region, labels, openingOwner, width)
                };
                room.vertices.Add(new PixelPoint(region.left, region.top));
                room.vertices.Add(new PixelPoint(region.right + 1, region.top));
                room.vertices.Add(new PixelPoint(region.right + 1, region.bottom + 1));
                room.vertices.Add(new PixelPoint(region.left, region.bottom + 1));
                result.annotations.rooms.Add(room);
            }
            result.diagnostics.extractedComponents = regionNumber;
        }

        private static byte[] BuildFootprint(byte[] walls, byte[] boundaryEvidence, int width, int height, int thickness, Action checkCancellation)
        {
            var minimumSpan = Math.Max(8, thickness * 4);
            var boundLeft = width; var boundRight = 0; var boundTop = height; var boundBottom = 0;
            for (var p = 0; p < walls.Length; p++) if (walls[p] != 0)
            { var x = p % width; var y = p / width; boundLeft = Math.Min(boundLeft, x); boundRight = Math.Max(boundRight, x); boundTop = Math.Min(boundTop, y); boundBottom = Math.Max(boundBottom, y); }
            var rowMin = new int[height]; var rowMax = new int[height];
            var colMin = new int[width]; var colMax = new int[width];
            for (var y = 0; y < height; y++)
            {
                checkCancellation(); var left = width; var right = -1;
                for (var x = boundLeft; x <= boundRight; x++) if (walls[y * width + x] != 0 || boundaryEvidence[y * width + x] != 0)
                { left = Math.Min(left, x); right = x; }
                rowMin[y] = right - left >= minimumSpan ? left : width;
                rowMax[y] = right - left >= minimumSpan ? right : -1;
            }
            for (var x = 0; x < width; x++)
            {
                checkCancellation(); var top = height; var bottom = -1;
                if (x < boundLeft || x > boundRight) { colMin[x] = height; colMax[x] = -1; continue; }
                for (var y = boundTop; y <= boundBottom; y++) if (walls[y * width + x] != 0 || boundaryEvidence[y * width + x] != 0)
                { top = Math.Min(top, y); bottom = y; }
                colMin[x] = bottom - top >= minimumSpan ? top : height;
                colMax[x] = bottom - top >= minimumSpan ? bottom : -1;
            }
            BridgeSpans(rowMin, rowMax, height, Math.Min(width, height) / 2);
            BridgeSpans(colMin, colMax, width, Math.Min(width, height) / 2);
            ExpandNarrowSpans(rowMin, rowMax, height, Math.Min(width, height) / 4);
            var footprint = new byte[walls.Length];
            for (var y = 0; y < height; y++)
            {
                checkCancellation();
                if (rowMax[y] < rowMin[y]) continue;
                for (var x = rowMin[y]; x <= rowMax[y]; x++)
                    if (colMin[x] <= y && y <= colMax[x]) footprint[y * width + x] = 1;
            }
            return footprint;
        }

        private static List<PixelRasterRun> BuildRegionRuns(Region region, int[] labels, int[] openingOwner, int width)
        {
            var runs = new List<PixelRasterRun>();
            var previous = new Dictionary<long, PixelRasterRun>();
            for (var y = region.top; y <= region.bottom; y++)
            {
                var current = new Dictionary<long, PixelRasterRun>();
                for (var x = region.left; x <= region.right;)
                {
                    var p = y * width + x;
                    if (labels[p] != region.id && openingOwner[p] != region.id) { x++; continue; }
                    var start = x;
                    while (x <= region.right && (labels[y * width + x] == region.id || openingOwner[y * width + x] == region.id)) x++;
                    var key = ((long)start << 32) | (uint)x;
                    if (previous.TryGetValue(key, out var prior) && prior.maxY == y) prior.maxY = y + 1;
                    else { prior = new PixelRasterRun { minX = start, maxX = x, minY = y, maxY = y + 1 }; runs.Add(prior); }
                    current[key] = prior;
                }
                previous = current;
            }
            return runs;
        }

        private static void BridgeSpans(int[] low, int[] high, int length, int maxGap)
        {
            for (var i = 0; i < length; i++)
            {
                if (high[i] >= low[i]) continue;
                var before = i - 1; var after = i + 1;
                while (before >= 0 && i - before <= maxGap && high[before] < low[before]) before--;
                while (after < length && after - i <= maxGap && high[after] < low[after]) after++;
                if (before < 0 || after >= length || i - before > maxGap || after - i > maxGap) continue;
                low[i] = Math.Min(low[before], low[after]);
                high[i] = Math.Max(high[before], high[after]);
            }
        }

        private static void ExpandNarrowSpans(int[] low, int[] high, int length, int maxGap)
        {
            var originalLow = (int[])low.Clone(); var originalHigh = (int[])high.Clone();
            for (var i = 0; i < length; i++)
            {
                var span = originalHigh[i] - originalLow[i] + 1;
                if (span <= 0) continue;
                var before = i - 1; var after = i + 1;
                while (before >= 0 && i - before <= maxGap && originalHigh[before] - originalLow[before] + 1 < span * 2) before--;
                while (after < length && after - i <= maxGap && originalHigh[after] - originalLow[after] + 1 < span * 2) after++;
                if (before < 0 || after >= length || i - before > maxGap || after - i > maxGap) continue;
                if (originalHigh[before] < originalLow[i] || originalLow[before] > originalHigh[i] ||
                    originalHigh[after] < originalLow[i] || originalLow[after] > originalHigh[i]) continue;
                low[i] = Math.Min(originalLow[before], originalLow[after]);
                high[i] = Math.Max(originalHigh[before], originalHigh[after]);
            }
        }

        private static void Visit(int p, int id, byte[] walls, byte[] closure, byte[] footprint, int[] labels, int[] queue, ref int tail)
        { if (footprint[p] != 0 && walls[p] == 0 && closure[p] == 0 && labels[p] == 0) { labels[p] = id; queue[tail++] = p; } }

        private static void CheckNeighbor(int id, List<Region> regions, bool[] voidRegion, ref int interior, ref bool exterior)
        { if (id == 0) return; if (regions[id].exterior) exterior = true; else if (!voidRegion[id]) interior = id; }

        private static void ClosureNeighbor(int p, byte[] closure, byte[] footprint, byte[] seen, int[] labels,
            List<Region> regions, bool[] voidRegion, int[] queue, ref int tail, ref int interior, ref bool exterior)
        {
            if (closure[p] != 0 && footprint[p] != 0)
            { if (seen[p] == 0) { seen[p] = 1; queue[tail++] = p; } }
            else CheckNeighbor(labels[p], regions, voidRegion, ref interior, ref exterior);
        }

        private static void Pair(int id, ref int a, ref int b)
        { if (id == 0 || id == a || id == b) return; if (a == 0) a = id; else b = id; }

        private static bool HasContinuation(byte[] walls, int p, int step, int length)
        { for (var i = 1; i <= length; i++) if (walls[p + step * i] == 0) return false; return true; }
    }
}
