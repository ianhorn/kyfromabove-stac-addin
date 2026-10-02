/*
 * Checks the installed build's version against the latest published GitHub release.
 * Ported from kylidar-addin (see ../check-version-notes.md); only the repo URL/user agent
 * changed. Pure HttpClient + System.Text.Json + reflection -- no Pro dependency.
 */
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KyFromAboveSTAC.Services
{
    public sealed record UpdateCheckResult(Version Current, Version Latest, string ReleaseUrl)
    {
        public bool IsNewerAvailable => Latest > Current;
    }

    public static class UpdateCheckService
    {
        private const string LatestReleaseApi = "https://api.github.com/repos/ianhorn/kyfromabove-stac-addin/releases/latest";
        public const string ReleasesPage = "https://github.com/ianhorn/kyfromabove-stac-addin/releases";

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("KyFromAboveProAddin/1.0"); // required by GitHub's API
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return http;
        }

        public static Version CurrentVersion() =>
            Normalize(System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

        public static async Task<UpdateCheckResult> CheckAsync(Version current = null, CancellationToken ct = default)
        {
            current = Normalize(current ?? CurrentVersion());

            using var resp = await Http.GetAsync(LatestReleaseApi, ct).ConfigureAwait(false);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new InvalidOperationException("No release has been published yet.");
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            var url = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() : null;
            if (!TryParseTag(tag, out var latest))
                throw new InvalidOperationException($"The latest release's tag \"{tag}\" isn't a version number.");
            return new UpdateCheckResult(current, latest, url ?? ReleasesPage);
        }

        public static bool TryParseTag(string tag, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(tag)) return false;
            if (!Version.TryParse(tag.Trim().TrimStart('v', 'V'), out var parsed)) return false;
            version = Normalize(parsed);
            return true;
        }

        private static Version Normalize(Version v) => new Version(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));
    }
}
