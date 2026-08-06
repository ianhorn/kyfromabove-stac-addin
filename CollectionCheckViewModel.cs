/*
 * Checkbox view model for selecting STAC collections to search within.
 */
using ArcGIS.Desktop.Framework.Contracts;
using KyFromAboveSTAC.Stac;

namespace KyFromAboveSTAC
{
    /// <summary>A checkable STAC collection entry in the collections filter list.</summary>
    public class CollectionCheckViewModel : PropertyChangedBase
    {
        private bool _isChecked;

        public CollectionCheckViewModel(StacCollection collection, StacApiSource source = null)
        {
            Collection = collection;
            Source = source;
            Id = collection.Id;
            Title = collection.TitleOrId;
        }

        public StacCollection Collection { get; }
        /// <summary>Which API source (built-in or "bring your own") this collection came from.</summary>
        public StacApiSource Source { get; }
        public string Id { get; }
        public string Title { get; }

        /// <summary>Title, with the source name appended when it's not the default (built-in) catalog -- so collections from different APIs aren't ambiguous once merged into one list.</summary>
        public string DisplayLabel => (Source != null && !Source.IsDefault) ? $"{Title} · {Source.Name}" : Title;

        public bool IsChecked
        {
            get => _isChecked;
            set => SetProperty(ref _isChecked, value, () => IsChecked);
        }

        public override string ToString() => $"{Title} ({Id})";
    }
}
