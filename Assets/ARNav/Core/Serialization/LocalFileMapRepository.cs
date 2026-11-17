using System;
using System.Collections.Generic;
using System.IO;
using ARNav.Core.Data;
using ARNav.Core.Validation;
using UnityEngine;

namespace ARNav.Core.Serialization
{
    /// <summary>Small filesystem boundary used to make interrupted writes deterministic in tests.</summary>
    public interface IMapFileOperations
    {
        bool FileExists(string path);
        void CreateDirectory(string path);
        string ReadAllText(string path);
        void WriteAndFlush(string path, string contents);
        void Copy(string source, string destination, bool overwrite);
        void Move(string source, string destination);
        void Replace(string source, string destination);
        void Delete(string path);
        string[] GetFiles(string directory, string searchPattern);
    }

    /// <summary>
    /// Stores each project under &lt;root&gt;/ARNav/Maps/&lt;project-id&gt;/map.json.
    /// Saves use same-directory staging, a validated previous-version backup, and
    /// rename/replace promotion. A backup also permits recovery from a process death
    /// between the two renames needed by filesystems without File.Replace support.
    /// </summary>
    public sealed class LocalFileMapRepository : IMapRepository
    {
        private const string PrimaryName = "map.json";
        private const string BackupName = "map.json.bak";
        private const string TemporaryPrefix = "map.json.tmp.";
        private const string RecoveryPrefix = "map.json.recovery.";
        private const string RollbackPrefix = "map.json.rollback.";
        private const string CorruptPrefix = "map.json.corrupt.";

        private readonly string mapsRoot;
        private readonly IMapFileOperations files;

        public LocalFileMapRepository(string rootPath = null) : this(rootPath, new PhysicalMapFileOperations()) { }

        public LocalFileMapRepository(string rootPath, IMapFileOperations fileOperations)
        {
            mapsRoot = Path.Combine(rootPath ?? Application.persistentDataPath, "ARNav", "Maps");
            files = fileOperations ?? throw new ArgumentNullException("fileOperations");
        }

        public void CreateProject(MapProjectData project)
        {
            RequireProject(project);
            var directory = ProjectDirectory(project.projectId);
            if (Directory.Exists(directory)) throw new IOException("Map project already exists: " + project.projectId);
            files.CreateDirectory(directory);
            SaveProject(project);
        }

        public void SaveProject(MapProjectData project)
        {
            RequireProject(project);
            var directory = ProjectDirectory(project.projectId);
            files.CreateDirectory(directory);

            // Validate and serialize before touching the current primary or backup.
            var oldModifiedUtc = project.modifiedUtc;
            project.modifiedUtc = DateTime.UtcNow.ToString("o");
            string json;
            try
            {
                RequireProject(project);
                json = MapJsonSerializer.Serialize(project);
                ValidateJsonForProject(json, project.projectId, "Serialized map");
            }
            catch
            {
                project.modifiedUtc = oldModifiedUtc;
                throw;
            }

            var primary = Path.Combine(directory, PrimaryName);
            var backup = Path.Combine(directory, BackupName);
            var temporary = Path.Combine(directory, TemporaryPrefix + Guid.NewGuid().ToString("N"));
            files.WriteAndFlush(temporary, json);
            try
            {
                // If the primary is damaged, first restore a trustworthy same-ID
                // backup. Never turn damaged bytes into the new known-good backup.
                if (files.FileExists(primary) && !TryReadValid(primary, project.projectId, out _))
                    RecoverPrimary(project.projectId, primary, backup);

                if (files.FileExists(primary))
                {
                    // Keep the old primary in the backup before attempting promotion.
                    files.Copy(primary, backup, true);
                    FlushExistingFile(backup);
                    ValidateJsonForProject(files.ReadAllText(backup), project.projectId, "Map backup");
                    PromoteOverExisting(temporary, primary, backup, directory);
                }
                else
                {
                    // First save (or a previously interrupted promotion): a valid
                    // backup remains available while the same-directory rename runs.
                    files.Move(temporary, primary);
                }
            }
            catch
            {
                TryDelete(temporary);
                throw;
            }

            CleanupTransientFiles(directory);
        }

