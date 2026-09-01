# Getting Started

## Open the pane

Click the **KyFromAbove-STAC-AddIn** tab, then **STAC Search**. This opens a dockable pane (docked
alongside the Contents pane by default) that stays open as you work.

<!-- SCREENSHOT: images/getting-started-ribbon.png -- ArcGIS Pro ribbon with the
     KyFromAbove-STAC-AddIn tab active and the STAC Search button visible -->

<!-- SCREENSHOT: images/getting-started-dockpane.png -- the dockpane open alongside the Contents
     pane, showing the overall layout (Collections, AOI, filters, results) before any search -->

## A first search, end to end

1. **Load collections.** Under **Collections**, click **Load collections**. This populates the
   checkbox list from the built-in KyFromAbove catalog (and any [extra API sources](collections-and-sources.md)
   you've added). Leave everything unchecked to search *all* collections, or check specific ones to
   narrow the search.
2. **Set an area of interest** *(optional)*. Use the **Draw** dropdown, **Use Extent**, **Use
   Layer**, or **Browse for AOI...** -- see [Area of Interest](area-of-interest.md) for details.
   Skip this step to search without a spatial filter.
3. **Set a date range** *(optional)*. Expand **Date range (optional)** and pick start/end dates.
4. **Set a result limit.** The **Limit** dropdown offers 50/100/200/500 presets or a custom value
   (default: 10). Limits above 50 automatically disable thumbnail loading to save network traffic.
5. **Click Search.** Results appear below with a summary line (e.g. "Showing 10 of 340 matched").
   If more results are available, a **Next page** button appears next to the status message.
6. **Work with results** -- select items, download them, add rasters to the map, or view
   footprints. See [Search & Results](search-and-results.md) and [Downloads](downloads.md).

<!-- SCREENSHOT: images/getting-started-first-results.png -- a completed search with a handful of
     results showing thumbnails, the summary line, and Next page button -->

!!! tip "Hover for details"
    Hover over any result row to see its full underlying STAC item JSON (id, properties, assets,
    links) as a tooltip -- useful for checking exact capture dates, CRS, or asset hrefs.
