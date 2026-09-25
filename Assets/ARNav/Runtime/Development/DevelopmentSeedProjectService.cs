using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ARNav.Core.Data;
using ARNav.Core.Geo;
using ARNav.Core.Geometry;
using ARNav.Core.Serialization;
using ARNav.Runtime.Import;
using UnityEngine;

namespace ARNav.Runtime.Development
{
    /// <summary>Creates the explicit, development-only on-device smoke-test map.</summary>
    public static class DevelopmentSeedProjectService
    {
        public const string ProjectId = "arnav-device-seed";
        public const int Width = 1100, Height = 400;

        public static bool TryCreate(LocalFileMapRepository repository, MapImageStorageService imageStorage, out MapProjectData project, out string error)
        {
            project = null; error = null;
            try
            {
                if (repository.ListProjects().Contains(ProjectId)) repository.DeleteProject(ProjectId);
                project = TestMapFactory.CreateTinyThreeRoomMap(); project.projectId = ProjectId; project.projectName = "DEV Navigation Test Map";
                project.sourceImage = new SourceImageData { fileName = "seed-floorplan.png", originalWidthPixels = Width, originalHeightPixels = Height, format = "png" };
                AddSourceAnnotations(project.floors[0]);
                project.scaleCalibration = new ScaleCalibrationData { firstPixel = new PixelPoint(0, Height), secondPixel = new PixelPoint(100, Height), knownDistanceMetres = 1f, pixelDistance = 100f, metresPerPixel = 0.01f, isValid = true, validationMessage = "Development seed scale" };
                if (!PixelToLocalGeometryConverter.TryRegenerate(project.floors[0], project.scaleCalibration, Width, Height, new MapGenerationDefaults(), out var conversion)) { error = string.Join(" ", conversion.errors.ToArray()); return false; }
                repository.CreateProject(project);
                var temporaryPath = Path.Combine(Application.temporaryCachePath, "arnav-seeded-floorplan.png"); var image = CreateSyntheticFloorPlanImage();
                try { File.WriteAllBytes(temporaryPath, image.EncodeToPNG()); } finally { UnityEngine.Object.Destroy(image); }
                StoredMapImage stored = null;
                try { stored = imageStorage.ImportAndStore(project, temporaryPath); project.sourceImage.pathReference = stored.relativePath; project.sourceImage.originalWidthPixels = stored.width; project.sourceImage.originalHeightPixels = stored.height; project.sourceImage.sha256 = stored.sha256; project.sourceImage.sourceByteLength = stored.sourceByteLength; project.sourceImage.importedUtc = DateTime.UtcNow.ToString("o"); }
                finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                AddSyntheticGeoCalibration(project); repository.SaveProject(project);
                if (stored != null && stored.texture != null) UnityEngine.Object.Destroy(stored.texture);
                return true;
            }
            catch (Exception exception) { project = null; error = exception.Message; Debug.LogException(exception); return false; }
        }

