using System;
using System.Collections.Generic;
using System.Linq;
using ARNav.Core.Data;

namespace ARNav.Core.Geo
{
    public sealed class GeoCalibrationQualitySettings
    {
        public double excellentRmsMetres = 0.5, goodRmsMetres = 2, fairRmsMetres = 5;
        public double excellentScaleMismatchPercent = 1, goodScaleMismatchPercent = 3, fairScaleMismatchPercent = 8;
    }

    [Serializable]
    public sealed class GeoCalibrationSolveResult
    {
        public bool IsValid; public string Error; public GeoCalibrationMethod Method; public GeoCoordinate ReferenceCoordinate;
        public string ReferenceAnchorId; public double HeadingDegrees; public double TranslationEastMetres; public double TranslationNorthMetres;
        public double RmsErrorMetres; public double MaxErrorMetres; public double ImpliedScaleRatio = 1; public double ScaleMismatchPercent;
        public GeoCalibrationQuality Quality; public readonly Dictionary<string, double> Residuals = new Dictionary<string, double>();
    }

    /// <summary>Solves fixed-scale rigid local-map to geographic East/North transforms.</summary>
    public static class GeoCalibrationService
    {
        public static GeoCalibrationSolveResult Solve(IList<GeoAnchorData> input, GeoCalibrationMethod method, double headingDegrees = 0, GeoCalibrationQualitySettings settings = null)
        {
            var result = new GeoCalibrationSolveResult(); settings = settings ?? new GeoCalibrationQualitySettings();
            var anchors = input == null ? new List<GeoAnchorData>() : input.Where(a => a != null).OrderBy(a => a.id, StringComparer.Ordinal).ToList();
            if (anchors.Count == 0) return Fail(result, "At least one geographic anchor is required.");
            if (method != GeoCalibrationMethod.OneAnchorHeading && method != GeoCalibrationMethod.TwoAnchors && method != GeoCalibrationMethod.MultiAnchorRigid) return Fail(result, "A geographic calibration method is required.");
            foreach (var anchor in anchors) if (string.IsNullOrWhiteSpace(anchor.id) || !GeoProjectionService.IsValid(anchor.coordinate)) return Fail(result, "Anchor IDs and coordinates must be valid.");
            var ids = new HashSet<string>(); foreach (var anchor in anchors) if (!ids.Add(anchor.id)) return Fail(result, "Anchor IDs must be unique.");
            if (method == GeoCalibrationMethod.OneAnchorHeading && anchors.Count != 1) return Fail(result, "One-anchor calibration requires exactly one anchor.");
            if (method == GeoCalibrationMethod.TwoAnchors && anchors.Count != 2) return Fail(result, "Two-anchor calibration requires exactly two anchors.");
            if (method == GeoCalibrationMethod.MultiAnchorRigid && anchors.Count < 3) return Fail(result, "Multi-anchor calibration requires at least three anchors.");
            var reference = anchors[0].coordinate; var geo = new EastNorthPoint[anchors.Count]; var local = new EastNorthPoint[anchors.Count];
            for (var i = 0; i < anchors.Count; i++) { geo[i] = GeoProjectionService.ToEastNorth(anchors[i].coordinate, reference); local[i] = new EastNorthPoint(anchors[i].localPosition.x, anchors[i].localPosition.z); }
            for (var i = 0; i < anchors.Count; i++) for (var j = i + 1; j < anchors.Count; j++) if (Length(local[i].east - local[j].east, local[i].north - local[j].north) < 0.001) return Fail(result, "Anchor local positions must not be identical.");
            if (method == GeoCalibrationMethod.TwoAnchors)
            {
                var dlx = local[1].east - local[0].east; var dln = local[1].north - local[0].north; var dge = geo[1].east - geo[0].east; var dgn = geo[1].north - geo[0].north;
                if (Length(dlx, dln) < 0.001 || Length(dge, dgn) < 0.001) return Fail(result, "Anchor separation is too small.");
                headingDegrees = GeoProjectionService.RadiansToDegrees(Math.Atan2(dge, dgn) - Math.Atan2(dlx, dln));
            }
            else if (method == GeoCalibrationMethod.MultiAnchorRigid)
            {
                var lc = Centroid(local); var gc = Centroid(geo); double c = 0, s = 0;
                for (var i = 0; i < anchors.Count; i++) { var x = local[i].east - lc.east; var z = local[i].north - lc.north; var e = geo[i].east - gc.east; var n = geo[i].north - gc.north; c += x * e + z * n; s += z * e - x * n; }
                if (Length(c, s) < 0.000001) return Fail(result, "Anchors do not provide enough geometric information.");
                headingDegrees = GeoProjectionService.RadiansToDegrees(Math.Atan2(s, c));
            }
            headingDegrees = NormalizeHeading(headingDegrees);
            var localCentroid = Centroid(local); var geoCentroid = Centroid(geo); var transformedCentroid = Transform(localCentroid, headingDegrees);
            var translation = new EastNorthPoint(geoCentroid.east - transformedCentroid.east, geoCentroid.north - transformedCentroid.north);
            double sum = 0, max = 0, dot = 0, localSquares = 0;
            var centerLocal = localCentroid; var centerGeo = geoCentroid;
            for (var i = 0; i < anchors.Count; i++)
            {
                var predicted = Add(Transform(local[i], headingDegrees), translation); var de = predicted.east - geo[i].east; var dn = predicted.north - geo[i].north; var error = Length(de, dn); result.Residuals[anchors[i].id] = error; sum += error * error; if (error > max) max = error;
                var x = local[i].east - centerLocal.east; var z = local[i].north - centerLocal.north; var e = geo[i].east - centerGeo.east; var n = geo[i].north - centerGeo.north; dot += Transform(new EastNorthPoint(x, z), headingDegrees).east * e + Transform(new EastNorthPoint(x, z), headingDegrees).north * n; localSquares += x * x + z * z;
            }
            var ratio = localSquares < 0.000001 ? 1 : dot / localSquares;
            if (!Finite(ratio) || ratio <= 0) ratio = 1;
            result.IsValid = true; result.Method = method; result.ReferenceCoordinate = reference; result.ReferenceAnchorId = anchors[0].id; result.HeadingDegrees = headingDegrees; result.TranslationEastMetres = translation.east; result.TranslationNorthMetres = translation.north; result.RmsErrorMetres = Math.Sqrt(sum / anchors.Count); result.MaxErrorMetres = max; result.ImpliedScaleRatio = ratio; result.ScaleMismatchPercent = (ratio - 1) * 100; result.Quality = Quality(result.RmsErrorMetres, Math.Abs(result.ScaleMismatchPercent), anchors.Count, settings); return result;
        }

