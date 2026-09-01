# Downloads

## Downloading selected results

Click **Download Selected** (above the results list). If nothing is selected, every downloadable
result is used instead. You'll be prompted to **pick a destination folder** -- there's no
persistent "download to" box, so this happens fresh each time; the dialog starts in whatever folder
you picked last.

A progress dialog shows per-asset status as downloads run, with a **Cancel** button that stops any
downloads still in progress (already-completed files are kept).

Both raster (COG) and point-cloud (`.laz`/`.las`, etc.) assets can be downloaded this way, even
though point-clouds can't be added directly to the map.

<!-- SCREENSHOT: images/downloads-progress.png -- the download progress dialog mid-run, showing
     several assets with per-asset status and the Cancel button -->

## Parallel Downloads

The **Parallel Downloads ▾** button (bottom of the pane) controls how many assets download at once:

| Option | Meaning |
|---|---|
| All but 1 core | `core count - 1` parallel downloads |
| 75% *(default)* | 75% of available cores |
| 50% | 50% of available cores |
| 25% | 25% of available cores |
| Custom | Enter any number in the box that appears |

## Download each item to its own folder

The **Item folders** checkbox next to **Parallel Downloads** controls folder layout:

- **Unchecked** *(default)* -- all files download flat into the chosen folder. Filenames are
  prefixed with the item ID to avoid collisions.
- **Checked** -- each item gets its own subfolder, named after the item ID.

<!-- SCREENSHOT: images/downloads-parallel-options.png -- the bottom of the pane showing the
     Parallel Downloads dropdown open and the Item folders checkbox/label -->

## Downloading a single item

Each result row also has its own **Download** button, which prompts for a save location (a single
file, via a standard Save dialog) instead of using the shared destination-folder flow above.
