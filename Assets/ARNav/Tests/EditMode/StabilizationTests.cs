using System.Collections.Generic;
using ARNav.Core.Data;
using ARNav.Core.Geometry;
using ARNav.Core.Generation;
using ARNav.Core.Location;
using ARNav.Core.Navigation;
using ARNav.Core.Serialization;
using ARNav.Runtime.Generation;
using NUnit.Framework;
using UnityEngine;

namespace ARNav.Tests.EditMode
{
    public sealed class StabilizationTests
    {
        [Test]
        public void SavedGeometryAndEstimatedScaleSurviveJsonRoundTrip()
        {
            var map = TestMapFactory.CreateTinyThreeRoomMap();
            map.scaleCalibration = new ScaleCalibrationData { isValid = true, isEstimated = true, metresPerPixel = 0.01f,
                validationMessage = "Estimated preview scale; dimensions are not measured." };
            var loaded = MapJsonSerializer.Deserialize(MapJsonSerializer.Serialize(map));
            Assert.IsTrue(loaded.scaleCalibration.isEstimated);
            Assert.AreEqual(map.floors[0].rooms.Count, loaded.floors[0].rooms.Count);
            for (var room = 0; room < map.floors[0].rooms.Count; room++)
            {
                var original = map.floors[0].rooms[room]; var restored = loaded.floors[0].rooms[room];
                Assert.AreEqual(original.id, restored.id);
                Assert.AreEqual(original.polygon.vertices.Count, restored.polygon.vertices.Count);
                for (var vertex = 0; vertex < original.polygon.vertices.Count; vertex++)
                {
                    Assert.AreEqual(original.polygon.vertices[vertex].x, restored.polygon.vertices[vertex].x);
                    Assert.AreEqual(original.polygon.vertices[vertex].z, restored.polygon.vertices[vertex].z);
                }
            }
        }

        [Test]
        public void LegacyDevelopmentScaleIsMarkedEstimatedWhenLoaded()
        {
            var loaded = MapJsonSerializer.Deserialize("{\"schemaVersion\":1,\"projectId\":\"legacy\",\"scaleCalibration\":{\"isValid\":true,\"metresPerPixel\":0.01,\"validationMessage\":\"Development scale; replace with surveyed calibration.\"}}");
            Assert.IsTrue(loaded.scaleCalibration.isEstimated);
        }

        [Test]
        public void GpsDisplayMarksMissingAltitudeAndStaleFix()
        {
            var reading = new LocationReading { latitude = 12.5, longitude = 77.6, altitudeMetres = double.NaN,
                horizontalAccuracyMetres = 5, verticalAccuracyMetres = 0, timestampUnixSeconds = 100, isValid = true };
            Assert.IsTrue(reading.IsFiniteAndValid(), "Missing altitude must not invalidate a horizontal fix.");
            StringAssert.Contains("Altitude unavailable", LocationDisplayFormatter.Format(reading, 101));
            StringAssert.Contains("GPS stale", LocationDisplayFormatter.Format(reading, 120));
        }

        [Test]
        public void CameraYawWrapsAndKeepsAngleAndZoom()
        {
            var view = new CameraViewState(0.5f, 0.4f);
            view.SetYaw(405f);
            Assert.That(view.yaw, Is.EqualTo(45f).Within(0.0001f));
            Assert.That(view.angle, Is.EqualTo(0.5f));
            Assert.That(view.zoom, Is.EqualTo(0.4f));
        }

