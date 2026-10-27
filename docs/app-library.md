# App library and artwork

The Apps menu (above the bar) opens apps that are already installed on the TV. A catalog entry does not download an
app or create a subscription. It shows up to 11 apps from the catalog plus Samsung Home, four to a row.

The public catalog `launcher/assets/apps.json` is an example (Netflix, YouTube, Prime Video, HBO Max, Disney+, Hulu,
Spotify, Apple TV, Tubi, Amazon Luna, Xbox) with text-only placeholder cards. App IDs vary by region, release and
package: match them against your TV's own app list, and test each one.

Copy it to the ignored `launcher/assets/apps.local.json` to customize it; `tools/build.py launcher` packages the
local file as `apps.json`. Each entry has `Name`, `Id` (the native app ID), `Asset` (a path inside the resource
directory), `Fit` (`cover`, `contain` or `contain-full`), `Background` (behind a contained image), and the asset's
`Width` and `Height`. An optional `Crop` is a normalized `[x, y, width, height]` of the asset. SilentGlass refuses
the whole catalog if an entry is incomplete, an ID repeats, a path leaves the resource directory or a crop leaves
the image.

## Publisher artwork (optional, local only)

Official artwork is fetched locally rather than redistributed under this project's license:

```sh
python3 -m venv .venv
.venv/bin/python -m pip install Pillow
.venv/bin/python tools/fetch_artwork.py --all
# or only some:
.venv/bin/python tools/fetch_artwork.py --app 'Prime Video' --app Xbox
python3 tools/build.py launcher --framework .local/tv-framework
```

Downloads go to the ignored `launcher/assets/app-icons/`, the updated catalog to `apps.local.json` (app IDs you
edited there are kept), and source and output hashes to `.local/artwork-receipts.json`. The downloader checks
raster sizes, reads only the chosen member of a ZIP and rejects SVG with scripts or external references. If a
publisher URL changes, that entry keeps its previous artwork and the command reports the failure.

Sources, per asset: [artwork-sources.json](../launcher/artwork-sources.json). Netflix, Hulu and Spotify are
publisher vector artwork; Prime Video is Amazon's 2024 wordmark (via Wikimedia Commons, with a white fill); YouTube
and Disney+ large brand artwork; HBO Max, Apple TV and Tubi 1024-pixel App Store artwork; Luna Amazon artwork; Xbox
the logo from Microsoft's 2560×1440 wallpaper (raster, not a vector), framed with a `Crop` whose decode size accounts
for the crop, so the card stays sharp.

Catalog and artwork changes need no rebuild of the DLL, only a redeploy of the resources (backup first, keep the
ownership and SMACK labels, then restart SilentGlass so it reads the catalog again).