        public MapProjectData LoadProject(string projectId)
        {
            var directory = ProjectDirectory(projectId);
            var primary = Path.Combine(directory, PrimaryName);
            var backup = Path.Combine(directory, BackupName);
            if (!Directory.Exists(directory)) throw new FileNotFoundException("Map project was not found.", primary);

            if (files.FileExists(primary))
            {
                try
                {
                    var project = ReadValid(primary, projectId, "Saved map");
                    CleanupTransientFiles(directory);
                    return project;
                }
                catch (Exception primaryError)
                {
                    if (!files.FileExists(backup)) throw new InvalidDataException("Saved map is invalid and no backup is available: " + primaryError.Message, primaryError);
                    try
                    {
                        RecoverPrimary(projectId, primary, backup);
                        var recovered = ReadValid(primary, projectId, "Recovered map");
                        CleanupTransientFiles(directory);
                        return recovered;
                    }
                    catch (Exception backupError)
                    {
                        throw new InvalidDataException("Saved map and its backup could not be safely recovered. Original files were preserved where possible. Primary: " + primaryError.Message + " Backup: " + backupError.Message, backupError);
                    }
                }
            }

            if (files.FileExists(backup))
            {
                try
                {
                    RecoverPrimary(projectId, primary, backup);
                    var recovered = ReadValid(primary, projectId, "Recovered map");
                    CleanupTransientFiles(directory);
                    return recovered;
                }
                catch (Exception exception)
                {
                    throw new InvalidDataException("Primary map is missing and its backup could not be safely recovered: " + exception.Message, exception);
                }
            }

            // A staged file is never a recovery candidate. Once it is established
            // that no primary or backup exists, it is safe to discard such remnants.
            CleanupTransientFiles(directory);
            throw new FileNotFoundException("Map project was not found.", primary);
        }

        public void DeleteProject(string projectId)
        {
            var directory = ProjectDirectory(projectId);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        public IReadOnlyList<string> ListProjects()
        {
            var ids = new List<string>();
            if (!Directory.Exists(mapsRoot)) return ids;
            foreach (var directory in Directory.GetDirectories(mapsRoot))
            {
                var id = new DirectoryInfo(directory).Name;
                if (File.Exists(Path.Combine(directory, PrimaryName))) { ids.Add(id); continue; }
                // A crash during fallback promotion can leave only the validated
                // backup at the primary path's sibling. Recover it so the project
                // remains discoverable through the existing project-list flow.
                if (!File.Exists(Path.Combine(directory, BackupName))) continue;
                try { LoadProject(id); ids.Add(id); }
                catch (Exception exception) { Debug.LogWarning("[ARNav][Storage] Skipping unrecoverable map " + id + ": " + exception.Message); }
            }
            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        public string GetProjectDirectory(string projectId) { return ProjectDirectory(projectId); }
        public string GetProjectImagePath(string projectId, string relativePath) { return Path.Combine(ProjectDirectory(projectId), SafeRelativeFileName(relativePath)); }

        private void PromoteOverExisting(string temporary, string primary, string backup, string directory)
        {
            try
            {
                // File.Replace is atomic on supported local filesystems. Its support
                // varies across Unity's Mono/IL2CPP and Android filesystems, so retain
                // the independently validated backup and use rename fallback below.
                files.Replace(temporary, primary);
                return;
            }
            catch (PlatformNotSupportedException) { }
            catch (NotSupportedException) { }
            catch (IOException)
            {
                // IOException also covers unsupported replacement on some runtimes.
                // The fallback is safe because backup has already been copied and read-validated.
            }

            var rollback = Path.Combine(directory, RollbackPrefix + Guid.NewGuid().ToString("N"));
            files.Move(primary, rollback);
            try
            {
                files.Move(temporary, primary);
                TryDelete(rollback);
            }
            catch
            {
                // Restore the old primary when possible; the validated backup remains
                // available if the process dies before this restoration completes.
                if (!files.FileExists(primary) && files.FileExists(rollback))
                {
                    try { files.Move(rollback, primary); }
                    catch (Exception restoreError) { Debug.LogError("[ARNav][Storage] Could not restore map primary; validated backup remains at " + backup + ": " + restoreError.Message); }
                }
                throw;
            }
        }

        private void RecoverPrimary(string projectId, string primary, string backup)
        {
            var backupProject = ReadValid(backup, projectId, "Map backup");
            if (!string.Equals(backupProject.projectId, projectId, StringComparison.Ordinal))
                throw new InvalidDataException("Map backup belongs to a different project.");

            var directory = Path.GetDirectoryName(primary);
            var recovery = Path.Combine(directory, RecoveryPrefix + Guid.NewGuid().ToString("N"));
            files.Copy(backup, recovery, false);
            try
            {
                FlushExistingFile(recovery);
                ValidateJsonForProject(files.ReadAllText(recovery), projectId, "Recovery copy");
                if (files.FileExists(primary))
                {
                    var corrupt = Path.Combine(directory, CorruptPrefix + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "." + Guid.NewGuid().ToString("N"));
                    files.Move(primary, corrupt); // retain corrupt bytes for diagnosis
                }
                files.Move(recovery, primary);
            }
            catch
            {
                TryDelete(recovery);
                throw;
            }
        }

        private MapProjectData ReadValid(string path, string expectedProjectId, string label)
        {
            var json = files.ReadAllText(path);
            return ValidateJsonForProject(json, expectedProjectId, label);
        }

        private bool TryReadValid(string path, string expectedProjectId, out MapProjectData project)
        {
            try { project = ReadValid(path, expectedProjectId, "Saved map"); return true; }
            catch { project = null; return false; }
        }

        private static MapProjectData ValidateJsonForProject(string json, string expectedProjectId, string label)
        {
            MapProjectData project;
            try { project = MapJsonSerializer.Deserialize(json); }
            catch (Exception exception) { throw new InvalidDataException(label + " JSON could not be deserialized.", exception); }
            if (!string.Equals(project.projectId, expectedProjectId, StringComparison.Ordinal))
                throw new InvalidDataException(label + " project ID does not match its directory.");
            var validation = MapValidator.Validate(project);
            if (!validation.IsValid) throw new InvalidDataException(label + " failed validation: " + string.Join(" ", validation.errors.ToArray()));
            return project;
        }

        private void CleanupTransientFiles(string directory)
        {
            // Transient/recovery files are never considered authoritative. Cleanup is
            // best-effort and only occurs after a valid primary has been established.
            foreach (var pattern in new[] { TemporaryPrefix + "*", RecoveryPrefix + "*", RollbackPrefix + "*" })
                foreach (var path in files.GetFiles(directory, pattern)) TryDelete(path);
        }

        private void FlushExistingFile(string path)
        {
            // Re-open a copied backup/recovery file and request a durable flush. Some
            // Android providers/filesystems may not honor physical-media durability.
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)) stream.Flush(true);
        }

