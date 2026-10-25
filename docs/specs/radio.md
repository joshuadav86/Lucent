# Online radio

## What it does

- Search, sort, and filter thousands of stations from Radio Browser (free, no key), by genre, country, and minimum bitrate. Favorites and recents.
- **Featured:** SomaFM, Radio Paradise, FRISKY, NTS, and The Lot Radio, high quality only (MP3 192k+, AAC/Opus/Ogg 128k+, FLAC, video). Each keeps a backup stream if the best one fails.
- **Formats:** MP3, ADTS AAC/HE-AAC, and HLS (TS segments, starts 30 s behind live). Anything else, and video stations, plays through libVLC.
- **Live video radio:** The Lot Radio's picture shows in the Video window; stations with video get a camera icon.
- **Now playing:** song titles from the stream (ICY, ID3, or SomaFM's song API), cover art from iTunes or the station, who's on (The Lot, NTS), today's schedule in the right-click menus.
- Station website and Support links in the Artwork window's right-click menu.
- Stations go through the player, so the EQ and visuals follow them. The visuals follow the beat by ear (see [Visuals](visuals.md)).

## Windows and Mac

Same on both. All radio logic is in Lucent.Core.

## Parity gaps

None.

## Code

- `Lucent.Core/Radio/` (`RadioDirectory`, `RadioGuide`, `FeaturedStations`, `RadioStream`, `IcyStream`, `HlsStream`, `RadioDecoder`, `LiveShows`, `StationArt`)
- `Lucent.Core/Video/VideoStream.cs` (`OpenStation`) for libVLC stations
- `RadioWindow.*`, `MainWindow.Radio.cs` in each app
- Settings: `RadioFavorites`, `RadioRecent`, `RadioSection`, `RadioSort`, `RadioMinBitrate`, `RadioCountry`, `RadioDetailsWidth`

## Not supported

AAC in MP4/fMP4 HLS (falls back to libVLC), encrypted HLS. Live show parsing for The Lot and SomaFM song titles were written without network access; verify on a real run after changes.
