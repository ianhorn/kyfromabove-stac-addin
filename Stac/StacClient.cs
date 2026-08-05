/*
 * STAC API HTTP client for the Kentucky From Above catalog.
 * Base: https://spved5ihrl.execute-api.us-west-2.amazonaws.com
 * Endpoints used: /collections, /search (GET), pagination via "next" links.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KyFromAboveSTAC.Stac
{
    /// <summary>Search parameters for a STAC item search.</summary>
    public class StacSearchQuery
    {
        /// <summary>Collection ids to search within (null/empty = all).</summary>
        public List<string> Collections { get; set; }
        /// <summary>Bounding box [minX, minY, maxX, maxY] in lon/lat (CRS84), or null.</summary>
        public double[] Bbox { get; set; }
        /// <summary>Start of datetime range (inclusive), or null.</summary>
        public DateTime? StartDate { get; set; }
        /// <summary>End of datetime range (inclusive), or null.</summary>
        public DateTime? EndDate { get; set; }
        /// <summary>Maximum items per page (server caps at 10,000).</summary>
        public int Limit { get; set; } = 50;
        /// <summary>Optional list of item ids to fetch directly.</summary>
        public List<string> Ids { get; set; }
        /// <summary>Optional free-text query string (q parameter, if supported).</summary>
        public string FreeText { get; set; }
        /// <summary>
        /// Optional GeoJSON geometry string (lon/lat, CRS84) to use with the STAC
        /// 'intersects' parameter. When set, the search is performed via POST /search.
        /// Mutually exclusive with Bbox (intersects takes precedence).
        /// </summary>
        public string IntersectsGeoJson { get; set; }
    }

    /// <summary>
    /// HttpClient-backed client for the KyFromAbove STAC API.
    /// Uses a single shared static HttpClient (best practice to avoid socket exhaustion).
    /// </summary>
    public class StacClient : IDisposable
    {
        /// <summary>Default catalog base URL (no trailing slash).</summary>
        public const string DefaultBaseUri = "https://spved5ihrl.execute-api.us-west-2.amazonaws.com";

        private static readonly HttpClient _http;
        private static readonly JsonSerializerOptions _json;

        static StacClient()
        {
            _http = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate })
            {
                Timeout = TimeSpan.FromSeconds(120)
            };
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("KyFromAboveProAddin/1.0");
            _json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        }

        public string BaseUri { get; set; } = DefaultBaseUri;

        public StacClient() { }

        /// <summary>Fetch all collections from the catalog.</summary>
        public async Task<List<StacCollection>> GetCollectionsAsync(CancellationToken ct = default)
        {
            var url = BaseUri.TrimEnd('/') + "/collections";
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var data = await JsonSerializer.DeserializeAsync<StacCollectionsResponse>(stream, _json, ct).ConfigureAwait(false);
            return data?.Collections ?? new List<StacCollection>();
        }

        /// <summary>Run an item search. Uses POST /search with 'intersects' when a
        /// GeoJSON geometry is provided; otherwise GET /search with bbox.</summary>
        public async Task<StacItemCollection> SearchAsync(StacSearchQuery q, CancellationToken ct = default)
        {
            if (!string.IsNullOrWhiteSpace(q.IntersectsGeoJson))
                return await SearchPostAsync(q, ct).ConfigureAwait(false);
            var url = BuildSearchUrl(q);
            return await GetItemCollectionAsync(url, ct).ConfigureAwait(false);
        }

        /// <summary>Fetch the next page using the "next" link href from a prior result.</summary>
        public async Task<StacItemCollection> GetPageAsync(string fullUrl, CancellationToken ct = default)
        {
            return await GetItemCollectionAsync(fullUrl, ct).ConfigureAwait(false);
        }

        /// <summary>POST /search with a JSON body (used when an 'intersects' geometry is set).</summary>
        private async Task<StacItemCollection> SearchPostAsync(StacSearchQuery q, CancellationToken ct)
        {
            using var ms = new MemoryStream();
            using (var writer = new System.Text.Json.Utf8JsonWriter(ms))
            {
                writer.WriteStartObject();
                if (q.Collections != null && q.Collections.Count > 0)
                {
                    writer.WritePropertyName("collections");
                    writer.WriteStartArray();
                    foreach (var c in q.Collections) writer.WriteStringValue(c);
                    writer.WriteEndArray();
                }
                if (!string.IsNullOrWhiteSpace(q.IntersectsGeoJson))
                {
                    writer.WritePropertyName("intersects");
                    using var doc = JsonDocument.Parse(q.IntersectsGeoJson);
                    doc.RootElement.WriteTo(writer);
                }
                if (q.Ids != null && q.Ids.Count > 0)
                {
                    writer.WritePropertyName("ids");
                    writer.WriteStartArray();
                    foreach (var id in q.Ids) writer.WriteStringValue(id);
                    writer.WriteEndArray();
                }
                if (q.StartDate.HasValue || q.EndDate.HasValue)
                    writer.WriteString("datetime", BuildDatetime(q.StartDate, q.EndDate));
                if (!string.IsNullOrWhiteSpace(q.FreeText))
                {
                    writer.WritePropertyName("q");
                    writer.WriteStartArray();
                    writer.WriteStringValue(q.FreeText);
                    writer.WriteEndArray();
                }
                writer.WriteNumber("limit", Clamp(q.Limit, 1, 10000));
                writer.WriteEndObject();
                writer.Flush();
            }

            using var content = new ByteArrayContent(ms.ToArray());
            content.Headers.ContentType = new MediaTypeHeaderValue("application/geo+json");
            using var resp = await _http.PostAsync(BaseUri.TrimEnd('/') + "/search", content, ct).ConfigureAwait(false);
            await EnsureSuccessWithBodyAsync(resp, ct).ConfigureAwait(false);
            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<StacItemCollection>(stream, _json, ct).ConfigureAwait(false);
        }

        /// <summary>Throw with the response body included so 4xx errors are self-explanatory.</summary>
        private static async Task EnsureSuccessWithBodyAsync(HttpResponseMessage resp, CancellationToken ct)
        {
            if (resp.IsSuccessStatusCode) return;
            string body = null;
            try { body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false); } catch { /* ignore */ }
            throw new HttpRequestException($"STAC API {(int)resp.StatusCode} {resp.StatusCode}: {body}");
        }

        /// <summary>Fetch items for a specific collection (optionally paged).</summary>
        public async Task<StacItemCollection> GetCollectionItemsAsync(string collectionId, int limit = 50, CancellationToken ct = default)
        {
            var url = $"{BaseUri.TrimEnd('/')}/collections/{Uri.EscapeDataString(collectionId)}/items?limit={Clamp(limit,1,10000)}";
            return await GetItemCollectionAsync(url, ct).ConfigureAwait(false);
        }

        /// <summary>Fetch a single item.</summary>
        public async Task<StacItem> GetItemAsync(string collectionId, string itemId, CancellationToken ct = default)
        {
            var url = $"{BaseUri.TrimEnd('/')}/collections/{Uri.EscapeDataString(collectionId)}/items/{Uri.EscapeDataString(itemId)}";
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<StacItem>(stream, _json, ct).ConfigureAwait(false);
        }

        /// <summary>Open a download stream for a given asset href (used by the download service).</summary>
        public Task<Stream> OpenAssetStreamAsync(string href, CancellationToken ct = default)
        {
            return _http.GetStreamAsync(href, ct);
        }

        /// <summary>Issue a HEAD request to determine an asset's content length, if available.</summary>
        public async Task<long?> GetContentLengthAsync(string href, CancellationToken ct = default)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Head, href);
                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (resp.Content?.Headers?.ContentLength != null)
                    return resp.Content.Headers.ContentLength;
            }
            catch { /* not all endpoints support HEAD; ignore */ }
            return null;
        }

        private async Task<StacItemCollection> GetItemCollectionAsync(string url, CancellationToken ct)
        {
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            await EnsureSuccessWithBodyAsync(resp, ct).ConfigureAwait(false);
            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<StacItemCollection>(stream, _json, ct).ConfigureAwait(false);
        }

        private string BuildSearchUrl(StacSearchQuery q)
        {
            var parts = new List<string>();
            if (q.Collections != null && q.Collections.Count > 0)
                parts.Add("collections=" + string.Join(",", q.Collections));
            if (q.Ids != null && q.Ids.Count > 0)
                parts.Add("ids=" + string.Join(",", q.Ids));
            if (q.Bbox != null && q.Bbox.Length >= 4)
                parts.Add("bbox=" + string.Join(",", q.Bbox));
            if (q.StartDate.HasValue || q.EndDate.HasValue)
                parts.Add("datetime=" + Uri.EscapeDataString(BuildDatetime(q.StartDate, q.EndDate)));
            if (!string.IsNullOrWhiteSpace(q.FreeText))
                parts.Add("q=" + Uri.EscapeDataString(q.FreeText));
            parts.Add("limit=" + Clamp(q.Limit, 1, 10000).ToString());
            return BaseUri.TrimEnd('/') + "/search?" + string.Join("&", parts);
        }

        private static string BuildDatetime(DateTime? start, DateTime? end)
        {
            // STAC datetime range uses '/' separator. Use UTC 'Z' suffix.
            string ToIso(DateTime dt) => dt.ToUniversalTime().ToString("yyyy-MM-ddTHH\\:mm\\:ssZ");
            if (start.HasValue && end.HasValue) return $"{ToIso(start.Value)}/{ToIso(end.Value)}";
            if (start.HasValue) return $"{ToIso(start.Value)}/..";
            if (end.HasValue) return $"../{ToIso(end.Value)}";
            return null;
        }

        private static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);

        public void Dispose() { /* static http is shared; nothing to dispose here */ }
    }
}
