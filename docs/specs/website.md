# Website

lucentaudio.net, served by GitHub Pages from `site/` (`pages.yml`).

## What it does

- One page: hero window tour (layered full-screen shot; chips toggle windows, click to zoom), visuals gallery with family counts, editor, analyzer, radio, and live video radio sections, downloads, and coffee link.
- Headline: "See what you're hearing." Lucent stands on its own: no Winamp comparisons in copy or branding (2026-10-30). Copy stays short. "Beta" labels until 1.0.
- Display font is Sora (no fonts with notched letters).
- Download buttons point at the "latest" release; once a stable release exists, `pages.yml` rewrites them to `releases/latest/download`.
- GoatCounter counts visits and download clicks (private dashboard). CI posts GitHub download and update-check counts there before each release is replaced.

## Screenshots

- App screenshots are drawn headless by the Mac app on Linux: `Lucent --screenshots <dir> <Names>` (`Screenshots*.cs`). Env vars `LUCENT_PREVIEW_COVERS`, `LUCENT_PREVIEW_VISUAL`, `LUCENT_PREVIEW_VIDEO`, `LUCENT_PREVIEW_SCALE`, and `LUCENT_PREVIEW_LAYERS` fill in sample art.
- When the player's look changes, re-render the tour layers and screenshots.
- Motion: the tour's player, equalizer, analyzer, and visual play from one 60 fps atlas video (`img/tour/live.mp4`, drawn into canvases), and every gallery tile and the player section use muted loops in `img/clips` (each with a poster from its first frame) that play only on screen; a once-a-second check restarts any the browser pauses or stalls. All move to Kepler North's Self Actualize (the starter song). `LUCENT_PREVIEW_CLIP` (`start:seconds`) with `LUCENT_PREVIEW_AUDIO` (a WAV) makes FullScreen save every frame of the moving windows (`Screenshots.Clip.cs`, which turns off the analyzer's and equalizer's own frame ticks so they move at the song's pace); visuals come from their real shaders, rendered offline. The README's `site/img/lucent-live.webp` is the same clip at 30 fps.
- The README's images come from `site/img`, so they update with the site.

## Counts to keep in step

Total visuals, Lucent originals, MilkDrop, AVS, and family counts appear in `site/index.html` (and the `V` gallery object) and in the README's intro and first bullet.

## Checks

Site-only changes skip the app builds (`tools/ci/apps-changed.sh`).
