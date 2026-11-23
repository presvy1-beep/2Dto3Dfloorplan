using System;
using System.Collections.Generic;
using ARNav.Core.Data;
using ARNav.Core.Geometry;
using UnityEngine;

namespace ARNav.Runtime.Extraction
{
    /// <summary>Converts the offline OpenCV result into the existing versioned map geometry.</summary>
    public sealed class OpenCvFloorPlanExtractor : IFloorPlanExtractor
    {
        private const int Magic = 0x4f435650;
        public FloorPlanExtractionResult Extract(FloorPlanImageData image, FloorPlanCropRegion region)
        {
            if (image == null || !region.IsValid(image.width, image.height))
                return new FloorPlanExtractionResult { message = "Invalid floorplan image." };
            image.ThrowIfCancellationRequested();
            image.ReportStage(FloorPlanAnalysisStage.DetectingWalls);
            var blackness = PrepareTopLeftBlackness(image);
            image.ThrowIfCancellationRequested();
#if UNITY_ANDROID && !UNITY_EDITOR
            byte[] nativeResult;
            AndroidJNI.AttachCurrentThread();
            try
            {
                using (var processor = new AndroidJavaClass("com.arnav.filepicker.OpenCvFloorplanProcessor"))
                    nativeResult = processor.CallStatic<byte[]>("process", blackness, image.width, image.height);
            }
            catch (Exception exception)
            {
                return new FloorPlanExtractionResult { message = "OpenCV floorplan analysis failed: " + exception.Message };
            }
            finally { AndroidJNI.DetachCurrentThread(); }
            image.ThrowIfCancellationRequested();
            image.ReportStage(FloorPlanAnalysisStage.DetectingRoomRegions);
            return ParseNativeResult(nativeResult, image.width, image.height);
#else
            return new FloorPlanExtractionResult { analyzedRegion = region, message = "OpenCV native floorplan processing requires an Android build." };
#endif
        }

        /// <summary>Unity texture rows start at the bottom; stored annotation pixels start at the top.</summary>
        public static byte[] PrepareTopLeftBlackness(FloorPlanImageData image)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            var blackness = new byte[image.luminance.Length];
            for (var y = 0; y < image.height; y++)
            {
                image.ThrowIfCancellationRequested();
                var sourceRow = (image.height - 1 - y) * image.width;
                var destinationRow = y * image.width;
                if (image.colorPixels == null) Buffer.BlockCopy(image.luminance, sourceRow, blackness, destinationRow, image.width);
                else for (var x = 0; x < image.width; x++)
                {
                    var pixel = image.colorPixels[sourceRow + x];
                    blackness[destinationRow + x] = Math.Max(pixel.r, Math.Max(pixel.g, pixel.b));
                }
            }
            return blackness;
        }


        public static FloorPlanExtractionResult ParseNativeResult(byte[] data, int width, int height)
        {
            var result = new FloorPlanExtractionResult();
            if (data == null || width <= 0 || height <= 0 || data.Length != 32L + 2L * width * height)
            { result.message = "OpenCV returned an invalid mask buffer."; return result; }
            if (BitConverter.ToInt32(data, 0) != Magic || BitConverter.ToInt32(data, 4) != 1)
            { result.message = "OpenCV returned an unsupported result version."; return result; }
            var crop = new FloorPlanCropRegion(BitConverter.ToInt32(data, 8), BitConverter.ToInt32(data, 12), BitConverter.ToInt32(data, 16), BitConverter.ToInt32(data, 20));
            if (!crop.IsValid(width, height)) { result.message = "OpenCV returned an invalid drawing boundary."; return result; }
            result.analyzedRegion = crop;
            result.annotations.wallRuns = BuildRuns(data, 32, width, height);
            // OpenCV's cleaned wall occupancy is authoritative. Legacy vector
            // paths remain readable in older JSON, but are not generated here.
            result.annotations.wallThreshold = 150;
            result.annotations.wallPathDiagnostics.estimatedWidthPixels = Math.Max(1, BitConverter.ToInt32(data, 24));
            var floorRuns = BuildRuns(data, 32 + width * height, width, height);
            if (floorRuns.Count > 0)
            {
                var minX = (float)width; var minY = (float)height; var maxX = 0f; var maxY = 0f;
                foreach (var run in floorRuns)
                {
                    minX = Math.Min(minX, run.minX); minY = Math.Min(minY, run.minY);
                    maxX = Math.Max(maxX, run.maxX); maxY = Math.Max(maxY, run.maxY);
                }
                var room = new RoomAnnotationData
                {
                    id = "opencv_floor_1", displayName = "Detected Interior", category = "Detected",
                    regionType = "Room", walkable = true, destinationSelectable = false,
                    detectionConfidence = .55f, preferRasterGeometry = true, rasterRuns = floorRuns
                };
                room.vertices.Add(new PixelPoint(minX, minY)); room.vertices.Add(new PixelPoint(maxX, minY));
                room.vertices.Add(new PixelPoint(maxX, maxY)); room.vertices.Add(new PixelPoint(minX, maxY));
                result.annotations.rooms.Add(room);
            }
            long wallPixels = 0;
            foreach (var run in result.annotations.wallRuns)
                wallPixels += (long)(run.maxX - run.minX) * (long)(run.maxY - run.minY);
            result.annotations.architecturalWallPixelCount = wallPixels;
            result.succeeded = result.annotations.wallRuns.Count > 0 && result.annotations.rooms.Count > 0;
            result.confidence = result.succeeded ? .55f : 0f;
            result.message = result.succeeded
                ? "OpenCV detected structural wall and floor candidates. Estimated scale — dimensions are approximate."
                : "OpenCV could not identify both structural walls and a usable floor.";
            return result;
        }

        private static List<PixelRasterRun> BuildRuns(byte[] data, int offset, int width, int height)
        {
            var output = new List<PixelRasterRun>();
            var previous = new Dictionary<long, PixelRasterRun>();
            for (var y = 0; y < height; y++)
            {
                var current = new Dictionary<long, PixelRasterRun>();
                for (var x = 0; x < width;)
                {
                    while (x < width && data[offset + y * width + x] == 0) x++;
                    if (x >= width) break;
                    var start = x;
                    while (x < width && data[offset + y * width + x] != 0) x++;
                    var key = ((long)start << 32) | (uint)x;
                    if (previous.TryGetValue(key, out var prior) && Math.Abs(prior.maxY - y) < .01f)
                        prior.maxY = y + 1;
                    else { prior = new PixelRasterRun { minX = start, maxX = x, minY = y, maxY = y + 1 }; output.Add(prior); }
                    current[key] = prior;
                }
                previous = current;
            }
            return output;
        }
    }
}
