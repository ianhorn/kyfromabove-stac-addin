/*
 * One STAC API endpoint the dockpane can query. The built-in KyFromAbove catalog is always
 * present as the default source; "Bring Your Own API" adds another source alongside it, or
 * replaces the whole list with a different endpoint.
 *
 * Each source owns its own StacClient. That's safe: StacClient's HttpClient and
 * JsonSerializerOptions are static/shared across instances, so many StacClient objects
 * pointed at different BaseUri values can coexist without any extra socket/connection cost.
 */
using System;

namespace KyFromAboveSTAC.Stac
{
    public class StacApiSource
    {
        public string Name { get; set; }
        public StacClient Client { get; }

        /// <summary>True for the built-in KyFromAbove catalog. Can't be removed via the UI.</summary>
        public bool IsDefault { get; }

        /// <summary>Convenience: the source's catalog base URL.</summary>
        public string BaseUri => Client.BaseUri;

        /// <summary>True for any non-default source -- used to show/hide the "remove" button.</summary>
        public bool CanRemove => !IsDefault;

        /// <summary>Wrap an existing StacClient (used for the default KyFromAbove source, which reuses Module1's singleton client).</summary>
        public StacApiSource(string name, StacClient client, bool isDefault = false)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
            Name = string.IsNullOrWhiteSpace(name) ? Client.BaseUri : name;
            IsDefault = isDefault;
        }

        /// <summary>Create a new source (and its own StacClient) pointed at a base URL. Used for user-added sources.</summary>
        public StacApiSource(string name, string baseUri, bool isDefault = false)
            : this(name, new StacClient { BaseUri = baseUri }, isDefault)
        {
        }

        public override string ToString() => Name;
    }
}
