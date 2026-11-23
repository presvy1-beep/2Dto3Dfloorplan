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
            for (var i = 0; i < ownedPixels.Length; i++) { if ((i & 4095) == 0) token.ThrowIfCancellationRequested(); var p = ownedPixels[i]; luminance[i] = (byte)Math.Max(0, Math.Min(255, Mathf.RoundToInt(p.r * 0.2126f + p.g * 0.7152f + p.b * 0.0722f))); }
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
}
