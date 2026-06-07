using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using UnityEngine;

namespace GroundStation.DigitalTwin
{
    /// <summary>
    /// Google Earth / Earth Studio KML icinden kamera yolu: gx:Track (gx:coord) veya LineString coordinates.
    /// gx:coord formati: "lon lat alt" (boslukla); LineString: "lon,lat,alt lon,lat,alt" ...
    /// </summary>
    public static class DigitalTwinKmlCameraPathReader
    {
        public struct KmlPoint
        {
            public double latitude;
            public double longitude;
            public double altitudeM;
        }

        public static bool TryParse(string kmlXml, out List<KmlPoint> points)
        {
            points = new List<KmlPoint>();
            if (string.IsNullOrWhiteSpace(kmlXml))
                return false;

            try
            {
                var doc = new XmlDocument { XmlResolver = null };
                doc.LoadXml(kmlXml.Trim());

                CollectGxCoords(doc.DocumentElement, points);
                if (points.Count == 0)
                    CollectLineStringCoords(doc.DocumentElement, points);

                return points.Count > 0;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DigitalTwinKml] Parse failed: " + e.Message);
                points = new List<KmlPoint>();
                return false;
            }
        }

        private static void CollectGxCoords(XmlNode root, List<KmlPoint> points)
        {
            if (root == null) return;
            if (root.LocalName == "coord" && root.NamespaceURI.IndexOf("google.com/kml", StringComparison.Ordinal) >= 0)
            {
                if (TryParseGxCoordText(root.InnerText, out var p))
                    points.Add(p);
            }

            foreach (XmlNode child in root.ChildNodes)
                CollectGxCoords(child, points);
        }

        private static void CollectLineStringCoords(XmlNode root, List<KmlPoint> points)
        {
            if (root == null) return;
            if (root.LocalName.Equals("LineString", StringComparison.OrdinalIgnoreCase))
            {
                foreach (XmlNode child in root.ChildNodes)
                {
                    if (child.LocalName.Equals("coordinates", StringComparison.OrdinalIgnoreCase))
                    {
                        AppendCoordinatesText(child.InnerText, points);
                        break;
                    }
                }
            }

            foreach (XmlNode child in root.ChildNodes)
                CollectLineStringCoords(child, points);
        }

        private static void AppendCoordinatesText(string inner, List<KmlPoint> points)
        {
            if (string.IsNullOrWhiteSpace(inner)) return;
            var tuples = inner.Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tuples.Length; i++)
            {
                var parts = tuples[i].Split(',');
                if (parts.Length < 2) continue;
                if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon)) continue;
                if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat)) continue;
                double alt = 0;
                if (parts.Length >= 3)
                    double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out alt);
                points.Add(new KmlPoint { longitude = lon, latitude = lat, altitudeM = alt });
            }
        }

        private static bool TryParseGxCoordText(string text, out KmlPoint p)
        {
            p = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var parts = text.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return false;
            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon)) return false;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat)) return false;
            double alt = 0;
            if (parts.Length >= 3)
                double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out alt);
            p = new KmlPoint { longitude = lon, latitude = lat, altitudeM = alt };
            return true;
        }

        /// <summary>
        /// Iki nokta arasinda basit buyuk daire yonu (yaw), derece.
        /// </summary>
        public static float BearingDegreesDeg(double latFrom, double lonFrom, double latTo, double lonTo)
        {
            double f = latFrom * (Math.PI / 180d);
            double t = latTo * (Math.PI / 180d);
            double dLon = (lonTo - lonFrom) * (Math.PI / 180d);
            double y = Math.Sin(dLon) * Math.Cos(t);
            double x = Math.Cos(f) * Math.Sin(t) - Math.Sin(f) * Math.Cos(t) * Math.Cos(dLon);
            double br = Math.Atan2(y, x);
            return (float)(br * (180d / Math.PI));
        }
    }
}
