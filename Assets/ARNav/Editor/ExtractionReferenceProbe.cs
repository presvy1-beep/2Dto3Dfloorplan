using System;
using System.IO;
using ARNav.Runtime.Extraction;
using UnityEditor;
using UnityEngine;

namespace ARNav.Editor
{
    public static class ExtractionReferenceProbe
    {
        public static void Run()
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                var input = ValueAfter(args, "-probeImage");
                var output = ValueAfter(args, "-probeOutput") ?? "/tmp/arnav-extraction-diagnostics";
                if (string.IsNullOrEmpty(input) || !File.Exists(input)) throw new FileNotFoundException("Pass an existing -probeImage path.", input);
                Directory.CreateDirectory(output);
                var bytes = File.ReadAllBytes(input); var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    if (!texture.LoadImage(bytes)) throw new InvalidDataException("Unity failed to decode probe image.");
                    var pixels = texture.GetPixels32(); var luma = new byte[pixels.Length];
                    for (var i = 0; i < pixels.Length; i++) luma[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(pixels[i].r * .2126f + pixels[i].g * .7152f + pixels[i].b * .0722f), 0, 255);
                    var extractor = new SimpleFloorPlanExtractor { CaptureDiagnosticMasks = true };
                    var result = extractor.Extract(new FloorPlanImageData(texture.width, texture.height, luma, pixels), FloorPlanCropRegion.Full(texture.width, texture.height));
                    var annotationJson = JsonUtility.ToJson(result.annotations, true);
                    File.WriteAllText(Path.Combine(output, "annotations.json"), annotationJson);
                    var jsonReloadedAnnotations = JsonUtility.FromJson<ARNav.Core.Data.FloorPlanAnnotationData>(annotationJson);
                    WriteColor(Path.Combine(output, "01-original.png"), texture.width, texture.height, pixels);
                    WriteGray(Path.Combine(output, "02-grayscale.png"), texture.width, texture.height, luma);
                    WriteGray(Path.Combine(output, "02b-binary-black.png"), texture.width, texture.height, result.diagnostics.binaryBlackMask);
                    WriteGray(Path.Combine(output, "02c-text-exclusion.png"), texture.width, texture.height, result.diagnostics.textExclusionMask);
                    WriteGray(Path.Combine(output, "02d-architectural-wall-mask.png"), texture.width, texture.height, result.diagnostics.architecturalWallMask);
                    WriteGray(Path.Combine(output, "03-threshold.png"), texture.width, texture.height, result.diagnostics.rawThresholdMask);
                    WriteGray(Path.Combine(output, "04-wall-cleaned.png"), texture.width, texture.height, result.diagnostics.cleanedWallMask);
                    WriteGray(Path.Combine(output, "05-exterior.png"), texture.width, texture.height, result.diagnostics.exteriorMask);
                    WriteGray(Path.Combine(output, "06-interior.png"), texture.width, texture.height, result.diagnostics.interiorMask);
                    WriteGray(Path.Combine(output, "07-region-labels.png"), texture.width, texture.height, result.diagnostics.regionLabels);
                    WriteGray(Path.Combine(output, "08-contours.png"), texture.width, texture.height, result.diagnostics.contourMask);
                    WriteGray(Path.Combine(output, "09-openings.png"), texture.width, texture.height, result.diagnostics.openingMask);
                    WriteReconstruction(Path.Combine(output, "10-json-reconstruction.png"), texture.width, texture.height, jsonReloadedAnnotations);
                    WriteWallMask(Path.Combine(output, "11-json-roundtrip-wallmask.png"), texture.width, texture.height, jsonReloadedAnnotations);
                    Debug.Log("[ARNav][ExtractionProbe] " + input + " " + texture.width + "x" + texture.height + " success=" + result.succeeded + " rooms=" + result.annotations.rooms.Count + " walls=" + result.annotations.walls.Count + " openings=" + result.annotations.doors.Count + " threshold=" + result.annotations.wallThreshold + " rawBlack=" + result.annotations.initialBlackPixelCount + " textComponents=" + result.annotations.textComponentCount + " textPixelsRemoved=" + result.annotations.textExclusionPixelCount + " wallPixels=" + result.annotations.architecturalWallPixelCount + " wallRuns=" + result.annotations.wallRuns.Count + " runRoundTripIoU=" + WallRunRoundTripIoU(result.diagnostics.architecturalWallMask, jsonReloadedAnnotations, texture.width, texture.height).ToString("P2") + " wallThickness=" + result.diagnostics.estimatedWallThicknessPixels + " grid=" + result.diagnostics.gridWidth + "x" + result.diagnostics.gridHeight + " coverage=" + result.diagnostics.roomMaskCoverage.ToString("P1") + " falseFill=" + result.diagnostics.roomMaskFalseFill.ToString("P1") + " wallPrecision=" + result.diagnostics.wallCenterlinePrecision.ToString("P1") + " wallRecall=" + result.diagnostics.wallMaskRecall.ToString("P1") + " wallIoU=" + result.diagnostics.wallMaskIoU.ToString("P1") + " output=" + output);
                }
                finally { UnityEngine.Object.DestroyImmediate(texture); }
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        private static string ValueAfter(string[] args, string flag) { for (var i = 0; i + 1 < args.Length; i++) if (args[i] == flag) return args[i + 1]; return null; }
        private static void WriteGray(string path, int width, int height, byte[] source)
        {
            if (source == null || source.Length != width * height) source = new byte[width * height];
            var colors = new Color32[source.Length]; for (var i = 0; i < colors.Length; i++) colors[i] = new Color32(source[i], source[i], source[i], 255);
            WriteColor(path, width, height, colors);
        }
        private static void WriteColor(string path, int width, int height, Color32[] pixels)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false); try { texture.SetPixels32(pixels); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); } finally { UnityEngine.Object.DestroyImmediate(texture); }
        }
        private static void WriteWallMask(string path, int width, int height, ARNav.Core.Data.FloorPlanAnnotationData annotations)
        {
            var bytes = new byte[width * height];
            if (annotations.wallRuns != null) foreach (var run in annotations.wallRuns)
                for (var y = Mathf.Clamp(Mathf.FloorToInt(run.minY), 0, height); y < Mathf.Clamp(Mathf.CeilToInt(run.maxY), 0, height); y++)
                    for (var x = Mathf.Clamp(Mathf.FloorToInt(run.minX), 0, width); x < Mathf.Clamp(Mathf.CeilToInt(run.maxX), 0, width); x++) bytes[y * width + x] = 255;
            WriteGray(path, width, height, bytes);
        }
        private static float WallRunRoundTripIoU(byte[] expected, ARNav.Core.Data.FloorPlanAnnotationData annotations, int width, int height)
        {
            if (expected == null || expected.Length != width * height) return 0;
            var actual = new byte[expected.Length];
            if (annotations.wallRuns != null) foreach (var run in annotations.wallRuns)
                for (var y = Mathf.Clamp(Mathf.FloorToInt(run.minY), 0, height); y < Mathf.Clamp(Mathf.CeilToInt(run.maxY), 0, height); y++)
                    for (var x = Mathf.Clamp(Mathf.FloorToInt(run.minX), 0, width); x < Mathf.Clamp(Mathf.CeilToInt(run.maxX), 0, width); x++) actual[y * width + x] = 255;
            var intersection = 0; var union = 0;
            for (var i = 0; i < expected.Length; i++) { if (expected[i] != 0 && actual[i] != 0) intersection++; if (expected[i] != 0 || actual[i] != 0) union++; }
            return union == 0 ? 1 : intersection / (float)union;
        }
        private static void WriteReconstruction(string path, int width, int height, ARNav.Core.Data.FloorPlanAnnotationData annotations)
        {
            var colors = new Color32[width * height]; for (var i = 0; i < colors.Length; i++) colors[i] = new Color32(245, 245, 245, 255);
            foreach (var room in annotations.rooms) if (room.walkable && room.rasterRuns != null) foreach (var run in room.rasterRuns)
                for (var y = Mathf.Clamp(Mathf.FloorToInt(run.minY), 0, height); y < Mathf.Clamp(Mathf.CeilToInt(run.maxY), 0, height); y++) for (var x = Mathf.Clamp(Mathf.FloorToInt(run.minX), 0, width); x < Mathf.Clamp(Mathf.CeilToInt(run.maxX), 0, width); x++) colors[y * width + x] = new Color32(220, 225, 230, 255);
            foreach (var room in annotations.rooms) if (room.walkable && (room.rasterRuns == null || room.rasterRuns.Count == 0)) FillPolygon(colors, width, height, room.vertices, new Color32(220, 225, 230, 255));
            foreach (var wall in annotations.walls) DrawLine(colors, width, height, wall.start.x, wall.start.y, wall.end.x, wall.end.y, new Color32(20, 25, 30, 255));
            foreach (var door in annotations.doors) { var wall = annotations.walls.Find(w => w.id == door.wallId); if (wall == null) continue; DrawLine(colors, width, height, Mathf.Lerp(wall.start.x, wall.end.x, door.startT), Mathf.Lerp(wall.start.y, wall.end.y, door.startT), Mathf.Lerp(wall.start.x, wall.end.x, door.endT), Mathf.Lerp(wall.start.y, wall.end.y, door.endT), new Color32(30, 210, 70, 255)); }
            foreach (var room in annotations.rooms) DrawPolygon(colors, width, height, room.vertices);
            WriteColor(path, width, height, colors);
        }
        private static void DrawPolygon(Color32[] pixels, int width, int height, System.Collections.Generic.List<ARNav.Core.Geometry.PixelPoint> points)
        { if (points == null || points.Count < 2) return; for (var i = 0; i < points.Count; i++) DrawLine(pixels, width, height, points[i].x, points[i].y, points[(i + 1) % points.Count].x, points[(i + 1) % points.Count].y, new Color32(0, 190, 220, 255)); }
        private static void FillPolygon(Color32[] pixels, int width, int height, System.Collections.Generic.List<ARNav.Core.Geometry.PixelPoint> points, Color32 color)
        {
            if (points == null || points.Count < 3) return;
            var minY = height - 1; var maxY = 0; foreach (var p in points) { minY = Mathf.Min(minY, Mathf.FloorToInt(p.y)); maxY = Mathf.Max(maxY, Mathf.CeilToInt(p.y)); }
            var intersections = new System.Collections.Generic.List<float>();
            for (var y = Mathf.Clamp(minY, 0, height - 1); y <= Mathf.Clamp(maxY, 0, height - 1); y++)
            {
                intersections.Clear(); var scanY = y + .5f;
                for (var i = 0; i < points.Count; i++) { var a = points[i]; var b = points[(i + 1) % points.Count]; if ((a.y <= scanY && b.y > scanY) || (b.y <= scanY && a.y > scanY)) intersections.Add(a.x + (scanY - a.y) * (b.x - a.x) / (b.y - a.y)); }
                intersections.Sort(); for (var k = 0; k + 1 < intersections.Count; k += 2) for (var x = Mathf.Clamp(Mathf.CeilToInt(intersections[k]), 0, width); x < Mathf.Clamp(Mathf.FloorToInt(intersections[k + 1]), 0, width); x++) pixels[y * width + x] = color;
            }
        }
        private static void DrawLine(Color32[] pixels, int width, int height, float ax, float ay, float bx, float by, Color32 color)
        { var steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(new Vector2(ax, ay), new Vector2(bx, by)))); for (var i = 0; i <= steps; i++) { var x = Mathf.RoundToInt(Mathf.Lerp(ax, bx, i / (float)steps)); var y = Mathf.RoundToInt(Mathf.Lerp(ay, by, i / (float)steps)); if (x >= 0 && y >= 0 && x < width && y < height) pixels[y * width + x] = color; } }
    }
}
