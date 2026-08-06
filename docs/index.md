# KyFromAbove-STAC Browser

**KyFromAbove-STAC Browser** is an ArcGIS Pro add-in for searching, previewing, and downloading
aerial imagery, DEM, and LiDAR point cloud data from the [Kentucky From Above](https://kyfromabove.ky.gov/)
STAC API -- without leaving ArcGIS Pro.

It adds a dockable **KyFromAbove-STAC** pane with tools to build an area of interest from the map,
filter by collection and date, run a [STAC](https://stacspec.org/) search, and add or download the
results, all backed by a standard [STAC API](https://github.com/radiantearth/stac-api-spec).

## Features

- **Search** the built-in KyFromAbove STAC catalog, or [bring your own STAC API](collections-and-sources.md)
  and search it alongside (or instead of) the built-in one.
- **Area of interest** tools: draw a point/line/polygon on the map, use the current map extent, use
  an existing layer's selection or full extent, or load an AOI from a `.shp`/`.geojson` file -- even
  before a map is open.
- **Filter** by collection, date range, and free text.
- **Preview** results with thumbnails and a full item-detail tooltip (the raw STAC JSON) on hover.
- **Add to map** individual raster (COG) results, or build one merged mosaic dataset from all of them.
- **Download** selected assets (raster or point-cloud) in parallel, with a cancelable progress dialog.
- **Footprints**: draw result footprints on the map to see coverage at a glance.

## Where to start

- [**Installation**](installation.md) -- build and deploy the add-in in ArcGIS Pro
- [**Getting Started**](getting-started.md) -- your first search, end to end
- [**Area of Interest**](area-of-interest.md) -- every way to define a search AOI
- [**Downloads**](downloads.md) -- parallel downloads and folder options

!!! note "Built on open standards"
    The add-in speaks plain [STAC](https://stacspec.org/) -- the same API style used by
    Development Seed's [TiTiler](https://developmentseed.org/titiler/) and
    [stac-fastapi](https://github.com/stac-utils/stac-fastapi) projects. Any STAC API that supports
    `/collections` and `/search` can be added as a source; see
    [Bring Your Own API](collections-and-sources.md#bring-your-own-api).
