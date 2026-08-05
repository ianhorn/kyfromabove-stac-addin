/*
 * Draw AOI map tools: let the user sketch a point, line, or polygon on the map.
 * The resulting geometry is projected to WGS84 (lon/lat), converted to GeoJSON,
 * and passed to the search dockpane to be used as the STAC 'intersects' AOI.
 *
 * One tool class per sketch type (each with a fixed SketchType set in the
 * constructor) — this is the reliable Pro SDK pattern: a single tool instance
 * with a dynamically-changed SketchType does not switch sketch behavior.
 */
using System.Threading.Tasks;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework;
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
}

