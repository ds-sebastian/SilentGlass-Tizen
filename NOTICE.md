# Attribution and distribution

SilentGlass-Tizen is a fork of [Quiet Tizen](https://github.com/jss8649/quiet-tizen), commit
`cf518d7a9278dca7990e27756218258e44044a29`, copyright 2026 Quiet Tizen contributors, MIT (kept in `LICENSE`).
Its reversible-cleanup method, install recipes, Probe tool and agent workflow are the base this project builds on;
the original U8000F-specific access tooling and case study were removed, and what they taught about Samsung's
services is kept in [docs/samsung-tizen-notes.md](docs/samsung-tizen-notes.md).

Quiet Home, the launcher SilentGlass grew out of, adapted layout and palette ideas from
[Free My TV](https://github.com/simbasjungles/free-my-tv/tree/73409804e5085e720dccea9ab95078a515cae098),
commit `73409804e5085e720dccea9ab95078a515cae098`, copyright 2026 FreeMyTV contributors, MIT
(`licenses/FreeMyTV-MIT.txt`). The Android APK is not used.

The bundled Quiet Inter fonts are static derivatives of Inter, copyright 2016 The Inter Project Authors, renamed
after conversion (Medium: weight 500, optical size 14; Semibold: 600 / 14; Display: 600 / 32). See
`licenses/Inter-OFL.txt`; the fonts keep the SIL Open Font License.

The symbols (`launcher/assets/sym-*.svg`) are drawn by `tools/make_symbols.py` for this project.
`devices/s90d/device/privacy/helper/` adapts native metadata bindings from
[s90c-tools](https://github.com/invaliddayta/s90c-tools) (MIT), as noted in its source.

No Samsung SDB executable, firmware, SDK/reference assembly, stock app binary, account database or private backup is
distributed.

Publisher streaming-app artwork is not bundled. `launcher/artwork-sources.json` records optional sources and
transformations; `tools/fetch_artwork.py` downloads selected artwork into ignored local files. Those assets and
trademarks remain subject to their owners' terms. The repository and its contributors are not affiliated with
Samsung, NVIDIA, Nintendo, Sony, Microsoft, Amazon, Apple, Google, the Thread Group or any streaming service.

The S90D photo wallpaper (macOS Catalina Day/Night) is Apple artwork: it is not distributed, and
`tools/make_wallpapers.py` builds it from images you supply. The screenshots in `docs/screenshots/` show it as
rendered on the owner's TV; remove or replace them before publishing this repository.
