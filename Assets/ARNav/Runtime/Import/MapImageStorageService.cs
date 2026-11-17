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

    /// <summary>Copies and decodes user images in project-owned storage. It does not save map JSON.</summary>
    public sealed class MapImageStorageService
    {
        private readonly LocalFileMapRepository repository;
        private readonly ImageImportLimits limits;

        public MapImageStorageService(LocalFileMapRepository repository) : this(repository, null) { }

        public MapImageStorageService(LocalFileMapRepository repository, ImageImportLimits limits)
        {
            this.repository = repository ?? throw new ArgumentNullException("repository");
            this.limits = limits ?? ImageImportLimits.Default;
            this.limits.ValidateConfiguration();
        }

        /// <summary>
        /// Synchronously reads, validates and decodes on Unity's main thread. Dimensions
        /// are checked from the file header before LoadImage can allocate its texture.
        /// The caller owns the returned texture and must destroy it after use.
        /// </summary>
        public StoredMapImage ImportAndStore(MapProjectData project, string sourcePath)
        {
            if (project == null) throw new ArgumentNullException("project");
            if (string.IsNullOrWhiteSpace(sourcePath)) throw new FileNotFoundException("The selected image is no longer available. Select it again.");

            byte[] bytes;
            try { bytes = ReadBoundedFile(sourcePath); }
            catch (FileNotFoundException) { throw new FileNotFoundException("The selected image is no longer available. Select it again.", sourcePath); }
            catch (DirectoryNotFoundException) { throw new FileNotFoundException("The selected image is no longer available. Select it again.", sourcePath); }
            catch (UnauthorizedAccessException exception) { throw new UnauthorizedAccessException("The selected image cannot be accessed. Choose it again or select a different file.", exception); }
            catch (IOException exception) { throw new IOException("The selected image could not be read. Check the file and available storage, then try again.", exception); }

            var header = ImageImportValidation.Validate(bytes, limits);
            var sha256 = ComputeSha256(bytes);
            Texture2D texture = null;
            string temporary = null;
            var transferredTexture = false;
            try
            {
                try
                {
                    texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    // markNonReadable=false is required by FloorPlanAnalysisController.
                    if (!texture.LoadImage(bytes, false)) throw new InvalidDataException("This image cannot be decoded. Check that it is a complete PNG or JPEG file.");
                }
                catch (OutOfMemoryException exception)
                {
                    throw new InvalidOperationException("There is not enough memory to import this image. Choose a smaller floor plan and close other apps.", exception);
                }
                catch (InvalidDataException) { throw; }
                catch (Exception exception)
                {
                    throw new InvalidDataException("This image cannot be decoded. Check that it is a complete PNG or JPEG file.", exception);
                }

                if (texture == null || texture.width != header.width || texture.height != header.height || texture.width <= 0 || texture.height <= 0)
                    throw new InvalidDataException("The decoded image dimensions do not match its header. Choose a valid PNG or JPEG file.");

                var relativePath = "floorplan." + header.format;
                if (project.sourceImage != null && project.sourceImage.pathReference == relativePath && File.Exists(repository.GetProjectImagePath(project.projectId, relativePath)))
                    relativePath = "floorplan-" + Guid.NewGuid().ToString("N") + "." + header.format;
                var destination = repository.GetProjectImagePath(project.projectId, relativePath);
                // Never overwrite an existing image. The map JSON is switched to this
                // unique file only after its complete bytes have reached final storage.
                while (File.Exists(destination))
                {
                    relativePath = "floorplan-" + Guid.NewGuid().ToString("N") + "." + header.format;
                    destination = repository.GetProjectImagePath(project.projectId, relativePath);
                }

                var directory = repository.GetProjectDirectory(project.projectId);
                Directory.CreateDirectory(directory);
                temporary = Path.Combine(directory, ".floorplan-importing-" + Guid.NewGuid().ToString("N") + "." + header.format);
                WriteAndFlush(temporary, bytes);
                File.Move(temporary, destination);
                temporary = null;
                transferredTexture = true;
                return new StoredMapImage
                {
                    relativePath = relativePath,
                    absolutePath = destination,
                    width = texture.width,
                    height = texture.height,
                    format = header.format,
                    sha256 = sha256,
                    sourceByteLength = bytes.LongLength,
                    texture = texture
                };
            }
            catch (InvalidDataException) { throw; }
            catch (OutOfMemoryException exception)
            {
                throw new InvalidOperationException("There is not enough memory to import this image. Choose a smaller floor plan and close other apps.", exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new UnauthorizedAccessException("The image cannot be saved in app storage. Free device storage and try again.", exception);
            }
            catch (IOException exception)
            {
                throw new IOException("The image could not be stored in app storage. Check available storage and try again.", exception);
            }
            finally
            {
                if (!string.IsNullOrEmpty(temporary)) TryDelete(temporary);
                if (!transferredTexture) DestroyTexture(texture);
            }
        }

        public Texture2D LoadStoredTexture(MapProjectData project, out int width, out int height)
        {
            width = 0; height = 0;
            if (project == null || project.sourceImage == null || string.IsNullOrWhiteSpace(project.sourceImage.pathReference)) return null;
            var path = repository.GetProjectImagePath(project.projectId, project.sourceImage.pathReference);
            if (!File.Exists(path)) throw new FileNotFoundException("The saved floor-plan image is missing. Import the image again.", path);
            byte[] bytes;
            try { bytes = ReadBoundedFile(path); }
            catch (UnauthorizedAccessException exception) { throw new UnauthorizedAccessException("The saved floor-plan image cannot be accessed.", exception); }
            var header = ImageImportValidation.Validate(bytes, limits);
            var texture = DecodeStoredTexture(bytes, header);
            width = texture.width; height = texture.height;
            return texture;
        }

        private static Texture2D DecodeStoredTexture(byte[] bytes, ImageHeaderInfo header)
        {
            Texture2D texture = null;
            try
            {
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(bytes, false) || texture.width != header.width || texture.height != header.height)
                    throw new InvalidDataException("The saved floor-plan image cannot be decoded. Import it again.");
                return texture;
            }
            catch (OutOfMemoryException exception)
            {
                DestroyTexture(texture);
                throw new InvalidOperationException("There is not enough memory to load this floor plan. Choose a smaller image.", exception);
            }
            catch (InvalidDataException) { DestroyTexture(texture); throw; }
            catch (Exception exception)
            {
                DestroyTexture(texture);
                throw new InvalidDataException("The saved floor-plan image cannot be decoded. Import it again.", exception);
            }
        }

        private byte[] ReadBoundedFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new FileNotFoundException("Image file was not found.", path);
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > limits.maximumFileBytes)
                    throw new InvalidDataException("This image file exceeds the supported " + (limits.maximumFileBytes / (1024f * 1024f)).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " MB limit. Choose a smaller floor-plan image.");
                if (stream.Length <= 0) throw new InvalidDataException("The selected image file is empty. Choose a PNG or JPEG image.");
                var bytes = new byte[(int)stream.Length];
                var offset = 0;
                while (offset < bytes.Length)
                {
                    var read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read <= 0) throw new InvalidDataException("The image file ended unexpectedly. Select it again or choose another file.");
                    offset += read;
                }
                if (stream.ReadByte() != -1) throw new InvalidDataException("The image changed while it was being read. Select it again.");
                return bytes;
            }
        }

        private static void WriteAndFlush(string path, byte[] bytes)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
        }

        private static string ComputeSha256(byte[] bytes)
        { using (var sha = SHA256.Create()) return ToHex(sha.ComputeHash(bytes)); }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
            else UnityEngine.Object.DestroyImmediate(texture);
        }

        private static void TryDelete(string path)
        { try { if (File.Exists(path)) File.Delete(path); } catch (Exception exception) { Debug.LogWarning("ARNav: temporary image cleanup failed: " + exception.Message); } }

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

        public static string DetectImageFormat(byte[] bytes) { return ImageImportValidation.DetectFormat(bytes); }

        public static string ToUserFacingError(Exception exception)
        {
            if (exception == null) return "The image could not be imported. Choose a valid PNG or JPEG file and try again.";
            if (exception is OutOfMemoryException) return "There is not enough memory to import this image. Choose a smaller floor plan and close other apps.";
            if (exception is FileNotFoundException || exception is DirectoryNotFoundException) return "The selected image is no longer available. Select it again.";
            if (exception is UnauthorizedAccessException) return "The selected image cannot be accessed. Choose it again or select a different file.";
            if (exception is NotSupportedException || exception is InvalidDataException || exception is ArgumentException)
                return string.IsNullOrWhiteSpace(exception.Message) ? "This image is invalid or unsupported. Choose a PNG or JPEG floor plan." : exception.Message;
            if (exception is IOException) return string.IsNullOrWhiteSpace(exception.Message) ? "The image could not be read or saved. Check available storage and try again." : exception.Message;
            if (exception is InvalidOperationException && !string.IsNullOrWhiteSpace(exception.Message)) return exception.Message;
            return "The image could not be imported. Choose a valid PNG or JPEG file and try again.";
        }

        private static string ToHex(byte[] bytes)
        { var builder = new StringBuilder(bytes.Length * 2); foreach (var value in bytes) builder.Append(value.ToString("x2")); return builder.ToString(); }
    }
}
