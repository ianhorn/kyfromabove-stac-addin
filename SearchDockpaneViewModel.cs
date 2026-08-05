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
using KyFromAbove.Stac;

namespace KyFromAbove
{
    internal class SearchDockpaneViewModel : DockPane
    {
        private const string _dockPaneID = "KyFromAbove_SearchDockpane";

        private readonly StacClient _client;
        private string _nextPageUrl;
        private CancellationTokenSource _searchCts;

        #region CTOR + Show

        protected SearchDockpaneViewModel()
        {
            _client = Module1.Current.StacClient;
            Collections = new ObservableCollection<CollectionCheckViewModel>();
            Results = new ObservableCollection<ResultItemViewModel>();
            StatusMessage = "Load collections to begin.";
            SearchCommand = new RelayCommand(async () => await OnSearchAsync(reset: true), () => !IsSearchBusy);
            NextPageCommand = new RelayCommand(async () => await OnSearchAsync(reset: false), () => !IsSearchBusy && !string.IsNullOrEmpty(_nextPageUrl));
            LoadCollectionsCommand = new RelayCommand(async () => await OnLoadCollectionsAsync(), () => !IsSearchBusy);
            DrawPointAoiCommand = new RelayCommand(async () => await OnDrawAoiAsync(DrawPointAoiTool.ToolId), () => !IsSearchBusy);
            DrawLineAoiCommand = new RelayCommand(async () => await OnDrawAoiAsync(DrawLineAoiTool.ToolId), () => !IsSearchBusy);
            DrawPolygonAoiCommand = new RelayCommand(async () => await OnDrawAoiAsync(DrawPolygonAoiTool.ToolId), () => !IsSearchBusy);
            RefreshLayersCommand = new RelayCommand(async () => await OnRefreshLayersAsync(), () => !IsSearchBusy);
            UseLayerAoiCommand = new RelayCommand(async () => await OnUseLayerAoiAsync(), () => !IsSearchBusy && SelectedLayer != null);
                        MosaicAllCommand = new RelayCommand(async () => await OnMosaicAllAsync(), () => !IsSearchBusy && Results.Count > 0);
                        ClearAoiCommand = new RelayCommand(OnClearAoi, () => !IsSearchBusy);
            UseExtentAoiCommand = new RelayCommand(async () => await OnUseExtentAoiAsync(), () => !IsSearchBusy);
            DownloadAllCommand = new RelayCommand(async () => await OnDownloadAllAsync(), () => !IsSearchBusy && Results.Count > 0);
            BrowseDownloadFolderCommand = new RelayCommand(() => OnBrowseDownloadFolder());
            ShowFootprintsCommand = new RelayCommand(async () => await OnShowFootprintsAsync(), () => !IsSearchBusy && Results.Count > 0);
            ToggleSelectAllCommand = new RelayCommand(() => OnToggleSelectAll(), () => !IsSearchBusy && Results.Count > 0);

            var projectDir = Path.GetDirectoryName(Project.Current.DefaultGeodatabasePath);
            if (string.IsNullOrWhiteSpace(projectDir)) projectDir = Path.GetTempPath();
            DownloadFolder = Path.Combine(projectDir, "downloads");
            Directory.CreateDirectory(DownloadFolder);
            MapLayers = new ObservableCollection<LayerViewModel>();
            _ = OnRefreshLayersAsync(); // populate on load
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
        public int Limit
        {
            get => _limit;
            set => SetProperty(ref _limit, value, () => Limit);
        }

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
            set => SetProperty(ref _resultCount, value, () => ResultCount);
        }

        private int? _totalMatched;
        public int? TotalMatched
        {
            get => _totalMatched;
            set => SetProperty(ref _totalMatched, value, () => TotalMatched);
        }

                private bool _buildOverviews = true;
        /// <summary>If checked (default), the mosaic's overviews are defined + built after the rasters are added.</summary>
        public bool BuildOverviews
        {
            get => _buildOverviews;
            set => SetProperty(ref _buildOverviews, value, () => BuildOverviews);
        }

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