        public static void ApplyTo(GeoCalibrationData target, GeoCalibrationSolveResult result)
        {
            if (target == null || result == null || !result.IsValid) return;
            target.method = result.Method.ToString(); target.mapHeadingDegrees = result.HeadingDegrees; target.rotationDegrees = (float)result.HeadingDegrees; target.translationEastMetres = result.TranslationEastMetres; target.translationNorthMetres = result.TranslationNorthMetres; target.geographicOrigin = result.ReferenceCoordinate; target.localOrigin = new ARNav.Core.Geometry.LocalPoint(0, 0); target.anchorCount = result.Residuals.Count; target.referenceAnchorId = result.ReferenceAnchorId; target.rmsErrorMetres = (float)result.RmsErrorMetres; target.maxErrorMetres = result.MaxErrorMetres; target.impliedScaleRatio = result.ImpliedScaleRatio; target.scaleMismatchPercent = result.ScaleMismatchPercent; target.quality = result.Quality.ToString(); target.validationMessage = "Valid"; target.confidence = result.Quality == GeoCalibrationQuality.Excellent ? 1f : result.Quality == GeoCalibrationQuality.Good ? 0.8f : result.Quality == GeoCalibrationQuality.Fair ? 0.55f : 0.25f; target.isValid = true;
        }

        public static EastNorthPoint Transform(EastNorthPoint local, double headingDegrees) { var r = GeoProjectionService.DegreesToRadians(headingDegrees); var c = Math.Cos(r); var s = Math.Sin(r); return new EastNorthPoint(local.east * c + local.north * s, -local.east * s + local.north * c); }
        public static double NormalizeHeading(double value) { value %= 360; return value < 0 ? value + 360 : value; }
        private static EastNorthPoint Add(EastNorthPoint a, EastNorthPoint b) { return new EastNorthPoint(a.east + b.east, a.north + b.north); }
        private static EastNorthPoint Centroid(EastNorthPoint[] points) { double e = 0, n = 0; foreach (var p in points) { e += p.east; n += p.north; } return new EastNorthPoint(e / points.Length, n / points.Length); }
        private static double Length(double a, double b) { return Math.Sqrt(a * a + b * b); }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static GeoCalibrationQuality Quality(double rms, double mismatch, int count, GeoCalibrationQualitySettings s) { if (count < 2) return GeoCalibrationQuality.Fair; if (rms <= s.excellentRmsMetres && mismatch <= s.excellentScaleMismatchPercent && count >= 3) return GeoCalibrationQuality.Excellent; if (rms <= s.goodRmsMetres && mismatch <= s.goodScaleMismatchPercent) return GeoCalibrationQuality.Good; if (rms <= s.fairRmsMetres && mismatch <= s.fairScaleMismatchPercent) return GeoCalibrationQuality.Fair; return GeoCalibrationQuality.Poor; }
        private static GeoCalibrationSolveResult Fail(GeoCalibrationSolveResult result, string error) { result.Error = error; return result; }
    }
}
