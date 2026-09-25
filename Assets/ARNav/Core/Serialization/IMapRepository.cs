using System.Collections.Generic;
using ARNav.Core.Data;

namespace ARNav.Core.Serialization
{
    public interface IMapRepository
    {
        void CreateProject(MapProjectData project);
        void SaveProject(MapProjectData project);
        MapProjectData LoadProject(string projectId);
        void DeleteProject(string projectId);
        IReadOnlyList<string> ListProjects();
    }
}