        private static void AddSourceAnnotations(FloorData floor)
        {
            var source = floor.sourceAnnotations ?? new FloorPlanAnnotationData(); floor.sourceAnnotations = source; source.rooms.Clear(); source.walls.Clear(); source.doors.Clear();
            source.rooms.Add(Room("room_a", "Room A", 0, 400)); var corridor = Room("corridor", "Corridor", 400, 700); corridor.regionType = "Corridor"; corridor.destinationSelectable = false; source.rooms.Add(corridor); source.rooms.Add(Room("room_b", "Room B", 700, 1100));
            AddWall(source, "wall_a_corridor", new PixelPoint(400, 0), new PixelPoint(400, Height)); AddWall(source, "wall_corridor_b", new PixelPoint(700, 0), new PixelPoint(700, Height));
            AddWall(source, "wall_outer_bottom", new PixelPoint(0, Height), new PixelPoint(Width, Height)); AddWall(source, "wall_outer_top", new PixelPoint(Width, 0), new PixelPoint(0, 0)); AddWall(source, "wall_outer_left", new PixelPoint(0, 0), new PixelPoint(0, Height)); AddWall(source, "wall_outer_right", new PixelPoint(Width, Height), new PixelPoint(Width, 0));
            source.doors.Add(new DoorAnnotationData { id = "door_a_corridor", wallId = "wall_a_corridor", startT = .375f, endT = .625f, connectedRoomIds = new List<string> { "room_a", "corridor" } });
            source.doors.Add(new DoorAnnotationData { id = "door_corridor_b", wallId = "wall_corridor_b", startT = .375f, endT = .625f, connectedRoomIds = new List<string> { "corridor", "room_b" } });
        }
        private static RoomAnnotationData Room(string id, string name, float left, float right) { return new RoomAnnotationData { id = id, displayName = name, vertices = new List<PixelPoint> { new PixelPoint(left, 0), new PixelPoint(right, 0), new PixelPoint(right, Height), new PixelPoint(left, Height) } }; }
        private static void AddWall(FloorPlanAnnotationData source, string id, PixelPoint start, PixelPoint end) { source.walls.Add(new WallAnnotationData { id = id, start = start, end = end, linkedRoomIds = new List<string>() }); }

        /// <summary>Creates only pixel input for development verification; annotations are still extracted.</summary>
        public static Texture2D CreateSyntheticFloorPlanImage()
        {
            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false); var pixels = new Color32[Width * Height];
            for (var y = 0; y < Height; y++) for (var x = 0; x < Width; x++) pixels[y * Width + x] = x < 400 ? new Color32(224, 240, 248, 255) : x < 700 ? new Color32(238, 238, 220, 255) : new Color32(232, 246, 224, 255);
            DrawHorizontal(pixels, 0, 8, 8, Width - 8); DrawHorizontal(pixels, Height - 8, Height, 8, Width - 8); DrawVertical(pixels, 8, 392, 0, 8); DrawVertical(pixels, 8, 392, Width - 8, Width); DrawVertical(pixels, 8, 148, 395, 405); DrawVertical(pixels, 252, 392, 395, 405); DrawVertical(pixels, 8, 148, 695, 705); DrawVertical(pixels, 252, 392, 695, 705);
            texture.SetPixels32(pixels); texture.Apply(false, false); return texture;
        }
        private static void DrawVertical(Color32[] p, int y0, int y1, int x0, int x1) { var c = new Color32(30, 30, 35, 255); for (var y = y0; y < y1; y++) for (var x = x0; x < x1; x++) p[y * Width + x] = c; }
        private static void DrawHorizontal(Color32[] p, int y0, int y1, int x0, int x1) { var c = new Color32(30, 30, 35, 255); for (var y = y0; y < y1; y++) for (var x = x0; x < x1; x++) p[y * Width + x] = c; }

        private static void AddSyntheticGeoCalibration(MapProjectData project)
        {
            var geo = project.geographicCalibration; geo.anchors.Clear(); var origin = new GeoCoordinate(45.497, -73.578); var provisional = new GeoCalibrationData { geographicOrigin = origin, mapHeadingDegrees = 30, isValid = true }; var transformer = new GeoCoordinateTransformer(provisional); var locals = new[] { new LocalPoint(2, 1), new LocalPoint(5.5f, 2), new LocalPoint(9, 1) };
            for (var i = 0; i < locals.Length; i++) { var local = locals[i]; geo.anchors.Add(new GeoAnchorData { id = "seed_anchor_" + i, label = "Seed anchor " + i, localPosition = local, sourcePixel = PixelToLocalGeometryConverter.ToPixel(local, .01f, Height), hasSourcePixel = true, coordinate = transformer.LocalToGeo(local) }); }
            var solved = GeoCalibrationService.Solve(geo.anchors, GeoCalibrationMethod.MultiAnchorRigid); if (!solved.IsValid) throw new InvalidDataException(solved.Error); GeoCalibrationService.ApplyTo(geo, solved);
        }
    }
}
