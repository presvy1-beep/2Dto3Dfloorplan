namespace ARNav.Core.Geometry
{
    /// <summary>Shared physical limits for serialized wall-path geometry.</summary>
    public static class WallPathGeometryConfiguration
    {
        public const float DefaultWallHeightMeters = 0.35f;
        public const float DefaultBaseThicknessMeters = 0.025f;
        // Small safety floor for low-relief wall presentation. Source thicknesses
        // above this value remain source-derived.
        public const float MinimumWallThicknessMeters = 0.06f;
        public const float MaximumWallThicknessMeters = 0.45f;
    }
}
