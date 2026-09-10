using System;

namespace GroundStation.DigitalTwin
{
    /// <summary>WGS84 to the raster's UTM CRS; approximate display-origin metadata is not used.</summary>
    public static class SiteGeoReference
    {
        public static bool TryProject(double latitude, double longitude, string crs, out double east, out double north)
        {
            east = north = 0;
            if (double.IsNaN(latitude) || double.IsNaN(longitude) || latitude < -80 || latitude > 84 || longitude < -180 || longitude > 180) return false;
            if (string.IsNullOrEmpty(crs) || !crs.StartsWith("EPSG:") || !int.TryParse(crs.Substring(5), out int epsg)) return false;
            bool south = epsg >= 32701 && epsg <= 32760;
            int zone = south ? epsg - 32700 : epsg - 32600;
            if (zone < 1 || zone > 60 || (!south && (epsg < 32601 || epsg > 32660))) return false;
            double central = zone * 6 - 183;
            if (Math.Abs(longitude - central) > 6) return false;
            const double a = 6378137, e2 = 0.0066943799901413165, k = 0.9996;
            double lat = latitude * Math.PI / 180, dl = (longitude-central)*Math.PI/180;
            double sin = Math.Sin(lat), cos = Math.Cos(lat), tan = Math.Tan(lat);
            double ep = e2/(1-e2), n = a/Math.Sqrt(1-e2*sin*sin), t=tan*tan, c=ep*cos*cos, A=cos*dl;
            double e4=e2*e2,e6=e4*e2;
            double m=a*((1-e2/4-3*e4/64-5*e6/256)*lat-(3*e2/8+3*e4/32+45*e6/1024)*Math.Sin(2*lat)
                +(15*e4/256+45*e6/1024)*Math.Sin(4*lat)-35*e6/3072*Math.Sin(6*lat));
            east=500000+k*n*(A+(1-t+c)*Math.Pow(A,3)/6+(5-18*t+t*t+72*c-58*ep)*Math.Pow(A,5)/120);
            north=k*(m+n*tan*(A*A/2+(5-t+9*c+4*c*c)*Math.Pow(A,4)/24+(61-58*t+t*t+600*c-330*ep)*Math.Pow(A,6)/720));
            if(south) north+=10000000;
            return !double.IsNaN(east) && !double.IsInfinity(east) && !double.IsNaN(north) && !double.IsInfinity(north);
        }

        public static bool TryUnproject(double east, double north, string crs, out double latitude, out double longitude)
        {
            latitude = longitude = 0;
            if (double.IsNaN(east) || double.IsInfinity(east) || double.IsNaN(north) || double.IsInfinity(north)) return false;
            if (string.IsNullOrEmpty(crs) || !crs.StartsWith("EPSG:") || !int.TryParse(crs.Substring(5), out int epsg)) return false;
            bool south = epsg >= 32701 && epsg <= 32760;
            int zone = south ? epsg - 32700 : epsg - 32600;
            if (zone < 1 || zone > 60 || (!south && (epsg < 32601 || epsg > 32660))) return false;
            if (south) north -= 10000000;
            const double a = 6378137, e2 = 0.0066943799901413165, k = 0.9996;
            double e1 = (1 - Math.Sqrt(1 - e2)) / (1 + Math.Sqrt(1 - e2));
            double mu = (north / k) / (a * (1 - e2 / 4 - 3 * e2 * e2 / 64 - 5 * e2 * e2 * e2 / 256));
            double phi = mu + (3 * e1 / 2 - 27 * Math.Pow(e1, 3) / 32) * Math.Sin(2 * mu)
                + (21 * e1 * e1 / 16 - 55 * Math.Pow(e1, 4) / 32) * Math.Sin(4 * mu)
                + 151 * Math.Pow(e1, 3) / 96 * Math.Sin(6 * mu);
            double ep = e2 / (1 - e2), sin = Math.Sin(phi), cos = Math.Cos(phi), tan = Math.Tan(phi);
            double n = a / Math.Sqrt(1 - e2 * sin * sin), t = tan * tan, c = ep * cos * cos;
            double r = a * (1 - e2) / Math.Pow(1 - e2 * sin * sin, 1.5);
            double d = (east - 500000) / (n * k);
            latitude = (phi - n * tan / r * (d * d / 2 - (5 + 3 * t + 10 * c - 4 * c * c - 9 * ep) * Math.Pow(d, 4) / 24
                + (61 + 90 * t + 298 * c + 45 * t * t - 252 * ep - 3 * c * c) * Math.Pow(d, 6) / 720)) * 180 / Math.PI;
            longitude = (zone * 6 - 183) + (d - (1 + 2 * t + c) * Math.Pow(d, 3) / 6
                + (5 - 2 * c + 28 * t - 3 * c * c + 8 * ep + 24 * t * t) * Math.Pow(d, 5) / 120) / cos * 180 / Math.PI;
            return DigitalTwinMessageValidation.Geo(latitude, longitude);
        }
    }
}
