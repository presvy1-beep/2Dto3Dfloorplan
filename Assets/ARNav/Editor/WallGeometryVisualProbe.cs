using System;
using System.Collections.Generic;
using System.IO;
using ARNav.Core.Data;
using ARNav.Core.Geometry;
using ARNav.Runtime.Extraction;
using ARNav.Runtime.Generation;
using UnityEditor;
using UnityEngine;

namespace ARNav.Editor
{
    /// <summary>Local visual/geometry comparison harness for the three physical test plans.</summary>
    public static class WallGeometryVisualProbe
    {
        public static void Run()
        {
            var output = ValueAfter(Environment.GetCommandLineArgs(), "-wallProbeOutput") ?? "/tmp/arnav-wall-visual";
            var imageDirectory = ValueAfter(Environment.GetCommandLineArgs(), "-wallProbeInputs") ?? "/tmp/arnav-test-plans";
            var mppValue = ValueAfter(Environment.GetCommandLineArgs(), "-wallProbeMetresPerPixel");
            var mpp = string.IsNullOrEmpty(mppValue) ? .01f : float.Parse(mppValue, System.Globalization.CultureInfo.InvariantCulture);
            Directory.CreateDirectory(output);
            try
            {
                foreach (var name in new[] { "Test1", "Test2", "Test3" }) Probe(Path.Combine(imageDirectory, name + (name == "Test3" ? ".png" : ".jpg")), output, name, mpp);
                EditorApplication.Exit(0);
            }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }

