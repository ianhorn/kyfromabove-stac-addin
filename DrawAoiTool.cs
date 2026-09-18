/*
 * Draw AOI map tools: let the user sketch a point, line, or polygon on the map.
 * The resulting geometry is projected to WGS84 (lon/lat), converted to GeoJSON,
 * and passed to the search dockpane to be used as the STAC 'intersects' AOI.
 *
 * One tool class per sketch type (each with a fixed SketchType set in the
 * constructor) — this is the reliable Pro SDK pattern: a single tool instance
 * with a dynamically-changed SketchType does not switch sketch behavior.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;

namespace KyFromAboveSTAC
{
    internal class DrawAoiToolBase : MapTool
    {
        protected DrawAoiToolBase(SketchGeometryType sketchType) : base()
        {
            IsSketchTool = true;
            SketchType = sketchType;
            SketchOutputMode = SketchOutputMode.Map; // geometry in map coordinates
        }

        protected override Task<bool> OnSketchCompleteAsync(Geometry geometry)
        {
            if (geometry == null) return Task.FromResult(false);

            // Project to WGS84 (lon/lat) so the GeoJSON matches STAC's CRS84 expectation.
            Geometry wgs = GeometryEngine.Instance.Project(geometry, SpatialReferences.WGS84);

            var pane = FrameworkApplication.DockPaneManager.Find("KyFromAbove_SearchDockpane") as SearchDockpaneViewModel;
            pane?.SetAoi(wgs);

            // Bring the dockpane forward so the user sees the captured AOI.
            FrameworkApplication.DockPaneManager.Find("KyFromAbove_SearchDockpane")?.Activate();

            return Task.FromResult(true);
        }
    }

    internal class DrawPointAoiTool : DrawAoiToolBase
    {
        public const string ToolId = "KyFromAbove_DrawPointAoiTool";
        public DrawPointAoiTool() : base(SketchGeometryType.Point) { }
    }

    internal class DrawLineAoiTool : DrawAoiToolBase
    {
        public const string ToolId = "KyFromAbove_DrawLineAoiTool";
        public DrawLineAoiTool() : base(SketchGeometryType.Line) { }
    }

    internal class DrawPolygonAoiTool : DrawAoiToolBase
    {
        public const string ToolId = "KyFromAbove_DrawPolygonAoiTool";
        public DrawPolygonAoiTool() : base(SketchGeometryType.Polygon) { }
    }

    /// <summary>
    /// Lets the user click (or drag a box over) existing map features and uses their unioned
    /// geometry as the AOI, instead of sketching a new one. Uses MapView.SelectFeatures so the
    /// picked features get the normal Pro selection highlight, matching the built-in Select tool.
    /// </summary>
    internal class SelectFeatureAoiTool : MapTool
    {
        public const string ToolId = "KyFromAbove_SelectFeatureAoiTool";

        public SelectFeatureAoiTool()
        {
            IsSketchTool = true;
            SketchType = SketchGeometryType.Rectangle;
            // Screen (not Map) coordinates -- MapView.SelectFeatures throws in 3D scenes if handed
            // a map-coordinate sketch ("3D views only support selecting features interactively
            // using geometry in screen coordinates..."); screen coordinates work for both 2D maps
            // and 3D scenes, so this is the one mode that works everywhere.
            SketchOutputMode = SketchOutputMode.Screen;
        }

        protected override async Task<bool> OnSketchCompleteAsync(Geometry geometry)
        {
            if (geometry == null) return false;

            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                var (ok, unioned, count) = await QueuedTask.Run(() =>
                {
                    var mapView = MapView.Active;
                    if (mapView == null) return (false, (Geometry)null, 0);

                    var selection = mapView.SelectFeatures(geometry, SelectionCombinationMethod.New);
                    if (selection.Count == 0) return (false, (Geometry)null, 0);

                    var mapSr = mapView.Map.SpatialReference;
                    var shapes = new List<Geometry>();
                    foreach (var kvp in selection.ToDictionary())
                    {
                        if (kvp.Key is not FeatureLayer featureLayer) continue;

                        using var cursor = featureLayer.GetTable().Search(new QueryFilter { ObjectIDs = kvp.Value.ToList() }, false);
                        while (cursor.MoveNext())
                        {
                            using var feature = (Feature)cursor.Current;
                            var shape = feature.GetShape();
                            if (shape == null) continue;
                            if (mapSr != null && shape.SpatialReference != null && !shape.SpatialReference.IsEqual(mapSr))
                                shape = GeometryEngine.Instance.Project(shape, mapSr);
                            shapes.Add(shape);
                        }
                    }

                    if (shapes.Count == 0) return (false, (Geometry)null, 0);

                    // Union requires matching geometry dimension (point/multipoint vs polyline vs
                    // polygon/envelope) -- batch-union within each dimension group (fast, one native
                    // call instead of N-1 pairwise calls), then fold the handful of group results.
                    var result = shapes
                        .GroupBy(s => s.GeometryType)
                        .Select(g => g.Count() == 1 ? g.First() : GeometryEngine.Instance.Union(g))
                        .Aggregate((a, b) => GeometryEngine.Instance.Union(a, b));

                    return (true, result, shapes.Count);
                });

                if (!ok)
                {
                    MessageBox.Show("No feature found there. Click directly on a feature, or drag a box over one.",
                        "KyFromAbove-STAC", MessageBoxButton.OK, MessageBoxImage.Information);
                    return false;
                }

                // Project to WGS84 (lon/lat) so the GeoJSON matches STAC's CRS84 expectation.
                Geometry wgs = GeometryEngine.Instance.Project(unioned, SpatialReferences.WGS84);

                var pane = FrameworkApplication.DockPaneManager.Find("KyFromAbove_SearchDockpane") as SearchDockpaneViewModel;
                pane?.SetAoi(wgs, $"AOI from {count} selected feature(s)");

                // Bring the dockpane forward so the user sees the captured AOI.
                FrameworkApplication.DockPaneManager.Find("KyFromAbove_SearchDockpane")?.Activate();

                return true;
            }
            catch (Exception ex)
            {
                // A malformed feature geometry (e.g. a bad SR/Z mismatch written by some other tool)
                // could throw here during Project/Union -- show it instead of letting it escape
                // OnSketchCompleteAsync unhandled, which has previously crashed Pro entirely rather
                // than just failing this one selection.
                MessageBox.Show($"Couldn't use the selected feature(s) as an AOI: {ex.Message}",
                    "KyFromAbove-STAC", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }
    }
}

