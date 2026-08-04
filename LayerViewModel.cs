/*
 * View model for a map layer entry in the "Select layer" dropdown.
 */
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Mapping;

namespace KyFromAbove
{
    /// <summary>A feature layer from the active map, shown in the Select-Layer dropdown.</summary>
    public class LayerViewModel : PropertyChangedBase
    {
        public LayerViewModel(BasicFeatureLayer layer)
        {
            Layer = layer;
            DisplayName = layer?.Name ?? "(layer)";
        }

        public BasicFeatureLayer Layer { get; }
        public string DisplayName { get; }

        public override string ToString() => DisplayName;
    }
}
