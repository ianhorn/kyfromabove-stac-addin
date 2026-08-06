/*
 * KyFromAbove STAC search dockpane view model.
 * Hosts the search UI logic: load collections, run searches, paginate results.
 * Derives from DockPane (className in Config.daml points here; the framework
 * pairs it with SearchDockpaneView as the content).
 */
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Forms;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Catalog;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Core.Geoprocessing;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using KyFromAboveSTAC.Stac;

namespace KyFromAboveSTAC
{
    /// <summary>One entry in the "threads" dropdown: a preset core count, or "Custom" (Value == null) to enable manual entry.</summary>
    public class ThreadOption
    {
        public string Label { get; set; }
        public int? Value { get; set; }
    }

    /// <summary>One entry in the "Limit" dropdown: a preset result-count cap, or "Custom" (Value == null) to enable manual entry.</summary>
    public class LimitOption
    {
        public string Label { get; set; }
        public int? Value { get; set; }
    }

    internal class SearchDockpaneViewModel : DockPane
    {
        private const string _dockPaneID = "KyFromAbove_SearchDockpane";

        // Per-source pagination: each active API source tracks its own "next" link, since a
        // merged multi-source search can have some sources exhausted and others not.
        private readonly Dictionary<StacApiSource, string> _nextPageUrls = new Dictionary<StacApiSource, string>();
        private CancellationTokenSource _searchCts;

        #region CTOR + Show

        protected SearchDockpaneViewModel()
        {
            // The built-in KyFromAbove catalog is always present, wrapping Module1's shared
            // StacClient singleton (unchanged from before -- downloads etc. still use it directly).
            ApiSources = new ObservableCollection<StacApiSource>
            {
                new StacApiSource("KyFromAbove", Module1.Current.StacClient, isDefault: true)
            };
            BringYourOwnApiCommand = new RelayCommand(() => OnBringYourOwnApi(), () => !IsSearchBusy);
            RemoveApiSourceCommand = new RelayCommand(
                param => OnRemoveApiSource(param as StacApiSource),
                param => !IsSearchBusy && (param as StacApiSource)?.CanRemove == true);

            Collections = new ObservableCollection<CollectionCheckViewModel>();
            Results = new ObservableCollection<ResultItemViewModel>();
            Results.CollectionChanged += Results_CollectionChanged;
            StatusMessage = "Load collections to begin.";
            SearchCommand = new RelayCommand(async () => await OnSearchAsync(reset: true), () => !IsSearchBusy);
            NextPageCommand = new RelayCommand(async () => await OnSearchAsync(reset: false), () => !IsSearchBusy && HasNextPage);
            LoadCollectionsCommand = new RelayCommand(async () => await OnLoadCollectionsAsync(), () => !IsSearchBusy);
            DrawPointAoiCommand = new RelayCommand(async () => await OnDrawAoiAsync(DrawPointAoiTool.ToolId), () => !IsSearchBusy);
            DrawLineAoiCommand = new RelayCommand(async () => await OnDrawAoiAsync(DrawLineAoiTool.ToolId), () => !IsSearchBusy);
            DrawPolygonAoiCommand = new RelayCommand(async () => await OnDrawAoiAsync(DrawPolygonAoiTool.ToolId), () => !IsSearchBusy);
            RefreshLayersCommand = new RelayCommand(async () => await OnRefreshLayersAsync(), () => !IsSearchBusy);
            UseLayerAoiCommand = new RelayCommand(async () => await OnUseLayerAoiAsync(), () => !IsSearchBusy && SelectedLayer != null);
                        MosaicAllCommand = new RelayCommand(async () => await OnMosaicAllAsync(), () => !IsSearchBusy && Results.Count > 0);
                        ClearAoiCommand = new RelayCommand(OnClearAoi, () => !IsSearchBusy);
            UseExtentAoiCommand = new RelayCommand(async () => await OnUseExtentAoiAsync(), () => !IsSearchBusy);
            BrowseAoiFileCommand = new RelayCommand(async () => await OnBrowseAoiFileAsync(), () => !IsSearchBusy);
            DownloadAllCommand = new RelayCommand(async () => await OnDownloadAllAsync(), () => !IsSearchBusy && Results.Count > 0);
            BrowseDownloadFolderCommand = new RelayCommand(() => OnBrowseDownloadFolder());
            ShowFootprintsCommand = new RelayCommand(async () => await OnShowFootprintsAsync(), () => !IsSearchBusy && Results.Count > 0);
            ToggleSelectAllCommand = new RelayCommand(() => OnToggleSelectAll(), () => !IsSearchBusy && Results.Count > 0);

            var projectDir = Path.GetDirectoryName(Project.Current.DefaultGeodatabasePath);
            if (string.IsNullOrWhiteSpace(projectDir)) projectDir = Path.GetTempPath();
            DownloadFolder = Path.Combine(projectDir, "downloads");
            Directory.CreateDirectory(DownloadFolder);
            MapLayers = new ObservableCollection<LayerViewModel>();
            _ = OnRefreshLayersAsync(); // populate immediately, in case a map view is already active
            // The dockpane can be constructed before the default map view finishes activating
            // (e.g. at Pro startup), in which case the call above finds nothing and never retries.
            // Re-run the refresh every time a map view becomes active so the dropdown doesn't stay
            // blank until the user notices and clicks Refresh manually.
            ArcGIS.Desktop.Mapping.Events.ActiveMapViewChangedEvent.Subscribe(OnActiveMapViewChanged);

            SelectedThreadOption = ThreadOptions.FirstOrDefault(t => t.Label.StartsWith("75%")) ?? ThreadOptions[0];
            SelectedLimitOption = LimitOptions.FirstOrDefault(o => o.Label == "50") ?? LimitOptions[0];
        }

