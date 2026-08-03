/*
 * Map integration service: adds COG rasters to the active map and zooms to extents.
 */
using System;
using System.Threading.Tasks;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using KyFromAbove.Stac;

namespace KyFromAbove.Services
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
    }
}
