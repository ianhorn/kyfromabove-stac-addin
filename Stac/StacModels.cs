/*
 * STAC API data models. POCOs deserialized from the KyFromAbove STAC API
 * (https://spved5ihrl.execute-api.us-west-2.amazonaws.com/).
 * Conforms to STAC API 1.0.0 / stac-fastapi responses.
 */
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KyFromAboveSTAC.Stac
{
    /// <summary>A STAC Link (pagination, self, parent, etc.).</summary>
    public class StacLink
    {
        [JsonPropertyName("rel")] public string Rel { get; set; }
        [JsonPropertyName("href")] public string Href { get; set; }
        [JsonPropertyName("type")] public string Type { get; set; }
        [JsonPropertyName("title")] public string Title { get; set; }
        [JsonPropertyName("method")] public string Method { get; set; }
    }

    /// <summary>A STAC asset (the downloadable file: data, thumbnail, metadata).</summary>
    public class StacAsset
    {
        [JsonPropertyName("href")] public string Href { get; set; }
        [JsonPropertyName("type")] public string Type { get; set; }
        [JsonPropertyName("title")] public string Title { get; set; }
        [JsonPropertyName("roles")] public List<string> Roles { get; set; }
        [JsonPropertyName("file:size")] public long? FileSize { get; set; }
    }

    /// <summary>Item properties: known STAC core + common extensions, plus arbitrary extras.</summary>
    public class StacProperties
    {
        [JsonPropertyName("datetime")] public string Datetime { get; set; }
        [JsonPropertyName("start_datetime")] public string StartDatetime { get; set; }
        [JsonPropertyName("end_datetime")] public string EndDatetime { get; set; }
        [JsonPropertyName("title")] public string Title { get; set; }
        [JsonPropertyName("description")] public string Description { get; set; }
        [JsonPropertyName("proj:epsg")] public int? ProjEpsg { get; set; }
        [JsonPropertyName("proj:bbox")] public double[] ProjBbox { get; set; }
        [JsonPropertyName("proj:shape")] public int[] ProjShape { get; set; }
        [JsonPropertyName("instruments")] public List<string> Instruments { get; set; }

        /// <summary>Any additional properties not explicitly mapped.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement> Extra { get; set; }
    }

    /// <summary>A single STAC Item (a Feature).</summary>
    public class StacItem
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("collection")] public string Collection { get; set; }
        [JsonPropertyName("type")] public string Type { get; set; }
        [JsonPropertyName("bbox")] public double[] Bbox { get; set; }
        /// <summary>Raw GeoJSON geometry (kept as JsonElement to avoid modeling every geometry type).</summary>
        [JsonPropertyName("geometry")] public JsonElement Geometry { get; set; }
        [JsonPropertyName("assets")] public Dictionary<string, StacAsset> Assets { get; set; }
        [JsonPropertyName("properties")] public StacProperties Properties { get; set; }
        [JsonPropertyName("links")] public List<StacLink> Links { get; set; }
        [JsonPropertyName("stac_version")] public string StacVersion { get; set; }

        /// <summary>Convenience: bbox as [minX, minY, maxX, maxY] (or false if absent).</summary>
        public bool TryGetBbox(out double minX, out double minY, out double maxX, out double maxY)
        {
            minX = minY = maxX = maxY = 0;
            if (Bbox == null || Bbox.Length < 4) return false;
            minX = Bbox[0]; minY = Bbox[1]; maxX = Bbox[2]; maxY = Bbox[3];
            return true;
        }

        /// <summary>Get the primary "data" asset (the COG/LAZ), or the first data-role asset.</summary>
        public StacAsset GetDataAsset()
        {
            if (Assets == null || Assets.Count == 0) return null;
            if (Assets.TryGetValue("data", out var data)) return data;
            foreach (var kv in Assets)
                if (kv.Value?.Roles != null && kv.Value.Roles.Contains("data")) return kv.Value;
            // Fallback: first asset that doesn't look like a thumbnail/metadata
            foreach (var kv in Assets)
            {
                var key = (kv.Key ?? "").ToLowerInvariant();
                if (key == "thumbnail" || key == "metadata" || key == "xml") continue;
                if (kv.Value?.Roles != null && (kv.Value.Roles.Contains("thumbnail") || kv.Value.Roles.Contains("metadata"))) continue;
                return kv.Value;
            }
            return null;
        }

        /// <summary>Get the thumbnail asset (preview), if present.</summary>
        public StacAsset GetThumbnailAsset()
        {
            if (Assets == null) return null;
            if (Assets.TryGetValue("thumbnail", out var t)) return t;
            foreach (var kv in Assets)
                if (kv.Value?.Roles != null && kv.Value.Roles.Contains("thumbnail")) return kv.Value;
            // Fallback: any image-like asset that isn't the data asset
            foreach (var kv in Assets)
            {
                var href = kv.Value?.Href ?? "";
                if (href.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                    href.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                    href.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            }
            return null;
        }
    }

    /// <summary>A GeoJSON FeatureCollection returned by STAC search / items endpoints.</summary>
    public class StacItemCollection
    {
        [JsonPropertyName("type")] public string Type { get; set; }
        [JsonPropertyName("features")] public List<StacItem> Features { get; set; } = new List<StacItem>();
        [JsonPropertyName("links")] public List<StacLink> Links { get; set; } = new List<StacLink>();
        [JsonPropertyName("numberMatched")] public int? NumberMatched { get; set; }
        [JsonPropertyName("numberReturned")] public int? NumberReturned { get; set; }

        /// <summary>The "next" pagination link href, if present.</summary>
        public string NextLinkHref
        {
            get
            {
                if (Links == null) return null;
                foreach (var l in Links)
                    if (string.Equals(l.Rel, "next", StringComparison.OrdinalIgnoreCase))
                        return l.Href;
                return null;
            }
        }
    }

    /// <summary>A STAC Collection summary.</summary>
    public class StacCollection
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("title")] public string Title { get; set; }
        [JsonPropertyName("description")] public string Description { get; set; }
        [JsonPropertyName("stac_version")] public string StacVersion { get; set; }
        [JsonPropertyName("license")] public string License { get; set; }
        [JsonPropertyName("extent")] public StacExtent Extent { get; set; }
        [JsonPropertyName("assets")] public Dictionary<string, StacAsset> Assets { get; set; }
        [JsonPropertyName("links")] public List<StacLink> Links { get; set; }

        public string TitleOrId => string.IsNullOrWhiteSpace(Title) ? Id : Title;
    }

    public class StacExtent
    {
        [JsonPropertyName("spatial")] public StacSpatialExtent Spatial { get; set; }
        [JsonPropertyName("temporal")] public StacTemporalExtent Temporal { get; set; }
    }

    public class StacSpatialExtent
    {
        [JsonPropertyName("bbox")] public List<double[]> Bbox { get; set; }
    }

    public class StacTemporalExtent
    {
        [JsonPropertyName("interval")] public List<string[]> Interval { get; set; }
    }

    /// <summary>Wrapper for the /collections response.</summary>
    public class StacCollectionsResponse
    {
        [JsonPropertyName("collections")] public List<StacCollection> Collections { get; set; } = new List<StacCollection>();
        [JsonPropertyName("links")] public List<StacLink> Links { get; set; }
        [JsonPropertyName("numberMatched")] public int? NumberMatched { get; set; }
        [JsonPropertyName("numberReturned")] public int? NumberReturned { get; set; }
    }
}
