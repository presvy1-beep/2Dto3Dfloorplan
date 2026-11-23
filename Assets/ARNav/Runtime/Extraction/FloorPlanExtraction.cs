using System;
using System.Collections.Generic;
using System.Threading;
using ARNav.Core.Data;
using ARNav.Core.Geometry;
using ARNav.Core.Generation;
using UnityEngine;

namespace ARNav.Runtime.Extraction
{
    [Serializable]
    public struct FloorPlanCropRegion
    {
        public int x, y, width, height;
        public FloorPlanCropRegion(int x, int y, int width, int height) { this.x = x; this.y = y; this.width = width; this.height = height; }
        public static FloorPlanCropRegion Full(int width, int height) { return new FloorPlanCropRegion(0, 0, width, height); }
        public bool IsValid(int imageWidth, int imageHeight) { return width > 0 && height > 0 && x >= 0 && y >= 0 && x + width <= imageWidth && y + height <= imageHeight; }
    }

    public sealed class FloorPlanImageData
    {
        public readonly int width, height;
        public readonly byte[] luminance;
        // Keep the decoded colour snapshot: a saturated page/background colour can
        // have the same luminance as a dark wall (for example green -> grey).
        public readonly Color32[] colorPixels;
        private CancellationToken cancellationToken;
        private int accessCounter;
        private readonly Action<FloorPlanAnalysisStage> stageReporter;
        public FloorPlanImageData(int width, int height, byte[] luminance)
            : this(width, height, luminance, null) { }
        public FloorPlanImageData(int width, int height, byte[] luminance, Color32[] colorPixels)
        {
            this.width = width; this.height = height; this.luminance = luminance; this.colorPixels = colorPixels;
            if (width <= 0 || height <= 0 || luminance == null || luminance.Length != width * height) throw new ArgumentException("Invalid floor-plan pixel buffer.");
            if (colorPixels != null && colorPixels.Length != width * height) throw new ArgumentException("Invalid floor-plan colour buffer.");
        }
        internal FloorPlanImageData(int width, int height, Color32[] ownedPixels, CancellationToken token, Action<FloorPlanAnalysisStage> reportStage)
        {
            this.width = width; this.height = height; colorPixels = ownedPixels; cancellationToken = token; stageReporter = reportStage;
            if (width <= 0 || height <= 0 || ownedPixels == null || (long)width * height != ownedPixels.Length) throw new ArgumentException("Invalid floor-plan pixel buffer.");
            luminance = new byte[ownedPixels.Length];
            for (var i = 0; i < ownedPixels.Length; i++) { if ((i & 4095) == 0) token.ThrowIfCancellationRequested(); var p = ownedPixels[i]; luminance[i] = (byte)Math.Max(0, Math.Min(255, ExtractionMath.RoundToInt(p.r * 0.2126f + p.g * 0.7152f + p.b * 0.0722f))); }
        }
        public byte Get(int x, int y) { CheckCancellation(); return luminance[y * width + x]; }
        public Color32 GetColor(int x, int y) { CheckCancellation(); return colorPixels == null ? new Color32(GetUnchecked(x, y), GetUnchecked(x, y), GetUnchecked(x, y), 255) : colorPixels[y * width + x]; }
        private byte GetUnchecked(int x, int y) { return luminance[y * width + x]; }
        private void CheckCancellation() { if ((++accessCounter & 4095) == 0) cancellationToken.ThrowIfCancellationRequested(); }
        internal void ThrowIfCancellationRequested() { cancellationToken.ThrowIfCancellationRequested(); }
        internal CancellationToken CancellationToken { get { return cancellationToken; } }
        internal void ReportStage(FloorPlanAnalysisStage stage) { stageReporter?.Invoke(stage); }
    }

    public enum FloorPlanAnalysisStage { PreparingImage, DetectingWalls, DetectingRoomRegions, GeneratingMapGeometry, Complete }

    public sealed class FloorPlanExtractionResult
    {
        public bool succeeded;
        public float confidence;
        public string message;
        public FloorPlanAnnotationData annotations = new FloorPlanAnnotationData();
        public FloorPlanCropRegion analyzedRegion;
        public int uncertainRegionCount;
        public int uncertainDoorCandidateCount;
        public string sessionId;
        public string sourceSha256;
        public long sourceByteLength;
        public FloorPlanExtractionDiagnostics diagnostics = new FloorPlanExtractionDiagnostics();
    }

    [Serializable]
    public sealed class FloorPlanExtractionDiagnostics
    {
        public int grayscaleThreshold, estimatedWallThicknessPixels, sourceDrawingPixels;
        public int gridWidth, gridHeight, blockedCells, exteriorCells, interiorCells, extractedComponents;
        public float roomMaskCoverage, roomMaskFalseFill, wallCenterlinePrecision, wallMaskRecall, wallMaskIoU;
        [NonSerialized] public byte[] rawThresholdMask, cleanedWallMask, exteriorMask, interiorMask, regionLabels, contourMask, openingMask;
        [NonSerialized] public byte[] binaryBlackMask, textExclusionMask, architecturalWallMask;
    }

    public interface IFloorPlanExtractor
    {
        FloorPlanExtractionResult Extract(FloorPlanImageData image, FloorPlanCropRegion region);
    }

    /// Dependency-free first-pass extractor for clean architectural sheets. It detects
    /// long horizontal/vertical dark runs and treats the enclosed cells as editable
    /// region candidates. It intentionally returns confidence/diagnostics: review remains
    /// part of the workflow and a future CV extractor can implement the same interface.
    public sealed class SimpleFloorPlanExtractor : IFloorPlanExtractor
    {
        private const int DarkThreshold = 150;
        private const int MinimumCell = 12;
        public bool CaptureDiagnosticMasks { get; set; }

        public FloorPlanExtractionResult Extract(FloorPlanImageData image, FloorPlanCropRegion region)
        {
            var result = new FloorPlanExtractionResult { analyzedRegion = region };
            if (image == null || !region.IsValid(image.width, image.height)) { result.message = "Invalid image or analysis region."; return result; }
            if (TryCropDocumentSidebar(image, region, out var contentRegion)) region = contentRegion;
            result.analyzedRegion = region;
            image.ThrowIfCancellationRequested(); image.ReportStage(FloorPlanAnalysisStage.DetectingWalls);
            if (TryGetColouredBorderBackground(image, region, out var backgroundColor, out var backgroundShare))
            {
                result = ExtractLocalized(image, region, backgroundColor);
                result.diagnostics.sourceDrawingPixels = CountNonBackgroundPixels(image, region, backgroundColor);
                AddAxisAlignedOpeningCandidates(image, region, backgroundColor, result.annotations);
                MeasureWallFidelity(image, backgroundColor, result.annotations, result.diagnostics.grayscaleThreshold, result.diagnostics.estimatedWallThicknessPixels, result.diagnostics);
                if (CaptureDiagnosticMasks && result.diagnostics.openingMask != null) CaptureOpeningAnnotations(result.annotations, image.width, image.height, result.diagnostics.openingMask);
                result.message += " Coloured border background excluded (" + ExtractionMath.RoundToInt(backgroundShare * 100) + "% edge agreement).";
                return result;
            }
            var xs = FindRuns(image, region, true); var ys = FindRuns(image, region, false);
            image.ThrowIfCancellationRequested(); image.ReportStage(FloorPlanAnalysisStage.DetectingRoomRegions);
            if (xs.Count < 2 || ys.Count < 2) return ExtractLocalized(image, region);
            var roomIndex = 1;
            for (var yi = 0; yi < ys.Count - 1; yi++) for (var xi = 0; xi < xs.Count - 1; xi++)
            {
                var left = xs[xi]; var right = xs[xi + 1]; var top = ys[yi]; var bottom = ys[yi + 1];
                if (right - left < MinimumCell || bottom - top < MinimumCell) continue;
                var isCorridor = (right - left) < (bottom - top) * 0.9f;
                var room = new RoomAnnotationData { id = "extracted_room_" + roomIndex++, displayName = isCorridor ? "Detected Corridor" : "Detected Room " + (roomIndex - 1), category = "Detected", regionType = isCorridor ? "Corridor" : "Room", walkable = true, destinationSelectable = !isCorridor, detectionConfidence = 0.7f };
                room.vertices.Add(new PixelPoint(left, top)); room.vertices.Add(new PixelPoint(right, top)); room.vertices.Add(new PixelPoint(right, bottom)); room.vertices.Add(new PixelPoint(left, bottom)); result.annotations.rooms.Add(room);
            }
            var wallIndex = 1;
            foreach (var x in xs) { var wall = new WallAnnotationData { id = "extracted_wall_" + wallIndex++, start = new PixelPoint(x, ys[0]), end = new PixelPoint(x, ys[ys.Count - 1]) }; result.annotations.walls.Add(wall); AppendGapDoor(image, region, wall, true, result.annotations); }
            foreach (var y in ys) { var wall = new WallAnnotationData { id = "extracted_wall_" + wallIndex++, start = new PixelPoint(xs[0], y), end = new PixelPoint(xs[xs.Count - 1], y) }; result.annotations.walls.Add(wall); AppendGapDoor(image, region, wall, false, result.annotations); }
            result.succeeded = result.annotations.rooms.Count > 0;
            result.confidence = result.succeeded ? (xs.Count >= 3 && ys.Count >= 2 ? 0.7f : 0.45f) : 0f;
            result.message = result.succeeded ? "Detected editable room and wall candidates." : "No valid room candidates detected; manual review is required.";
            image.ReportStage(FloorPlanAnalysisStage.GeneratingMapGeometry);
            ArchitecturalWallMaskProcessor.Process(image, region, result.annotations, CaptureDiagnosticMasks ? result.diagnostics : null);
            result.diagnostics.grayscaleThreshold = 225;
            result.diagnostics.estimatedWallThicknessPixels = EstimateWallThickness(image, region.x, region.y, region.x + region.width - 1, region.y + region.height - 1, null, DarkThreshold);
            MeasureRasterRoomFidelity(image, null, result.annotations, 225, result.diagnostics);
            MeasureWallFidelity(image, null, result.annotations, DarkThreshold, result.diagnostics.estimatedWallThicknessPixels, result.diagnostics);
            return result;
        }

