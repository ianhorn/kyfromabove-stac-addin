/*
 * Converts an ArcGIS.Core.Geometry geometry (projected to WGS84 / CRS84) to a
 * GeoJSON geometry string suitable for the STAC 'intersects' parameter.
 * Handles MapPoint (Point), Polyline (LineString), Polygon, and Multipoint.
 */
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using ArcGIS.Core.Geometry;

namespace KyFromAbove.Services
{
    public static class GeoJsonConverter
    {
        /// <summary>
        /// Convert a geometry to a GeoJSON geometry JSON string.
        /// The geometry should already be projected to WGS84 (lon/lat).
        /// </summary>
        public static string ToGeoJsonGeometry(Geometry geometry)
        {
            if (geometry == null) return null;
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms))
            {
                WriteGeometry(writer, geometry);
                writer.Flush();
            }
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private static void WriteGeometry(Utf8JsonWriter w, Geometry g)
        {
            switch (g)
            {
                case MapPoint p:
                    w.WriteStartObject();
                    w.WriteString("type", "Point");
                    w.WritePropertyName("coordinates");
                    w.WriteStartArray();
                    w.WriteNumberValue(p.X);
                    w.WriteNumberValue(p.Y);
                    w.WriteEndArray();
                    w.WriteEndObject();
                    break;

                case Polygon poly:
                    w.WriteStartObject();
                    w.WriteString("type", "Polygon");
                    w.WritePropertyName("coordinates");
                    w.WriteStartArray();
                    WriteRing(w, poly.Copy2DCoordinatesToList(), closeRing: true);
                    w.WriteEndArray();
                    w.WriteEndObject();
                    break;

                case Polyline line:
                    w.WriteStartObject();
                    w.WriteString("type", "LineString");
                    w.WritePropertyName("coordinates");
                    w.WriteStartArray();
                    // LineString coordinates is a FLAT array of positions: [[x,y],[x,y]]
                    WritePositions(w, line.Copy2DCoordinatesToList(), closeRing: false);
                    w.WriteEndArray();
                    w.WriteEndObject();
                    break;

                case Multipoint mp:
                    w.WriteStartObject();
                    w.WriteString("type", "MultiPoint");
                    w.WritePropertyName("coordinates");
                    w.WriteStartArray();
                    // MultiPoint coordinates is a FLAT array of positions: [[x,y],[x,y]]
                    WritePositions(w, mp.Copy2DCoordinatesToList(), closeRing: false);
                    w.WriteEndArray();
                    w.WriteEndObject();
                    break;
            }
        }

        /// <summary>Write positions flat into the current array context: [x,y], [x,y], ...</summary>
        private static void WritePositions(Utf8JsonWriter w, IReadOnlyList<Coordinate2D> coords, bool closeRing)
        {
            foreach (var c in coords)
            {
                w.WriteStartArray();
                w.WriteNumberValue(c.X);
                w.WriteNumberValue(c.Y);
                w.WriteEndArray();
            }
            // GeoJSON polygon rings must be closed (first point repeated as last).
            if (closeRing && coords.Count > 0)
            {
                w.WriteStartArray();
                w.WriteNumberValue(coords[0].X);
                w.WriteNumberValue(coords[0].Y);
                w.WriteEndArray();
            }
        }

        /// <summary>Write a single ring = [ [x,y], [x,y], ... ] (used for Polygon).</summary>
        private static void WriteRing(Utf8JsonWriter w, IReadOnlyList<Coordinate2D> coords, bool closeRing)
        {
            w.WriteStartArray();
            WritePositions(w, coords, closeRing);
            w.WriteEndArray();
        }
    }
}
