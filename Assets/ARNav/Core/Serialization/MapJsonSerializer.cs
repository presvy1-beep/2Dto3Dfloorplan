using ARNav.Core.Data;
using UnityEngine;

namespace ARNav.Core.Serialization
{
    public static class MapJsonSerializer
    {
        public static string Serialize(MapProjectData project) { return JsonUtility.ToJson(project, true); }
        public static MapProjectData Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new System.ArgumentException("Map JSON is empty.", "json");
            var project = JsonUtility.FromJson<MapProjectData>(json);
            if (project == null) throw new System.ArgumentException("Map JSON did not contain a project.", "json");
            return project;
        }
    }
}