        private static bool TryCropDocumentSidebar(FloorPlanImageData image, FloorPlanCropRegion region, out FloorPlanCropRegion content)
        {
            content = region;
            if (region.width < 240 || region.height < 300) return false;
            var columns = new List<int>();
            for (var x = region.x; x < region.x + region.width; x++)
            {
                var dark = 0;
                for (var y = region.y; y < region.y + region.height; y++) if (image.Get(x, y) < DarkThreshold) dark++;
                if (dark < region.height * .83f) continue;
                if (columns.Count == 0 || x - columns[columns.Count - 1] > 3) columns.Add(x);
            }
            // A title block has several independent full-height rules just to the
            // right of the drawing. Require a gap after the building's last wall
            // before selecting the sidebar; otherwise the exterior wall is clipped.
            var right = -1;
            for (var i = 1; i + 2 < columns.Count; i++)
            {
                var first = columns[i] - region.x;
                if (first < region.width * .6f || first > region.width * .85f) continue;
                if (columns[i] - columns[i - 1] >= region.width * .03f &&
                    columns[i] - columns[i - 1] <= region.width * .08f &&
                    columns[i + 2] - columns[i] <= region.width * .1f)
                { right = columns[i] + Math.Max(3, region.width / 100); break; }
            }
            if (right < 0 || right >= region.x + region.width - region.width / 8) return false;
            var left = region.x;
            foreach (var x in columns) if (x - region.x < region.width * .08f && x + 3 < right) left = x + 3;
            if (right - left < region.width * .45f) return false;
            content = new FloorPlanCropRegion(left, region.y, right - left, region.height);
            return true;
        }