        private void TryDelete(string path)
        {
            try { if (files.FileExists(path)) files.Delete(path); }
            catch (Exception exception) { Debug.LogWarning("[ARNav][Storage] Could not clean transient map file " + path + ": " + exception.Message); }
        }

        private string ProjectDirectory(string projectId) { return Path.Combine(mapsRoot, SafeProjectId(projectId)); }
        private static string SafeProjectId(string projectId)
        {
            if (string.IsNullOrWhiteSpace(projectId) || projectId == "." || projectId == ".." || projectId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || projectId.IndexOf('/') >= 0 || projectId.IndexOf('\\') >= 0) throw new ArgumentException("Invalid project ID.", "projectId");
            return projectId;
        }
        private static string SafeRelativeFileName(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || relativePath != Path.GetFileName(relativePath) || relativePath.IndexOf('/') >= 0 || relativePath.IndexOf('\\') >= 0) throw new ArgumentException("Invalid project-relative file name.", "relativePath");
            return relativePath;
        }
        private static void RequireProject(MapProjectData project)
        {
            if (project == null) throw new ArgumentNullException("project");
            SafeProjectId(project.projectId);
            var validation = MapValidator.Validate(project);
            if (!validation.IsValid) throw new ArgumentException("Cannot persist invalid map: " + string.Join(" ", validation.errors.ToArray()), "project");
        }

        private sealed class PhysicalMapFileOperations : IMapFileOperations
        {
            public bool FileExists(string path) { return File.Exists(path); }
            public void CreateDirectory(string path) { Directory.CreateDirectory(path); }
            public string ReadAllText(string path) { return File.ReadAllText(path); }
            public void WriteAndFlush(string path, string contents)
            {
                var bytes = new System.Text.UTF8Encoding(false).GetBytes(contents);
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            }
            public void Copy(string source, string destination, bool overwrite) { File.Copy(source, destination, overwrite); }
            public void Move(string source, string destination) { File.Move(source, destination); }
            public void Replace(string source, string destination) { File.Replace(source, destination, null); }
            public void Delete(string path) { File.Delete(path); }
            public string[] GetFiles(string directory, string searchPattern) { return Directory.GetFiles(directory, searchPattern); }
        }
    }
}
