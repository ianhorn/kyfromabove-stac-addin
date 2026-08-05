/*
 * Map integration service: adds COG rasters to the active map and zooms to extents.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Desktop.Core.Geoprocessing;
using KyFromAboveSTAC.Stac;

namespace KyFromAboveSTAC.Services
{
    /// <summary>Map integration helpers for STAC items.</summary>
    public class MapService
    {
        /// <summary>
        /// Add a Cloud-Optimized GeoTIFF (or any raster) to the active map by URL.
        /// Pro streams COGs directly from the S3 URL (no local download required).
        /// </summary>
        public static Task<bool> AddRasterLayerFromUrlAsync(string url, string layerName)
        {
            return QueuedTask.Run<bool>(() =>
            {
                var mv = MapView.Active;
                if (mv == null) return false;

                try
                {
                    // Add the raster (COG) by URL directly. Pro streams cloud-optimized
                    // GeoTIFFs from the S3 URL via GDAL /vsicurl/ — no local download needed.
                    LayerFactory.Instance.CreateLayer(new Uri(url), mv.Map, 0,
                        string.IsNullOrWhiteSpace(layerName) ? "STAC raster" : layerName);
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        /// <summary>
        /// Zoom the active map to an item's bounding box (lon/lat, WGS84).
        /// </summary>
        public static Task<bool> ZoomToItemAsync(StacItem item)
        {
            return QueuedTask.Run<bool>(() =>
            {
                var mv = MapView.Active;
                if (mv == null || item == null) return false;
                if (!item.TryGetBbox(out double minX, out double minY, out double maxX, out double maxY))
                    return false;

                var envelope = EnvelopeBuilderEx.CreateEnvelope(minX, minY, maxX, maxY, SpatialReferences.WGS84);
                mv.ZoomToAsync(envelope, TimeSpan.FromSeconds(2), false);
                return true;
            });
        }

        /// <summary>
        /// Add STAC item footprints as a GeoJSON feature layer to the active map.
        /// Writes a temporary GeoJSON file under %TEMP%\KyFromAbove\ and adds it as a layer, then zooms to the union bbox.
        /// </summary>
        public static async Task<bool> AddFootprintsLayerAsync(IEnumerable<StacItem> items)
        {
            var list = items?.Where(i => i != null).ToList() ?? new List<StacItem>();
            if (list.Count == 0) return false;

            var tempDir = Path.Combine(Path.GetTempPath(), "KyFromAbove-STAC");
            Directory.CreateDirectory(tempDir);
            var tempFile = Path.Combine(tempDir, $"footprints_{DateTime.Now:yyyyMMdd_HHmmss}.geojson");

            var features = new List<object>();
            foreach (var item in list)
            {
                var geomNode = JsonNode.Parse(item.Geometry.GetRawText());
                var props = new Dictionary<string, object>
                {
                    ["title"] = item.Properties?.Title,
                    ["datetime"] = item.Properties?.Datetime,
                    ["collection"] = item.Collection
                };
                if (item.Bbox != null && item.Bbox.Length >= 4)
                {
                    props["bbox_minx"] = item.Bbox[0];
                    props["bbox_miny"] = item.Bbox[1];
                    props["bbox_maxx"] = item.Bbox[2];
                    props["bbox_maxy"] = item.Bbox[3];
                }
                features.Add(new
                {
                    type = "Feature",
                    id = item.Id,
                    geometry = geomNode,
                    properties = props
                });
            }

            var fc = new { type = "FeatureCollection", features = features.ToArray() };
            var options = new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
            File.WriteAllText(tempFile, JsonSerializer.Serialize(fc, options));

            // Try to add GeoJSON directly. If Pro doesn't support GeoJSON, fall back to JSON To Features GP tool and add the resulting feature class.
            try
            {
                await QueuedTask.Run(() =>
                {
                    var mv = MapView.Active;
                    if (mv == null) throw new InvalidOperationException("No active map view");
                    LayerFactory.Instance.CreateLayer(new Uri(tempFile), mv.Map, 0, "KyFromAbove Footprints");
                });
            }
            catch
            {
                // GeoJSON not supported or adding failed; try conversion to shapefile via geoprocessing
                try
                {
                    var outShp = Path.Combine(tempDir, $"footprints_{DateTime.Now:yyyyMMdd_HHmmss}.shp");
                    var parameters = Geoprocessing.MakeValueArray(tempFile, outShp);
                    var gpResult = await Geoprocessing.ExecuteToolAsync("JSON To Features", parameters, null, null, null);
                    if (gpResult == null || gpResult.IsFailed)
                        return false;

                    await QueuedTask.Run(() =>
                    {
                        var mv = MapView.Active;
                        if (mv == null) throw new InvalidOperationException("No active map view");
                        LayerFactory.Instance.CreateLayer(new Uri(outShp), mv.Map, 0, "KyFromAbove Footprints");
                    });
                }
                catch
                {
                    return false;
                }
            }

            // Zoom to overall extent of all footprints.
            double? minX = null, minY = null, maxX = null, maxY = null;
            foreach (var item in list)
            {
                if (item.TryGetBbox(out double bx0, out double by0, out double bx1, out double by1))
                {
                    minX = minX.HasValue ? Math.Min(minX.Value, bx0) : bx0;
                    minY = minY.HasValue ? Math.Min(minY.Value, by0) : by0;
                    maxX = Math.Max(maxX ?? double.MinValue, bx1);
                    maxY = Math.Max(maxY ?? double.MinValue, by1);
                }
            }
            if (minX.HasValue)
            {
                var env = EnvelopeBuilderEx.CreateEnvelope(minX.Value, minY.Value, maxX.Value, maxY.Value, SpatialReferences.WGS84);
                // Zoom must run on the QueuedTask
                await QueuedTask.Run(() =>
                {
                    var mv = MapView.Active;
                    if (mv != null) mv.ZoomToAsync(env, TimeSpan.FromSeconds(2), false);
                });
            }

            return true;
        }
    }
}
