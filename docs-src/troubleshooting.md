# Troubleshooting

## Thumbnails aren't showing

A result row's **Status** text will say `Thumbnail unavailable: ...` with the actual reason
(network error, unreachable host, unresolvable relative URL, etc.) if a thumbnail fails to load.
If you see that message, check that ArcGIS Pro has network access to the STAC API and its asset
host. If a thumbnail simply never loaded and no status text appears, the item likely has no
thumbnail-style asset at all -- not a bug.

## "Open a map view first"

Several tools (Draw AOI, Use Extent, Use Layer, Mosaic All to Map, Show Footprints) operate on the
active map, so they need a map view open in the project. Open or create a map, then retry.

## The Layer dropdown is empty

Click **Refresh** next to the dropdown. It also refreshes automatically whenever a map view
becomes active (opening a map, switching map tabs, opening the project), so this should be rare.

## `git push` fails with "Password authentication is not supported"

GitHub removed password-based Git authentication in 2021. Use a
[personal access token](https://github.com/settings/tokens) as the password, or authenticate once
with `gh auth login` (GitHub CLI) and run `gh auth setup-git` so future pushes just work.

## Still stuck?

Click **Feedback** (ribbon or the pane's title row) to report it -- as a GitHub issue, or sent
directly with no GitHub account needed. Either way, the add-in version, ArcGIS Pro version, and OS
are attached automatically.
