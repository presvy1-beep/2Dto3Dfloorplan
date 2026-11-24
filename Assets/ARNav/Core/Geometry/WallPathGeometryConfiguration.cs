namespace ARNav.Core.Geometry
{
    /// <summary>Shared physical limits for serialized wall-path geometry.</summary>
    public static class WallPathGeometryConfiguration
    {
        public const float DefaultWallHeightMeters = 2.7f;
        public const float DefaultBaseThicknessMeters = 0.025f;
        // Source thicknesses below this safety floor remain visible in 3D.
        public const float MinimumWallThicknessMeters = 0.06f;
        public const float MaximumWallThicknessMeters = 0.45f;
    }
}
