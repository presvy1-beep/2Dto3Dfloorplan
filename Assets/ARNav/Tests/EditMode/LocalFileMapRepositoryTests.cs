using System;
using System.IO;
using ARNav.Core.Data;
using ARNav.Core.Serialization;
using ARNav.Core.Validation;
using NUnit.Framework;

namespace ARNav.Tests.EditMode
{
    public sealed class LocalFileMapRepositoryTests
    {
        private string root;
        private string projectDirectory;
        private string primaryPath;
        private string backupPath;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "arnav-safe-save-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test]
        public void NewProjectCanBeSavedAndLoadedInExistingSchema()
        {
            var repository = new LocalFileMapRepository(root);
            var map = TestMapFactory.CreateTinyThreeRoomMap();
            SetProjectPaths(map);
            Assert.AreEqual(1, map.schemaVersion);
            repository.CreateProject(map);
            var loaded = repository.LoadProject(map.projectId);
            Assert.AreEqual(map.projectId, loaded.projectId);
            Assert.AreEqual(3, loaded.floors[0].rooms.Count);
            StringAssert.Contains("\"schemaVersion\": 1", File.ReadAllText(primaryPath));
        }

        [Test]
        public void UpdatingAndRepeatedSavesKeepLatestProjectLoadable()
        {
            var repository = CreateRepositoryWithSavedMap(out var map);
            for (var i = 0; i < 4; i++) { map.projectName = "Revision " + i; repository.SaveProject(map); }
            Assert.AreEqual("Revision 3", repository.LoadProject(map.projectId).projectName);
            Assert.IsTrue(File.Exists(backupPath));
        }

        [Test]
        public void SerializerRoundTripAndRepositoryRoundTripPreserveExistingFields()
        {
            var map = TestMapFactory.CreateTinyThreeRoomMap();
            var json = MapJsonSerializer.Serialize(map);
            var fromJson = MapJsonSerializer.Deserialize(json);
            Assert.AreEqual(map.floors[0].doors[0].parentWallId, fromJson.floors[0].doors[0].parentWallId);
            Assert.AreEqual(map.floors[0].navigation.edges[1].distanceMetres, fromJson.floors[0].navigation.edges[1].distanceMetres);
            var repository = new LocalFileMapRepository(root); repository.CreateProject(map);
            var loaded = repository.LoadProject(map.projectId);
            Assert.AreEqual(map.schemaVersion, loaded.schemaVersion);
            Assert.AreEqual(map.floors[0].rooms[0].polygon.vertices[2].x, loaded.floors[0].rooms[0].polygon.vertices[2].x);
        }

        [Test]
        public void InvalidProjectIsRejectedBeforeExistingFilesChange()
        {
            var repository = CreateRepositoryWithSavedMap(out var map);
            var oldPrimary = File.ReadAllBytes(primaryPath);
            map.floors[0].doors[0].parentWallId = "missing-wall";
            Assert.Throws<ArgumentException>(() => repository.SaveProject(map));
            CollectionAssert.AreEqual(oldPrimary, File.ReadAllBytes(primaryPath));
        }

        [Test]
        public void CorruptPrimaryRecoversValidatedSameProjectBackupAndPreservesCorruptBytes()
        {
            var repository = CreateRepositoryWithSavedMap(out var map);
            map.projectName = "Known good backup"; repository.SaveProject(map);
            map.projectName = "Newer primary"; repository.SaveProject(map);
            var expectedBackup = MapJsonSerializer.Deserialize(File.ReadAllText(backupPath)).projectName;
            var corrupt = "{ broken primary"; File.WriteAllText(primaryPath, corrupt);
            var loaded = repository.LoadProject(map.projectId);
            Assert.AreEqual(expectedBackup, loaded.projectName);
            Assert.AreEqual("Known good backup", loaded.projectName);
            Assert.AreEqual(corrupt, ReadOnlyCorruptFile());
            Assert.IsTrue(File.Exists(primaryPath));

            string ReadOnlyCorruptFile()
            {
                foreach (var path in Directory.GetFiles(projectDirectory, "map.json.corrupt.*")) return File.ReadAllText(path);
                Assert.Fail("Corrupt primary was not retained for diagnosis."); return null;
            }
        }

        [Test]
        public void InvalidBackupDoesNotReplaceOrDeleteCorruptPrimary()
        {
            var repository = CreateRepositoryWithSavedMap(out var map);
            File.WriteAllText(primaryPath, "corrupt primary");
            File.WriteAllText(backupPath, "corrupt backup");
            Assert.Throws<InvalidDataException>(() => repository.LoadProject(map.projectId));
            Assert.AreEqual("corrupt primary", File.ReadAllText(primaryPath));
            Assert.AreEqual("corrupt backup", File.ReadAllText(backupPath));
        }

