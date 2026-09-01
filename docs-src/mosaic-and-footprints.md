# Mosaic & Footprints

## Mosaic All to Map

Builds **one** mosaic dataset layer from the current raster (COG) results and adds it to the active
map -- useful for viewing many tiles as a single seamless layer instead of adding them one at a
time.

- Uses the **selected** results if any are checked; otherwise uses every raster result in the list.
  Point-cloud assets are skipped automatically.
- **Build overviews** *(checked by default)* defines and builds raster overviews after the mosaic
  is created, for faster display at small map scales. Unchecking it skips that step (faster to
  create, slower to redraw when zoomed out).
- Requires an open map view (the mosaic is created in the project's default geodatabase, using the
  map's spatial reference).
- A progress dialog reports each step (create dataset, add rasters, define/build overviews) and
  has a **Cancel** button. Cancelling takes effect before the *next* step starts -- it can't
  interrupt a geoprocessing step that's already running.

<!-- SCREENSHOT: images/mosaic-on-map.png -- a mosaic dataset layer added to the map from several
     search results, shown as one seamless raster in the Contents pane and map view -->

## Show Footprints

Draws the footprint of every current result as an outline on a **KyFromAbove Footprints** graphics
layer, then zooms to their combined extent. Re-running it replaces the previous footprints rather
than stacking duplicates. Requires an open map view.

Each result row also has its own **Footprint** button to draw just that one item's footprint (and
zoom to it) without affecting the others.

<!-- SCREENSHOT: images/footprints-on-map.png -- the KyFromAbove Footprints graphics layer showing
     several outlined footprints on the map, zoomed to their combined extent -->
