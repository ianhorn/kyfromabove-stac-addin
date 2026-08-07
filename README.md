# kyfromabove-stac-addin

ArcGIS Pro add-in for searching and downloading Kentucky From Above STAC imagery, DEM, and LiDAR
point cloud data.

## Documentation

Published at **https://ianhorn.github.io/kyfromabove-stac-addin/**, built with
[MkDocs](https://www.mkdocs.org/) and the [Material theme](https://squidfunk.github.io/mkdocs-material/).

Markdown source lives in [`docs-src/`](docs-src/index.md); the `docs/` folder is the built HTML
site itself, served straight from the repo via GitHub Pages (Settings &rarr; Pages &rarr; Deploy
from a branch &rarr; `master` / `/docs`).

To preview locally:

```bash
pip install -r docs-requirements.txt
mkdocs serve
```

Then open <http://127.0.0.1:8000>.

To publish an update, rebuild and commit the regenerated `docs/` folder along with your `docs-src/`
changes:

```bash
mkdocs build
git add docs docs-src
git commit -m "docs: update"
git push
```