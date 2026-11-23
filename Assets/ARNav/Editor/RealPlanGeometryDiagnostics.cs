using System;
using System.IO;
using ARNav.Core.Data;
using ARNav.Core.Geometry;
using ARNav.Runtime.Extraction;
using ARNav.Runtime.Generation;
using UnityEditor;
using UnityEngine;

namespace ARNav.Editor
{
    /// <summary>Editor-only diagnostic renders from cached production OpenCV outputs.</summary>
    public static class RealPlanGeometryDiagnostics
    {
        [MenuItem("ARNav/Diagnostics/Render Cached Real Plan Geometry")]
        public static void RenderCachedSamples()
        {
            var args = Environment.GetCommandLineArgs();
            var root = ValueAfter(args, "-realPlanDiagnosticRoot") ?? Directory.GetCurrentDirectory();
            var output = ValueAfter(args, "-realPlanDiagnosticOutput") ?? Path.Combine(root, "Logs", "real-plan-geometry");
            var nativeDirectory = ValueAfter(args, "-realPlanNativeDir") ?? Path.Combine(root, "Logs", "opencv-native-runs");
            Directory.CreateDirectory(output);
            try
            {
                RenderOne(nativeDirectory, output, "f1", 640, 480, .03f);
                RenderOne(nativeDirectory, output, "fp2", 477, 642, .03f);
                Debug.Log("[ARNav][RealPlanDiagnostic] Wrote actual generation-pipeline renders to " + output);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void RenderOne(string nativeDirectory, string output, string id, int width, int height, float metresPerPixel)
        {
            var downloads = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads";
            var sourcePath = Path.Combine(downloads, id + ".jpeg");
            var nativePath = Path.Combine(nativeDirectory, id == "f1" ? "A_baseline.bin" : "B.bin");
            if (!File.Exists(sourcePath) || !File.Exists(nativePath))
                throw new FileNotFoundException("Expected source JPEG and cached production OpenCV binary result: " + sourcePath + " ; " + nativePath);

            var native = File.ReadAllBytes(nativePath);
            var extraction = OpenCvFloorPlanExtractor.ParseNativeResult(native, width, height);
            if (!extraction.succeeded) throw new InvalidDataException(id + " OpenCV result is invalid: " + extraction.message);
            var project = MapProjectData.Create("diagnostic-" + id, "Real plan diagnostic " + id);
            project.sourceImage.fileName = id + ".jpeg";
            project.sourceImage.originalWidthPixels = width;
            project.sourceImage.originalHeightPixels = height;
            project.scaleCalibration.isValid = true;
            project.scaleCalibration.isEstimated = true;
            project.scaleCalibration.metresPerPixel = metresPerPixel;
            var floor = new FloorData { id = "floor-1", name = "Ground", sourceAnnotations = extraction.annotations };
            project.floors.Add(floor);
            if (!PixelToLocalGeometryConverter.TryRegenerate(floor, project.scaleCalibration, width, height, new MapGenerationDefaults(), out var validation))
                throw new InvalidDataException(id + " pixel-to-local conversion failed: " + string.Join(" ", validation.errors.ToArray()));

            var owner = new GameObject("RealPlanDiagnostic_" + id);
            var generator = owner.AddComponent<ProceduralBuildingGenerator>();
            try
            {
                if (!generator.Generate(project, out var error)) throw new InvalidOperationException(id + " generation failed: " + error);
                var renderers = generator.GeneratedRoot.GetComponentsInChildren<MeshRenderer>(true);
                var wallMeshes = 0; var wallVertices = 0; var wallTriangles = 0; var floorMeshes = 0; var floorVertices = 0; var floorTriangles = 0;
                var wallBounds = new Bounds(); var floorBounds = new Bounds(); var wallBoundsSet = false; var floorBoundsSet = false;
                foreach (var renderer in renderers)
                {
                    var filter = renderer.GetComponent<MeshFilter>(); if (filter == null || filter.sharedMesh == null) continue;
                    var mesh = filter.sharedMesh;
                    var isWall = IsUnder(renderer.transform, "Walls");
                    if (isWall)
                    {
                        wallMeshes++; wallVertices += mesh.vertexCount; wallTriangles += mesh.triangles.Length / 3;
                        if (!wallBoundsSet) { wallBounds = renderer.bounds; wallBoundsSet = true; } else wallBounds.Encapsulate(renderer.bounds);
                    }
                    else
                    {
                        floorMeshes++; floorVertices += mesh.vertexCount; floorTriangles += mesh.triangles.Length / 3;
                        if (!floorBoundsSet) { floorBounds = renderer.bounds; floorBoundsSet = true; } else floorBounds.Encapsulate(renderer.bounds);
                    }
                }
                if (wallMeshes == 0 || floorMeshes == 0) throw new InvalidOperationException(id + " generated empty wall or floor geometry.");
                var firstWallMaterial = Array.Find(renderers, renderer => IsUnder(renderer.transform, "Walls"))?.sharedMaterial;
                var wallColor = firstWallMaterial != null && firstWallMaterial.HasProperty("_BaseColor") ? firstWallMaterial.GetColor("_BaseColor") : Color.magenta;
                Debug.Log("[ARNav][RealPlanDiagnostic] " + id + " wallMeshes=" + wallMeshes + " wallVertices=" + wallVertices + " wallTriangles=" + wallTriangles + " worldWallBounds=" + wallBounds + " wallHeight=" + wallBounds.size.y.ToString("0.###") + " wallMaterial=" + (firstWallMaterial == null ? "null" : firstWallMaterial.shader.name + " " + wallColor) + " floorMeshes=" + floorMeshes + " floorVertices=" + floorVertices + " floorTriangles=" + floorTriangles + " worldFloorBounds=" + floorBounds);

                WriteMaskAndOverlay(sourcePath, native, width, height, Path.Combine(output, id));
                Capture(generator.GeneratedRoot, renderers, Path.Combine(output, id + "-walls-only.png"), 0, id, new Color(.96f, .05f, .55f, 1));
                Capture(generator.GeneratedRoot, renderers, Path.Combine(output, id + "-floors-only.png"), 1, id, new Color(.18f, .8f, .9f, 1));
                Capture(generator.GeneratedRoot, renderers, Path.Combine(output, id + "-walls-material-only.png"), 3, id, Color.white);
                Capture(generator.GeneratedRoot, renderers, Path.Combine(output, id + "-combined-isometric.png"), 2, id, Color.white);
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        private static void Capture(GameObject root, MeshRenderer[] renderers, string path, int mode, string id, Color diagnosticColor)
        {
            var diagnosticShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (diagnosticShader == null) throw new InvalidOperationException("URP Unlit shader is unavailable for diagnostics.");
            var diagnostic = new Material(diagnosticShader) { name = "Temporary Diagnostic " + id };
            if (diagnostic.HasProperty("_BaseColor")) diagnostic.SetColor("_BaseColor", diagnosticColor);
            var savedMaterials = new Material[renderers.Length][];
            var savedEnabled = new bool[renderers.Length];
            try
            {
                for (var i = 0; i < renderers.Length; i++)
                {
                    var renderer = renderers[i]; savedMaterials[i] = renderer.sharedMaterials; savedEnabled[i] = renderer.enabled;
                    var wall = IsUnder(renderer.transform, "Walls");
                    renderer.enabled = mode == 2 || (mode == 0 && wall) || (mode == 1 && !wall) || (mode == 3 && wall);
                    if (mode != 2 && mode != 3) renderer.sharedMaterial = diagnostic;
                }
                var bounds = EnabledBounds(renderers);
                var cameraObject = new GameObject("DiagnosticCamera");
                var camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.09f, .105f, .13f, 1);
                camera.orthographic = true; camera.aspect = 4f / 3f; camera.nearClipPlane = .01f; camera.farClipPlane = 1000;
                var extent = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z, .1f);
                camera.orthographicSize = extent * .78f;
                camera.transform.position = bounds.center + new Vector3(extent * 1.25f, extent * 1.15f, -extent * 1.25f);
                camera.transform.rotation = Quaternion.LookRotation(bounds.center - camera.transform.position, Vector3.up);
                var target = new RenderTexture(960, 720, 24, RenderTextureFormat.ARGB32);
                var oldTarget = RenderTexture.active;
                try
                {
                    camera.targetTexture = target; RenderTexture.active = target; camera.Render();
                    var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                    try { image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG()); }
                    finally { UnityEngine.Object.DestroyImmediate(image); }
                }
                finally
                {
                    RenderTexture.active = oldTarget; camera.targetTexture = null; target.Release();
                    UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(cameraObject);
                }
            }
            finally
            {
                for (var i = 0; i < renderers.Length; i++)
                { if (savedMaterials[i] != null) renderers[i].sharedMaterials = savedMaterials[i]; renderers[i].enabled = savedEnabled[i]; }
                UnityEngine.Object.DestroyImmediate(diagnostic);
            }
        }

        private static Bounds EnabledBounds(MeshRenderer[] renderers)
        {
            var set = false; var bounds = new Bounds();
            foreach (var renderer in renderers) if (renderer.enabled)
            { if (!set) { bounds = renderer.bounds; set = true; } else bounds.Encapsulate(renderer.bounds); }
            return set ? bounds : new Bounds();
        }

        private static bool IsUnder(Transform transform, string name)
        { for (var parent = transform; parent != null; parent = parent.parent) if (parent.name == name) return true; return false; }

        private static void WriteMaskAndOverlay(string sourcePath, byte[] native, int width, int height, string prefix)
        {
            var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!image.LoadImage(File.ReadAllBytes(sourcePath))) throw new InvalidDataException("Could not decode " + sourcePath);
                var source = image.GetPixels32(); var output = new Color32[width * height]; var wallMask = new byte[width * height];
                for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                {
                    var unityIndex = (height - 1 - y) * width + x; var maskIndex = 32 + y * width + x;
                    var wall = native[maskIndex] != 0; wallMask[unityIndex] = wall ? (byte)0 : (byte)255;
                    var original = source[unityIndex];
                    output[unityIndex] = wall ? new Color32((byte)((original.r + 255) / 2), (byte)(original.g / 2), (byte)(original.b / 2), 255) : original;
                }
                WritePng(prefix + "-wall-mask.png", width, height, wallMask);
                image.SetPixels32(output); image.Apply(); File.WriteAllBytes(prefix + "-wall-overlay.png", image.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(image); }
        }

        private static void WritePng(string path, int width, int height, byte[] values)
        {
            var image = new Texture2D(width, height, TextureFormat.RGBA32, false); var colors = new Color32[values.Length];
            for (var i = 0; i < values.Length; i++) colors[i] = new Color32(values[i], values[i], values[i], 255);
            try { image.SetPixels32(colors); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG()); }
            finally { UnityEngine.Object.DestroyImmediate(image); }
        }

        private static string ValueAfter(string[] args, string key)
        { for (var i = 0; i + 1 < args.Length; i++) if (args[i] == key) return args[i + 1]; return null; }
    }
}
