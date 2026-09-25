using System;
using System.Collections.Generic;
using System.IO;
using ARNav.Core.Data;
using ARNav.Core.Validation;
using UnityEngine;

namespace ARNav.Core.Serialization
{
    /// Stores each project under <root>/ARNav/Maps/<project-id>/map.json.
    public sealed class LocalFileMapRepository : IMapRepository
    {
        private readonly string mapsRoot;
        public LocalFileMapRepository(string rootPath = null)
        { mapsRoot = Path.Combine(rootPath ?? Application.persistentDataPath, "ARNav", "Maps"); }

        public void CreateProject(MapProjectData project)
        {
            RequireProject(project);
            var directory = ProjectDirectory(project.projectId);
            if (Directory.Exists(directory)) throw new IOException("Map project already exists: " + project.projectId);
            Directory.CreateDirectory(directory);
            SaveProject(project);
        }

        public void SaveProject(MapProjectData project)
        {
            RequireProject(project);
            var directory = ProjectDirectory(project.projectId);
            Directory.CreateDirectory(directory);
            project.modifiedUtc = DateTime.UtcNow.ToString("o");
            File.WriteAllText(Path.Combine(directory, "map.json"), MapJsonSerializer.Serialize(project));
        }

        public MapProjectData LoadProject(string projectId)
        {
            var path = Path.Combine(ProjectDirectory(projectId), "map.json");
            if (!File.Exists(path)) throw new FileNotFoundException("Map project was not found.", path);
            return MapJsonSerializer.Deserialize(File.ReadAllText(path));
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
            foreach (var directory in Directory.GetDirectories(mapsRoot)) if (File.Exists(Path.Combine(directory, "map.json"))) ids.Add(new DirectoryInfo(directory).Name);
            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        public string GetProjectDirectory(string projectId) { return ProjectDirectory(projectId); }
        public string GetProjectImagePath(string projectId, string relativePath) { return Path.Combine(ProjectDirectory(projectId), SafeRelativeFileName(relativePath)); }

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
    }
}
