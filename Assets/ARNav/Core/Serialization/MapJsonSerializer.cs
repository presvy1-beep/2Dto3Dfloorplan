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
            // Older builds stored an arbitrary 0.01 m/pixel development scale as
            // valid calibration. Keep its geometry, but expose its estimated status.
            if (project.scaleCalibration != null && project.scaleCalibration.isValid &&
                !project.scaleCalibration.isEstimated &&
                !string.IsNullOrEmpty(project.scaleCalibration.validationMessage) &&
                project.scaleCalibration.validationMessage.StartsWith("Development scale", System.StringComparison.OrdinalIgnoreCase))
                project.scaleCalibration.isEstimated = true;
            return project;
        }
    }
}
