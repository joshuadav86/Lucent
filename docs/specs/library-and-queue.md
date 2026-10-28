# Library and queue

## What it does

**Library (L):**
- Folders you add, plus single dropped files. A folder with subfolders asks whether each subfolder gets its own heading.
- Search (title, artist, album, album artist, genre, composer, file name), sort, store headings, and small album covers beside every song (classic skins keep plain rows).
- Tag columns: Sort > Columns (or right-click the headings) adds any of 19 tags beside the songs: artist, album, album artist, genre, year, track, disc, composer, BPM, key, bitrate, sample rate, format, rating, plays, last played, date added, file name, comment. Genre and Year are on to start with (once, for new and existing installs; `OfferLibraryColumns`), and the library widens once to fit them. Thin divider lines run between the columns, their headings, and the time. Drag a heading to move it. Click a heading (Title and Length too) to sort by it, again to flip; blanks always go last, and ties sort in album order. Sort > Sort by tag sorts by any of them with no columns shown. Adding a column widens the window to keep room for the title; docked on the player's left (or against any window on its right) it grows leftward, so it stays docked.
- Find duplicates (folder button menu, or right-click a song): songs with the same artist and title (ignoring case, accents, punctuation, and a leading "The") and lengths within 3 s. Untagged songs match on file name instead. The best copy is unticked: lossless first, then sample rate and bitrate, then plays and rating, then most tags filled in, then the oldest. Ticked copies go to the Recycle Bin or Trash; their plays, ratings, favorites, and saved playlist entries move to the kept copy.
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

- Duplicates go to the Recycle Bin with `SHFileOperation` (`Library/Trash.cs`); Windows asks first if a file can't be recycled.
- Default player: Windows protects defaults, so Lucent registers itself (`FileAssociations.cs`) and opens Settings > Default apps for the user to choose.
- A song opened while Lucent runs is handed to the running copy over a pipe (`OpenRequests.cs`).
- Bandcamp sign-in is stored with DPAPI.

## Mac

- Duplicates go to the Trash with `NSFileManager trashItemAtURL` (`Library/Trash.cs`).
- Default player: set directly with LaunchServices (`Interop/LaunchServices.cs`).
- macOS sends opened songs to the running app (`App.axaml.cs`).
- Bandcamp sign-in is stored in the login keychain.

## Parity gaps

- MusicBee import button is Windows-only (MusicBee is Windows-only). The Mac's Apple Music import reads a MusicBee library file too.

## Code

- `Lucent.Core/Library/` (tag columns: `LibraryColumns.cs`, `LibrarySort.cs`; duplicates: `DuplicateFinder.cs`), `Lucent.Core/Playlist/`, `Lucent.Core/Import/`, `Lucent.Core/Stores/`
- `LibraryWindow.*` (columns in `LibraryWindow.Columns.cs`), `DuplicatesWindow.*`, `QueueWindow.*`, `ImportWindow.*`, `StoreWindow.*`, `SubfolderWindow.*`, `TrackInfoWindow.*`, `Windows/TrackThumb.cs`, `Windows/PlayingRowScroll.cs` (with `Lucent.Core/Layout/ScrollGlide.cs`), `MainWindow.Sources.cs`, `MainWindow.Favorites.cs`, `MainWindow.Import.cs`, `MainWindow.DefaultPlayer.cs` in each app
- Drop handling: `ToolWindow.OnMusicDropped`
- Settings: `LibraryFolders`, `LibraryFiles`, `LibraryColumnsShown`, `LibrarySortColumn`, `LibrarySortDescending`, and the other library sort and view settings in `AppSettings`
- Screenshots (Mac): `LibraryColumns`, `Duplicates`
