using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace ARNav.Runtime.Generation
{
    /// <summary>Small runtime glTF 2.0 GLB writer for static generated architecture.</summary>
    public static class RuntimeGlbExporter
    {
        private const uint GlbMagic = 0x46546C67;
        private const uint JsonChunk = 0x4E4F534A;
        private const uint BinChunk = 0x004E4942;

        public sealed class Result
        {
            public byte[] bytes;
            public int meshCount;
            public int vertexCount;
            public int triangleCount;
        }

        public static bool TryExport(GameObject generatedRoot, out Result result, out string error)
        {
            result = null;
            error = null;
            if (generatedRoot == null) { error = "Generated model is unavailable."; return false; }

            try
            {

            var renderers = generatedRoot.GetComponentsInChildren<MeshRenderer>(true);
            var meshes = new List<MeshPayload>();
            var binary = new List<byte>(1024 * 1024);
            foreach (var renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || IsExcluded(renderer.transform, generatedRoot.transform)) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = filter == null ? null : filter.sharedMesh;
                if (mesh == null || mesh.vertexCount < 3) continue;

                var vertices = mesh.vertices;
                var sourceNormals = mesh.normals;
                var normals = new Vector3[vertices.Length];
                var positions = new Vector3[vertices.Length];
                var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
                var normalMatrix = renderer.transform.localToWorldMatrix.inverse.transpose;
                for (var i = 0; i < vertices.Length; i++)
                {
                    var world = renderer.transform.TransformPoint(vertices[i]);
                    var p = new Vector3(world.x, world.y, -world.z);
                    if (!Finite(p)) { error = "Generated mesh has non-finite vertex positions."; return false; }
                    positions[i] = p;
                    min = Vector3.Min(min, p); max = Vector3.Max(max, p);
                    var n = sourceNormals != null && sourceNormals.Length == vertices.Length ? normalMatrix.MultiplyVector(sourceNormals[i]).normalized : Vector3.up;
                    if (!Finite(n) || n.sqrMagnitude < 0.000001f) n = Vector3.up;
                    normals[i] = new Vector3(n.x, n.y, -n.z);
                }

                var sourceIndices = mesh.triangles;
                if (sourceIndices.Length < 3 || sourceIndices.Length % 3 != 0) continue;
                foreach (var index in sourceIndices)
                    if (index < 0 || index >= vertices.Length) { error = "Generated mesh has an out-of-range triangle index."; return false; }
                Align4(binary);
                var positionOffset = binary.Count;
                foreach (var p in positions) { WriteFloat(binary, p.x); WriteFloat(binary, p.y); WriteFloat(binary, p.z); }
                var positionLength = binary.Count - positionOffset;
                Align4(binary);
                var normalOffset = binary.Count;
                foreach (var n in normals) { WriteFloat(binary, n.x); WriteFloat(binary, n.y); WriteFloat(binary, n.z); }
                var normalLength = binary.Count - normalOffset;
                Align4(binary);
                var indexOffset = binary.Count;
                // Reflecting Unity's Z-up-plane coordinates into glTF's right-handed space
                // reverses handedness, so reverse each triangle's winding as well.
                for (var i = 0; i < sourceIndices.Length; i += 3)
                {
                    WriteUInt(binary, (uint)sourceIndices[i]);
                    WriteUInt(binary, (uint)sourceIndices[i + 2]);
                    WriteUInt(binary, (uint)sourceIndices[i + 1]);
                }
                var indexLength = binary.Count - indexOffset;

                var material = renderer.sharedMaterial;
                var color = Color.white;
                if (material != null)
                {
                    if (material.HasProperty("_BaseColor")) color = material.GetColor("_BaseColor");
                    else if (material.HasProperty("_Color")) color = material.color;
                }
                meshes.Add(new MeshPayload
                {
                    name = renderer.gameObject.name,
                    materialName = material == null ? "ARNav_Default" : material.name,
                    color = color,
                    vertexCount = positions.Length,
                    triangleCount = sourceIndices.Length / 3,
                    min = min, max = max,
                    positionOffset = positionOffset, positionLength = positionLength,
                    normalOffset = normalOffset, normalLength = normalLength,
                    indexOffset = indexOffset, indexLength = indexLength
                });
            }
            if (meshes.Count == 0) { error = "Generated architectural model contains no enabled meshes."; return false; }

            var json = BuildJson(meshes, binary.Count);
            // glTF 2.0 requires JSON chunk padding to use spaces (0x20), not
            // zero bytes. Zero padding made otherwise plausible GLBs fail JSON
            // parsing in strict consumers.
            var jsonBytes = Encoding.UTF8.GetBytes(json);
            var alignedJsonLength = (jsonBytes.Length + 3) & ~3;
            if (alignedJsonLength != jsonBytes.Length)
            {
                var paddedJson = new byte[alignedJsonLength];
                Buffer.BlockCopy(jsonBytes, 0, paddedJson, 0, jsonBytes.Length);
                for (var i = jsonBytes.Length; i < paddedJson.Length; i++) paddedJson[i] = 0x20;
                jsonBytes = paddedJson;
            }
            var binBytes = binary.ToArray();
            while ((binBytes.Length & 3) != 0) Array.Resize(ref binBytes, binBytes.Length + 1);
            var totalLength = 12 + 8 + jsonBytes.Length + 8 + binBytes.Length;
            using (var output = new MemoryStream(totalLength))
            using (var writer = new BinaryWriter(output))
            {
                writer.Write(GlbMagic); writer.Write(2u); writer.Write((uint)totalLength);
                writer.Write((uint)jsonBytes.Length); writer.Write(JsonChunk); writer.Write(jsonBytes);
                writer.Write((uint)binBytes.Length); writer.Write(BinChunk); writer.Write(binBytes);
                result = new Result { bytes = output.ToArray(), meshCount = meshes.Count };
                foreach (var mesh in meshes) { result.vertexCount += mesh.vertexCount; result.triangleCount += mesh.triangleCount; }
            }
            return true;
            }
            catch (Exception exception) { result = null; error = "Could not serialize generated mesh: " + exception.Message; return false; }
        }

        private static bool Finite(Vector3 value)
        { return !float.IsNaN(value.x) && !float.IsInfinity(value.x) && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z); }

        private static string BuildJson(List<MeshPayload> meshes, int binaryLength)
        {
            var json = new StringBuilder(binaryLength / 2 + 1024);
            json.Append("{\"asset\":{\"version\":\"2.0\",\"generator\":\"ARNav Runtime GLB Exporter\"},\"scene\":0,\"scenes\":[{\"nodes\":[");
            for (var i = 0; i < meshes.Count; i++) { if (i > 0) json.Append(','); json.Append(i); }
            json.Append("]}],\"nodes\":[");
            for (var i = 0; i < meshes.Count; i++) { if (i > 0) json.Append(','); json.Append("{\"name\":").Append(Quote(meshes[i].name)).Append(",\"mesh\":").Append(i).Append('}'); }
            json.Append("],\"meshes\":[");
            for (var i = 0; i < meshes.Count; i++)
            {
                if (i > 0) json.Append(',');
                var a = i * 3;
                json.Append("{\"name\":").Append(Quote(meshes[i].name)).Append(",\"primitives\":[{\"attributes\":{\"POSITION\":").Append(a).Append(",\"NORMAL\":").Append(a + 1).Append("},\"indices\":").Append(a + 2).Append(",\"material\":").Append(i).Append(",\"mode\":4}]}");
            }
            json.Append("],\"materials\":[");
            for (var i = 0; i < meshes.Count; i++)
            {
                if (i > 0) json.Append(','); var c = meshes[i].color;
                json.Append("{\"name\":").Append(Quote(meshes[i].materialName)).Append(",\"doubleSided\":true,\"pbrMetallicRoughness\":{\"baseColorFactor\":[")
                    .Append(F(c.r)).Append(',').Append(F(c.g)).Append(',').Append(F(c.b)).Append(',').Append(F(c.a))
                    .Append("],\"metallicFactor\":0,\"roughnessFactor\":0.85}}");
            }
            json.Append("],\"buffers\":[{\"byteLength\":").Append(binaryLength).Append("}],\"bufferViews\":[");
            for (var i = 0; i < meshes.Count; i++)
            {
                if (i > 0) json.Append(','); var m = meshes[i];
                json.Append("{\"buffer\":0,\"byteOffset\":").Append(m.positionOffset).Append(",\"byteLength\":").Append(m.positionLength).Append(",\"target\":34962},")
                    .Append("{\"buffer\":0,\"byteOffset\":").Append(m.normalOffset).Append(",\"byteLength\":").Append(m.normalLength).Append(",\"target\":34962},")
                    .Append("{\"buffer\":0,\"byteOffset\":").Append(m.indexOffset).Append(",\"byteLength\":").Append(m.indexLength).Append(",\"target\":34963}");
            }
            json.Append("],\"accessors\":[");
            for (var i = 0; i < meshes.Count; i++)
            {
                if (i > 0) json.Append(','); var m = meshes[i]; var a = i * 3;
                json.Append("{\"bufferView\":").Append(a).Append(",\"componentType\":5126,\"count\":").Append(m.vertexCount).Append(",\"type\":\"VEC3\",\"min\":[")
                    .Append(F(m.min.x)).Append(',').Append(F(m.min.y)).Append(',').Append(F(m.min.z)).Append("],\"max\":[")
                    .Append(F(m.max.x)).Append(',').Append(F(m.max.y)).Append(',').Append(F(m.max.z)).Append("]},")
                    .Append("{\"bufferView\":").Append(a + 1).Append(",\"componentType\":5126,\"count\":").Append(m.vertexCount).Append(",\"type\":\"VEC3\"},")
                    .Append("{\"bufferView\":").Append(a + 2).Append(",\"componentType\":5125,\"count\":").Append(m.triangleCount * 3).Append(",\"type\":\"SCALAR\"}");
            }
            return json.Append("]}").ToString();
        }

        private static bool IsExcluded(Transform item, Transform root)
        {
            for (var at = item; at != null && at != root; at = at.parent)
            {
                var name = at.name;
                if (name == "NavigationFloorMesh" || name == "NavigationRuntime" || name == "RoomAnchors" || name == "AutomaticWaypointNetwork" || name == "ActiveRoute" || name.StartsWith("Debug", StringComparison.Ordinal) || name.StartsWith("Waypoint", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static string Quote(string value)
        {
            var s = new StringBuilder("\"");
            foreach (var c in value ?? string.Empty)
            {
                if (c == '\"' || c == '\\') s.Append('\\').Append(c);
                else if (c < 32) s.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else s.Append(c);
            }
            return s.Append('\"').ToString();
        }
        private static string F(float value) { return value.ToString("R", CultureInfo.InvariantCulture); }
        private static void Align4(List<byte> bytes) { while ((bytes.Count & 3) != 0) bytes.Add(0); }
        private static void WriteFloat(List<byte> bytes, float value) { var bits = new FloatInt32 { floating = value }; WriteUInt(bytes, unchecked((uint)bits.integer)); }
        private static void WriteUInt(List<byte> bytes, uint value) { bytes.Add((byte)value); bytes.Add((byte)(value >> 8)); bytes.Add((byte)(value >> 16)); bytes.Add((byte)(value >> 24)); }

        private sealed class MeshPayload
        {
            public string name, materialName;
            public Color color;
            public int vertexCount, triangleCount;
            public Vector3 min, max;
            public int positionOffset, positionLength, normalOffset, normalLength, indexOffset, indexLength;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct FloatInt32
        {
            [FieldOffset(0)] public float floating;
            [FieldOffset(0)] public int integer;
        }
    }
}