        // Real architectural sheets rarely have lines spanning the whole crop. This
        // conservative fallback works on a coarse raster: it finds the drawing bounds,
        // extracts long localized line runs (including diagonals), and uses large enclosed
        // white components as editable region candidates. All points are returned in the
        // original image coordinate system.
        private FloorPlanExtractionResult ExtractLocalized(FloorPlanImageData image, FloorPlanCropRegion region, Color32? knownBackground = null)
        {
            var result = new FloorPlanExtractionResult { analyzedRegion = region };
            var minX = image.width; var minY = image.height; var maxX = -1; var maxY = -1;
            var drawingPixels = 0;
            for (var y = region.y; y < region.y + region.height; y++) for (var x = region.x; x < region.x + region.width; x++)
            {
                var isBackground = knownBackground.HasValue && IsNearBackground(image.GetColor(x, y), knownBackground.Value);
                if (isBackground || image.Get(x, y) >= 248) continue;
                drawingPixels++;
                if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y;
            }
            result.diagnostics.sourceDrawingPixels = drawingPixels;
            if (maxX < minX || maxY < minY) { result.message = "No architectural drawing pixels were found."; return result; }
            ArchitecturalWallMaskProcessor.Process(image, region, result.annotations, CaptureDiagnosticMasks ? result.diagnostics : null);
            var structuralPixels = new bool[image.width * image.height];
            foreach (var run in result.annotations.wallRuns)
                for (var py = (int)run.minY; py < (int)run.maxY; py++)
                    for (var px = (int)run.minX; px < (int)run.maxX; px++)
                        structuralPixels[py * image.width + px] = true;
            var step = ExtractionMath.Clamp(ExtractionMath.RoundToInt(ExtractionMath.Min(region.width, region.height) / 220f), 2, 6);
            var cols = ExtractionMath.CeilToInt((maxX - minX + 1) / (float)step); var rows = ExtractionMath.CeilToInt((maxY - minY + 1) / (float)step);
            var blocked = new bool[cols * rows];
            var blockedCount = 0;
            var backgroundCellCount = 0; var interiorCellCount = 0;
            // With a coloured edge/background, the source contains pale furniture,
            // labels and dimensions inside the plan. Keep those out of the wall mask;
            // dark architectural strokes remain the wall class. For monochrome plans
            // retain the more tolerant local threshold used by the established path.
            var wallThreshold = knownBackground.HasValue ? 155 : 225;
            for (var gy = 0; gy < rows; gy++) for (var gx = 0; gx < cols; gx++)
            {
                var wallInk = 0; var background = 0; var samples = 0;
                for (var oy = 0; oy < step && minY + gy * step + oy <= maxY; oy++) for (var ox = 0; ox < step && minX + gx * step + ox <= maxX; ox++)
                {
                    var px = minX + gx * step + ox; var py = minY + gy * step + oy; samples++;
                    if (knownBackground.HasValue && IsNearBackground(image.GetColor(px, py), knownBackground.Value)) { background++; continue; }
                    if (structuralPixels[py * image.width + px]) wallInk++;
                }
                // The previous 1/3 rule erased single-pixel architectural strokes
                // during downsampling. A 16% occupancy retains thin walls while
                // suppressing isolated antialias/noise pixels.
                blocked[gy * cols + gx] = (knownBackground.HasValue && background >= ExtractionMath.Max(1, samples / 3)) || wallInk >= ExtractionMath.Max(2, ExtractionMath.CeilToInt(samples * .16f));
                if (blocked[gy * cols + gx]) { blockedCount++; if (knownBackground.HasValue && background >= ExtractionMath.Max(1, samples / 3)) backgroundCellCount++; }
            }
            result.diagnostics.gridWidth = cols; result.diagnostics.gridHeight = rows; result.diagnostics.blockedCells = blockedCount; result.diagnostics.grayscaleThreshold = wallThreshold;
            result.diagnostics.estimatedWallThicknessPixels = EstimateWallThickness(image, minX, minY, maxX, maxY, knownBackground, wallThreshold);
            image.ThrowIfCancellationRequested(); image.ReportStage(FloorPlanAnalysisStage.DetectingRoomRegions);
            var boundaryMask = (bool[])blocked.Clone();
            var bridgeCells = new bool[blocked.Length];
            if (!knownBackground.HasValue)
                SealRoomBoundaryGaps(boundaryMask, bridgeCells, cols, rows, ExtractionMath.Max(6, ExtractionMath.RoundToInt(ExtractionMath.Min(region.width, region.height) * .25f / step)));
            var assignedBridge = new bool[blocked.Length];
            var visited = new bool[blocked.Length]; var minimumCells = ExtractionMath.Max(55, cols * rows / 1800); var regionIndex = 1;
            for (var gy = 0; gy < rows; gy++) for (var gx = 0; gx < cols; gx++) if (!boundaryMask[gy * cols + gx] && !visited[gy * cols + gx])
            {
                var cells = new List<int>(); var queue = new Queue<int>(); queue.Enqueue(gy * cols + gx); visited[gy * cols + gx] = true; var left = gx; var right = gx; var top = gy; var bottom = gy;
                while (queue.Count > 0) { var current = queue.Dequeue(); cells.Add(current); var cx = current % cols; var cy = current / cols; left = ExtractionMath.Min(left, cx); right = ExtractionMath.Max(right, cx); top = ExtractionMath.Min(top, cy); bottom = ExtractionMath.Max(bottom, cy); Visit(cx - 1, cy); Visit(cx + 1, cy); Visit(cx, cy - 1); Visit(cx, cy + 1); }
                var pixelWidth = (right - left + 1) * step; var pixelHeight = (bottom - top + 1) * step; if (cells.Count < minimumCells || pixelWidth < ExtractionMath.Max(28, step * 7) || pixelHeight < ExtractionMath.Max(28, step * 7)) continue;
                // Exterior is a topological property: free space connected to the
                // raster boundary is outside, regardless of its area. Large interior
                // halls/irregular rooms must not be mislabeled as void by area ratio.
                var touchesRasterBoundary = false; foreach (var cell in cells) { var cx = cell % cols; var cy = cell / cols; if (cx == 0 || cy == 0 || cx == cols - 1 || cy == rows - 1) { touchesRasterBoundary = true; break; } }
                var corridor = ExtractionMath.Max(pixelWidth, pixelHeight) > ExtractionMath.Min(pixelWidth, pixelHeight) * 3.2f;
                // On a saturated page background, edge contact by itself does not
                // imply exterior: entrances and raster gaps can connect inside space
                // to the crop edge. The background colour is the exterior signal.
                var isVoid = touchesRasterBoundary && !knownBackground.HasValue;
                if (!isVoid)
                {
                    // The temporary bridges are floor openings, not physical walls.
                    // Assign each bridge to one adjacent interior region so the floor
                    // stays continuous through exterior and interior doorways.
                    for (var i = 0; i < cells.Count; i++)
                    {
                        var c = cells[i]; var cx = c % cols; var cy = c / cols;
                        AddBridge(cx - 1, cy); AddBridge(cx + 1, cy); AddBridge(cx, cy - 1); AddBridge(cx, cy + 1);
                    }
                }
                if (!isVoid) interiorCellCount += cells.Count;
                var room = new RoomAnnotationData { id = "localized_region_" + regionIndex++, displayName = isVoid ? "Uncertain Open Area" : corridor ? "Detected Corridor" : "Detected Room", category = "Detected", regionType = isVoid ? "Void" : corridor ? "Corridor" : "Room", walkable = !isVoid, destinationSelectable = !isVoid && !corridor, detectionConfidence = isVoid ? 0.35f : 0.55f, preferRasterGeometry = true };
                if (isVoid || room.detectionConfidence < 0.6f) result.uncertainRegionCount++;
                room.rasterRuns = BuildPixelRuns(cells, cols, step, minX, minY, maxX, maxY, image.width, image.height);
                var loops = TraceComponentContours(cells, cols, step, minX, minY, image.width, image.height);
                if (loops.Count == 0)
                {
                    result.uncertainRegionCount++;
                    // A placeholder envelope keeps the semantic object valid in legacy
                    // polygon consumers; all reconstruction, waypoint and navigation
                    // occupancy uses the exact serialized raster runs for this region.
                    var l = ExtractionMath.Clamp(minX + left * step, 0, image.width - 1); var r = ExtractionMath.Clamp(minX + (right + 1) * step, l + 1, image.width - 1);
                    var t = ExtractionMath.Clamp(minY + top * step, 0, image.height - 1); var b = ExtractionMath.Clamp(minY + (bottom + 1) * step, t + 1, image.height - 1);
                    room.vertices.AddRange(new[] { new PixelPoint(l, t), new PixelPoint(r, t), new PixelPoint(r, b), new PixelPoint(l, b) });
                }
                else
                {
                    loops.Sort((a, b) => Math.Abs(PolygonArea(b)).CompareTo(Math.Abs(PolygonArea(a))));
                    room.vertices.AddRange(loops[0]);
                    for (var loopIndex = 1; loopIndex < loops.Count; loopIndex++) if (Math.Sign(PolygonArea(loops[loopIndex])) != Math.Sign(PolygonArea(loops[0]))) room.holes.Add(new PixelPolygon { vertices = loops[loopIndex] });
                }
                result.annotations.rooms.Add(room);
                void Visit(int nx, int ny) { if (nx < 0 || ny < 0 || nx >= cols || ny >= rows) return; var index = ny * cols + nx; if (boundaryMask[index] || visited[index]) return; visited[index] = true; queue.Enqueue(index); }
                void AddBridge(int nx, int ny) { if (nx < 0 || ny < 0 || nx >= cols || ny >= rows) return; var index = ny * cols + nx; if (!bridgeCells[index] || assignedBridge[index]) return; assignedBridge[index] = true; cells.Add(index); }
            }
            result.diagnostics.extractedComponents = result.annotations.rooms.Count;
            result.diagnostics.exteriorCells = backgroundCellCount;
            result.diagnostics.interiorCells = interiorCellCount;
            if (!knownBackground.HasValue && !result.annotations.rooms.Exists(room => room.walkable))
                TryAddEstimatedInterior(result, structuralPixels, image.width, region);
            result.diagnostics.extractedComponents = result.annotations.rooms.Count;
            image.ReportStage(FloorPlanAnalysisStage.GeneratingMapGeometry);
            AddLocalizedWalls(image, region, minX, minY, maxX, maxY, result.annotations, knownBackground, result.diagnostics.estimatedWallThicknessPixels);
            MeasureRasterRoomFidelity(image, knownBackground, result.annotations, wallThreshold, result.diagnostics);
            MeasureWallFidelity(image, knownBackground, result.annotations, wallThreshold, result.diagnostics.estimatedWallThicknessPixels, result.diagnostics);
            if (CaptureDiagnosticMasks) CaptureStages(image, minX, minY, maxX, maxY, cols, rows, step, blocked, knownBackground, result.annotations, wallThreshold, result.diagnostics);
            result.succeeded = result.annotations.rooms.Count > 0 || result.annotations.walls.Count > 0;
            result.confidence = result.succeeded ? 0.45f : 0f;
            result.message = result.succeeded ? "Detected localized architectural regions and wall candidates; manual review is required." : "No useful localized architectural geometry was detected.";
            if (result.annotations.rooms.Exists(room => room.regionType == "EstimatedInterior"))
                result.message += " Interior floor is estimated from the wall envelope; review its boundary before export.";
            return result;
        }

