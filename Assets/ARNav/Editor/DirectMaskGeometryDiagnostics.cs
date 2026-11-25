using System;
using System.IO;
using ARNav.Core.Data;
using ARNav.Core.Geometry;
using ARNav.Core.Navigation;
using ARNav.Runtime.Extraction;
using ARNav.Runtime.Generation;
using UnityEditor;
using UnityEngine;

namespace ARNav.Editor
{
    /// <summary>Renders imported JPEGs through the production direct-mask extractor and building generator.</summary>
    public static class DirectMaskGeometryDiagnostics
    {
        public static void RenderRealSamples()
        {
            var args = Environment.GetCommandLineArgs();
            var output = ValueAfter(args, "-directMaskOutput") ?? Path.Combine(Directory.GetCurrentDirectory(), "Logs", "direct-mask-geometry");
            var f1 = ValueAfter(args, "-directMaskF1") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "f1.jpeg");
            var fp2 = ValueAfter(args, "-directMaskFp2") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "fp2.jpeg");
            var reconstructOverlay = Array.IndexOf(args, "-directMaskReconstructOverlay") >= 0;
            Directory.CreateDirectory(output);
            try
            {
                RenderOne("f1", f1, output, reconstructOverlay);
                RenderOne("fp2", fp2, output, reconstructOverlay);
                EditorApplication.Exit(0);
            }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }

        private static void RenderOne(string name, string source, string output, bool reconstructOverlay)
        {
            if (!File.Exists(source)) throw new FileNotFoundException(source);
            var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!image.LoadImage(File.ReadAllBytes(source))) throw new InvalidDataException("Cannot decode " + source);
            var colors = image.GetPixels32(); var luminance = new byte[colors.Length];
            if (reconstructOverlay)
            {
                // Old diagnostics marked every black candidate with this exact red.
                // This recovers only the thresholded fixture, not the original JPEG.
                for (var i = 0; i < colors.Length; i++) if (colors[i].r == 255 && colors[i].g == 30 && colors[i].b == 30)
                    colors[i] = new Color32(0, 0, 0, 255);
                image.SetPixels32(colors); image.Apply();
                WriteImage(Path.Combine(output, name + "-derived-input.png"), image.width, image.height, colors);
            }
            var result = new DirectBlackMaskExtractor().Extract(new FloorPlanImageData(image.width, image.height, luminance, colors), FloorPlanCropRegion.Full(image.width, image.height));
            if (!result.succeeded) throw new InvalidOperationException(result.message);
            var project = MapProjectData.Create("direct-" + name, name);
            project.sourceImage.fileName = name + ".jpeg";
            project.sourceImage.originalWidthPixels = image.width; project.sourceImage.originalHeightPixels = image.height;
            project.scaleCalibration.isValid = true; project.scaleCalibration.isEstimated = true; project.scaleCalibration.metresPerPixel = .03f;
            var floor = new FloorData { id = "floor-1", name = "Ground", sourceAnnotations = result.annotations };
            project.floors.Add(floor);
            if (!PixelToLocalGeometryConverter.TryRegenerate(floor, project.scaleCalibration, image.width, image.height, new MapGenerationDefaults(), out var validation))
                throw new InvalidDataException(string.Join(" ", validation.errors.ToArray()));
            var owner = new GameObject("Diagnostic " + name); var generator = owner.AddComponent<ProceduralBuildingGenerator>();
            try
            {
                if (!generator.Generate(project, out var error)) throw new InvalidOperationException(error);
                var renderers = generator.GeneratedRoot.GetComponentsInChildren<MeshRenderer>(true);
                if (!reconstructOverlay) File.Copy(source, Path.Combine(output, name + "-input" + Path.GetExtension(source)), true);
                WriteMask(image, result, Path.Combine(output, name));
                Capture(renderers, Path.Combine(output, name + "-walls-only.png"), true, false, false);
                Capture(renderers, Path.Combine(output, name + "-floors-only.png"), false, true, false);
                Capture(renderers, Path.Combine(output, name + "-top-down.png"), true, false, true);
                Capture(renderers, Path.Combine(output, name + "-combined.png"), false, false, false);
                var walls = 0; var triangles = 0;
                foreach (var renderer in renderers) if (IsWall(renderer)) { walls++; triangles += renderer.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3; }
                Debug.Log("[DirectMaskDiagnostic] " + name + " estimatedWidthPixels=" + result.diagnostics.estimatedWallThicknessPixels +
                          " wallPixels=" + result.annotations.architecturalWallPixelCount +
                          " wallRuns=" + result.annotations.wallRuns.Count + " regions=" + result.annotations.rooms.Count +
                          " wallMeshes=" + walls + " wallTriangles=" + triangles + " bounds=" + generator.GeneratedBounds);
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); UnityEngine.Object.DestroyImmediate(image); }
        }

        private static void WriteMask(Texture2D source, FloorPlanExtractionResult result, string prefix)
        {
            var width = source.width; var height = source.height;
            var d = result.diagnostics; var original = source.GetPixels32();
            WriteLayer(prefix + "-candidate-mask.png", width, height, d.binaryBlackMask, new Color32(20, 20, 20, 255));
            WriteLayer(prefix + "-structural-mask.png", width, height, d.architecturalWallMask, new Color32(20, 20, 20, 255));
            WriteLayer(prefix + "-rejected-mask.png", width, height, d.rejectedObjectMask, new Color32(230, 55, 55, 255));
            WriteLayer(prefix + "-exterior-void-mask.png", width, height, Combine(d.exteriorMask, d.voidMask), new Color32(85, 95, 105, 255));
            WriteLayer(prefix + "-floor-mask.png", width, height, d.floorMask, new Color32(60, 175, 90, 255));
            var regions = new Color32[original.Length]; var overlay = new Color32[original.Length];
            var palette = new[] { new Color32(60, 175, 90, 255), new Color32(45, 135, 215, 255),
                new Color32(245, 180, 45, 255), new Color32(170, 95, 205, 255) };
            for (var i = 0; i < original.Length; i++)
            {
                var p = (height - 1 - i / width) * width + i % width;
                var color = new Color32(255, 255, 255, 255);
                if (d.exteriorMask[p] != 0) color = new Color32(215, 220, 225, 255);
                if (d.voidMask[p] != 0) color = new Color32(75, 80, 90, 255);
                if (d.regionLabels[p] != 0) color = palette[d.regionLabels[p] % palette.Length];
                if (d.openingMask[p] != 0) color = new Color32(255, 215, 45, 255);
                if (d.architecturalWallMask[p] != 0) color = new Color32(25, 35, 45, 255);
                regions[i] = color;
                overlay[i] = d.architecturalWallMask[p] != 0 ? new Color32(20, 40, 220, 255) :
                    d.rejectedObjectMask[p] != 0 ? new Color32(230, 55, 55, 255) :
                    d.openingMask[p] != 0 ? new Color32(255, 215, 45, 255) :
                    d.voidMask[p] != 0 ? new Color32(75, 80, 90, 255) :
                    d.floorMask[p] != 0 ? new Color32(90, 205, 120, 255) : original[i];
            }
            WriteImage(prefix + "-regions.png", width, height, regions);
            WriteImage(prefix + "-footprint-overlay.png", width, height, overlay);
        }
        private static byte[] Combine(byte[] a, byte[] b)
        { var combined = new byte[a.Length]; for (var i = 0; i < a.Length; i++) if (a[i] != 0 || b[i] != 0) combined[i] = 1; return combined; }
        private static void WriteLayer(string path, int width, int height, byte[] layer, Color32 marked)
        {
            var pixels = new Color32[layer.Length];
            for (var i = 0; i < pixels.Length; i++)
            { var p = (height - 1 - i / width) * width + i % width; pixels[i] = layer[p] != 0 ? marked : new Color32(255, 255, 255, 255); }
            WriteImage(path, width, height, pixels);
        }
        private static void WriteImage(string path, int width, int height, Color32[] pixels)
        { var texture = new Texture2D(width, height, TextureFormat.RGBA32, false); texture.SetPixels32(pixels); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture); }
        private static bool IsWall(MeshRenderer renderer)
        { for (var parent = renderer.transform; parent != null; parent = parent.parent) if (parent.name == "Walls") return true; return false; }

        private static string ValueAfter(string[] args, string key)
        { for (var i = 0; i + 1 < args.Length; i++) if (args[i] == key) return args[i + 1]; return null; }

        private static void Capture(MeshRenderer[] renderers, string path, bool wallsOnly, bool floorsOnly, bool topDown)
        {
            var enabled = new bool[renderers.Length]; var set = false; var bounds = new Bounds();
            for (var i = 0; i < renderers.Length; i++)
            {
                enabled[i] = renderers[i].enabled; renderers[i].enabled = (!wallsOnly && !floorsOnly) || (wallsOnly && IsWall(renderers[i])) || (floorsOnly && !IsWall(renderers[i]));
                if (!renderers[i].enabled) continue;
                if (!set) { bounds = renderers[i].bounds; set = true; } else bounds.Encapsulate(renderers[i].bounds);
            }
            var cameraObject = new GameObject("DirectMaskCamera"); var camera = cameraObject.AddComponent<Camera>();
            var target = new RenderTexture(960, 720, 24, RenderTextureFormat.ARGB32); var oldTarget = RenderTexture.active;
            try
            {
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.09f, .105f, .13f, 1);
                camera.orthographic = true; camera.aspect = 4f / 3f; camera.nearClipPlane = .01f; camera.farClipPlane = 1000;
                var extent = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z, .1f); camera.orthographicSize = extent * .78f;
                camera.transform.position = topDown ? bounds.center + Vector3.up * extent * 2f : bounds.center + new Vector3(extent * 1.25f, extent * 1.15f, -extent * 1.25f);
                camera.transform.rotation = topDown ? CameraFramingUtility.SourceAlignedTopRotation() : Quaternion.LookRotation(bounds.center - camera.transform.position, Vector3.up);
                camera.targetTexture = target; RenderTexture.active = target; camera.Render();
                var screenshot = new Texture2D(960, 720, TextureFormat.RGBA32, false);
                screenshot.ReadPixels(new Rect(0, 0, 960, 720), 0, 0); screenshot.Apply(); File.WriteAllBytes(path, screenshot.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(screenshot);
            }
            finally
            { RenderTexture.active = oldTarget; camera.targetTexture = null; target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(cameraObject);
              for (var i = 0; i < renderers.Length; i++) renderers[i].enabled = enabled[i]; }
        }
    }
}
