# Library and queue

## What it does

**Library (L):**
- Folders you add, plus single dropped files. A folder with subfolders asks whether each subfolder gets its own heading.
- Headings (the FOLDERS button, which names the choice): **Folders** (as on disk), **Artists** (a heading per artist, with a subheading per album, "Isles (2021)"), **Albums** (a heading per album, "Isles · Bicep"), or **No headings** (one long list). An album is its name and album artist; with no album artist, its name and the folder it's in, so a compilation stays together, listed under its album artist, the artist most of its songs share, or "Various artists". Names that differ only in case share a heading. Songs without an album sit right under their artist's heading, before the albums; "Unknown artist" and "No album" go last. Under any heading the headings follow the sort (judged by each heading's first song, so Newest first puts the latest arrivals on top; by name for Title and File name), and songs go in album order (album, disc, track; no disc counts as disc 1, no track number goes last, by file name) for Newest, Oldest, and Artist. Other sorts order the songs under each heading, ties in album order. Artist and album headings can't be removed from the library (Delete and Ctrl+A act on folders only); right-click one to play it or add it to the queue, and "Play this folder" on a song becomes "Play this album".
- Search (title, artist, album, album artist, genre, composer, file name), sort (Artist lists each artist's albums in track order), store headings, and small album covers beside every song (classic skins keep plain rows).
- Tag columns: Sort > Columns (or right-click the headings) adds any of 19 tags beside the songs: artist, album, album artist, genre, year, track, disc, composer, BPM, key, bitrate, sample rate, format, rating, plays, last played, date added, file name, comment. Genre and Year are on to start with (once, for new and existing installs; `OfferLibraryColumns`), and the library widens once to fit them. Thin divider lines run between the columns, their headings, and the time. Drag a heading to move it. Click a heading (Title and Length too) to sort by it, again to flip; blanks always go last, and ties sort in album order. Sort > Sort by tag sorts by any of them with no columns shown. Adding a column widens the window to keep room for the title; docked on the player's left (or against any window on its right) it grows leftward, so it stays docked.
- Find duplicates (folder button menu, or right-click a song): songs with the same artist and title (ignoring case, accents, punctuation, and a leading "The") and lengths within 3 s. Untagged songs match on file name instead. The best copy is unticked: lossless first, then sample rate and bitrate, then plays and rating, then most tags filled in, then the oldest. Ticked copies go to the Recycle Bin or Trash; their plays, ratings, favorites, and saved playlist entries move to the kept copy.
- PLAYLISTS view: saved playlists, favorites, imported playlists, and the controlled app's playlists (see [Streaming control](streaming-control.md)).
- Opened streaming playlist (Spotify on both, Apple Music on Mac): right-click a song for Play, Add to playlist, and Share (as in the queue window), even while Lucent controls another app or none. Its columns: the same headings as the tag columns (click to sort, again to flip, drag to move, right-click to choose), filled from what the app says: playlist order (#), artist, album, album artist, year, track, disc, and date added; Apple Music adds genre. Album and Date added are on to start with, and the library widens once to fit them. A column the app has nothing for (Spotify has no genres) is left out of its playlists but kept for the others. The sort button offers playlist order or any column; it only changes how the list is shown, the app still plays in its own order. Its own settings (`PlaylistColumnsShown`, `PlaylistSortColumn`, `PlaylistSortDescending`); the queue window's Sort keeps `RemoteListOrder`.
- Edit sound info (Ctrl+I): tags and cover; Detect fills in BPM and key.
- Fill in missing BPM and key (Options > Library, off to start with): a song that plays without a BPM or key in its file is listened to (the same tempo and key detection as Detect) and what's heard is written into the file. Only blank fields are written; a BPM or key already there is never changed, and an unsure key (often the relative major or minor) is left out. The playing file is written once another song plays, since saving it would stop the music for a moment; a file in use is tried again every 20 s, up to 6 times. "Check the whole library too" goes through every library song missing either, one at a time: one core at the lowest thread priority, resting 3 times as long as each song took (a quarter of a core at most; a 10-minute WAV takes about 6 s). Turning it off pauses it, and songs already checked are listed in `tempo-key-checked.txt` in the app data folder so they're not listened to again. Files over 20 minutes (DJ mixes, live sets) and videos are skipped. The tab shows how many songs were filled in and how many are left.
- The song playing is marked with a bar and kept in the middle of the list: in folders, saved playlists, an opened Spotify or Apple Music playlist, and the queue window's streaming playlist. On each new song the list glides there, however long the list: about 0.4 s, eased, for a song nearby, and longer for one further off (a little more per doubling of the distance, never more than 0.85 s, easing in and out). The list never moves faster than 20 screens a second, so each frame shares most of its rows with the one before; a far song's flight speeds up to that from where the list is, skips the stretch in between at full speed, and settles on the song. Each step is timed by the frame being drawn (WPF's `RenderingTime`, Avalonia's animation-frame time), so it moves evenly at the screen's refresh rate; the visuals' Power cap doesn't touch it. Covers aren't fetched for rows flying past; the rows it lands on load theirs (`ScrollGlide`, `Thumbnails.Hold`). After a manual scroll or arrow key the list is left alone for 4 seconds, then catches up.

**Queue (Q):** what plays next. Shuffle (S), repeat (R), favorite (H). The queue is kept between runs (`Queue.m3u8` in the app data folder, saved 2 s after a change and on closing); on the next start it comes back with the song that was playing selected, and Play starts it. Files that are gone are left out.

**Sources panel (M):** add music, tune in to radio, or connect a music app.

**Import:** Apple Music/iTunes, MusicBee, foobar2000, playlist files (M3U and friends), file tags, Plex, and YouTube Music (Google Takeout). Loved songs become favorites; playlists come in with the source's name.

**Stores:** Bandcamp and Beatport downloads are picked up as they arrive. Bandcamp sign-in can sync the whole collection (experimental).

**Drag and drop:**
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
- Default player: set with LaunchServices (`Interop/LaunchServices.cs`). macOS 26.4 and later asks the user to confirm each type, so the button sets only the common music types (MP3, M4A, FLAC, WAV, AAC, AIFF, OGG, Opus), one at a time, skipping ones already Lucent's, and stops at the first one the user keeps. An Every file type button adds videos and rarer types. On Windows, Set default in Settings covers every type at once.
- macOS sends opened songs to the running app (`App.axaml.cs`).
- Bandcamp sign-in is stored in the login keychain.

## Parity gaps

- MusicBee import button is Windows-only (MusicBee is Windows-only). The Mac's Apple Music import reads a MusicBee library file too.

## Code

- `Lucent.Core/Library/` (tag columns: `LibraryColumns.cs`, `LibrarySort.cs`; headings: `LibraryGroups.cs`, test `LibraryGroupsSelfTest`; streaming playlist columns: `PlaylistColumns.cs`, test `PlaylistColumnsSelfTest`; duplicates: `DuplicateFinder.cs`), `Lucent.Core/Playlist/`, `Lucent.Core/Import/`, `Lucent.Core/Stores/`
- `LibraryWindow.*` (columns in `LibraryWindow.Columns.cs`, streaming playlist columns in `LibraryWindow.PlaylistColumns.cs`), `DuplicatesWindow.*`, `QueueWindow.*`, `ImportWindow.*`, `StoreWindow.*`, `SubfolderWindow.*`, `TrackInfoWindow.*`, `Windows/TrackThumb.cs`, `Windows/PlayingRowScroll.cs` (with `Lucent.Core/Layout/ScrollGlide.cs`), `MainWindow.Sources.cs`, `MainWindow.Favorites.cs`, `MainWindow.Import.cs`, `MainWindow.DefaultPlayer.cs`, `MainWindow.SavedQueue.cs` (with `Lucent.Core/Playlist/SavedQueue.cs`) in each app
- Drop handling: `ToolWindow.OnMusicDropped`
- Missing BPM and key: `Lucent.Core/Library/TempoKeyFiller.cs` (test: `TempoKeySelfTest`), `MainWindow.TempoKey.cs` in each app; settings `FillMissingTempoAndKey`, `FillTempoAndKeyInLibrary`
- Settings: `LibraryFolders`, `LibraryFiles`, `GroupLibraryByFolder` (headings on), `LibraryGrouping`, `LibraryColumnsShown`, `LibrarySortColumn`, `LibrarySortDescending`, `PlaylistColumnsShown`, `PlaylistSortColumn`, `PlaylistSortDescending`, and the other library sort and view settings in `AppSettings`
- Screenshots (Mac): `LibraryColumns`, `LibraryArtists`, `LibraryAlbums`, `LibrarySpotifyPlaylist`, `Duplicates`, `Player-Options-Library`
