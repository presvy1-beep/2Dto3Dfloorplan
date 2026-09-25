using System;
using ARNav.Core.Data;
using ARNav.Runtime.App;
using ARNav.Runtime.Import;
using UnityEngine;

namespace ARNav.Runtime.Extraction
{
    /// Coordinates extraction jobs. Every job owns an immutable pixel snapshot and its
    /// session/hash identity; obsolete coroutines can never commit another import's data.
    public sealed class FloorPlanAnalysisController : MonoBehaviour
    {
        private sealed class Job
        {
            public long revision;
            public string sessionId, sourceSha256;
            public long byteLength;
            public FloorPlanImageData image;
            public Action<FloorPlanExtractionResult> callback;
        }

        private IFloorPlanExtractor extractor = new SimpleFloorPlanExtractor();
        private long revision;
        public bool IsAnalyzing { get; private set; }

        public void DiscardTransientState()
        {
            revision++;
            IsAnalyzing = false;
        }

        public void Analyze(Texture2D texture, int width, int height, Action<FloorPlanExtractionResult> callback)
        { Analyze(texture, width, height, null, null, 0, callback); }

        public void Analyze(Texture2D texture, int width, int height, string sessionId, string sourceSha256, long byteLength, Action<FloorPlanExtractionResult> callback)
        {
            var jobRevision = ++revision;
            if (texture == null || width <= 0 || height <= 0)
            {
                callback?.Invoke(new FloorPlanExtractionResult { message = "No valid floor-plan image is loaded.", sessionId = sessionId, sourceSha256 = sourceSha256, sourceByteLength = byteLength });
                return;
            }
            var pixels = texture.GetPixels32();
            var luminance = new byte[pixels.Length];
            for (var i = 0; i < pixels.Length; i++) luminance[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(pixels[i].r * 0.2126f + pixels[i].g * 0.7152f + pixels[i].b * 0.0722f), 0, 255);
            var job = new Job { revision = jobRevision, sessionId = sessionId, sourceSha256 = sourceSha256, byteLength = byteLength, image = new FloorPlanImageData(width, height, luminance, pixels), callback = callback };
            IsAnalyzing = true;
            StartCoroutine(Run(job));
        }

        private System.Collections.IEnumerator Run(Job job)
        {
            yield return null;
            if (job.revision != revision) yield break;
            FloorPlanExtractionResult result;
            try { result = extractor.Extract(job.image, FloorPlanCropRegion.Full(job.image.width, job.image.height)); }
            catch (Exception exception) { Debug.LogException(exception); result = new FloorPlanExtractionResult { message = "Floor-plan analysis failed." }; }
            if (job.revision != revision) yield break;
            result.sessionId = job.sessionId;
            result.sourceSha256 = job.sourceSha256;
            result.sourceByteLength = job.byteLength;
            IsAnalyzing = false;
            if (result.diagnostics != null) Debug.Log("[ARNav][ExtractDiagnostics] image=" + job.image.width + "x" + job.image.height + " threshold=" + result.annotations.wallThreshold + " wallThicknessPx=" + result.diagnostics.estimatedWallThicknessPixels + " grid=" + result.diagnostics.gridWidth + "x" + result.diagnostics.gridHeight + " blocked=" + result.diagnostics.blockedCells + " exterior=" + result.diagnostics.exteriorCells + " interior=" + result.diagnostics.interiorCells + " regions=" + result.annotations.rooms.Count + " rawBlack=" + result.annotations.initialBlackPixelCount + " textComponents=" + result.annotations.textComponentCount + " textPixelsRemoved=" + result.annotations.textExclusionPixelCount + " wallPixels=" + result.annotations.architecturalWallPixelCount + " wallRuns=" + result.annotations.wallRuns.Count + " roomCoverage=" + result.diagnostics.roomMaskCoverage.ToString("P1") + " falseFill=" + result.diagnostics.roomMaskFalseFill.ToString("P1"));
            job.callback?.Invoke(result);
        }
    }
}