        private void Results_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (var oi in e.OldItems)
                {
                    if (oi is System.ComponentModel.INotifyPropertyChanged ipc)
                        ipc.PropertyChanged -= ResultItem_PropertyChanged;
                }
            }
            if (e.NewItems != null)
            {
                foreach (var ni in e.NewItems)
                {
                    if (ni is System.ComponentModel.INotifyPropertyChanged ipc)
                        ipc.PropertyChanged += ResultItem_PropertyChanged;
                }
            }
            UpdateHasSelection();
        }

        private void ResultItem_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ResultItemViewModel.IsSelected))
            {
                UpdateHasSelection();
            }
        }

        private void UpdateHasSelection()
        {
            HasSelection = Results.Any(r => r.IsSelected);
        }

        private void OnShowHelp()
        {
            var msg = "Point-cloud assets (e.g. .laz, .las) cannot be added directly to the map from this UI.\n\n" +
                      "You can select and download them for external processing. If you need to work with point-clouds inside ArcGIS Pro, " +
                      "import them via the appropriate geoprocessing tools or use a point-cloud/ LAS dataset workflow.\n\n" +
                      "This tool supports downloading both raster (COG) and point-cloud assets.";
            System.Windows.MessageBox.Show(msg, "KyFromAbove-STAC: Download help", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        /// <summary>Show the DockPane.</summary>
        internal static void Show()
        {
            DockPane pane = FrameworkApplication.DockPaneManager.Find(_dockPaneID);
            if (pane == null) return;
            pane.Activate();
        }

        #endregion

        #region Bound properties

        public ObservableCollection<CollectionCheckViewModel> Collections { get; }
        public ObservableCollection<ResultItemViewModel> Results { get; }
        public ObservableCollection<LayerViewModel> MapLayers { get; }

        /// <summary>Active STAC API sources this search queries. Always has at least the built-in KyFromAbove source.</summary>
        public ObservableCollection<StacApiSource> ApiSources { get; }

        private LayerViewModel _selectedLayer;
        public LayerViewModel SelectedLayer
        {
            get => _selectedLayer;
            set
            {
                SetProperty(ref _selectedLayer, value, () => SelectedLayer);
                SetProperty(ref _selectedLayerName, value?.DisplayName, () => SelectedLayerName);
            }
        }

        private string _selectedLayerName;
        public string SelectedLayerName
        {
            get => _selectedLayerName;
            private set => SetProperty(ref _selectedLayerName, value, () => SelectedLayerName);
        }

        private bool _isBusy;
        public bool IsSearchBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value, () => IsSearchBusy);
        }

        private string _statusMessage;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value, () => StatusMessage);
        }

        private string _freeText;
        public string FreeText
        {
            get => _freeText;
            set => SetProperty(ref _freeText, value, () => FreeText);
        }

        private int _limit = 50;
        /// <summary>Search result limit. Floored at 50 -- the dropdown's lowest preset -- even for custom entry.</summary>
        public int Limit
        {
            get => _limit;
            set
            {
                SetProperty(ref _limit, Math.Max(50, value), () => Limit);
                NotifyPropertyChanged(() => ThumbnailsDisabled);
            }
        }

        /// <summary>When the search limit exceeds 50, thumbnails are skipped for new results to avoid a burst of extra network requests.</summary>
        public bool ThumbnailsDisabled => Limit > 50;

        public ObservableCollection<LimitOption> LimitOptions { get; } = new ObservableCollection<LimitOption>
        {
            new LimitOption { Label = "50", Value = 50 },
            new LimitOption { Label = "100", Value = 100 },
            new LimitOption { Label = "200", Value = 200 },
            new LimitOption { Label = "500", Value = 500 },
            new LimitOption { Label = "Custom", Value = null }
        };

        private LimitOption _selectedLimitOption;
        /// <summary>The selected result-limit preset. Choosing "Custom" reveals a manual entry box bound to Limit.</summary>
        public LimitOption SelectedLimitOption
        {
            get => _selectedLimitOption;
            set
            {
                SetProperty(ref _selectedLimitOption, value, () => SelectedLimitOption);
                NotifyPropertyChanged(() => IsCustomLimit);
                if (value?.Value.HasValue == true)
                    Limit = value.Value.Value;
            }
        }

        /// <summary>True when "Custom" is selected in the Limit dropdown, showing the manual entry box.</summary>
        public bool IsCustomLimit => SelectedLimitOption?.Value == null;

        private DateTime? _startDate;
        public DateTime? StartDate
        {
            get => _startDate;
            set => SetProperty(ref _startDate, value, () => StartDate);
        }

        private DateTime? _endDate;
        public DateTime? EndDate
        {
            get => _endDate;
            set => SetProperty(ref _endDate, value, () => EndDate);
        }

        private string _aoiText;
        public string AoiText
        {
            get => _aoiText;
            set => SetProperty(ref _aoiText, value, () => AoiText);
        }

        // GeoJSON geometry string (lon/lat) set via a Draw* AOI tool; used as the STAC 'intersects' AOI.
        private string _intersectsGeoJson;

        private int _resultCount;
        public int ResultCount
        {
            get => _resultCount;
            set
            {
                SetProperty(ref _resultCount, value, () => ResultCount);
                NotifyPropertyChanged(() => ResultsSummaryText);
            }
        }

        /// <summary>Human-readable summary of the last search's result count (e.g. "Showing 50 of 137 matched"), shown above the results list.</summary>
        public string ResultsSummaryText
        {
            get
            {
                if (!TotalMatched.HasValue && ResultCount == 0) return string.Empty;
                if (ResultCount == 0) return "No results found.";
                return TotalMatched.HasValue && TotalMatched.Value > ResultCount
                    ? $"Showing {ResultCount} of {TotalMatched.Value} matched"
                    : $"{ResultCount} result{(ResultCount == 1 ? "" : "s")} found";
            }
        }

        private bool _hasSelection;
        public bool HasSelection
        {
            get => _hasSelection;
            private set => SetProperty(ref _hasSelection, value, () => HasSelection);
        }

        private int? _totalMatched;
        public int? TotalMatched
        {
            get => _totalMatched;
            set
            {
                SetProperty(ref _totalMatched, value, () => TotalMatched);
                NotifyPropertyChanged(() => ResultsSummaryText);
            }
        }

        private bool _hasPointCloudResults;
        public bool HasPointCloudResults
        {
            get => _hasPointCloudResults;
            private set => SetProperty(ref _hasPointCloudResults, value, () => HasPointCloudResults);
        }

                private bool _buildOverviews = true;
        /// <summary>If checked (default), the mosaic's overviews are defined + built after the rasters are added.</summary>
        public bool BuildOverviews
        {
            get => _buildOverviews;
            set => SetProperty(ref _buildOverviews, value, () => BuildOverviews);
        }

        public ICommand ShowHelpCommand { get; }

        private string _downloadFolder;
        /// <summary>Local folder where selected assets are downloaded. Defaults to &lt;project_dir&gt;\downloads.</summary>
        public string DownloadFolder
        {
            get => _downloadFolder;
            set => SetProperty(ref _downloadFolder, value, () => DownloadFolder);
        }

        private int _downloadConcurrency = Math.Max(1, Environment.ProcessorCount);
        /// <summary>Number of assets downloaded in parallel (= threads/cores used).</summary>
        public int DownloadConcurrency
        {
            get => _downloadConcurrency;
            set => SetProperty(ref _downloadConcurrency, Math.Max(1, value), () => DownloadConcurrency);
        }

        public ObservableCollection<ThreadOption> ThreadOptions { get; } = BuildThreadOptions();

        private static ObservableCollection<ThreadOption> BuildThreadOptions()
        {
            int cores = Math.Max(1, Environment.ProcessorCount);
            int nMinus1 = Math.Max(1, cores - 1);
            int p75 = Math.Max(1, (int)Math.Round(cores * 0.75));
            int p50 = Math.Max(1, (int)Math.Round(cores * 0.50));
            int p25 = Math.Max(1, (int)Math.Round(cores * 0.25));
            return new ObservableCollection<ThreadOption>
            {
                new ThreadOption { Label = $"All but 1 core ({nMinus1})", Value = nMinus1 },
                new ThreadOption { Label = $"75% ({p75})", Value = p75 },
                new ThreadOption { Label = $"50% ({p50})", Value = p50 },
                new ThreadOption { Label = $"25% ({p25})", Value = p25 },
                new ThreadOption { Label = "Custom", Value = null }
            };
        }

        private ThreadOption _selectedThreadOption;
        /// <summary>The selected threads preset. Choosing "Custom" reveals a manual entry box bound to DownloadConcurrency.</summary>
        public ThreadOption SelectedThreadOption
        {
            get => _selectedThreadOption;
            set
            {
                SetProperty(ref _selectedThreadOption, value, () => SelectedThreadOption);
                NotifyPropertyChanged(() => IsCustomThreadCount);
                if (value?.Value.HasValue == true)
                    DownloadConcurrency = value.Value.Value;
            }
        }

        /// <summary>True when "Custom" is selected in the threads dropdown, showing the manual entry box.</summary>
        public bool IsCustomThreadCount => SelectedThreadOption?.Value == null;

        private bool _downloadPerItemFolder;
        /// <summary>If true, each item downloads into its own subfolder under DownloadFolder. Off by default (flat into DownloadFolder).</summary>
        public bool DownloadPerItemFolder
        {
            get => _downloadPerItemFolder;
            set => SetProperty(ref _downloadPerItemFolder, value, () => DownloadPerItemFolder);
        }

        public bool HasNextPage => _nextPageUrls.Values.Any(v => !string.IsNullOrEmpty(v));

        public ICommand SearchCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand LoadCollectionsCommand { get; }
        public ICommand DrawPointAoiCommand { get; }
        public ICommand DrawLineAoiCommand { get; }
        public ICommand DrawPolygonAoiCommand { get; }
        public ICommand RefreshLayersCommand { get; }
                        public ICommand UseLayerAoiCommand { get; }
        public ICommand UseExtentAoiCommand { get; }
        public ICommand BrowseAoiFileCommand { get; }
        public ICommand MosaicAllCommand { get; }
        public ICommand ClearAoiCommand { get; }
        public ICommand DownloadAllCommand { get; }
        public ICommand BrowseDownloadFolderCommand { get; }
        public ICommand ShowFootprintsCommand { get; }
        public ICommand ToggleSelectAllCommand { get; }
        public ICommand BringYourOwnApiCommand { get; }
        public ICommand RemoveApiSourceCommand { get; }

        #endregion

        #region Command handlers

        private async Task OnLoadCollectionsAsync()
        {
            IsSearchBusy = true;
            StatusMessage = "Loading collections...";
            try
            {
                var sources = ApiSources.ToList();
                var loaded = new List<CollectionCheckViewModel>();
                var errors = new List<string>();

                // Load each source independently -- one bad/unreachable "bring your own" API
                // shouldn't stop the built-in catalog (or any other source) from loading.
                foreach (var source in sources)
                {
                    try
                    {
                        var cols = await source.Client.GetCollectionsAsync();
                        loaded.AddRange(cols.Select(c => new CollectionCheckViewModel(c, source)));
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{source.Name}: {ex.Message}");
                    }
                }

                Collections.Clear();
                foreach (var c in loaded.OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase))
                    Collections.Add(c);

                if (Collections.Count > 0)
                {
                    StatusMessage = $"{Collections.Count} collections loaded. Choose filters and search.";
                    if (errors.Count > 0) StatusMessage += " (" + string.Join("; ", errors) + ")";
                }
                else
                {
                    StatusMessage = errors.Count > 0
                        ? "Error loading collections: " + string.Join("; ", errors)
                        : "No collections returned.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "Error loading collections: " + ex.Message;
            }
            finally { IsSearchBusy = false; }
        }

        /// <summary>Open the "Bring Your Own API" dialog to add another STAC API alongside the current sources, or replace them all.</summary>
        private void OnBringYourOwnApi()
        {
            var dlg = new AddApiSourceDialog { Owner = System.Windows.Application.Current?.MainWindow };
            if (dlg.ShowDialog() != true || dlg.Result == AddApiSourceResult.Cancel) return;

            var newSource = new StacApiSource(
                string.IsNullOrWhiteSpace(dlg.SourceName) ? dlg.BaseUrl : dlg.SourceName,
                dlg.BaseUrl,
                isDefault: dlg.Result == AddApiSourceResult.Replace); // sole source after a replace behaves like the default (no "remove" button, no label suffix)

            if (dlg.Result == AddApiSourceResult.Replace)
            {
                ApiSources.Clear();
                ApiSources.Add(newSource);
                StatusMessage = $"Switched to API source '{newSource.Name}'. Load collections to continue.";
            }
            else
            {
                ApiSources.Add(newSource);
                StatusMessage = $"Added API source '{newSource.Name}'. Load collections to include it.";
            }

            Collections.Clear();
            Results.Clear();
            ResultCount = 0;
            TotalMatched = null;
            _nextPageUrls.Clear();
            NotifyPropertyChanged(() => HasNextPage);
        }

        /// <summary>Remove a "bring your own" source (the default KyFromAbove source can't be removed).</summary>
        private void OnRemoveApiSource(StacApiSource source)
        {
            if (source == null || source.IsDefault) return;
            ApiSources.Remove(source);
            _nextPageUrls.Remove(source);
            NotifyPropertyChanged(() => HasNextPage);

            // Drop any collections/results that came from the removed source so the UI doesn't
            // show stale entries the user can no longer search against.
            var stale = Collections.Where(c => c.Source == source).ToList();
            foreach (var c in stale) Collections.Remove(c);
            var staleResults = Results.Where(r => r.CollectionId != null &&
                stale.Any(c => c.Id == r.CollectionId)).ToList();
            foreach (var r in staleResults) Results.Remove(r);

            StatusMessage = $"Removed API source '{source.Name}'.";
        }

        /// <summary>
        /// Capture a geometry drawn by a Draw* AOI tool (already projected to WGS84),
        /// convert it to GeoJSON, and store it as the search 'intersects' AOI.
        /// </summary>
        public void SetAoi(Geometry geometry)
        {
            if (geometry == null) return;
            try
            {
                _intersectsGeoJson = Services.GeoJsonConverter.ToGeoJsonGeometry(geometry);
                var e = geometry.Extent;
                string label;
                switch (geometry)
                {
                    case MapPoint _:
                        label = $"Point [{e.XMin:F4}, {e.YMin:F4}]";
                        break;
                    case Polyline _:
                        label = $"Line [{e.XMin:F4}, {e.YMin:F4}, {e.XMax:F4}, {e.YMax:F4}]";
                        break;
                    default:
                        label = $"Polygon [{e.XMin:F4}, {e.YMin:F4}, {e.XMax:F4}, {e.YMax:F4}]";
                        break;
                }
                AoiText = label;
                StatusMessage = "AOI set from drawn geometry.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Could not set AOI: " + ex.Message;
            }
        }

        /// <summary>Activate the requested Draw* AOI tool.</summary>
        private async Task OnDrawAoiAsync(string toolId)
        {
            if (MapView.Active == null)
            {
                StatusMessage = "Open a map view first.";
                return;
                        }
            await FrameworkApplication.SetCurrentToolAsync(toolId);
        }

        /// <summary>Use the active map view's current extent as the search AOI (projected to WGS84).</summary>
        private async Task OnUseExtentAoiAsync()
        {
            var mv = MapView.Active;
            if (mv == null) { StatusMessage = "Open a map view first."; return; }
                                    try
            {
                Envelope bbox = await QueuedTask.Run(() =>
                {
                    var e = mv.Extent;
                    if (e == null || e.IsEmpty) return null;
                    // Project to WGS84 (lon/lat) so the corners are valid STAC intersects coords.
                    if (e.SpatialReference == null ||
                        e.SpatialReference.Wkid != SpatialReferences.WGS84.Wkid)
                        e = (Envelope)GeometryEngine.Instance.Project(e, SpatialReferences.WGS84);
                    return e;
                });
                if (bbox == null) { StatusMessage = "Map view extent is empty."; return; }

                // Build the STAC 'intersects' polygon (a closed 4-corner bbox ring) in WGS84 lon/lat
                // directly from the projected extent's coordinates. (GeoJsonConverter only handles
                // MapPoint/Polyline/Polygon, not Envelope, so we emit the GeoJSON here.)
                var f = System.Globalization.CultureInfo.InvariantCulture.NumberFormat;
                string coords = string.Format(f,
                    "[[{0},{1}],[{2},{1}],[{2},{3}],[{0},{3}],[{0},{1}]]",
                    bbox.XMin, bbox.YMin, bbox.XMax, bbox.YMax);
                _intersectsGeoJson = "{\"type\":\"Polygon\",\"coordinates\":[" + coords + "]}";
                AoiText = string.Format(f,
                    "Extent [{0:F4}, {1:F4}, {2:F4}, {3:F4}]", bbox.XMin, bbox.YMin, bbox.XMax, bbox.YMax);
                StatusMessage = "AOI set from map extent.";
            }
            catch (Exception ex) { StatusMessage = "Could not set AOI from extent: " + ex.Message; }
        }

        /// <summary>Browse for an AOI file (.shp or .geojson/.json). Works with no map view open.</summary>
        private async Task OnBrowseAoiFileAsync()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Browse for AOI file",
                Filter = Services.AoiImportService.OpenDialogFilter,
                CheckFileExists = true
            };
            if (dlg.ShowDialog() != true) return;
            await LoadAoiFromFileAsync(dlg.FileName);
        }

        /// <summary>
        /// Load an AOI from a Shapefile (.shp) or GeoJSON (.geojson/.json) file and set it as the
        /// search 'intersects' AOI. Used by the Browse button and by drag-and-drop onto the AOI
        /// panel. Unlike Draw/Use Extent/Use Layer, this does not require an active map view --
        /// useful when the user hasn't opened a map yet.
        /// </summary>
        public async Task LoadAoiFromFileAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            if (!Services.AoiImportService.IsSupportedFile(filePath))
            {
                StatusMessage = "Unsupported AOI file type. Use .shp or .geojson/.json.";
                return;
            }

            StatusMessage = $"Loading AOI from {System.IO.Path.GetFileName(filePath)}...";
            try
            {
                Geometry geometry = await QueuedTask.Run(() => Services.AoiImportService.LoadGeometry(filePath));
                if (geometry == null || geometry.IsEmpty)
                {
                    StatusMessage = "AOI file has no usable geometry.";
                    return;
                }
                SetAoi(geometry);
            }
            catch (Exception ex)
            {
                StatusMessage = "Could not load AOI file: " + ex.Message;
            }
        }

        /// <summary>Build ONE mosaic dataset layer from selected (or all) imagery/DEM result COGs and add it to the map. Skips point-cloud (LAZ) assets.</summary>
        private async System.Threading.Tasks.Task OnMosaicAllAsync()
        {
            var rasterResults = Results.Where(r => r.DataAsset != null && IsRasterAsset(r.DataAsset)).ToList();
            var selected = rasterResults.Where(r => r.IsSelected).ToList();
            var hrefs = (selected.Count > 0 ? selected : rasterResults).Select(r => r.DataAsset.Href).ToList();
            if (hrefs.Count == 0) { StatusMessage = "No raster (COG) results to mosaic."; return; }

            var buildOverviews = BuildOverviews;
            StatusMessage = "Building mosaic dataset layer...";

            // Plain cancellation token, checked between geoprocessing steps. Note: this can't abort
            // a GP tool call that's already running (ArcGIS Pro's own progress dialog would be needed
            // for that), but it stops before starting the *next* step -- most usefully, before
            // "Build Overviews", which is the one described as "can take a while".
            var cts = new CancellationTokenSource();

            // Show a closeable/cancelable progress dialog so the user can watch and interrupt the long-running work.
            var dlg = new ProgressDialog("Mosaic progress");
            dlg.Show();
            dlg.CancelRequested += (s, e) => cts.Cancel();

            bool Cancelled(ProgressDialog d)
            {
                if (!cts.IsCancellationRequested) return false;
                StatusMessage = "Mosaic cancelled.";
                d.Append("Mosaic cancelled by user (before the next step).");
                return true;
            }

            try
            {
                await QueuedTask.Run(async () =>
                {
                    dlg.Append($"Mosaicking {hrefs.Count} tile(s)...");
                    var mv = MapView.Active;
                    if (mv == null) { StatusMessage = "Open a map view first."; return; }

                    // Inputs: default GDB, unique name, the map's spatial reference.
                    string gdb = Project.Current.DefaultGeodatabasePath;
                    string mosaicName = "KyFromAbove_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    var sr = mv.Map.SpatialReference ?? SpatialReferences.WGS84;

                    if (Cancelled(dlg)) return;

                    // 1) Create Mosaic Dataset: (out_gdb_path, in_mosaic_dataset_name, spatial_reference)
                    // GPExecuteToolFlags.None on every call here: without it, Pro's default GP behavior
                    // (AddOutputsToMap) adds the mosaic dataset to the map on its own, and then our own
                    // LayerFactory.CreateLayer call below adds it again -- that's the duplicate-layer bug.
                    dlg.Append("Creating mosaic dataset...");
                    var createArgs = Geoprocessing.MakeValueArray(gdb, mosaicName, sr);
                    var createRes = await Geoprocessing.ExecuteToolAsync("management.CreateMosaicDataset", createArgs,
                        null, null, null, GPExecuteToolFlags.None).ConfigureAwait(true);
                    if (createRes.IsFailed) { StatusMessage = "CreateMosaicDataset failed: " + GpMessages(createRes); return; }
                    string mosaicPath = System.IO.Path.Combine(gdb, mosaicName);
                    if (Cancelled(dlg)) return;

                    // 2) Add Rasters To Mosaic Dataset: (in_mosaic_dataset, raster_type, data_path)
                    dlg.Append($"Adding {hrefs.Count} rasters to mosaic...");
                    var addArgs = Geoprocessing.MakeValueArray(mosaicPath, "Raster Dataset", string.Join(";", hrefs));
                    var addRes = await Geoprocessing.ExecuteToolAsync("management.AddRastersToMosaicDataset", addArgs,
                        null, null, null, GPExecuteToolFlags.None).ConfigureAwait(true);
                    if (addRes.IsFailed) { StatusMessage = "AddRastersToMosaicDataset failed: " + GpMessages(addRes); return; }
                    if (Cancelled(dlg)) return;

                    // (optional) Define + Build overviews for faster display at small scales.
                    if (buildOverviews)
                    {
                        dlg.Append("Defining overviews...");
                        var defRes = await Geoprocessing.ExecuteToolAsync("management.DefineOverviews",
                            Geoprocessing.MakeValueArray(mosaicPath), null, null, null, GPExecuteToolFlags.None).ConfigureAwait(true);
                        if (defRes.IsFailed) { StatusMessage = "DefineOverviews failed: " + GpMessages(defRes); return; }
                        if (Cancelled(dlg)) return;

                        dlg.Append("Building overviews (this can take a while)...");
                        var buildRes = await Geoprocessing.ExecuteToolAsync("management.BuildOverviews",
                            Geoprocessing.MakeValueArray(mosaicPath), null, null, null, GPExecuteToolFlags.None).ConfigureAwait(true);
                        if (buildRes.IsFailed) { StatusMessage = "BuildOverviews failed: " + GpMessages(buildRes); return; }
                        if (Cancelled(dlg)) return;
                    }

                    // 3) Add the mosaic layer to the active map.
                    dlg.Append("Adding mosaic layer to map...");
                    var item = ItemFactory.Instance.Create(mosaicPath);
                    if (item != null)
                    {
                        LayerFactory.Instance.CreateLayer<MosaicLayer>(
                            new LayerCreationParams(item) { Name = mosaicName, MapMemberPosition = MapMemberPosition.AutoArrange },
                            mv.Map);
                        StatusMessage = $"Mosaic layer '{mosaicName}' created with {hrefs.Count} COG(s)" + (buildOverviews ? " + overviews." : ".");
                        dlg.Append(StatusMessage);
                    }
                    else { StatusMessage = "Mosaic created but could not be added to the map."; dlg.Append(StatusMessage); }
                });
            }
            catch (System.Exception ex) { StatusMessage = "Mosaic failed: " + ex.Message; dlg.Append(StatusMessage); }
            finally
            {
                dlg.DisableCancel();
                dlg.Append("Done.");
                dlg.CloseWhenReady();
            }
        }

        /// <summary>Browse for a local folder to download results into.</summary>
        private void OnBrowseDownloadFolder()
        {
                        var fbd = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select a folder to download the selected imagery/COGs to.",
                SelectedPath = string.IsNullOrWhiteSpace(DownloadFolder) ? Path.GetTempPath() : DownloadFolder,
                ShowNewFolderButton = true,
                AutoUpgradeEnabled = true
            };
            if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
            {
                DownloadFolder = fbd.SelectedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        /// <summary>
        /// Download the selected (or all) raster result assets to DownloadFolder using several
        /// parallel threads/cores. A closeable progress dialog reports per-asset status.
        /// </summary>
        private async System.Threading.Tasks.Task OnDownloadAllAsync()
        {
            if (string.IsNullOrWhiteSpace(DownloadFolder)) { StatusMessage = "Set a download folder."; return; }
            try { Directory.CreateDirectory(DownloadFolder); }
            catch (Exception ex) { StatusMessage = "Bad download folder: " + ex.Message; return; }

            // Selected results; fall back to all downloadable results if nothing is explicitly selected.
            // Downloadable includes raster COGs and point-cloud LAZ/LAS assets (we don't try to add point-clouds to the map).
            var toDownload = Results
                .Where(r => r.IsSelected && r.DataAsset != null && IsDownloadableAsset(r.DataAsset))
                .ToList();
            if (toDownload.Count == 0)
                toDownload = Results.Where(r => r.DataAsset != null && IsDownloadableAsset(r.DataAsset)).ToList();
            if (toDownload.Count == 0) { StatusMessage = "No downloadable results to download."; return; }

            var concurrency = Math.Max(1, DownloadConcurrency);
            var dl = new Services.DownloadService(Module1.Current.StacClient);
            var cts = new CancellationTokenSource();

            var dlg = new ProgressDialog("Downloading files");
            dlg.Append($"Downloading {toDownload.Count} asset(s) to:\n  {DownloadFolder}\n({concurrency} parallel thread(s))");
            dlg.Show();
            dlg.CancelRequested += (s, e) => cts.Cancel(); // Cancel button or closing the window stops the remaining downloads

            try
            {
                long ok = 0, fail = 0, bytes = 0;
                var sem = new SemaphoreSlim(concurrency, concurrency);
                var tasks = new List<Task>();

                foreach (var r in toDownload)
                {
                    var item = r;
                    var destDir = DownloadPerItemFolder ? Path.Combine(DownloadFolder, item.Item.Id) : DownloadFolder;
                    var fname = Services.DownloadService.SuggestFileName(item.DataAsset, item.Item);
                    // If flat download, avoid filename collisions by prefixing with item id,
                    // but don't double-prefix if the suggested name already starts with the id.
                    if (!DownloadPerItemFolder && !string.IsNullOrWhiteSpace(item.Item.Id))
                    {
                        var prefix = item.Item.Id + "_";
                        if (!fname.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(fname, item.Item.Id, StringComparison.OrdinalIgnoreCase))
                        {
                            fname = prefix + fname;
                        }
                    }
                    var dest = Path.Combine(destDir, fname);
                    var prog = new Progress<Services.DownloadProgress>(p =>
                    {
                        if (p.TotalBytes > 0)
                            dlg.Append($"  [{item.Item.Id}] {p.BytesReceived:n0} / {p.TotalBytes:n0} bytes");
                    });

                    tasks.Add(Task.Run(async () =>
                    {
                        await sem.WaitAsync();
                        try
                        {
                            var res = await dl.DownloadAssetAsync(item.DataAsset.Href, dest, prog, cts.Token);
                            if (res.Success)
                            {
                                Interlocked.Increment(ref ok);
                                Interlocked.Add(ref bytes, res.Bytes);
                            }
                            else
                            {
                                Interlocked.Increment(ref fail);
                                dlg.Append($"  [{item.Item.Id}] FAILED: {res.Error}");
                            }
                        }
                        catch (OperationCanceledException) { Interlocked.Increment(ref fail); dlg.Append($"  [{item.Item.Id}] cancelled."); }
                        catch (Exception ex) { Interlocked.Increment(ref fail); dlg.Append($"  [{item.Item.Id}] EXCEPTION: {ex.Message}"); }
                        finally { sem.Release(); }
                    }, cts.Token));
                }

                await Task.WhenAll(tasks);
                string sum = $"Done: {ok} succeeded, {fail} failed, {bytes / (1024.0 * 1024.0):F1} MB written.";
                StatusMessage = sum;
                dlg.Append(sum);
            }
            catch (Exception ex) { StatusMessage = "Download failed: " + ex.Message; dlg.Append(StatusMessage); }
            finally
            {
                dlg.DisableCancel();
                dlg.Append("Download complete.");
                dlg.CloseWhenReady();
            }
        }

        /// <summary>Add STAC search result footprints as a GeoJSON layer to the active map.</summary>
        private async Task OnShowFootprintsAsync()
        {
            if (Results.Count == 0) { StatusMessage = "No results to show footprints for."; return; }
            StatusMessage = "Adding footprints to map...";
            try
            {
                bool ok = await Services.MapService.AddFootprintsLayerAsync(Results.Select(r => r.Item));
                if (ok)
                {
                    StatusMessage = "Footprints added to map.";
                }
                else
                {
                    StatusMessage = "Failed to add footprints. Open a map view first.";
                    System.Windows.MessageBox.Show("Could not add footprints to the map. Open a map view first, then try again.", "KyFromAbove: Footprints", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "Footprints failed: " + ex.Message;
            }
        }

        private static string GpMessages(IGPResult result)
        {
            try { return string.Join(" ", (result.Messages ?? Enumerable.Empty<IGPMessage>()).Select(m => m.Text ?? "")); }
            catch { return "see GP messages"; }
        }

        private static bool IsRasterAsset(Stac.StacAsset a)
        {
            var ext = System.IO.Path.GetExtension(a.Href ?? "").ToLowerInvariant();
            if (ext == ".laz" || ext == ".las" || ext == ".copc" || ext == ".zlidar" || ext == ".zlas") return false;
            if (ext == ".tif" || ext == ".tiff" || ext == ".vrt" || ext == ".img" || ext == ".dem") return true;
            var t = a.Type ?? "";
            return t.IndexOf("tiff", System.StringComparison.OrdinalIgnoreCase) >= 0
                || t.IndexOf("image", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Return true if the asset is downloadable by the user (raster or point-cloud).</summary>
        private static bool IsDownloadableAsset(Stac.StacAsset a)
        {
            var ext = System.IO.Path.GetExtension(a.Href ?? "").ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(ext))
            {
                var t = a.Type ?? "";
                if (t.IndexOf("tiff", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || t.IndexOf("image", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (t.IndexOf("las", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || t.IndexOf("lidar", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                return false;
            }

            // Allow common raster extensions
            if (ext == ".tif" || ext == ".tiff" || ext == ".vrt" || ext == ".img" || ext == ".dem") return true;
            // Allow common point-cloud extensions
            if (ext == ".laz" || ext == ".las" || ext == ".copc" || ext == ".zlidar" || ext == ".zlas") return true;
            return false;
        }

        private static bool IsPointCloudAsset(Stac.StacAsset a)
        {
            var ext = System.IO.Path.GetExtension(a.Href ?? "").ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(ext)) return false;
            return ext == ".laz" || ext == ".las" || ext == ".copc" || ext == ".zlidar" || ext == ".zlas";
        }

        /// <summary>Fired whenever a map view becomes active (map opened, map tab switched, project opened) -- refresh the layer dropdown.</summary>
        private void OnActiveMapViewChanged(ArcGIS.Desktop.Mapping.Events.ActiveMapViewChangedEventArgs args)
        {
            _ = OnRefreshLayersAsync();
        }

        /// <summary>Populate the layer dropdown with feature layers from the active map.</summary>
        private async Task OnRefreshLayersAsync()
        {
            var layers = await QueuedTask.Run(() =>
            {
                if (MapView.Active?.Map == null) return new List<LayerViewModel>();
                return MapView.Active.Map.GetLayersAsFlattenedList()
                    .OfType<BasicFeatureLayer>()
                    .Select(l => new LayerViewModel(l))
                    .ToList();
            });
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                MapLayers.Clear();
                foreach (var l in layers) MapLayers.Add(l);
            });
        }

        /// <summary>Use the selected layer's feature geometry (union) as the STAC 'intersects' AOI.</summary>
        private async Task OnUseLayerAoiAsync()
        {
            var layer = SelectedLayer?.Layer;
            if (layer == null) { StatusMessage = "Pick a layer first."; return; }
            StatusMessage = "Reading layer geometry...";
            try
            {
                Geometry geom = await QueuedTask.Run(() =>
                {
                    var sel = layer.GetSelection();
                    bool hasSelection = sel != null && sel.GetCount() > 0;
                    var rows = new List<Geometry>();
                    if (hasSelection)
                    {
                        using (var rc = sel.Search())
                        {
                            while (rc.MoveNext())
                            {
                                if (rc.Current is ArcGIS.Core.Data.Feature f) { using (f) rows.Add(f.GetShape()); }
                                if (rows.Count > 1000) break; // safety cap
                            }
                        }
                    }
                    else
                    {
                        using (var rc = layer.Search())
                        {
                            while (rc.MoveNext())
                            {
                                if (rc.Current is ArcGIS.Core.Data.Feature f) { using (f) rows.Add(f.GetShape()); }
                                if (rows.Count > 1000) break; // safety cap
                            }
                        }
                    }
                    if (rows.Count == 0) return null;
                    var geoms = rows.Where(g => g != null).ToList();
                    if (geoms.Count == 0) return null;
                    var combined = GeometryEngine.Instance.Union(geoms);
                    return GeometryEngine.Instance.Project(combined, SpatialReferences.WGS84);
                });
                if (geom == null) { StatusMessage = "Layer has no geometry."; return; }
                SetAoi(geom);
            }
            catch (System.Exception ex) { StatusMessage = "Use layer failed: " + ex.Message; }
        }

        private void OnClearAoi()
        {
            _intersectsGeoJson = null;
            AoiText = null;
            StatusMessage = "AOI cleared.";
        }

        private void OnToggleSelectAll()
        {
            bool? allSelected = Results.Count > 0 && Results.All(r => r.IsSelected);
            bool newValue = !(allSelected ?? false);
            foreach (var r in Results) r.IsSelected = newValue;
            StatusMessage = newValue ? "All results selected." : "Selection cleared.";
        }

        private async Task OnSearchAsync(bool reset)
        {
            // cancel any in-flight search
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var ct = _searchCts.Token;

            IsSearchBusy = true;
            Results.Clear();
            ResultCount = 0;
            StatusMessage = "Searching...";

            try
            {
                // Which sources to query, and (for a fresh search) which of their collections to
                // filter to. If nothing is checked at all, search every source with no collection
                // filter -- same "search everything" behavior as before multi-source support.
                var checkedBySource = Collections.Where(c => c.IsChecked)
                    .GroupBy(c => c.Source)
                    .ToDictionary(g => g.Key, g => g.Select(c => c.Id).ToList());

                List<StacApiSource> sourcesToSearch;
                if (reset)
                {
                    sourcesToSearch = checkedBySource.Count > 0
                        ? checkedBySource.Keys.ToList()
                        : ApiSources.ToList();
                }
                else
                {
                    // Next page: only the sources that actually have a next link.
                    sourcesToSearch = ApiSources.Where(s => !string.IsNullOrEmpty(_nextPageUrls.GetValueOrDefault(s))).ToList();
                }

                var tasks = sourcesToSearch.Select(async source =>
                {
                    StacItemCollection page;
                    if (reset)
                    {
                        var query = new StacSearchQuery
                        {
                            Collections = checkedBySource.TryGetValue(source, out var ids) ? ids : null,
                            IntersectsGeoJson = _intersectsGeoJson,
                            StartDate = StartDate,
                            EndDate = EndDate,
                            FreeText = FreeText,
                            Limit = Limit
                        };
                        page = await source.Client.SearchAsync(query, ct);
                    }
                    else
                    {
                        page = await source.Client.GetPageAsync(_nextPageUrls[source], ct);
                    }
                    return (source, page);
                }).ToList();

                var pages = await Task.WhenAll(tasks);

                int totalMatched = 0;
                bool anyTotalMatched = false;
                foreach (var (source, page) in pages)
                {
                    _nextPageUrls[source] = page?.NextLinkHref;

                    if (page?.Features != null)
                    {
                        foreach (var f in page.Features)
                        {
                            var colVM = Collections.FirstOrDefault(c => c.Source == source && c.Id == f.Collection)
                                        ?? Collections.FirstOrDefault(c => c.Id == f.Collection);
                            Results.Add(new ResultItemViewModel(f, colVM?.Collection, loadThumbnail: !ThumbnailsDisabled));
                        }
                    }
                    if (page?.NumberMatched.HasValue == true)
                    {
                        totalMatched += page.NumberMatched.Value;
                        anyTotalMatched = true;
                    }
                }
                NotifyPropertyChanged(() => HasNextPage);

                // Indicate whether any results contain point-cloud assets (for conditional UI tips)
                HasPointCloudResults = Results.Any(r => IsPointCloudAsset(r.DataAsset));

                ResultCount = Results.Count;
                TotalMatched = anyTotalMatched ? totalMatched : (int?)null;
                StatusMessage = HasNextPage
                    ? $"Showing {Results.Count} of {TotalMatched?.ToString() ?? "?"} matched. (Next page available)"
                    : $"{Results.Count} result(s)" + (TotalMatched.HasValue ? $" of {TotalMatched} matched." : ".");
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Search cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Search failed: " + ex.Message;
            }
            finally { IsSearchBusy = false; }
        }

        #endregion
    }
}