        [Test]
        public void RebuildDoesNotCreateRectangularPresentationBaseOrDuplicateRoot()
        {
            var map = TestMapFactory.CreateTinyThreeRoomMap();
            map.floors[0].architecturalWallPaths = new List<LocalWallPathData>
            {
                new LocalWallPathData { id = 1, widthMetres = 0.2f,
                    points = new List<LocalPoint> { new LocalPoint(0, 0), new LocalPoint(8, 0), new LocalPoint(8, 6) } }
            };
            var owner = new GameObject("StabilizationGenerationTest");
            var generator = owner.AddComponent<ProceduralBuildingGenerator>();
            try
            {
                Assert.IsTrue(generator.Generate(map, out var firstError), firstError);
                var previous = generator.GeneratedRoot;
                Assert.IsTrue(generator.Generate(map, out var secondError), secondError);
                Assert.IsTrue(previous == null, "Previous generated hierarchy must be disposed.");
                Assert.IsNotNull(generator.GeneratedRoot);
                foreach (var filter in generator.GeneratedRoot.GetComponentsInChildren<MeshFilter>(true))
                    Assert.AreNotEqual("PresentationBase", filter.gameObject.name);
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [Test]
        public void HoledRoomUsesOccupancyAndNeverSolidPolygonFill()
        {
            var map = TestMapFactory.CreateTinyThreeRoomMap();
            var room = map.floors[0].rooms[0];
            room.holes.Add(new LocalPolygon { vertices = new List<LocalPoint>
            { new LocalPoint(1, 1), new LocalPoint(2, 1), new LocalPoint(2, 2), new LocalPoint(1, 2) } });
            var plan = BuildingGenerationPlanner.Build(map);
            Assert.IsFalse(plan.floors[0].rooms.Exists(value => value.source == room));
            room.rasterRuns.Add(new LocalRasterRun { minX = 0, minZ = 0, maxX = 1, maxZ = 1 });
            plan = BuildingGenerationPlanner.Build(map);
            Assert.IsTrue(plan.floors[0].rooms.Exists(value => value.source == room && value.rasterFallback));
        }

        [Test]
        public void IrregularRoomReportsActualAreaSeparateFromBoundingDimensions()
        {
            var room = new RoomData { id = "L", polygon = new LocalPolygon { vertices = new List<LocalPoint>
            { new LocalPoint(0, 0), new LocalPoint(4, 0), new LocalPoint(4, 1),
              new LocalPoint(1, 1), new LocalPoint(1, 3), new LocalPoint(0, 3) } } };
            Assert.IsTrue(RoomMeasurements.TryFromRoom(room, out var measured));
            Assert.That(measured.boundingLengthMetres, Is.EqualTo(4).Within(0.0001));
            Assert.That(measured.boundingBreadthMetres, Is.EqualTo(3).Within(0.0001));
            Assert.That(measured.areaSquareMetres, Is.EqualTo(6).Within(0.0001));
            Assert.That(measured.centroid.x, Is.EqualTo(1.5f).Within(0.0001));
            Assert.That(measured.centroid.z, Is.EqualTo(1f).Within(0.0001));
        }

        [Test]
        public void RasterPreferredRoomMeasurementsFollowOccupiedRuns()
        {
            var room = new RoomData { id = "raster", preferRasterGeometry = true,
                polygon = new LocalPolygon { vertices = new List<LocalPoint>
                { new LocalPoint(0, 0), new LocalPoint(4, 0), new LocalPoint(4, 3), new LocalPoint(0, 3) } },
                rasterRuns = new List<LocalRasterRun>
                { new LocalRasterRun { minX = 0, maxX = 1, minZ = 0, maxZ = 1 },
                  new LocalRasterRun { minX = 1, maxX = 2, minZ = 0, maxZ = 1 } } };
            Assert.IsTrue(RoomMeasurements.TryFromRoom(room, out var measured));
            Assert.That(measured.areaSquareMetres, Is.EqualTo(2).Within(0.0001));
            Assert.That(measured.boundingLengthMetres, Is.EqualTo(2).Within(0.0001));
            Assert.That(measured.centroid.x, Is.EqualTo(1).Within(0.0001));
            room.rasterRuns.Add(new LocalRasterRun { minX = 0.5f, maxX = 1.5f, minZ = 0, maxZ = 1 });
            Assert.IsFalse(RoomMeasurements.TryFromRoom(room, out _), "Overlapping cells must not double-count area.");
        }
    }
}
