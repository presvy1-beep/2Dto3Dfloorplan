using System;
using ARNav.Core.Data;

namespace ARNav.Runtime.App
{
    public enum MapSetupState { NoMap, ImageImported, GeometryReady, ScaleCalibrated, GeoCalibrated, MapGenerated, NavigationReady }

    public sealed class MapSession
    {
        public MapProjectData ActiveProject { get; private set; }
        public string CurrentProjectId { get { return ActiveProject == null ? null : ActiveProject.projectId; } }
        public MapSetupState SetupState { get; private set; }
        public bool IsDirty { get; private set; }
        public string SessionId { get; private set; }

        public MapSession() { SessionId = Guid.NewGuid().ToString("N"); SetupState = MapSetupState.NoMap; }
        public void Open(MapProjectData project, MapSetupState state = MapSetupState.NoMap) { ActiveProject = project; SessionId = Guid.NewGuid().ToString("N"); SetupState = project == null ? MapSetupState.NoMap : state; IsDirty = false; }
        public void MarkDirty() { if (ActiveProject != null) IsDirty = true; }
        public void MarkSaved() { IsDirty = false; }
        public void SetSetupState(MapSetupState state) { SetupState = state; MarkDirty(); }
        public void Close() { ActiveProject = null; SetupState = MapSetupState.NoMap; IsDirty = false; }
    }
}
