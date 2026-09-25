using UnityEngine;

namespace ARNav.Runtime.Navigation
{
    public sealed class NavigationWaypoint : MonoBehaviour
    {
        public int index;
        public string routeId;
        public string roomId;
        public string roomNumber;
        public float arrivalRadiusMetres;
        public bool isDestination;
    }
}
