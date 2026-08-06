/*
 * Loads an AOI geometry from a local file (Shapefile or GeoJSON) so the search AOI can be
 * set without an active map view -- e.g. before a project/map has been opened. Multiple
 * features/parts are unioned into a single geometry, same as the "Use Layer" AOI source.
 *
 * Must be called on the MCT thread (inside QueuedTask.Run) -- both the ArcGIS.Core.Data
 * shapefile access and the geometry builders below expect that.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;

namespace KyFromAboveSTAC.Services
{
    public static class AoiImportService
    {
        /// <summary>File types this importer can read, for use in an OpenFileDialog filter.</summary>
        public const string OpenDialogFilter = "AOI files (*.shp;*.geojson;*.json)|*.shp;*.geojson;*.json|Shapefile (*.shp)|*.shp|GeoJSON (*.geojson;*.json)|*.geojson;*.json|All files (*.*)|*.*";

        public static bool IsSupportedFile(string path)
        {
            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            return ext == ".shp" || ext == ".geojson" || ext == ".json";
        }

        /// <summary>
        /// Load a single AOI geometry (projected to WGS84) from a .shp, .geojson, or .json file.
        /// Returns null if the file has no usable geometry.
        /// </summary>
        public static Geometry LoadGeometry(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                throw new FileNotFoundException("AOI file not found.", filePath);

            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext switch
            {
                ".shp" => LoadFromShapefile(filePath),
                ".geojson" => LoadFromGeoJson(filePath),
                ".json" => LoadFromGeoJson(filePath),
                _ => throw new NotSupportedException($"Unsupported AOI file type: {ext}. Use .shp or .geojson/.json.")
            };
        }

        private static Geometry LoadFromShapefile(string filePath)
        {
            var folder = Path.GetDirectoryName(filePath);
            var name = Path.GetFileNameWithoutExtension(filePath);

            var connectionPath = new FileSystemConnectionPath(new Uri(folder), FileSystemDatastoreType.Shapefile);
            using var datastore = new FileSystemDatastore(connectionPath);
            using var fc = datastore.OpenDataset<FeatureClass>(name);

            var geoms = new List<Geometry>();
            using (var cursor = fc.Search())
            {
                while (cursor.MoveNext())
                {
                    using var feature = (Feature)cursor.Current;
                    var shp = feature.GetShape();
                    if (shp != null && !shp.IsEmpty) geoms.Add(shp);
                    if (geoms.Count >= 1000) break; // safety cap, same as the "Use Layer" AOI source
                }
            }
            if (geoms.Count == 0) return null;

            var combined = geoms.Count == 1 ? geoms[0] : GeometryEngine.Instance.Union(geoms);
            var sr = combined.SpatialReference;
            return (sr != null && sr.Wkid != SpatialReferences.WGS84.Wkid)
                ? GeometryEngine.Instance.Project(combined, SpatialReferences.WGS84)
                : combined;
        }

        private static Geometry LoadFromGeoJson(string filePath)
        {
            var json = File.ReadAllText(filePath);
            var root = JsonNode.Parse(json);
            if (root == null) return null;

            var geomNodes = new List<JsonNode>();
            CollectGeometryNodes(root, geomNodes);

            // GeoJSON coordinates are always WGS84 lon/lat per the spec -- no projection needed.
            var geoms = geomNodes.Select(BuildGeometryFromNode).Where(g => g != null).ToList();
            if (geoms.Count == 0) return null;
            return geoms.Count == 1 ? geoms[0] : GeometryEngine.Instance.Union(geoms);
        }

        private static void CollectGeometryNodes(JsonNode node, List<JsonNode> outList)
        {
            var type = node?["type"]?.GetValue<string>();
            switch (type)
            {
                case "FeatureCollection":
                    foreach (var f in node["features"]?.AsArray() ?? new JsonArray())
                    {
                        var g = f?["geometry"];
                        if (g != null) outList.Add(g);
                    }
                    break;
                case "Feature":
                    var geom = node["geometry"];
                    if (geom != null) outList.Add(geom);
                    break;
                case null:
                    break;
                default:
                    outList.Add(node); // a bare Geometry object
                    break;
            }
        }

        /// <summary>Build an ArcGIS geometry from a GeoJSON geometry node (Point/MultiPoint/LineString/MultiLineString/Polygon/MultiPolygon). Coordinates are assumed WGS84 lon/lat, per the GeoJSON spec.</summary>
        internal static Geometry BuildGeometryFromNode(JsonNode geomNode)
        {
            var type = geomNode["type"]?.GetValue<string>();
            var coords = geomNode["coordinates"];
            if (coords == null) return null;

            switch (type)
            {
                case "Point":
                {
                    var xy = coords.AsArray();
                    return MapPointBuilderEx.CreateMapPoint(xy[0].GetValue<double>(), xy[1].GetValue<double>(), SpatialReferences.WGS84);
                }
                case "MultiPoint":
                {
                    var pts = ReadPositions(coords);
                    return MultipointBuilderEx.CreateMultipoint(pts, SpatialReferences.WGS84);
                }
                case "LineString":
                {
                    var pts = ReadPositions(coords);
                    return PolylineBuilderEx.CreatePolyline(pts, SpatialReferences.WGS84);
                }
                case "MultiLineString":
                {
                    var builder = new PolylineBuilderEx(SpatialReferences.WGS84);
                    foreach (var line in coords.AsArray())
                        builder.AddPart(ReadPositions(line));
                    return builder.ToGeometry();
                }
                case "Polygon":
                {
                    var builder = new PolygonBuilderEx(SpatialReferences.WGS84);
                    foreach (var ring in coords.AsArray())
                        builder.AddPart(ReadPositions(ring));
                    return builder.ToGeometry();
                }
                case "MultiPolygon":
                {
                    var builder = new PolygonBuilderEx(SpatialReferences.WGS84);
                    foreach (var poly in coords.AsArray())
                        foreach (var ring in poly.AsArray())
                            builder.AddPart(ReadPositions(ring));
                    return builder.ToGeometry();
                }
                default:
                    return null;
            }
        }

        private static List<Coordinate2D> ReadPositions(JsonNode positions)
        {
            var list = new List<Coordinate2D>();
            foreach (var p in positions.AsArray())
            {
                var arr = p.AsArray();
                list.Add(new Coordinate2D(arr[0].GetValue<double>(), arr[1].GetValue<double>()));
            }
            return list;
        }
    }
}