        private static void Probe(string imagePath, string output, string name, float mpp)
        {
            var bytes = File.ReadAllBytes(imagePath); var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(bytes)) throw new InvalidDataException("Could not decode " + imagePath);
                var pixels = texture.GetPixels32(); var luma = new byte[pixels.Length];
                for (var i = 0; i < pixels.Length; i++) luma[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(pixels[i].r * .2126f + pixels[i].g * .7152f + pixels[i].b * .0722f), 0, 255);
                var annotations = new FloorPlanAnnotationData();
                ArchitecturalWallMaskProcessor.Process(new FloorPlanImageData(texture.width, texture.height, luma, pixels), FloorPlanCropRegion.Full(texture.width, texture.height), annotations);
                var raw = new List<LocalRasterRun>(annotations.wallRuns.Count);
                foreach (var run in annotations.wallRuns)
                    raw.Add(new LocalRasterRun { minX = run.minX * mpp, maxX = run.maxX * mpp, minZ = (texture.height - run.maxY) * mpp, maxZ = (texture.height - run.minY) * mpp });
                WriteMask(Path.Combine(output, name + "-raw-mask.png"), raw, texture.width, texture.height, mpp);
                var localPaths = new List<LocalWallPathData>();
                var widthMetres = Mathf.Clamp(annotations.wallPathDiagnostics.estimatedWidthPixels * mpp,
                    WallPathGeometryConfiguration.MinimumWallThicknessMeters, WallPathGeometryConfiguration.MaximumWallThicknessMeters);
                foreach (var path in annotations.wallPaths)
                {
                    var local = new LocalWallPathData { id = path.id, widthMetres = widthMetres, closed = path.closed };
                    foreach (var point in path.points) local.points.Add(new LocalPoint(point.x * mpp, (texture.height - point.y) * mpp));
                    localPaths.Add(local);
                }
                WritePathPreview(Path.Combine(output, name + "-wallpaths-preview.png"), annotations, texture.width, texture.height);
                File.WriteAllText(Path.Combine(output, name + "-wallpaths.json"), JsonUtility.ToJson(annotations, true));
                var probeHeight = new MapGenerationDefaults().defaultWallHeightMetres;
                var before = ArchitecturalWallMaskMeshBuilder.Build(raw, 0, probeHeight);
                var after = WallPathMeshBuilder.Build(localPaths, 0, probeHeight, out var stats);
                if (before == null || after == null) throw new InvalidOperationException(name + " produced an empty raw or vector wall mesh.");
                WriteObj(Path.Combine(output, name + "-raw.obj"), before);
                WriteObj(Path.Combine(output, name + "-wallpaths.obj"), after);
                Render(before, Path.Combine(output, name + "-raw-top.png"), Path.Combine(output, name + "-raw-angle.png"), false);
                Render(after, Path.Combine(output, name + "-wallpaths-top.png"), Path.Combine(output, name + "-wallpaths-angle.png"), true);
                var pathInfo = annotations.wallPathDiagnostics;
                var rawArea = pathInfo.rawMaskPixels * mpp * mpp;
                var areaChange = rawArea <= 0 ? 0 : (stats.footprintAreaSquareMetres - rawArea) * 100f / rawArea;
                var report = name + " source=" + texture.width + "x" + texture.height + " rawPixels=" + pathInfo.rawMaskPixels + " skeletonPixels=" + pathInfo.skeletonPixels +
                             " endpoints=" + pathInfo.endpoints + " junctions=" + pathInfo.junctions + " joinedGaps=" + pathInfo.joinedGaps +
                             " paths=" + pathInfo.initialPaths + "->" + pathInfo.finalPaths + " points=" + pathInfo.pointsBeforeSimplification + "->" + pathInfo.pointsAfterSimplification +
                             " estimatedWidthPx=" + pathInfo.estimatedWidthPixels.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " metersPerPixel=" + mpp.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) +
                             " widthMeters=" + widthMetres.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " rawAreaM2=" + rawArea.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
                             " pathAreaM2=" + stats.footprintAreaSquareMetres.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " delta=" + areaChange.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                             "% grid=" + stats.gridWidth + "x" + stats.gridHeight + " rawVertices=" + before.vertexCount + " rawTriangles=" + before.triangles.Length / 3 +
                             " pathVertices=" + after.vertexCount + " pathTriangles=" + after.triangles.Length / 3;
                File.WriteAllText(Path.Combine(output, name + "-stats.txt"), report + "\n"); Debug.Log("[ARNav][WallVisualProbe] " + report);
                UnityEngine.Object.DestroyImmediate(before); UnityEngine.Object.DestroyImmediate(after);
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        private static void Render(Mesh mesh, string topPath, string anglePath, bool normalized)
        {
            var root = new GameObject("WallVisualProbe"); var filter = root.AddComponent<MeshFilter>(); filter.sharedMesh = mesh;
            var renderer = root.AddComponent<MeshRenderer>(); var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { color = normalized ? new Color(.82f, .86f, .9f) : new Color(.82f, .86f, .9f) }; renderer.sharedMaterial = material;
            var lightObject = new GameObject("ProbeLight"); var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.2f; lightObject.transform.rotation = Quaternion.Euler(45, -35, 0);
            var cameraObject = new GameObject("ProbeCamera"); var camera = cameraObject.AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.13f, .15f, .18f); camera.orthographic = true; camera.orthographicSize = Mathf.Max(mesh.bounds.size.x, mesh.bounds.size.z) * .58f; camera.aspect = 1f; camera.nearClipPlane = .01f; camera.farClipPlane = 1000f;
            var target = new RenderTexture(1080, 1080, 24, RenderTextureFormat.ARGB32);
            try
            {
                camera.targetTexture = target; RenderView(camera, target, topPath, mesh.bounds, true); RenderView(camera, target, anglePath, mesh.bounds, false);
            }
            finally { camera.targetTexture = null; target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(lightObject); UnityEngine.Object.DestroyImmediate(cameraObject); UnityEngine.Object.DestroyImmediate(material); }
        }

        private static void RenderView(Camera camera, RenderTexture target, string path, Bounds bounds, bool top)
        {
            var center = new Vector3(bounds.center.x, .1f, bounds.center.z);
            camera.transform.position = top ? new Vector3(bounds.center.x, 60f, bounds.center.z) : center + new Vector3(0, Mathf.Max(8f, bounds.size.magnitude), -Mathf.Max(8f, bounds.size.magnitude));
            camera.transform.rotation = top ? Quaternion.Euler(90, 0, 0) : Quaternion.LookRotation(center - camera.transform.position, Vector3.up);
            camera.Render(); var old = RenderTexture.active; RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try { image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG()); }
            finally { RenderTexture.active = old; UnityEngine.Object.DestroyImmediate(image); }
        }

        private static void WriteMask(string path, List<LocalRasterRun> runs, int width, int height, float mpp)
        {
            var pixels = new Color32[width * height]; for (var i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            foreach (var run in runs) { var x0 = Mathf.Clamp(Mathf.FloorToInt(run.minX / mpp), 0, width); var x1 = Mathf.Clamp(Mathf.CeilToInt(run.maxX / mpp), 0, width); var y0 = Mathf.Clamp(height - Mathf.CeilToInt(run.maxZ / mpp), 0, height); var y1 = Mathf.Clamp(height - Mathf.FloorToInt(run.minZ / mpp), 0, height); for (var y = y0; y < y1; y++) for (var x = x0; x < x1; x++) pixels[y * width + x] = new Color32(0, 0, 0, 255); }
            var image = new Texture2D(width, height, TextureFormat.RGBA32, false); try { image.SetPixels32(pixels); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG()); } finally { UnityEngine.Object.DestroyImmediate(image); }
        }

        private static void WritePathPreview(string path, FloorPlanAnnotationData annotations, int width, int height)
        {
            var pixels=new Color32[width*height];for(var i=0;i<pixels.Length;i++)pixels[i]=new Color32(255,255,255,255);
            foreach(var run in annotations.wallRuns)for(var y=Mathf.Clamp(Mathf.FloorToInt(run.minY),0,height);y<Mathf.Clamp(Mathf.CeilToInt(run.maxY),0,height);y++)for(var x=Mathf.Clamp(Mathf.FloorToInt(run.minX),0,width);x<Mathf.Clamp(Mathf.CeilToInt(run.maxX),0,width);x++)pixels[y*width+x]=new Color32(220,225,230,255);
            foreach(var pathData in annotations.wallPaths)
            {
                if(pathData.points==null||pathData.points.Count<2)continue;var radius=Mathf.Max(.5f,pathData.widthPixels*.5f);
                for(var i=1;i<pathData.points.Count;i++)DrawStroke(pixels,width,height,pathData.points[i-1],pathData.points[i],radius);
                if(pathData.closed&&pathData.points.Count>2)DrawStroke(pixels,width,height,pathData.points[pathData.points.Count-1],pathData.points[0],radius);
            }
            var image=new Texture2D(width,height,TextureFormat.RGBA32,false);try{image.SetPixels32(pixels);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}finally{UnityEngine.Object.DestroyImmediate(image);}
        }
        private static void DrawStroke(Color32[] pixels,int width,int height,ARNav.Core.Geometry.PixelPoint a,ARNav.Core.Geometry.PixelPoint b,float radius)
        {var minX=Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x,b.x)-radius),0,width-1);var maxX=Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x,b.x)+radius),0,width-1);var minY=Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y,b.y)-radius),0,height-1);var maxY=Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y,b.y)+radius),0,height-1);var d=new Vector2(b.x-a.x,b.y-a.y);var len=d.x*d.x+d.y*d.y;for(var y=minY;y<=maxY;y++)for(var x=minX;x<=maxX;x++){var p=new Vector2(x+.5f,y+.5f);var t=len<1e-6f?0:Mathf.Clamp01(Vector2.Dot(p-new Vector2(a.x,a.y),d)/len);var q=new Vector2(a.x,a.y)+t*d;if((p-q).sqrMagnitude<=radius*radius)pixels[y*width+x]=new Color32(15,20,26,255);}}

        private static void WriteObj(string path, Mesh mesh)
        {
            var vertices = mesh.vertices; var triangles = mesh.triangles; using (var writer = new StreamWriter(path))
            { foreach (var vertex in vertices) writer.WriteLine("v " + vertex.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " " + vertex.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " " + vertex.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture)); for (var i = 0; i < triangles.Length; i += 3) writer.WriteLine("f " + (triangles[i] + 1) + " " + (triangles[i + 1] + 1) + " " + (triangles[i + 2] + 1)); }
        }
        private static string ValueAfter(string[] args, string flag) { for (var i = 0; i + 1 < args.Length; i++) if (args[i] == flag) return args[i + 1]; return null; }
    }
}