        [Test]
        public void StaleTemporaryFilesAreIgnoredAndCleanedAfterPrimaryLoads()
        {
            var repository = CreateRepositoryWithSavedMap(out var map);
            var stale = Path.Combine(projectDirectory, "map.json.tmp.stale");
            File.WriteAllText(stale, MapJsonSerializer.Serialize(TestMapFactory.CreateTinyThreeRoomMap()));
            Assert.AreEqual(map.projectId, repository.LoadProject(map.projectId).projectId);
            Assert.IsFalse(File.Exists(stale));
        }

        [Test]
        public void InterruptedTemporaryWriteLeavesOldPrimaryIntactAndLoadable()
        {
            var repository = CreateRepositoryWithSavedMap(out var map);
            var oldJson = File.ReadAllText(primaryPath);
            map.projectName = "Not committed";
            var failing = new LocalFileMapRepository(root, new FailingWriteOperations());
            Assert.Throws<IOException>(() => failing.SaveProject(map));
            Assert.AreEqual(oldJson, File.ReadAllText(primaryPath));
            var loaded = repository.LoadProject(map.projectId);
            Assert.AreNotEqual("Not committed", loaded.projectName);
            Assert.AreEqual(loaded.projectId, map.projectId);
        }

        [Test]
        public void ProjectIdentityIsCheckedOnLoadAndSave()
        {
            var repository = CreateRepositoryWithSavedMap(out var map);
            Assert.Throws<ArgumentException>(() => repository.GetProjectDirectory("../escape"));
            var other = TestMapFactory.CreateTinyThreeRoomMap(); other.projectId = "another-project";
            File.WriteAllText(primaryPath, MapJsonSerializer.Serialize(other));
            Assert.Throws<InvalidDataException>(() => repository.LoadProject(map.projectId));
            Assert.AreEqual(other.projectId, MapJsonSerializer.Deserialize(File.ReadAllText(primaryPath)).projectId);
        }

        [Test]
        public void BackupFromDifferentProjectIsNotUsedForRecovery()
        {
            var repository = CreateRepositoryWithSavedMap(out var map);
            File.WriteAllText(primaryPath, "bad primary");
            var other = TestMapFactory.CreateTinyThreeRoomMap(); other.projectId = "other-map";
            File.WriteAllText(backupPath, MapJsonSerializer.Serialize(other));
            Assert.Throws<InvalidDataException>(() => repository.LoadProject(map.projectId));
            Assert.AreEqual("bad primary", File.ReadAllText(primaryPath));
        }

        private LocalFileMapRepository CreateRepositoryWithSavedMap(out MapProjectData map)
        {
            var repository = new LocalFileMapRepository(root);
            map = TestMapFactory.CreateTinyThreeRoomMap();
            SetProjectPaths(map);
            repository.CreateProject(map);
            return repository;
        }

        private void SetProjectPaths(MapProjectData map)
        {
            projectDirectory = Path.Combine(root, "ARNav", "Maps", map.projectId);
            primaryPath = Path.Combine(projectDirectory, "map.json");
            backupPath = primaryPath + ".bak";
        }

        private sealed class FailingWriteOperations : IMapFileOperations
        {
            private readonly IMapFileOperations inner = new LocalFileMapRepositoryPhysicalOperations();
            public bool FileExists(string path) { return inner.FileExists(path); }
            public void CreateDirectory(string path) { inner.CreateDirectory(path); }
            public string ReadAllText(string path) { return inner.ReadAllText(path); }
            public void WriteAndFlush(string path, string contents)
            {
                File.WriteAllText(path, "interrupted partial");
                throw new IOException("Simulated interrupted write.");
            }
            public void Copy(string source, string destination, bool overwrite) { inner.Copy(source, destination, overwrite); }
            public void Move(string source, string destination) { inner.Move(source, destination); }
            public void Replace(string source, string destination) { inner.Replace(source, destination); }
            public void Delete(string path) { inner.Delete(path); }
            public string[] GetFiles(string directory, string searchPattern) { return inner.GetFiles(directory, searchPattern); }
        }

        // Uses ordinary System.IO without exposing the repository's private adapter.
        private sealed class LocalFileMapRepositoryPhysicalOperations : IMapFileOperations
        {
            public bool FileExists(string path) { return File.Exists(path); }
            public void CreateDirectory(string path) { Directory.CreateDirectory(path); }
            public string ReadAllText(string path) { return File.ReadAllText(path); }
            public void WriteAndFlush(string path, string contents) { File.WriteAllText(path, contents); }
            public void Copy(string source, string destination, bool overwrite) { File.Copy(source, destination, overwrite); }
            public void Move(string source, string destination) { File.Move(source, destination); }
            public void Replace(string source, string destination) { File.Replace(source, destination, null); }
            public void Delete(string path) { File.Delete(path); }
            public string[] GetFiles(string directory, string searchPattern) { return Directory.GetFiles(directory, searchPattern); }
        }
    }
}
