/*
 * Checkbox view model for selecting STAC collections to search within.
 */
using ArcGIS.Desktop.Framework.Contracts;
using KyFromAbove.Stac;

namespace KyFromAbove
{
    /// <summary>A checkable STAC collection entry in the collections filter list.</summary>
    public class CollectionCheckViewModel : PropertyChangedBase
    {
        private bool _isChecked;

        public CollectionCheckViewModel(StacCollection collection)
        {
            Collection = collection;
            Id = collection.Id;
            Title = collection.TitleOrId;
        }

        public StacCollection Collection { get; }
        public string Id { get; }
        public string Title { get; }

        public bool IsChecked
        {
            get => _isChecked;
            set => SetProperty(ref _isChecked, value, () => IsChecked);
        }

        public override string ToString() => $"{Title} ({Id})";
    }
}
