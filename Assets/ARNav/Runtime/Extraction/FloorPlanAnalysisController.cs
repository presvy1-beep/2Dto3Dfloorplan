using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ARNav.Core.Data;
using UnityEngine;

namespace ARNav.Runtime.Extraction
{
    /// <summary>Unity texture access and result delivery stay on the main thread; extraction uses an owned pixel snapshot on a worker.</summary>
    public sealed class FloorPlanAnalysisController : MonoBehaviour
    {
        private sealed class Job
        {
            public long revision;
            public string sessionId, sourceSha256;
            public long byteLength;
            public Action<FloorPlanExtractionResult> callback;
            public CancellationTokenSource cancellation;
            public ConcurrentQueue<FloorPlanAnalysisStage> progress = new ConcurrentQueue<FloorPlanAnalysisStage>();
            public Task<FloorPlanExtractionResult> task;
            public FloorPlanAnalysisStage lastStage;
        }
        private static readonly SemaphoreSlim ExtractionGate = new SemaphoreSlim(1, 1);
        private readonly List<Job> retiredJobs = new List<Job>();
        private IFloorPlanExtractor extractor = new DirectBlackMaskExtractor();
        private Job active;
        private long revision;
        private bool destroyed;
        public bool IsAnalyzing { get; private set; }
        public event Action<FloorPlanAnalysisStage> ProgressChanged;
        public event Action<bool> AnalysisStateChanged;

        public void DiscardTransientState() { CancelAnalysis(false); }

        public void Analyze(Texture2D texture, int width, int height, Action<FloorPlanExtractionResult> callback)
        { Analyze(texture, width, height, null, null, 0, callback); }

        public void Analyze(Texture2D texture, int width, int height, string sessionId, string sourceSha256, long byteLength, Action<FloorPlanExtractionResult> callback)
        {
            CancelAnalysis(false);
            var jobRevision = ++revision;
            if (texture == null || width <= 0 || height <= 0 || (long)width * height > 8L * 1024 * 1024)
            {
                callback?.Invoke(new FloorPlanExtractionResult { message = "No valid floor-plan image is loaded, or it exceeds the supported 8-megapixel analysis limit.", sessionId = sessionId, sourceSha256 = sourceSha256, sourceByteLength = byteLength });
                return;
            }
            Color32[] pixels;
            try
            {
                if (texture.width != width || texture.height != height) throw new ArgumentException("Image dimensions changed before analysis.");
                pixels = texture.GetPixels32(); // Unity API, intentionally main thread only.
            }
            catch (Exception exception)
            {
                callback?.Invoke(new FloorPlanExtractionResult { message = "Could not read the floor-plan image: " + exception.Message, sessionId = sessionId, sourceSha256 = sourceSha256, sourceByteLength = byteLength });
                return;
            }

            var job = new Job { revision = jobRevision, sessionId = sessionId, sourceSha256 = sourceSha256, byteLength = byteLength, callback = callback, cancellation = new CancellationTokenSource(), lastStage = FloorPlanAnalysisStage.PreparingImage };
            active = job; IsAnalyzing = true; ProgressChanged?.Invoke(job.lastStage); AnalysisStateChanged?.Invoke(true);
            var token = job.cancellation.Token;
            var selectedExtractor = extractor;
            var progress = job.progress;
            job.task = Task.Run(async () =>
            {
                await ExtractionGate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    token.ThrowIfCancellationRequested();
                    var image = new FloorPlanImageData(width, height, pixels, token, stage => progress.Enqueue(stage));
                    var result = selectedExtractor.Extract(image, FloorPlanCropRegion.Full(width, height));
                    token.ThrowIfCancellationRequested();
                    return result;
                }
                finally { ExtractionGate.Release(); }
            }, token);
        }

        public void CancelAnalysis() { CancelAnalysis(true); }
        private void CancelAnalysis(bool notify)
        {
            revision++;
            if (active == null) { IsAnalyzing = false; return; }
            var previous = active; active = null; IsAnalyzing = false;
            previous.cancellation.Cancel();
            if (previous.task != null && !previous.task.IsCompleted) retiredJobs.Add(previous);
            else previous.cancellation.Dispose();
            AnalysisStateChanged?.Invoke(false);
            if (notify) previous.callback?.Invoke(new FloorPlanExtractionResult { message = "Floor-plan analysis was cancelled.", sessionId = previous.sessionId, sourceSha256 = previous.sourceSha256, sourceByteLength = previous.byteLength });
        }

        private void Update()
        {
            for (var i = retiredJobs.Count - 1; i >= 0; i--) if (retiredJobs[i].task == null || retiredJobs[i].task.IsCompleted) { retiredJobs[i].cancellation.Dispose(); retiredJobs.RemoveAt(i); }
            var job = active; if (job == null) return;
            while (job.progress.TryDequeue(out var stage)) { job.lastStage = stage; ProgressChanged?.Invoke(stage); }
            if (!job.task.IsCompleted) return;
            active = null; IsAnalyzing = false; AnalysisStateChanged?.Invoke(false);
            FloorPlanExtractionResult result;
            try { result = job.task.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { result = new FloorPlanExtractionResult { message = "Floor-plan analysis was cancelled." }; }
            catch (Exception exception) { Debug.LogException(exception); result = new FloorPlanExtractionResult { message = "Floor-plan analysis failed. Try importing the image again." }; }
            finally { job.cancellation.Dispose(); }
            if (job.revision != revision || destroyed) return;
            while (job.progress.TryDequeue(out var stage)) { job.lastStage = stage; ProgressChanged?.Invoke(stage); }
            result.sessionId = job.sessionId; result.sourceSha256 = job.sourceSha256; result.sourceByteLength = job.byteLength;
            if (result.diagnostics != null) Debug.Log("[ARNav][ExtractDiagnostics] threshold=" + result.annotations.wallThreshold + " regions=" + result.annotations.rooms.Count + " walls=" + result.annotations.walls.Count + " doors=" + result.annotations.doors.Count);
            job.callback?.Invoke(result);
            ProgressChanged?.Invoke(FloorPlanAnalysisStage.Complete);
        }

        private void OnApplicationPause(bool paused) { if (paused) CancelAnalysis(false); }
        private void OnDestroy() { destroyed = true; CancelAnalysis(false); ProgressChanged = null; AnalysisStateChanged = null; }
    }
}
