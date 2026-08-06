/*
 * Map integration service: adds COG rasters to the active map and zooms to extents.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
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
        /// Draw STAC item footprints as graphic outlines on a "KyFromAbove Footprints" GraphicsLayer
        /// in the active map, then zoom to their combined extent.
        /// Graphics avoid the GeoJSON-file/GP-tool round trip (which is fragile: "JSON To Features"
        /// expects Esri JSON, not RFC 7946 GeoJSON, so that path could fail even as a fallback) --
        /// it's the same idea as a Draw AOI tool, just drawing many shapes instead of one.
        /// </summary>
        public static async Task<bool> AddFootprintsLayerAsync(IEnumerable<StacItem> items)
        {
            var list = items?.Where(i => i != null).ToList() ?? new List<StacItem>();
            if (list.Count == 0) return false;

            try
            {
                return await QueuedTask.Run(() =>
                {
                    var mv = MapView.Active;
                    if (mv == null) return false;

                    // Remove any previous footprints layer instead of stacking duplicates on repeated clicks.
                    var existing = mv.Map.GetLayersAsFlattenedList().OfType<GraphicsLayer>()
                        .FirstOrDefault(l => l.Name == "KyFromAbove Footprints");
                    if (existing != null) mv.Map.RemoveLayer(existing);

                    var gLayer = LayerFactory.Instance.CreateLayer<GraphicsLayer>(
                        new GraphicsLayerCreationParams { Name = "KyFromAbove Footprints" }, mv.Map);

                    var outline = SymbolFactory.Instance.ConstructStroke(ColorFactory.Instance.RedRGB, 2.0, SimpleLineStyle.Solid);
                    var symbol = SymbolFactory.Instance.ConstructPolygonSymbol(ColorFactory.Instance.CreateRGBColor(0, 0, 0, 0), SimpleFillStyle.Null, outline);
                    var symbolRef = symbol.MakeSymbolReference();

                    double? minX = null, minY = null, maxX = null, maxY = null;
                    int added = 0;
                    foreach (var item in list)
                    {
                        // STAC item footprints are always polygons in practice; fall back to the bbox
                        // rectangle if the geometry is missing/unparseable or comes back as some other type.
                        Polygon footprint = null;
                        try
                        {
                            var node = JsonNode.Parse(item.Geometry.GetRawText());
                            // MapService and AoiImportService are both in KyFromAboveSTAC.Services, so no qualifier needed.
                            footprint = AoiImportService.BuildGeometryFromNode(node) as Polygon;
                        }
                        catch { /* fall back to bbox below */ }

                        if (footprint == null && item.TryGetBbox(out double fx0, out double fy0, out double fx1, out double fy1))
                        {
                            footprint = PolygonBuilderEx.CreatePolygon(
                                EnvelopeBuilderEx.CreateEnvelope(fx0, fy0, fx1, fy1, SpatialReferences.WGS84));
                        }
                        if (footprint == null) continue;

                        // Confirmed API (Esri docs, "Polygon Graphic Element using CIMGraphic"):
                        // build a CIMPolygonGraphic and add it via the GraphicsLayerExtensions.AddElement
                        // extension method -- there's no overload that takes a raw Geometry + symbol pair.
                        var graphic = new CIMPolygonGraphic { Polygon = footprint, Symbol = symbolRef };
                        gLayer.AddElement(graphic);
                        added++;

                        if (item.TryGetBbox(out double bx0, out double by0, out double bx1, out double by1))
                        {
                            minX = minX.HasValue ? Math.Min(minX.Value, bx0) : bx0;
                            minY = minY.HasValue ? Math.Min(minY.Value, by0) : by0;
                            maxX = Math.Max(maxX ?? double.MinValue, bx1);
                            maxY = Math.Max(maxY ?? double.MinValue, by1);
                        }
                    }
                    if (added == 0) return false;

                    if (minX.HasValue)
                    {
                        var env = EnvelopeBuilderEx.CreateEnvelope(minX.Value, minY.Value, maxX.Value, maxY.Value, SpatialReferences.WGS84);
                        mv.ZoomToAsync(env, TimeSpan.FromSeconds(2), false);
                    }
                    return true;
                });
            }
            catch
            {
                return false;
            }
        }
    }
}
