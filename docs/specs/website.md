# Website

lucentaudio.net, served by GitHub Pages from `site/` (`pages.yml`).

## What it does

- One page: hero window tour (layered full-screen shot; chips toggle windows, click to zoom), visuals gallery with family counts, editor, analyzer, radio, and live video radio sections, downloads, and coffee link.
- Framing: "A spiritual successor to Winamp, built for audio nerds." Copy stays short. "Beta" labels until 1.0.
- Display font is Sora (no fonts with notched letters).
- Download buttons point at the "latest" release; once a stable release exists, `pages.yml` rewrites them to `releases/latest/download`.
- GoatCounter counts visits and download clicks (private dashboard). CI posts GitHub download and update-check counts there before each release is replaced.

## Screenshots

- App screenshots are drawn headless by the Mac app on Linux: `Lucent --screenshots <dir> <Names>` (`Screenshots*.cs`). Env vars `LUCENT_PREVIEW_COVERS`, `LUCENT_PREVIEW_VISUAL`, `LUCENT_PREVIEW_VIDEO`, `LUCENT_PREVIEW_SCALE`, and `LUCENT_PREVIEW_LAYERS` fill in sample art.
- When the player's look changes, re-render the tour layers and screenshots.
- The README's images come from `site/img`, so they update with the site.

## Counts to keep in step

Total visuals, Lucent originals, MilkDrop, AVS, and family counts appear in `site/index.html` (and the `V` gallery object) and in the README's intro and first bullet.

## Checks

Site-only changes skip the app builds (`tools/ci/apps-changed.sh`).