        private static void TryAddEstimatedInterior(FloorPlanExtractionResult result, bool[] wallPixels, int imageWidth, FloorPlanCropRegion region)
        {
            var minX = region.x + region.width; var minY = region.y + region.height;
            var maxX = region.x - 1; var maxY = region.y - 1;
            for (var y = region.y; y < region.y + region.height; y++)
            for (var x = region.x; x < region.x + region.width; x++) if (wallPixels[y * imageWidth + x])
            { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
            var width = maxX - minX + 1; var height = maxY - minY + 1;
            if (width < region.width * .4f || height < region.height * .4f) return;
            var left = new int[height]; var right = new int[height];
            for (var row = 0; row < height; row++)
            {
                left[row] = -1; right[row] = -1;
                var y = minY + row; var first = maxX + 1; var last = minX - 1;
                for (var x = minX; x <= maxX; x++) if (wallPixels[y * imageWidth + x])
                { first = Math.Min(first, x); last = Math.Max(last, x); }
                if (first <= minX + width * .25f && last - first >= width * .35f) left[row] = first;
                if (last >= maxX - width * .25f && last - first >= width * .35f) right[row] = last;
            }
            var room = new RoomAnnotationData { id = "estimated_interior_1", displayName = "Estimated Interior (review)", category = "Detected", regionType = "EstimatedInterior", walkable = true, destinationSelectable = false, detectionConfidence = .25f, preferRasterGeometry = true };
            for (var row = 0; row < height; row++)
            {
                var l = Interpolate(left, row, minX); var r = Interpolate(right, row, maxX);
                if (r - l < width * .3f) continue;
                room.rasterRuns.Add(new PixelRasterRun { minX = l, maxX = r + 1, minY = minY + row, maxY = minY + row + 1 });
            }
            if (room.rasterRuns.Count < height * .5f) return;
            room.vertices.AddRange(new[] { new PixelPoint(minX, minY), new PixelPoint(maxX + 1, minY), new PixelPoint(maxX + 1, maxY + 1), new PixelPoint(minX, maxY + 1) });
            result.annotations.rooms.Add(room);
            result.uncertainRegionCount++;
            int Interpolate(int[] values, int at, int fallback)
            {
                if (values[at] >= 0) return values[at];
                var before = at - 1; while (before >= 0 && values[before] < 0) before--;
                var after = at + 1; while (after < values.Length && values[after] < 0) after++;
                if (before < 0) return after < values.Length ? values[after] : fallback;
                if (after >= values.Length) return values[before];
                return ExtractionMath.RoundToInt(values[before] + (values[after] - values[before]) * (at - before) / (float)(after - before));
            }
        }

        private static void SealRoomBoundaryGaps(bool[] mask, bool[] bridges, int cols, int rows, int maxGap)
        {
            const int minimumSupport = 8;
            for (var axis = 0; axis < 2; axis++)
            {
                var lineCount = axis == 0 ? cols : rows;
                var lineLength = axis == 0 ? rows : cols;
                for (var line = 0; line < lineCount; line++)
                {
                    var position = 0;
                    var previousEnd = -1; var previousLength = 0;
                    while (position < lineLength)
                    {
                        while (position < lineLength && !mask[Index(line, position)]) position++;
                        if (position >= lineLength) break;
                        var start = position;
                        while (position < lineLength && mask[Index(line, position)]) position++;
                        var length = position - start;
                        var gap = start - previousEnd;
                        if (previousEnd >= 0 && previousLength >= minimumSupport && length >= minimumSupport &&
                            gap > 0 && gap <= maxGap &&
                            previousLength >= gap * .6f && length >= gap * .6f)
                            for (var p = previousEnd; p < start; p++) { var index = Index(line, p); mask[index] = true; bridges[index] = true; }
                        previousEnd = position; previousLength = length;
                    }
                    int Index(int fixedPosition, int variablePosition)
                    { return axis == 0 ? variablePosition * cols + fixedPosition : fixedPosition * cols + variablePosition; }
                }
            }
        }

        private static List<PixelRasterRun> BuildPixelRuns(List<int> cells, int cols, int step, int originX, int originY, int maxX, int maxY, int imageWidth, int imageHeight)
        {
            var sorted = new List<int>(cells); sorted.Sort(); var runs = new List<PixelRasterRun>(); var index = 0;
            while (index < sorted.Count)
            {
                var first = sorted[index]; var y = first / cols; var startX = first % cols; var endX = startX; index++;
                while (index < sorted.Count && sorted[index] / cols == y && sorted[index] % cols <= endX + 1) { endX = Math.Max(endX, sorted[index] % cols); index++; }
                var left = ExtractionMath.Clamp(originX + startX * step, 0, imageWidth - 1); var right = ExtractionMath.Clamp(originX + (endX + 1) * step, left + 1, imageWidth);
                var top = ExtractionMath.Clamp(originY + y * step, 0, imageHeight - 1); var bottom = ExtractionMath.Clamp(originY + (y + 1) * step, top + 1, imageHeight);
                if (left <= maxX && top <= maxY) runs.Add(new PixelRasterRun { minX = left, minY = top, maxX = Math.Min(right, imageWidth), maxY = Math.Min(bottom, imageHeight) });
            }
            // Input order is row-major. Merge equal horizontal spans in adjacent rows
            // in O(n), avoiding quadratic work for noisy/high-resolution masks.
            var compact = new List<PixelRasterRun>(); var previous = new Dictionary<string, PixelRasterRun>();
            foreach (var run in runs)
            {
                var key = run.minX.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" + run.maxX.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                if (previous.TryGetValue(key, out var last) && ExtractionMath.Abs(last.maxY - run.minY) < .01f) last.maxY = run.maxY;
                else { compact.Add(run); previous[key] = run; }
            }
            return compact;
        }

        private static bool TryGetColouredBorderBackground(FloorPlanImageData image, FloorPlanCropRegion region, out Color32 background, out float share)
        {
            background = new Color32(255, 255, 255, 255); share = 0;
            if (image.colorPixels == null) return false;
            var bins = new Dictionary<int, int>(); var samples = new List<Color32>(); var stride = ExtractionMath.Max(1, ExtractionMath.Min(region.width, region.height) / 500);
            for (var x = region.x; x < region.x + region.width; x += stride) { samples.Add(image.GetColor(x, region.y)); samples.Add(image.GetColor(x, region.y + region.height - 1)); }
            for (var y = region.y + stride; y < region.y + region.height - stride; y += stride) { samples.Add(image.GetColor(region.x, y)); samples.Add(image.GetColor(region.x + region.width - 1, y)); }
            foreach (var c in samples) { var key = ((c.r >> 4) << 8) | ((c.g >> 4) << 4) | (c.b >> 4); bins.TryGetValue(key, out var count); bins[key] = count + 1; }
            var bestKey = -1; var bestCount = 0;
            foreach (var pair in bins) if (pair.Value > bestCount || (pair.Value == bestCount && pair.Key < bestKey)) { bestKey = pair.Key; bestCount = pair.Value; }
            if (bestKey < 0 || samples.Count == 0) return false;
            var r = 0; var g = 0; var b = 0; var exactCount = 0;
            foreach (var c in samples) if ((((c.r >> 4) << 8) | ((c.g >> 4) << 4) | (c.b >> 4)) == bestKey) { r += c.r; g += c.g; b += c.b; exactCount++; }
            share = bestCount / (float)samples.Count; background = new Color32((byte)(r / exactCount), (byte)(g / exactCount), (byte)(b / exactCount), 255);
            var max = ExtractionMath.Max(background.r, background.g, background.b); var min = ExtractionMath.Min(background.r, background.g, background.b);
            return share >= .22f && max - min >= 28;
        }

        private static bool IsNearBackground(Color32 pixel, Color32 background)
        {
            var dr = pixel.r - background.r; var dg = pixel.g - background.g; var db = pixel.b - background.b;
            return dr * dr + dg * dg + db * db <= 38 * 38 * 3;
        }

        private static int CountNonBackgroundPixels(FloorPlanImageData image, FloorPlanCropRegion region, Color32 background)
        {
            var count = 0; for (var y = region.y; y < region.y + region.height; y++) for (var x = region.x; x < region.x + region.width; x++) if (!IsNearBackground(image.GetColor(x, y), background)) count++;
            return count;
        }

        private static int EstimateWallThickness(FloorPlanImageData image, int minX, int minY, int maxX, int maxY, Color32? background, int threshold)
        {
            var lengths = new int[25];
            for (var y = minY; y <= maxY; y += 7) CountRuns(image, minX, maxX, y, true, background, threshold, lengths);
            for (var x = minX; x <= maxX; x += 7) CountRuns(image, minY, maxY, x, false, background, threshold, lengths);
            var total = 0; for (var i = 1; i < lengths.Length; i++) total += lengths[i]; if (total == 0) return 1;
            var half = (total + 1) / 2; var cumulative = 0; for (var i = 1; i < lengths.Length; i++) { cumulative += lengths[i]; if (cumulative >= half) return i; }
            return 1;
        }

        private static void CountRuns(FloorPlanImageData image, int from, int to, int fixedCoordinate, bool horizontal, Color32? background, int threshold, int[] histogram)
        {
            var length = 0;
            for (var p = from; p <= to + 1; p++)
            {
                var x = horizontal ? p : fixedCoordinate; var y = horizontal ? fixedCoordinate : p;
                var ink = p <= to && image.Get(x, y) < threshold && (!background.HasValue || !IsNearBackground(image.GetColor(x, y), background.Value));
                if (ink) length++;
                else { if (length > 0 && length < histogram.Length) histogram[length]++; length = 0; }
            }
        }

        private static void MeasureRasterRoomFidelity(FloorPlanImageData image, Color32? background, FloorPlanAnnotationData annotations, int threshold, FloorPlanExtractionDiagnostics diagnostics)
        {
            var mask = new bool[image.width * image.height];
            long sourceInterior = 0; long reconstructed = 0; long overlap = 0;
            for (var y = 0; y < image.height; y++) for (var x = 0; x < image.width; x++)
                if ((!background.HasValue || !IsNearBackground(image.GetColor(x, y), background.Value)) && image.Get(x, y) > threshold) sourceInterior++;
            foreach (var room in annotations.rooms)
            {
                if (room == null || !room.walkable) continue;
                if (room.rasterRuns != null && room.rasterRuns.Count > 0) foreach (var run in room.rasterRuns)
                {
                    var x0 = ExtractionMath.Clamp(ExtractionMath.FloorToInt(run.minX), 0, image.width); var x1 = ExtractionMath.Clamp(ExtractionMath.CeilToInt(run.maxX), 0, image.width);
                    var y0 = ExtractionMath.Clamp(ExtractionMath.FloorToInt(run.minY), 0, image.height); var y1 = ExtractionMath.Clamp(ExtractionMath.CeilToInt(run.maxY), 0, image.height);
                    for (var y = y0; y < y1; y++) for (var x = x0; x < x1; x++) mask[y * image.width + x] = true;
                }
                else if (room.vertices != null && room.vertices.Count >= 3)
                {
                    var minX = image.width - 1; var maxX = 0; var minY = image.height - 1; var maxY = 0;
                    foreach (var p in room.vertices) { minX = ExtractionMath.Min(minX, ExtractionMath.FloorToInt(p.x)); maxX = ExtractionMath.Max(maxX, ExtractionMath.CeilToInt(p.x)); minY = ExtractionMath.Min(minY, ExtractionMath.FloorToInt(p.y)); maxY = ExtractionMath.Max(maxY, ExtractionMath.CeilToInt(p.y)); }
                    for (var y = ExtractionMath.Clamp(minY, 0, image.height - 1); y <= ExtractionMath.Clamp(maxY, 0, image.height - 1); y++) for (var x = ExtractionMath.Clamp(minX, 0, image.width - 1); x <= ExtractionMath.Clamp(maxX, 0, image.width - 1); x++)
                    {
                        var point = new PixelPoint(x + .5f, y + .5f); if (!Contains(room.vertices, point)) continue;
                        var inHole = false; if (room.holes != null) foreach (var hole in room.holes) if (hole != null && Contains(hole.vertices, point)) { inHole = true; break; }
                        if (!inHole) mask[y * image.width + x] = true;
                    }
                }
            }
            for (var y = 0; y < image.height; y++) for (var x = 0; x < image.width; x++) if (mask[y * image.width + x] && (!background.HasValue || !IsNearBackground(image.GetColor(x, y), background.Value)))
            { reconstructed++; if (image.Get(x, y) > threshold) overlap++; }
            diagnostics.roomMaskCoverage = sourceInterior == 0 ? 0 : (float)overlap / sourceInterior;
            diagnostics.roomMaskFalseFill = reconstructed == 0 ? 0 : (float)(reconstructed - overlap) / reconstructed;
        }

        private static bool Contains(List<PixelPoint> polygon, PixelPoint p)
        {
            var inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                var a = polygon[i]; var b = polygon[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        private static void AddAxisAlignedOpeningCandidates(FloorPlanImageData image, FloorPlanCropRegion region, Color32 background, FloorPlanAnnotationData annotations)
        {
            var xs = FindRuns(image, region, true, background, .55f); var ys = FindRuns(image, region, false, background, .55f);
            var minDoor = 8; var maxDoor = ExtractionMath.Max(30, ExtractionMath.RoundToInt(ExtractionMath.Min(region.width, region.height) * .12f)); var candidateIndex = 0;
            for (var i = 0; i < xs.Count; i++)
            {
                var candidate = new WallAnnotationData { id = "axis_opening_wall_" + candidateIndex++, start = new PixelPoint(xs[i], region.y), end = new PixelPoint(xs[i], region.y + region.height - 1) };
                var probe = new FloorPlanAnnotationData(); probe.rooms = annotations.rooms;
                AppendGapDoor(image, region, candidate, true, probe);
                KeepCandidateIfOpening(candidate, probe, annotations, minDoor, maxDoor);
            }
            for (var i = 0; i < ys.Count; i++)
            {
                var candidate = new WallAnnotationData { id = "axis_opening_wall_" + candidateIndex++, start = new PixelPoint(region.x, ys[i]), end = new PixelPoint(region.x + region.width - 1, ys[i]) };
                var probe = new FloorPlanAnnotationData(); probe.rooms = annotations.rooms;
                AppendGapDoor(image, region, candidate, false, probe);
                KeepCandidateIfOpening(candidate, probe, annotations, minDoor, maxDoor);
            }
        }

        private static void KeepCandidateIfOpening(WallAnnotationData candidate, FloorPlanAnnotationData probe, FloorPlanAnnotationData annotations, int minimumGap, int maximumGap)
        {
            if (probe.doors.Count == 0) return;
            var door = probe.doors[0]; var gapWidth = Length(candidate) * (door.endT - door.startT);
            if (gapWidth < minimumGap || gapWidth > maximumGap) return;
            // Replace overlapping line fragments by this full support line so the
            // opening remains explicitly attached to one serialized wall.
            for (var i = annotations.walls.Count - 1; i >= 0; i--) if (CollinearOverlap(annotations.walls[i], candidate, 10)) annotations.walls.RemoveAt(i);
            candidate.id = "axis_opening_wall_" + annotations.walls.Count;
            door.id = "extracted_opening_" + annotations.doors.Count; door.wallId = candidate.id;
            annotations.walls.Add(candidate); annotations.doors.Add(door);
        }

        private static bool CollinearOverlap(WallAnnotationData a, WallAnnotationData b, float tolerance)
        {
            var adx = a.end.x - a.start.x; var ady = a.end.y - a.start.y; var bdx = b.end.x - b.start.x; var bdy = b.end.y - b.start.y;
            var al = ExtractionMath.Sqrt(adx * adx + ady * ady); var bl = ExtractionMath.Sqrt(bdx * bdx + bdy * bdy); if (al < .001f || bl < .001f) return false;
            var ux = adx / al; var uy = ady / al; var bux = bdx / bl; var buy = bdy / bl; if (ux * bux + uy * buy < 0) { bux = -bux; buy = -buy; }
            if (ExtractionMath.Abs(ux * buy - uy * bux) > .06f) return false;
            var nx = -uy; var ny = ux; var distance = ExtractionMath.Abs(((a.start.x + a.end.x - b.start.x - b.end.x) * .5f) * nx + ((a.start.y + a.end.y - b.start.y - b.end.y) * .5f) * ny);
            if (distance > tolerance) return false;
            Project(a, ux, uy, out var a0, out var a1); Project(b, ux, uy, out var b0, out var b1);
            return b0 <= a1 && a0 <= b1;
        }

        private static void MeasureWallFidelity(FloorPlanImageData image, Color32? background, FloorPlanAnnotationData annotations, int threshold, int thickness, FloorPlanExtractionDiagnostics diagnostics)
        {
            if (threshold <= 0) return;
            var reconstructed = new bool[image.width * image.height]; var radius = ExtractionMath.Clamp(thickness / 2, 1, 8);
            foreach (var wall in annotations.walls)
            {
                var steps = ExtractionMath.Max(1, ExtractionMath.CeilToInt(Length(wall)));
                for (var i = 0; i <= steps; i++)
                {
                    var cx = ExtractionMath.RoundToInt(ExtractionMath.Lerp(wall.start.x, wall.end.x, i / (float)steps)); var cy = ExtractionMath.RoundToInt(ExtractionMath.Lerp(wall.start.y, wall.end.y, i / (float)steps));
                    for (var oy = -radius; oy <= radius; oy++) for (var ox = -radius; ox <= radius; ox++) if (ox * ox + oy * oy <= radius * radius)
                    {
                        var x = cx + ox; var y = cy + oy; if (x >= 0 && y >= 0 && x < image.width && y < image.height) reconstructed[y * image.width + x] = true;
                    }
                }
            }
            long sourceCount = 0, reconstructedCount = 0, overlap = 0;
            for (var y = 0; y < image.height; y++) for (var x = 0; x < image.width; x++)
            {
                var source = image.Get(x, y) < threshold && (!background.HasValue || !IsNearBackground(image.GetColor(x, y), background.Value));
                var model = reconstructed[y * image.width + x]; if (source) sourceCount++; if (model) reconstructedCount++; if (source && model) overlap++;
            }
            diagnostics.wallCenterlinePrecision = reconstructedCount == 0 ? 0 : (float)overlap / reconstructedCount;
            diagnostics.wallMaskRecall = sourceCount == 0 ? 0 : (float)overlap / sourceCount;
            diagnostics.wallMaskIoU = sourceCount + reconstructedCount - overlap == 0 ? 0 : (float)overlap / (sourceCount + reconstructedCount - overlap);
        }

        private static void CaptureStages(FloorPlanImageData image, int minX, int minY, int maxX, int maxY, int cols, int rows, int step, bool[] blocked, Color32? background, FloorPlanAnnotationData annotations, int threshold, FloorPlanExtractionDiagnostics d)
        {
            var size = image.width * image.height;
            d.rawThresholdMask = new byte[size]; d.cleanedWallMask = new byte[size]; d.exteriorMask = new byte[size]; d.interiorMask = new byte[size]; d.regionLabels = new byte[size]; d.contourMask = new byte[size]; d.openingMask = new byte[size];
            for (var y = minY; y <= maxY; y++) for (var x = minX; x <= maxX; x++)
            {
                var index = y * image.width + x; var bg = background.HasValue && IsNearBackground(image.GetColor(x, y), background.Value);
                if (bg) d.exteriorMask[index] = 255;
                else if (image.Get(x, y) < threshold) d.rawThresholdMask[index] = 255;
                var gx = ExtractionMath.Clamp((x - minX) / step, 0, cols - 1); var gy = ExtractionMath.Clamp((y - minY) / step, 0, rows - 1);
                if (blocked[gy * cols + gx] && !bg) d.cleanedWallMask[index] = 255;
            }
            for (var i = 0; i < annotations.rooms.Count; i++)
            {
                var room = annotations.rooms[i]; if (room.rasterRuns == null) continue;
                var label = (byte)ExtractionMath.Clamp(32 + (i * 29) % 224, 1, 255);
                foreach (var run in room.rasterRuns) for (var y = ExtractionMath.Clamp(ExtractionMath.FloorToInt(run.minY), 0, image.height); y < ExtractionMath.Clamp(ExtractionMath.CeilToInt(run.maxY), 0, image.height); y++) for (var x = ExtractionMath.Clamp(ExtractionMath.FloorToInt(run.minX), 0, image.width); x < ExtractionMath.Clamp(ExtractionMath.CeilToInt(run.maxX), 0, image.width); x++)
                {
                    var index = y * image.width + x;
                    if (room.walkable) { d.interiorMask[index] = 255; d.regionLabels[index] = label; }
                    else d.exteriorMask[index] = 255;
                }
                TraceDiagnosticPolygon(room.vertices, image.width, image.height, d.contourMask);
                if (room.holes != null) foreach (var hole in room.holes) TraceDiagnosticPolygon(hole.vertices, image.width, image.height, d.contourMask);
            }
            foreach (var door in annotations.doors)
            {
                var wall = annotations.walls.Find(w => w != null && w.id == door.wallId); if (wall == null) continue;
                var sx = wall.start.x + (wall.end.x - wall.start.x) * door.startT; var sy = wall.start.y + (wall.end.y - wall.start.y) * door.startT;
                var ex = wall.start.x + (wall.end.x - wall.start.x) * door.endT; var ey = wall.start.y + (wall.end.y - wall.start.y) * door.endT;
                TraceDiagnosticSegment(sx, sy, ex, ey, image.width, image.height, d.openingMask);
            }
        }

        private static void TraceDiagnosticPolygon(List<PixelPoint> points, int width, int height, byte[] mask)
        { if (points == null || points.Count < 2) return; for (var i = 0; i < points.Count; i++) TraceDiagnosticSegment(points[i].x, points[i].y, points[(i + 1) % points.Count].x, points[(i + 1) % points.Count].y, width, height, mask); }
        private static void CaptureOpeningAnnotations(FloorPlanAnnotationData annotations, int width, int height, byte[] mask)
        {
            foreach (var door in annotations.doors)
            {
                var wall = annotations.walls.Find(w => w != null && w.id == door.wallId); if (wall == null) continue;
                TraceDiagnosticSegment(ExtractionMath.Lerp(wall.start.x, wall.end.x, door.startT), ExtractionMath.Lerp(wall.start.y, wall.end.y, door.startT), ExtractionMath.Lerp(wall.start.x, wall.end.x, door.endT), ExtractionMath.Lerp(wall.start.y, wall.end.y, door.endT), width, height, mask);
            }
        }
        private static void TraceDiagnosticSegment(float ax, float ay, float bx, float by, int width, int height, byte[] mask)
        {
            var steps = ExtractionMath.Max(1, ExtractionMath.CeilToInt(ExtractionMath.Distance(ax, ay, bx, by)));
            for (var i = 0; i <= steps; i++) { var x = ExtractionMath.RoundToInt(ExtractionMath.Lerp(ax, bx, i / (float)steps)); var y = ExtractionMath.RoundToInt(ExtractionMath.Lerp(ay, by, i / (float)steps)); if (x >= 0 && y >= 0 && x < width && y < height) mask[y * width + x] = 255; }
        }

        // Preserve concave component outlines from the occupancy raster instead of
        // replacing every connected room/corridor with its axis-aligned bounding box.
        private static List<List<PixelPoint>> TraceComponentContours(List<int> cells, int cols, int step, int originX, int originY, int imageWidth, int imageHeight)
        {
            var members = new HashSet<int>(cells); var edges = new List<RasterEdge>();
            foreach (var cell in cells)
            {
                var x = cell % cols; var y = cell / cols;
                if (y == 0 || !members.Contains(cell - cols)) edges.Add(new RasterEdge(x, y, x + 1, y));
                if (x == cols - 1 || !members.Contains(cell + 1)) edges.Add(new RasterEdge(x + 1, y, x + 1, y + 1));
                if (!members.Contains(cell + cols)) edges.Add(new RasterEdge(x + 1, y + 1, x, y + 1));
                if (x == 0 || !members.Contains(cell - 1)) edges.Add(new RasterEdge(x, y + 1, x, y));
            }
            var loops = new List<List<PixelPoint>>(); var used = new bool[edges.Count];
            for (var seed = 0; seed < edges.Count; seed++)
            {
                if (used[seed]) continue;
                var points = new List<ExtractionPoint>(); var currentIndex = seed; var edge = edges[currentIndex]; var startX = edge.ax; var startY = edge.ay; var guard = edges.Count + 1;
                while (guard-- > 0)
                {
                    used[currentIndex] = true; points.Add(new ExtractionPoint(edge.ax, edge.ay));
                    if (edge.bx == startX && edge.by == startY) break;
                    // At diagonal pixel contacts several boundary edges can share a
                    // vertex. Taking the first edge in list order splices separate
                    // boundary loops together and creates a bow-tie. Follow the
                    // foreground on the right: right turn, straight, left, reverse.
                    var next = FindNextBoundaryEdge(edges, used, edge);
                    if (next < 0) { points.Clear(); break; }
                    currentIndex = next; edge = edges[currentIndex];
                }
                if (guard <= 0 || points.Count < 3) continue;
                RemoveCollinear(points);
                var loop = new List<PixelPoint>(points.Count);
                foreach (var p in points) loop.Add(new PixelPoint(ExtractionMath.Clamp(originX + p.x * step, 0, imageWidth - 1), ExtractionMath.Clamp(originY + p.y * step, 0, imageHeight - 1)));
                RemoveDuplicatePixelVertices(loop);
                if (loop.Count >= 3 && IsSimplePixelLoop(loop)) loops.Add(loop);
            }
            return loops;
        }

        private static int FindNextBoundaryEdge(List<RasterEdge> edges, bool[] used, RasterEdge incoming)
        {
            var dx = incoming.bx - incoming.ax; var dy = incoming.by - incoming.ay;
            var incomingDirection = dx > 0 ? 0 : dy > 0 ? 1 : dx < 0 ? 2 : 3;
            var preferred = new[] { (incomingDirection + 1) & 3, incomingDirection, (incomingDirection + 3) & 3, (incomingDirection + 2) & 3 };
            for (var p = 0; p < preferred.Length; p++)
                for (var i = 0; i < edges.Count; i++)
                {
                    if (used[i] || edges[i].ax != incoming.bx || edges[i].ay != incoming.by) continue;
                    var ex = edges[i].bx - edges[i].ax; var ey = edges[i].by - edges[i].ay;
                    var direction = ex > 0 ? 0 : ey > 0 ? 1 : ex < 0 ? 2 : 3;
                    if (direction == preferred[p]) return i;
                }
            return -1;
        }

        private static void RemoveDuplicatePixelVertices(List<PixelPoint> points)
        {
            for (var i = points.Count - 1; i >= 0 && points.Count > 1; i--)
            {
                var previous = points[(i + points.Count - 1) % points.Count]; var current = points[i];
                if (ExtractionMath.Abs(previous.x - current.x) < .001f && ExtractionMath.Abs(previous.y - current.y) < .001f) points.RemoveAt(i);
            }
        }

        private static bool IsSimplePixelLoop(List<PixelPoint> points)
        {
            var polygon = new List<LocalPoint>(points.Count);
            foreach (var point in points) polygon.Add(new LocalPoint(point.x, point.y));
            return PolygonTriangulator.IsSimple(polygon);
        }

        private static void RemoveCollinear(List<ExtractionPoint> points)
        {
            for (var i = points.Count - 1; i >= 0 && points.Count > 3; i--)
            {
                var a = points[(i + points.Count - 1) % points.Count]; var b = points[i]; var c = points[(i + 1) % points.Count];
                if ((b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x) == 0) points.RemoveAt(i);
            }
        }
        private static float PolygonArea(List<PixelPoint> points)
        { var area = 0f; for (var i = 0; i < points.Count; i++) { var a = points[i]; var b = points[(i + 1) % points.Count]; area += a.x * b.y - b.x * a.y; } return area * .5f; }
        private struct RasterEdge { public int ax, ay, bx, by; public RasterEdge(int x0, int y0, int x1, int y1) { ax = x0; ay = y0; bx = x1; by = y1; } }

        private static void AddLocalizedWalls(FloorPlanImageData image, FloorPlanCropRegion region, int minX, int minY, int maxX, int maxY, FloorPlanAnnotationData annotations, Color32? background, int estimatedThickness)
        {
            // Scale the minimum line length from observed stroke thickness instead
            // of 3.5% of the image: that old cutoff removed short partitions on tall plans.
            var wallThreshold = background.HasValue ? 155 : 225;
            var minimumRun = ExtractionMath.Max(14, estimatedThickness * 3); var id = 1;
            for (var y = minY; y <= maxY; y++) AddRuns(image, minX, maxX, y, true, minimumRun, annotations, ref id, background, wallThreshold);
            for (var x = minX; x <= maxX; x++) AddRuns(image, minY, maxY, x, false, minimumRun, annotations, ref id, background, wallThreshold);
            AddDiagonalRuns(image, minX, minY, maxX, maxY, true, minimumRun, annotations, ref id, background, wallThreshold);
            AddDiagonalRuns(image, minX, minY, maxX, maxY, false, minimumRun, annotations, ref id, background, wallThreshold);
            ConsolidateWalls(annotations, minimumRun * 1.5f, ExtractionMath.Max(3, estimatedThickness * 1.8f));
            CollapseParallelDuplicates(annotations);
            DetectGapDoors(annotations, minimumRun);
        }

        private static void ConsolidateWalls(FloorPlanAnnotationData annotations, float minimumLength, float lineTolerance)
        {
            var ordered = new List<WallAnnotationData>(annotations.walls);
            ordered.Sort((a, b) => Length(b).CompareTo(Length(a)));
            var consolidated = new List<WallAnnotationData>();
            foreach (var source in ordered)
            {
                var dx = source.end.x - source.start.x; var dy = source.end.y - source.start.y; var length = ExtractionMath.Sqrt(dx * dx + dy * dy); if (length < minimumLength) continue;
                var merged = false;
                for (var i = 0; i < consolidated.Count; i++) if (CanMerge(consolidated[i], source, lineTolerance)) { MergeInto(consolidated[i], source); merged = true; break; }
                if (!merged) { source.id = "localized_wall_" + (consolidated.Count + 1); consolidated.Add(source); }
            }
            annotations.walls = consolidated;
        }

        private static bool CanMerge(WallAnnotationData a, WallAnnotationData b, float tolerance)
        {
            var adx = a.end.x - a.start.x; var ady = a.end.y - a.start.y; var bdx = b.end.x - b.start.x; var bdy = b.end.y - b.start.y;
            var al = ExtractionMath.Sqrt(adx * adx + ady * ady); var bl = ExtractionMath.Sqrt(bdx * bdx + bdy * bdy); if (al < .001f || bl < .001f) return false;
            var ux = adx / al; var uy = ady / al; var bux = bdx / bl; var buy = bdy / bl;
            if (ux * bux + uy * buy < 0) { bux = -bux; buy = -buy; }
            if (ExtractionMath.Abs(ux * buy - uy * bux) > .06f) return false;
            var nx = -uy; var ny = ux;
            var da = (a.start.x + a.end.x) * .5f * nx + (a.start.y + a.end.y) * .5f * ny;
            var db = (b.start.x + b.end.x) * .5f * nx + (b.start.y + b.end.y) * .5f * ny;
            if (ExtractionMath.Abs(da - db) > tolerance) return false;
            Project(a, ux, uy, out var amin, out var amax); Project(b, ux, uy, out var bmin, out var bmax);
            return bmin <= amax + tolerance * 2 && amin <= bmax + tolerance * 2;
        }

        private static bool Overlap(float a0, float a1, float b0, float b1, float gap)
        { var amin = ExtractionMath.Min(a0, a1); var amax = ExtractionMath.Max(a0, a1); var bmin = ExtractionMath.Min(b0, b1); var bmax = ExtractionMath.Max(b0, b1); return bmin <= amax + gap && amin <= bmax + gap; }

        private static void MergeInto(WallAnnotationData target, WallAnnotationData source)
        {
            var dx = target.end.x - target.start.x; var dy = target.end.y - target.start.y; var length = ExtractionMath.Sqrt(dx * dx + dy * dy); if (length < .001f) return;
            var ux = dx / length; var uy = dy / length; var nx = -uy; var ny = ux;
            Project(target, ux, uy, out var t0, out var t1); Project(source, ux, uy, out var s0, out var s1);
            var d = (((target.start.x + target.end.x) * .5f + (source.start.x + source.end.x) * .5f) * nx + ((target.start.y + target.end.y) * .5f + (source.start.y + source.end.y) * .5f) * ny) * .5f;
            var lo = ExtractionMath.Min(t0, s0); var hi = ExtractionMath.Max(t1, s1);
            target.start = new PixelPoint(ux * lo + nx * d, uy * lo + ny * d); target.end = new PixelPoint(ux * hi + nx * d, uy * hi + ny * d);
        }

        private static void Project(WallAnnotationData wall, float ux, float uy, out float min, out float max)
        { var a = wall.start.x * ux + wall.start.y * uy; var b = wall.end.x * ux + wall.end.y * uy; min = ExtractionMath.Min(a, b); max = ExtractionMath.Max(a, b); }

        private static void CollapseParallelDuplicates(FloorPlanAnnotationData annotations)
        {
            var output = new List<WallAnnotationData>();
            foreach (var wall in annotations.walls)
            {
                var merged = false; for (var i = 0; i < output.Count; i++) if (ParallelDuplicate(output[i], wall)) { MergeInto(output[i], wall); merged = true; break; }
                if (!merged) output.Add(wall);
            }
            for (var i = 0; i < output.Count; i++) output[i].id = "localized_wall_" + (i + 1);
            annotations.walls = output;
        }

        private static bool ParallelDuplicate(WallAnnotationData a, WallAnnotationData b)
        {
            var adx = a.end.x - a.start.x; var ady = a.end.y - a.start.y; var bdx = b.end.x - b.start.x; var bdy = b.end.y - b.start.y;
            var ah = ExtractionMath.Abs(ady) <= 5; var bh = ExtractionMath.Abs(bdy) <= 5; var av = ExtractionMath.Abs(adx) <= 5; var bv = ExtractionMath.Abs(bdx) <= 5;
            if (ah && bh && ExtractionMath.Abs((a.start.y + a.end.y) * .5f - (b.start.y + b.end.y) * .5f) <= 9) return Overlap(a.start.x, a.end.x, b.start.x, b.end.x, -20);
            if (av && bv && ExtractionMath.Abs((a.start.x + a.end.x) * .5f - (b.start.x + b.end.x) * .5f) <= 9) return Overlap(a.start.y, a.end.y, b.start.y, b.end.y, -20);
            return false;
        }

        private static void DetectGapDoors(FloorPlanAnnotationData annotations, int minimumRun)
        {
            var output = new List<WallAnnotationData>(annotations.walls); var nextDoor = 1; var minGap = ExtractionMath.Max(10, minimumRun * .35f); var maxGap = ExtractionMath.Max(28, minimumRun * 1.35f);
            for (var i = 0; i < annotations.walls.Count; i++) for (var j = i + 1; j < annotations.walls.Count; j++)
            {
                var a = annotations.walls[i]; var b = annotations.walls[j]; if (!SameAxis(a, b)) continue;
                var horizontalCandidate = ExtractionMath.Abs(a.end.y - a.start.y) <= 5;
                if ((horizontalCandidate && ExtractionMath.Abs((a.start.y + a.end.y) * .5f - (b.start.y + b.end.y) * .5f) > 8) || (!horizontalCandidate && ExtractionMath.Abs((a.start.x + a.end.x) * .5f - (b.start.x + b.end.x) * .5f) > 8)) continue;
                if (Length(a) < minimumRun * 2.2f || Length(b) < minimumRun * 2.2f) continue;
                var horizontal = ExtractionMath.Abs(a.end.y - a.start.y) <= 5; var a0 = horizontal ? ExtractionMath.Min(a.start.x, a.end.x) : ExtractionMath.Min(a.start.y, a.end.y); var a1 = horizontal ? ExtractionMath.Max(a.start.x, a.end.x) : ExtractionMath.Max(a.start.y, a.end.y); var b0 = horizontal ? ExtractionMath.Min(b.start.x, b.end.x) : ExtractionMath.Min(b.start.y, b.end.y); var b1 = horizontal ? ExtractionMath.Max(b.start.x, b.end.x) : ExtractionMath.Max(b.start.y, b.end.y);
                if (b0 < a1) { var swap = a; a = b; b = swap; var t = a0; a0 = b0; b0 = t; t = a1; a1 = b1; b1 = t; }
                var gap = b0 - a1; if (gap < minGap || gap > maxGap || !HasRegionOnBothSides(annotations.rooms, a, b, horizontal, a1, b0)) continue;
                var merged = new WallAnnotationData { id = "localized_wall_door_" + nextDoor, start = horizontal ? new PixelPoint(a0, (a.start.y + b.start.y) * .5f) : new PixelPoint((a.start.x + b.start.x) * .5f, a0), end = horizontal ? new PixelPoint(b1, (a.start.y + b.start.y) * .5f) : new PixelPoint((a.start.x + b.start.x) * .5f, b1) };
                var startT = horizontal ? (a1 - a0) / (b1 - a0) : (a1 - a0) / (b1 - a0); var endT = horizontal ? (b0 - a0) / (b1 - a0) : (b0 - a0) / (b1 - a0); var door = new DoorAnnotationData { id = "localized_door_" + nextDoor++, wallId = merged.id, startT = startT, endT = endT, confidence = .55f, confidenceClass = "Medium" };
                var duplicate = false; foreach (var existing in annotations.doors) if (existing.wallId == merged.id || ExtractionMath.Abs(existing.startT - door.startT) < .08f) { duplicate = true; break; }
                if (!duplicate && annotations.doors.Count < 40) { output.Add(merged); annotations.doors.Add(door); }
            }
            annotations.walls = output;
        }

        private static bool SameAxis(WallAnnotationData a, WallAnnotationData b) { return (ExtractionMath.Abs(a.end.y - a.start.y) <= 5 && ExtractionMath.Abs(b.end.y - b.start.y) <= 5) || (ExtractionMath.Abs(a.end.x - a.start.x) <= 5 && ExtractionMath.Abs(b.end.x - b.start.x) <= 5); }
        private static float Length(WallAnnotationData wall) { return ExtractionMath.Distance(wall.start.x, wall.start.y, wall.end.x, wall.end.y); }
        private static bool HasRegionOnBothSides(List<RoomAnnotationData> rooms, WallAnnotationData a, WallAnnotationData b, bool horizontal, float first, float second) { var mid = (first + second) * .5f; var coordinate = horizontal ? (a.start.y + b.start.y) * .5f : (a.start.x + b.start.x) * .5f; var positive = false; var negative = false; foreach (var room in rooms) if (room != null && room.vertices != null && room.vertices.Count >= 3) { var minX = float.MaxValue; var maxX = float.MinValue; var minY = float.MaxValue; var maxY = float.MinValue; foreach (var p in room.vertices) { minX = ExtractionMath.Min(minX, p.x); maxX = ExtractionMath.Max(maxX, p.x); minY = ExtractionMath.Min(minY, p.y); maxY = ExtractionMath.Max(maxY, p.y); } if (horizontal && mid >= minX && mid <= maxX) { if (minY < coordinate - 8 && maxY > coordinate - 8) negative = true; if (minY < coordinate + 8 && maxY > coordinate + 8) positive = true; } if (!horizontal && mid >= minY && mid <= maxY) { if (minX < coordinate - 8 && maxX > coordinate - 8) negative = true; if (minX < coordinate + 8 && maxX > coordinate + 8) positive = true; } } return positive && negative; }

        private static void AddRuns(FloorPlanImageData image, int from, int to, int fixedCoordinate, bool horizontal, int minimumRun, FloorPlanAnnotationData annotations, ref int id, Color32? background, int threshold)
        { var start = -1; for (var p = from; p <= to + 1; p++) { var x = horizontal ? p : fixedCoordinate; var y = horizontal ? fixedCoordinate : p; var ink = p <= to && image.Get(x, y) < threshold && (!background.HasValue || !IsNearBackground(image.GetColor(x, y), background.Value)); if (ink && start < 0) start = p; if ((!ink || p == to + 1) && start >= 0) { if (p - start >= minimumRun) annotations.walls.Add(new WallAnnotationData { id = "localized_wall_" + id++, start = horizontal ? new PixelPoint(start, fixedCoordinate) : new PixelPoint(fixedCoordinate, start), end = horizontal ? new PixelPoint(p - 1, fixedCoordinate) : new PixelPoint(fixedCoordinate, p - 1) }); start = -1; } } }

        private static void AddDiagonalRuns(FloorPlanImageData image, int minX, int minY, int maxX, int maxY, bool positiveSlope, int minimumRun, FloorPlanAnnotationData annotations, ref int id, Color32? background, int threshold)
        {
            var starts = new List<ExtractionPoint>();
            if (positiveSlope) { for (var x = minX; x <= maxX; x++) starts.Add(new ExtractionPoint(x, minY)); for (var y = minY + 1; y <= maxY; y++) starts.Add(new ExtractionPoint(minX, y)); }
            else { for (var x = minX; x <= maxX; x++) starts.Add(new ExtractionPoint(x, minY)); for (var y = minY + 1; y <= maxY; y++) starts.Add(new ExtractionPoint(maxX, y)); }
            foreach (var startPoint in starts)
            {
                var x = startPoint.x; var y = startPoint.y; var runStart = -1; var runLength = 0;
                while (x >= minX && x <= maxX && y >= minY && y <= maxY)
                {
                    var ink = image.Get(x, y) < threshold && (!background.HasValue || !IsNearBackground(image.GetColor(x, y), background.Value));
                    if (ink) { if (runStart < 0) runStart = runLength; runLength++; }
                    else if (runStart >= 0) { AddDiagonalSegment(startPoint, runStart, runLength, positiveSlope, minimumRun, annotations, ref id); runStart = -1; runLength = 0; }
                    x++; y += positiveSlope ? 1 : -1;
                }
                if (runStart >= 0) AddDiagonalSegment(startPoint, runStart, runLength, positiveSlope, minimumRun, annotations, ref id);
            }
        }

        private static void AddDiagonalSegment(ExtractionPoint origin, int offset, int length, bool positiveSlope, int minimumRun, FloorPlanAnnotationData annotations, ref int id)
        {
            if (length < minimumRun) return;
            var yStep = positiveSlope ? 1 : -1;
            annotations.walls.Add(new WallAnnotationData { id = "localized_diagonal_wall_" + id++, start = new PixelPoint(origin.x + offset, origin.y + yStep * offset), end = new PixelPoint(origin.x + offset + length - 1, origin.y + yStep * (offset + length - 1)) });
        }

        private static void AppendGapDoor(FloorPlanImageData image, FloorPlanCropRegion region, WallAnnotationData wall, bool vertical, FloorPlanAnnotationData annotations)
        {
            var from = vertical ? (int)wall.start.y : (int)wall.start.x;
            var to = vertical ? (int)wall.end.y : (int)wall.end.x;
            var gapStart = -1; var bestStart = -1; var bestEnd = -1;
            for (var p = from + 2; p < to - 1; p++)
            {
                var x = vertical ? (int)wall.start.x : p; var y = vertical ? p : (int)wall.start.y;
                var isGap = image.Get(x, y) >= DarkThreshold;
                if (isGap && gapStart < 0) gapStart = p;
                if ((!isGap || p == to - 2) && gapStart >= 0) { var end = isGap ? p : p - 1; if (end - gapStart > bestEnd - bestStart) { bestStart = gapStart; bestEnd = end; } gapStart = -1; }
            }
            if (bestStart < 0 || bestEnd - bestStart < 8) return;
            var length = to - from; var startT = (bestStart - from) / (float)length; var endT = (bestEnd - from) / (float)length;
            if (startT <= 0.02f || endT >= 0.98f || endT - startT < 0.02f) return;
            annotations.doors.Add(new DoorAnnotationData { id = "extracted_door_" + annotations.doors.Count, wallId = wall.id, startT = startT, endT = endT });
        }

        private static List<int> FindRuns(FloorPlanImageData image, FloorPlanCropRegion region, bool vertical, Color32? background = null, float minimumCoverage = .9f)
        {
            var values = new List<int>(); var limit = vertical ? region.x + region.width : region.y + region.height;
            var start = vertical ? region.x : region.y;
            for (var p = start; p < limit; p++)
            {
                var dark = 0; var span = vertical ? region.height : region.width;
                for (var i = 0; i < span; i++) { var x = vertical ? p : region.x + i; var y = vertical ? region.y + i : p; if (image.Get(x, y) < DarkThreshold && (!background.HasValue || !IsNearBackground(image.GetColor(x, y), background.Value))) dark++; }
                // This shortcut creates full-span walls and rectangular rooms. It is
                // valid only for a near-complete drawing grid; a partial partition
                // must go through localized extraction so its openings remain open.
                if (dark >= span * minimumCoverage) values.Add(p);
            }
            var groups = new List<int>();
            for (var i = 0; i < values.Count; i++) { var first = values[i]; var last = first; while (i + 1 < values.Count && values[i + 1] <= last + 3) last = values[++i]; groups.Add((first + last) / 2); }
            return groups;
        }
    }
}
