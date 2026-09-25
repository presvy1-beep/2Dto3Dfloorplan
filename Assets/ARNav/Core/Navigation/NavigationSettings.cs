namespace ARNav.Core.Navigation
{
    /// <summary>Centralized runtime navigation heuristics for a pedestrian agent.</summary>
    public sealed class NavigationSettings
    {
        public float agentRadiusMetres = 0.3f;
        public float agentHeightMetres = 1.8f;
        public float maxSlopeDegrees = 45f;
        public float stepHeightMetres = 0.4f;
        public float waypointSpacingMetres = 1.5f;
        public float waypointArrivalRadiusMetres = 0.75f;
        public float destinationArrivalRadiusMetres = 1f;
        public float navMeshSampleRadiusMetres = 1.5f;
        public float startSampleRadiusMetres = 2f;
        public float offRouteThresholdMetres = 3f;
        public float rerouteMinimumMovementMetres = 2f;
        public float gpsMaximumAccuracyMetres = 15f;
    }
}