        private bool _downloadPerItemFolder;
        /// <summary>If true, each item downloads into its own subfolder under DownloadFolder. Off by default (flat into DownloadFolder).</summary>
        public bool DownloadPerItemFolder
        {
            get => _downloadPerItemFolder;
            set => SetProperty(ref _downloadPerItemFolder, value, () => DownloadPerItemFolder);
        }

        public bool HasNextPage => !string.IsNullOrEmpty(_nextPageUrl);

        public ICommand SearchCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand LoadCollectionsCommand { get; }
        public ICommand DrawPointAoiCommand { get; }
        public ICommand DrawLineAoiCommand { get; }
        public ICommand DrawPolygonAoiCommand { get; }
        public ICommand RefreshLayersCommand { get; }
                        public ICommand UseLayerAoiCommand { get; }
        public ICommand UseExtentAoiCommand { get; }
        public ICommand MosaicAllCommand { get; }
        public ICommand ClearAoiCommand { get; }
        public ICommand DownloadAllCommand { get; }
        public ICommand BrowseDownloadFolderCommand { get; }
        public ICommand ShowFootprintsCommand { get; }
        public ICommand ToggleSelectAllCommand { get; }

        #endregion

        #region Command handlers

        private async Task OnLoadCollectionsAsync()
        {
            IsSearchBusy = true;
            StatusMessage = "Loading collections...";
            try
            {
                var cols = await _client.GetCollectionsAsync();
                Collections.Clear();
                foreach (var c in cols.OrderBy(x => x.TitleOrId, StringComparer.OrdinalIgnoreCase))
                    Collections.Add(new CollectionCheckViewModel(c));
                StatusMessage = Collections.Count > 0
                    ? $"{Collections.Count} collections loaded. Choose filters and search."
                    : "No collections returned.";
            }
            catch (Exception ex)
            {
                StatusMessage = "Error loading collections: " + ex.Message;
            }
            finally { IsSearchBusy = false; }
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

        /// <summary>Build ONE mosaic dataset layer from selected (or all) imagery/DEM result COGs and add it to the map. Skips point-cloud (LAZ) assets.</summary>
        private async System.Threading.Tasks.Task OnMosaicAllAsync()
        {
            var rasterResults = Results.Where(r => r.DataAsset != null && IsRasterAsset(r.DataAsset)).ToList();
            var selected = rasterResults.Where(r => r.IsSelected).ToList();
            var hrefs = (selected.Count > 0 ? selected : rasterResults).Select(r => r.DataAsset.Href).ToList();
            if (hrefs.Count == 0) { StatusMessage = "No raster (COG) results to mosaic."; return; }

            var buildOverviews = BuildOverviews;
            StatusMessage = "Building mosaic dataset layer...";

            // Show a closeable progress dialog so the user can watch (and dismiss) the long-running work.
            var dlg = new ProgressDialog("Mosaic progress");
            dlg.Show();

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

                    // 1) Create Mosaic Dataset: (out_gdb_path, in_mosaic_dataset_name, spatial_reference)
                    dlg.Append("Creating mosaic dataset...");
                    var createArgs = Geoprocessing.MakeValueArray(gdb, mosaicName, sr);
                    var createRes = await Geoprocessing.ExecuteToolAsync("management.CreateMosaicDataset", createArgs)
                        .ConfigureAwait(true);
                    if (createRes.IsFailed) { StatusMessage = "CreateMosaicDataset failed: " + GpMessages(createRes); return; }
                    string mosaicPath = System.IO.Path.Combine(gdb, mosaicName);

                    // 2) Add Rasters To Mosaic Dataset: (in_mosaic_dataset, raster_type, data_path)
                    dlg.Append($"Adding {hrefs.Count} rasters to mosaic...");
                    var addArgs = Geoprocessing.MakeValueArray(mosaicPath, "Raster Dataset", string.Join(";", hrefs));
                    var addRes = await Geoprocessing.ExecuteToolAsync("management.AddRastersToMosaicDataset", addArgs)
                        .ConfigureAwait(true);
                    if (addRes.IsFailed) { StatusMessage = "AddRastersToMosaicDataset failed: " + GpMessages(addRes); return; }

                    // (optional) Define + Build overviews for faster display at small scales.
                    if (buildOverviews)
                    {
                        dlg.Append("Defining overviews...");
                        var defRes = await Geoprocessing.ExecuteToolAsync("management.DefineOverviews",
                            Geoprocessing.MakeValueArray(mosaicPath)).ConfigureAwait(true);
                        if (defRes.IsFailed) { StatusMessage = "DefineOverviews failed: " + GpMessages(defRes); return; }

                        dlg.Append("Building overviews (this can take a while)...");
                        var buildRes = await Geoprocessing.ExecuteToolAsync("management.BuildOverviews",
                            Geoprocessing.MakeValueArray(mosaicPath)).ConfigureAwait(true);
                        if (buildRes.IsFailed) { StatusMessage = "BuildOverviews failed: " + GpMessages(buildRes); return; }
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
                dlg.Append("Done.");
                try { dlg.Close(); } catch { /* ignore if already closed by user */ }
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

            // Selected results; fall back to all raster results if nothing is explicitly selected.
            var toDownload = Results
                .Where(r => r.IsSelected && r.DataAsset != null && IsRasterAsset(r.DataAsset))
                .ToList();
            if (toDownload.Count == 0)
                toDownload = Results.Where(r => r.DataAsset != null && IsRasterAsset(r.DataAsset)).ToList();
            if (toDownload.Count == 0) { StatusMessage = "No raster results to download."; return; }

            var concurrency = Math.Max(1, DownloadConcurrency);
            var dl = new Services.DownloadService(Module1.Current.StacClient);
            var cts = new CancellationTokenSource();

            var dlg = new ProgressDialog("Downloading files");
            dlg.Append($"Downloading {toDownload.Count} asset(s) to:\n  {DownloadFolder}\n({concurrency} parallel thread(s))");
            dlg.Show();
            dlg.Closing += (s, e) => cts.Cancel(); // user closing the window cancels the downloads

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
                    var dest = Path.Combine(destDir, fname);

                    // If flat download, avoid filename collisions by prefixing with item id.
                    if (!DownloadPerItemFolder && !string.IsNullOrWhiteSpace(item.Item.Id))
                    {
                        dest = Path.Combine(destDir, item.Item.Id + "_" + fname);
                    }
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
                dlg.Append("Download complete.");
                try { dlg.Close(); } catch { /* already closed by user */ }
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
                StatusMessage = ok ? "Footprints added to map." : "Failed to add footprints.";
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
                StacItemCollection page;
                if (reset)
                {
                    var query = new StacSearchQuery
                    {
                        Collections = Collections.Where(c => c.IsChecked).Select(c => c.Id).ToList(),
                        IntersectsGeoJson = _intersectsGeoJson,
                        StartDate = StartDate,
                        EndDate = EndDate,
                        FreeText = FreeText,
                        Limit = Limit
                    };
                    page = await _client.SearchAsync(query, ct);
                }
                else
                {
                    page = await _client.GetPageAsync(_nextPageUrl, ct);
                }

                _nextPageUrl = page?.NextLinkHref;
                NotifyPropertyChanged(() => HasNextPage);

                if (page?.Features != null)
                {
                    foreach (var f in page.Features)
                    {
                        var colVM = Collections.FirstOrDefault(c => c.Id == f.Collection);
                        Results.Add(new ResultItemViewModel(f, colVM?.Collection));
                    }
                }

                ResultCount = Results.Count;
                TotalMatched = page?.NumberMatched;
                StatusMessage = _nextPageUrl != null
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
