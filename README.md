# kyfromabove-stac-addin

ArcGIS Pro add-in for searching and downloading Kentucky From Above STAC imagery, DEM, and LiDAR
point cloud data.

## Documentation

Published at **https://ianhorn.github.io/kyfromabove-stac-addin/**, built with
[MkDocs](https://www.mkdocs.org/) and the [Material theme](https://squidfunk.github.io/mkdocs-material/).

Markdown source lives in [`docs-src/`](docs-src/index.md). A GitHub Actions workflow
([`.github/workflows/docs.yml`](.github/workflows/docs.yml)) builds the MkDocs site and deploys it
to GitHub Pages automatically on every push to `master` that touches `docs-src/`, `mkdocs.yml`, or
`docs-requirements.txt` — the built `docs/` output is never committed, it's ephemeral CI artifact
only. Repo Settings &rarr; Pages &rarr; Source must be set to **GitHub Actions**.

To preview locally:

```bash
pip install -r docs-requirements.txt
mkdocs serve
```

Then open <http://127.0.0.1:8000>.

To publish an update, just commit and push your `docs-src/` changes — the workflow rebuilds and
redeploys automatically:

```bash
git add docs-src
git commit -m "docs: update"
git push
```