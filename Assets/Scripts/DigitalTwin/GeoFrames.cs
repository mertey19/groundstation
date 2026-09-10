using System;

namespace GroundStation.DigitalTwin
{
    public enum AltitudeDatum
    {
        Unknown = 0,
        RelativeHome = 1,
        Ellipsoid = 2,
        MeanSeaLevel = 3
    }

    public struct FrameCalibration
    {
        public bool Verified;
        public double OriginLatitude;
        public double OriginLongitude;
        public double EastOffsetM;
        public double NorthOffsetM;
        public double YawDeg;
        public double Scale;
    }

    /// <summary>
    /// WGS84 geographic coordinates and a local east-north (ENU) tangent plane in metres.
    /// Unity world units are not treated as metres. SLAM map frames stay separate until
    /// an operator-verified calibration exists; missing calibration is never identity.
    /// </summary>
    public static class GeoFrames
    {
        public const double Wgs84A = 6378137.0;
        public const double MetresPerDegreeLatitude = 111132.954;
        const double Deg2Rad = Math.PI / 180.0;

        public static bool TryWgs84ToEnu(double originLat, double originLon, double latitude, double longitude,
            out double eastM, out double northM)
        {
            eastM = northM = 0;
            if (!DigitalTwinMessageValidation.Geo(originLat, originLon) || !DigitalTwinMessageValidation.Geo(latitude, longitude))
                return false;
            northM = (latitude - originLat) * MetresPerDegreeLatitude;
            eastM = (longitude - originLon) * MetresPerDegreeLatitude * Math.Cos(originLat * Deg2Rad);
            return DigitalTwinMessageValidation.Finite(eastM) && DigitalTwinMessageValidation.Finite(northM);
        }

        public static bool TryEnuToWgs84(double originLat, double originLon, double eastM, double northM,
            out double latitude, out double longitude)
        {
            latitude = longitude = 0;
            if (!DigitalTwinMessageValidation.Geo(originLat, originLon)
                || !DigitalTwinMessageValidation.Finite(eastM) || !DigitalTwinMessageValidation.Finite(northM))
                return false;
            double cos = Math.Cos(originLat * Deg2Rad);
            if (Math.Abs(cos) < 1e-6) return false;
            latitude = originLat + northM / MetresPerDegreeLatitude;
            longitude = originLon + eastM / (MetresPerDegreeLatitude * cos);
            return DigitalTwinMessageValidation.Geo(latitude, longitude);
        }

        public static bool TryCompareAltitude(double a, AltitudeDatum datumA, double b, AltitudeDatum datumB, out double deltaM)
        {
            deltaM = 0;
            if (!DigitalTwinMessageValidation.Finite(a) || !DigitalTwinMessageValidation.Finite(b)) return false;
            if (datumA == AltitudeDatum.Unknown || datumB == AltitudeDatum.Unknown || datumA != datumB) return false;
            deltaM = a - b;
            return true;
        }

        public static bool TrySlamToEnu(double slamX, double slamY, FrameCalibration calibration, out double eastM, out double northM)
        {
            eastM = northM = 0;
            if (!calibration.Verified || !DigitalTwinMessageValidation.Finite(slamX) || !DigitalTwinMessageValidation.Finite(slamY)
                || !DigitalTwinMessageValidation.Finite(calibration.Scale) || calibration.Scale <= 0)
                return false;
            double yaw = calibration.YawDeg * Deg2Rad;
            double c = Math.Cos(yaw), s = Math.Sin(yaw);
            eastM = calibration.EastOffsetM + calibration.Scale * (c * slamX - s * slamY);
            northM = calibration.NorthOffsetM + calibration.Scale * (s * slamX + c * slamY);
            return DigitalTwinMessageValidation.Finite(eastM) && DigitalTwinMessageValidation.Finite(northM);
        }

        public static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
        {
            double dLat = (lat2 - lat1) * Deg2Rad, dLon = (lon2 - lon1) * Deg2Rad;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(lat1 * Deg2Rad) * Math.Cos(lat2 * Deg2Rad) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return 2 * Wgs84A * Math.Asin(Math.Min(1, Math.Sqrt(a)));
        }
    }
}
