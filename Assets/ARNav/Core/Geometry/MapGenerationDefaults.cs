namespace ARNav.Core.Geometry
{
    /// <summary>Physical defaults used when source pixels do not describe a physical thickness or height.</summary>
    public sealed class MapGenerationDefaults
    {
        public float defaultRoomHeightMetres = 3f;
        // Walls are rendered as a low-relief plan, not full architectural height.
        public float defaultWallHeightMetres = WallPathGeometryConfiguration.DefaultWallHeightMeters;
        public float defaultWallThicknessMetres = 0.15f;
        public float defaultBaseThicknessMetres = WallPathGeometryConfiguration.DefaultBaseThicknessMeters;
        public float defaultDoorHeightMetres = 2.1f;
        public float floorUvScaleMetres = 1f;
    }
}
