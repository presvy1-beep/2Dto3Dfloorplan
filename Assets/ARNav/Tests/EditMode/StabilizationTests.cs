using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Reflection;
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
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        [Test]
        public void BuildingGeometryHashIsStableAcrossAtoBtoAAndWallsAreExtruded()
        {
            var a = TestMapFactory.CreateTinyThreeRoomMap();
            a.floors[0].architecturalWallPaths = new List<LocalWallPathData>
            { new LocalWallPathData { id = 1, widthMetres = .2f, points = new List<LocalPoint>{new LocalPoint(0,0),new LocalPoint(8,0),new LocalPoint(8,6)} } };
            var b = TestMapFactory.CreateTinyThreeRoomMap();
            b.floors[0].architecturalWallPaths = new List<LocalWallPathData>
            { new LocalWallPathData { id = 1, widthMetres = .2f, points = new List<LocalPoint>{new LocalPoint(0,0),new LocalPoint(4,0)} } };
            var owner = new GameObject("AtoBtoAGeometryTest"); var generator = owner.AddComponent<ProceduralBuildingGenerator>();
            try
            {
                Assert.IsTrue(generator.Generate(a, out var errorA), errorA); var hashA1 = MeshHash(generator.GeneratedRoot); var meshCountA = generator.GeneratedRoot.GetComponentsInChildren<MeshFilter>(true).Length;
                Assert.IsTrue(RuntimeGlbExporter.TryExport(generator.GeneratedRoot, out var glb, out var glbError), glbError);
                var glbJsonLength = (int)System.BitConverter.ToUInt32(glb.bytes, 12);
                var glbJson = Encoding.UTF8.GetString(glb.bytes, 20, glbJsonLength);
                StringAssert.Contains("ARNav Plaster Wall", glbJson);
                StringAssert.Contains("ARNav Tiled Floor", glbJson);
                StringAssert.Contains("baseColorFactor", glbJson);
                var wall = generator.GeneratedRoot.GetComponentsInChildren<MeshFilter>(true);
                var wallMesh = System.Array.Find(wall, f => f.name == "VectorWallMass").sharedMesh;
                Assert.That(wallMesh.bounds.max.y - wallMesh.bounds.min.y, Is.EqualTo(2.7f).Within(.001f));
                var floorMesh = System.Array.Find(wall, f => f.name == "FloorMesh").sharedMesh;
                Assert.That(wallMesh.bounds.min.y, Is.EqualTo(floorMesh.bounds.min.y).Within(.001f), "Wall bases must meet floor elevation.");
                Assert.That(floorMesh.bounds.size.x, Is.LessThan(20f), "Floor must use room geometry, not an oversized presentation slab.");
                Assert.IsTrue(generator.Generate(b, out var errorB), errorB);
                Assert.IsTrue(generator.Generate(a, out var errorA2), errorA2); var hashA2 = MeshHash(generator.GeneratedRoot);
                Assert.AreEqual(hashA1, hashA2, "A mesh geometry must survive rebuilding B in between.");
                Assert.AreEqual(meshCountA, generator.GeneratedRoot.GetComponentsInChildren<MeshFilter>(true).Length, "Rebuild must not duplicate generated meshes.");
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        [Test]
        public void RuntimeMaterialsHaveDistinctReusableTexturesAndColors()
        {
            var owner = new GameObject("MaterialTextureTest"); var generator = owner.AddComponent<ProceduralBuildingGenerator>();
            try
            {
                var floor = (Material)typeof(ProceduralBuildingGenerator).GetMethod("GetFloorMaterial", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(generator, null);
                var wall = (Material)typeof(ProceduralBuildingGenerator).GetMethod("GetWallMaterial", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(generator, null);
                var floorMap = floor.HasProperty("_BaseMap") ? floor.GetTexture("_BaseMap") : floor.GetTexture("_MainTex");
                var wallMap = wall.HasProperty("_BaseMap") ? wall.GetTexture("_BaseMap") : wall.GetTexture("_MainTex");
                Assert.IsNotNull(floorMap); Assert.IsNotNull(wallMap); Assert.AreNotSame(floorMap, wallMap);
                Assert.AreEqual(TextureWrapMode.Repeat, floorMap.wrapMode); Assert.AreEqual(TextureWrapMode.Repeat, wallMap.wrapMode);
                Assert.AreEqual("Universal Render Pipeline/Unlit", wall.shader.name, "Wall tint must not depend on scene lighting or the legacy Standard shader.");
                Assert.AreEqual("Universal Render Pipeline/Unlit", floor.shader.name, "Floor tint must use the supported URP shader.");
                var floorColor = floor.HasProperty("_BaseColor") ? floor.GetColor("_BaseColor") : floor.color;
                var wallColor = wall.HasProperty("_BaseColor") ? wall.GetColor("_BaseColor") : wall.color;
                Assert.That(floorColor.r, Is.GreaterThan(wallColor.r));
                Assert.That(wallColor.b, Is.GreaterThan(wallColor.r));
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        [Test]
        public void LegacyAssignedMaterialCannotOverrideGeneratedWallAppearance()
        {
            var standard = Shader.Find("Standard"); Assert.IsNotNull(standard);
            var legacy = new Material(standard) { color = Color.white };
            var owner = new GameObject("LegacyMaterialOverrideTest"); var generator = owner.AddComponent<ProceduralBuildingGenerator>();
            try
            {
                typeof(ProceduralBuildingGenerator).GetField("wallMaterial", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(generator, legacy);
                var generated = (Material)typeof(ProceduralBuildingGenerator).GetMethod("GetWallMaterial", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(generator, null);
                Assert.AreEqual("Universal Render Pipeline/Unlit", generated.shader.name);
                Assert.That(generated.GetColor("_BaseColor").r, Is.EqualTo(.34f).Within(.001f));
                Assert.That(generated.GetColor("_BaseColor").g, Is.EqualTo(.43f).Within(.001f));
                Assert.That(generated.GetColor("_BaseColor").b, Is.EqualTo(.52f).Within(.001f));
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); UnityEngine.Object.DestroyImmediate(legacy); }
        }

        private static string MeshHash(GameObject root)
        {
            var entries = new List<string>();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh; if (mesh == null) continue;
                var text = new StringBuilder(filter.name).Append('|');
                foreach (var v in mesh.vertices) text.Append(v.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(v.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(v.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(';');
                text.Append('|'); foreach (var i in mesh.triangles) text.Append(i).Append(','); entries.Add(text.ToString());
            }
            entries.Sort(StringComparer.Ordinal); using (var sha = SHA256.Create()) return System.Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", entries))));
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
