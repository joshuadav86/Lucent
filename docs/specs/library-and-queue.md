# Library and queue

## What it does

**Library (L):**
- Folders you add, plus single dropped files. A folder with subfolders asks whether each subfolder gets its own heading.
- Search, sort, store headings, and small album covers beside every song (classic skins keep plain rows).
- PLAYLISTS view: saved playlists, favorites, imported playlists, and the controlled app's playlists (see [Streaming control](streaming-control.md)).
- Edit sound info (Ctrl+I): tags and cover; Detect fills in BPM and key.
- The song playing is marked with a bar and kept in the middle of the list: in folders, saved playlists, and an opened Spotify or Apple Music playlist. On each new song the list glides there (about 0.4 s, eased); a song far away jumps to a screen and a half short first, so the glide is short in any size of list. After a manual scroll or arrow key the list is left alone for 4 seconds, then catches up.

**Queue (Q):** what plays next. Shuffle (S), repeat (R), favorite (H).

**Sources panel (M):** add music, tune in to radio, or connect a music app.

**Import:** Apple Music/iTunes, MusicBee, foobar2000, playlist files (M3U and friends), file tags, Plex, and YouTube Music (Google Takeout). Loved songs become favorites; playlists come in with the source's name.

**Stores:** Bandcamp and Beatport downloads are picked up as they arrive. Bandcamp sign-in can sync the whole collection (experimental).

**Drag and drop (like Winamp):**
- On the player or visuals: replaces the queue and plays now.
- On the queue: appends.
- On the library: adds (folders become library folders).

**Default player:** Options > Updates has a button to make Lucent the default for music and video. Both apps then read back what each type opens in and list the ones still going elsewhere.

## Windows

- Default player: Windows protects defaults, so Lucent registers itself (`FileAssociations.cs`) and opens Settings > Default apps for the user to choose.
- A song opened while Lucent runs is handed to the running copy over a pipe (`OpenRequests.cs`).
- Bandcamp sign-in is stored with DPAPI.

## Mac

- Default player: set directly with LaunchServices (`Interop/LaunchServices.cs`).
- macOS sends opened songs to the running app (`App.axaml.cs`).
- Bandcamp sign-in is stored in the login keychain.

## Parity gaps

- MusicBee import button is Windows-only (MusicBee is Windows-only). The Mac's Apple Music import reads a MusicBee library file too.

## Code

- `Lucent.Core/Library/`, `Lucent.Core/Playlist/`, `Lucent.Core/Import/`, `Lucent.Core/Stores/`
- `LibraryWindow.*`, `QueueWindow.*`, `ImportWindow.*`, `StoreWindow.*`, `SubfolderWindow.*`, `TrackInfoWindow.*`, `Windows/TrackThumb.cs`, `Windows/PlayingRowScroll.cs` (with `Lucent.Core/Layout/ScrollGlide.cs`), `MainWindow.Sources.cs`, `MainWindow.Favorites.cs`, `MainWindow.Import.cs`, `MainWindow.DefaultPlayer.cs` in each app
- Drop handling: `ToolWindow.OnMusicDropped`
- Settings: `LibraryFolders`, `LibraryFiles`, and the library sort and view settings in `AppSettings`
