using System;
using System.IO;
using ARNav.Core.Data;
using ARNav.Core.Serialization;
using ARNav.Runtime.Import;
using NUnit.Framework;
using UnityEngine;

namespace ARNav.Tests.EditMode
{
    public sealed class ImageImportSafetyTests
    {
        private string root;
        private string source;
        private LocalFileMapRepository repository;
        private MapProjectData project;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "arnav-image-import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            source = Path.Combine(root, "input.png");
            repository = new LocalFileMapRepository(root);
            project = TestMapFactory.CreateTinyThreeRoomMap();
            repository.CreateProject(project);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test]
        public void ValidPngImportsWithExpectedDimensionsAndHash()
        {
            WriteImage(source, false);
            var stored = new MapImageStorageService(repository).ImportAndStore(project, source);
            try
            {
                Assert.AreEqual("png", stored.format);
                Assert.AreEqual(64, stored.width); Assert.AreEqual(48, stored.height);
                Assert.AreEqual(new FileInfo(source).Length, stored.sourceByteLength);
                Assert.AreEqual(stored.sha256, MapImageStorageService.ComputeSha256(stored.absolutePath));
                Assert.IsTrue(File.Exists(stored.absolutePath));
            }
            finally { UnityEngine.Object.DestroyImmediate(stored.texture); }
        }

        [Test]
        public void ValidJpegImportsWithExpectedDimensions()
        {
            WriteImage(source, true);
            var stored = new MapImageStorageService(repository).ImportAndStore(project, source);
            try { Assert.AreEqual("jpg", stored.format); Assert.AreEqual(64, stored.width); Assert.AreEqual(48, stored.height); }
            finally { UnityEngine.Object.DestroyImmediate(stored.texture); }
        }

        [Test]
        public void UnsupportedFormatIsRejectedBeforeDecode()
        {
            File.WriteAllText(source, "not an image");
            var exception = Assert.Throws<NotSupportedException>(() => new MapImageStorageService(repository).ImportAndStore(project, source));
            StringAssert.Contains("PNG or JPEG", exception.Message);
        }

        [Test]
        public void CorruptImageWithPlausiblePngHeaderIsRejectedByDecoder()
        {
            WriteImage(source, false);
            var valid = File.ReadAllBytes(source);
            var truncated = new byte[24]; Array.Copy(valid, truncated, truncated.Length); File.WriteAllBytes(source, truncated);
            var exception = Assert.Throws<InvalidDataException>(() => new MapImageStorageService(repository).ImportAndStore(project, source));
            StringAssert.Contains("cannot be decoded", exception.Message);
            Assert.AreEqual(0, Directory.GetFiles(repository.GetProjectDirectory(project.projectId), ".floorplan-importing-*").Length);
        }

        [Test]
        public void OversizedCompressedFileIsRejectedBeforeReadingAllBytes()
        {
            WriteImage(source, false);
            var limits = new ImageImportLimits { maximumFileBytes = 16, maximumDimension = 8192, maximumPixels = 16L * 1024L * 1024L };
            var exception = Assert.Throws<InvalidDataException>(() => new MapImageStorageService(repository, limits).ImportAndStore(project, source));
            StringAssert.Contains("file exceeds", exception.Message);
        }

        [Test]
        public void ExcessiveDimensionsAreRejectedBeforeTextureDecode()
        {
            WriteImage(source, false);
            var limits = new ImageImportLimits { maximumFileBytes = 1024 * 1024, maximumDimension = 32, maximumPixels = 1024 * 1024 };
            var exception = Assert.Throws<InvalidDataException>(() => new MapImageStorageService(repository, limits).ImportAndStore(project, source));
            StringAssert.Contains("dimension limit", exception.Message);
        }

        [Test]
        public void IncompleteOrInvalidHeadersAreRejectedByPureByteValidation()
        {
            Assert.Throws<InvalidDataException>(() => ImageImportValidation.Validate(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }, ImageImportLimits.Default));
            var badHeader = new byte[24];
            Array.Copy(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }, badHeader, 8);
            badHeader[11] = 13; // PNG IHDR chunk length
            badHeader[16] = 0; badHeader[17] = 0; badHeader[18] = 0; badHeader[19] = 64;
            badHeader[20] = 0; badHeader[21] = 0; badHeader[22] = 0; badHeader[23] = 48;
            Assert.Throws<InvalidDataException>(() => ImageImportValidation.Validate(badHeader, ImageImportLimits.Default));
        }

        [Test]
        public void CancelledPickerResponseDoesNotStartAnImport()
        {
            var result = AndroidImagePickerResultParser.Parse("{\"status\":\"cancelled\"}");
            Assert.IsTrue(result.cancelled); Assert.IsFalse(result.succeeded);
        }

        [Test]
        public void RepeatedImportsUseDistinctFilesAndReturnIndependentlyOwnedTextures()
        {
            WriteImage(source, false);
            var storage = new MapImageStorageService(repository);
            var first = storage.ImportAndStore(project, source);
            project.sourceImage.pathReference = first.relativePath;
            try
            {
                var second = storage.ImportAndStore(project, source);
                try
                {
                    Assert.AreNotEqual(first.relativePath, second.relativePath);
                    Assert.IsTrue(File.Exists(first.absolutePath)); Assert.IsTrue(File.Exists(second.absolutePath));
                    Assert.AreNotSame(first.texture, second.texture);
                }
                finally { UnityEngine.Object.DestroyImmediate(second.texture); }
            }
            finally { UnityEngine.Object.DestroyImmediate(first.texture); }
        }

        [Test]
        public void FailedImportPreservesSavedProjectAndExistingImage()
        {
            WriteImage(source, false);
            var storage = new MapImageStorageService(repository);
            var stored = storage.ImportAndStore(project, source);
            project.sourceImage.pathReference = stored.relativePath;
            project.sourceImage.sha256 = stored.sha256;
            project.sourceImage.originalWidthPixels = stored.width;
            project.sourceImage.originalHeightPixels = stored.height;
            repository.SaveProject(project);
            UnityEngine.Object.DestroyImmediate(stored.texture);
            var mapPath = Path.Combine(repository.GetProjectDirectory(project.projectId), "map.json");
            var savedJson = File.ReadAllText(mapPath);
            var savedImageHash = MapImageStorageService.ComputeSha256(repository.GetProjectImagePath(project.projectId, stored.relativePath));
            File.WriteAllText(source, "broken image");

            Assert.Throws<NotSupportedException>(() => storage.ImportAndStore(project, source));
            Assert.AreEqual(savedJson, File.ReadAllText(mapPath));
            Assert.AreEqual(savedImageHash, MapImageStorageService.ComputeSha256(repository.GetProjectImagePath(project.projectId, stored.relativePath)));
            Assert.AreEqual(stored.relativePath, repository.LoadProject(project.projectId).sourceImage.pathReference);
        }

        [Test]
        public void MissingFileProducesHelpfulError()
        {
            File.Delete(source);
            var exception = Assert.Throws<FileNotFoundException>(() => new MapImageStorageService(repository).ImportAndStore(project, source));
            StringAssert.Contains("Select it again", exception.Message);
        }

        private static void WriteImage(string path, bool jpeg)
        {
            var texture = new Texture2D(64, 48, TextureFormat.RGBA32, false);
            try
            {
                var pixels = new Color32[64 * 48];
                for (var i = 0; i < pixels.Length; i++) pixels[i] = new Color32((byte)(i % 255), (byte)(i % 173), 220, 255);
                texture.SetPixels32(pixels); texture.Apply();
                File.WriteAllBytes(path, jpeg ? texture.EncodeToJPG(90) : texture.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }
    }
}
