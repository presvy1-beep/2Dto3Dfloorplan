using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ARNav.Core.Data;
using ARNav.Core.Serialization;
using UnityEngine;

namespace ARNav.Runtime.Import
{
    public sealed class StoredMapImage
    {
        public string relativePath;
        public string absolutePath;
        public int width;
        public int height;
        public string format;
        public string sha256;
        public long sourceByteLength;
        public Texture2D texture;
    }

    /// Copies and decodes user images in project-owned storage. It does not save map JSON.
    public sealed class MapImageStorageService
    {
        private readonly LocalFileMapRepository repository;
        public MapImageStorageService(LocalFileMapRepository repository) { this.repository = repository ?? throw new ArgumentNullException("repository"); }

        public StoredMapImage ImportAndStore(MapProjectData project, string sourcePath)
        {
            if (project == null) throw new ArgumentNullException("project");
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) throw new FileNotFoundException("Selected image was not found.", sourcePath);
            var bytes = File.ReadAllBytes(sourcePath);
            var format = DetectImageFormat(bytes);
            string sha256;
            using (var sha = SHA256.Create()) sha256 = ToHex(sha.ComputeHash(bytes));
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            // Keep the runtime copy readable: analysis samples luminance from the same decoded texture.
            if (!texture.LoadImage(bytes, false) || texture.width <= 0 || texture.height <= 0) { UnityEngine.Object.DestroyImmediate(texture); throw new InvalidDataException("The selected file is not a valid PNG/JPG image."); }
            var relativePath = "floorplan." + format;
            if (project.sourceImage != null && project.sourceImage.pathReference == relativePath && File.Exists(repository.GetProjectImagePath(project.projectId, relativePath))) relativePath = "floorplan-" + Guid.NewGuid().ToString("N") + "." + format;
            var destination = repository.GetProjectImagePath(project.projectId, relativePath);
            var directory = repository.GetProjectDirectory(project.projectId);
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, ".floorplan-importing-" + Guid.NewGuid().ToString("N") + "." + format);
            try
            {
                // Persist the exact snapshot that was hashed and decoded. The picker
                // source may be replaced while import is in progress.
                File.WriteAllBytes(temporary, bytes);
                File.Copy(temporary, destination, true);
                return new StoredMapImage { relativePath = relativePath, absolutePath = destination, width = texture.width, height = texture.height, format = format, sha256 = sha256, sourceByteLength = bytes.Length, texture = texture };
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(texture);
                throw;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public Texture2D LoadStoredTexture(MapProjectData project, out int width, out int height)
        {
            width = 0; height = 0;
            if (project == null || project.sourceImage == null || string.IsNullOrWhiteSpace(project.sourceImage.pathReference)) return null;
            var path = repository.GetProjectImagePath(project.projectId, project.sourceImage.pathReference);
            if (!File.Exists(path)) throw new FileNotFoundException("Stored floor-plan image is missing.", path);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(path), false)) { UnityEngine.Object.DestroyImmediate(texture); throw new InvalidDataException("Stored floor-plan image could not be decoded."); }
            width = texture.width; height = texture.height; return texture;
        }

        public void DeleteIfObsolete(MapProjectData project, string oldRelativePath, string newRelativePath)
        {
            if (string.IsNullOrWhiteSpace(oldRelativePath) || oldRelativePath == newRelativePath) return;
            var oldPath = repository.GetProjectImagePath(project.projectId, oldRelativePath);
            if (File.Exists(oldPath)) File.Delete(oldPath);
        }

        public void DeleteStoredImage(MapProjectData project, string relativePath)
        {
            if (project == null || string.IsNullOrWhiteSpace(relativePath)) return;
            var path = repository.GetProjectImagePath(project.projectId, relativePath);
            if (File.Exists(path)) File.Delete(path);
        }

        public static string ComputeSha256(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path)) return ToHex(sha.ComputeHash(stream));
        }

        /// <summary>Detects supported encoding from selected file bytes, not its display name.</summary>
        public static string DetectImageFormat(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");
            if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4e && bytes[3] == 0x47 && bytes[4] == 0x0d && bytes[5] == 0x0a && bytes[6] == 0x1a && bytes[7] == 0x0a) return "png";
            if (bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff) return "jpg";
            throw new NotSupportedException("Selected file bytes are not a supported PNG or JPEG image.");
        }

        private static string ToHex(byte[] bytes)
        { var builder = new StringBuilder(bytes.Length * 2); foreach (var value in bytes) builder.Append(value.ToString("x2")); return builder.ToString(); }
    }
}
