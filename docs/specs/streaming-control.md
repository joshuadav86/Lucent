# Streaming control

## What it does

Lucent controls another music app from its own player: song, cover, position, play/pause, next/previous, volume, and, where the app allows it, shuffle, repeat, its queue, and its playlists. While it controls an app, the visuals listen to that app alone.

| App | Windows | Mac |
| --- | --- | --- |
| Spotify | Local app for song and buttons (every 1 s), Web API for playlist, device, volume (every 30 s) | Same, local app through AppleScript |
| Apple Music | Windows' media controls | Music app through AppleScript |
| TIDAL | TIDAL app through media controls | tidal.com in a browser tab (the Mac app can't be controlled) |
| YouTube, YouTube Music, SoundCloud | One "Control your web browser" item, through media controls | A tab in Safari, Chrome, Edge, or Brave, through AppleScript and JavaScript |
| Pandora | Pandora's window through UI Automation, thumbs included | pandora.com in a browser tab, thumbs included |

**Spotify extras (both):** queue centred on the playing song with Sort (playlist order, recently added, title, artist); playlists and Liked Songs in the library up to 10,000 songs; click a playlist to open its songs; Add to playlist with a duplicate check; Share song link; "Play on this Mac/PC" when Spotify plays on a TV or speaker.

**Every app (both):** Share works for all (Apple Music link or song.link page when the app gives no link). When the app isn't open, an Open button replaces the volume bar. Queue and PLAYLISTS say why they're empty when an app can't share them.

**Pandora:** thumbs up/down (T, Shift+T) take shuffle/repeat's place.

## Rules

- Spotify rate limits: never poll the Web API faster than now (3 s playing, 5 s paused for the old path; 30 s with the local app). The dev-mode limit is shared by all users.
- Never run two Spotify sessions at once (refresh tokens rotate).
- Only call the local Spotify app while it's running, or macOS asks "Where is Spotify?".

## Parity gaps

Listed in [PARITY.md](../PARITY.md):
- Apple Music playlists, Apple Music queue, AirPlay "Play on this Mac", YouTube/YouTube Music queue, shuffle and repeat on web players, and site song links: Mac only. Windows' media controls give only the song and buttons.
- Not possible on either: YouTube and TIDAL playlists (need approved developer apps), SoundCloud (closed to new apps), Pandora playlists (no API), TIDAL web queue.

## Code

- `Lucent.Core/Streaming/` (`RemoteControl`, `RemotePlayer` interfaces, `SpotifyRemote`, `SpotifySignIn`, `SongLinks`)
- `Streaming/` in each app (`AppleMusicRemote`, `TidalRemote`, `YouTubeRemote`, `PandoraRemote`, `AppOpener`, `SpotifyAppRemote`; Windows `MediaControlsRemote`; Mac `BrowserTabRemote`, `SoundCloudRemote`)
- `MainWindow.Streaming.cs`, `MainWindow.Sharing.cs` in each app

## Not verified

Browser-tab selectors, Pandora on Windows, AirPlay AppleScript, and the Open button haven't been tried against the real apps from CI. Check on a real PC and Mac after changes.
