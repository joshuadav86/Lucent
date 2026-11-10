# Online radio

## What it does

- Search, sort, and filter thousands of stations from Radio Browser (free, no key), by genre, country, and minimum bitrate. Favorites and recents.
- **Add a station:** a station that isn't listed can be added by its stream's address, its .pls/.m3u/.m3u8 link, or a .pls/.m3u file from the station's site, then played or kept in Favorites. It takes the name its stream gives itself (icy-name) unless one is typed, and can be renamed from its right-click menu (`AddStationWindow`).
  - The address can be typed without https:// ("radio.net"); http:// is tried when https:// doesn't answer. A station's web page works too: Lucent reads it for the stream it links (og:audio, an audio player's source, or a .pls/.m3u/.m3u8 link, never a script's contents). A page that links none (a directory's station page, like radio.net's) searches All stations for the station's name from the page title, or the name typed. A directory's home page names no station, so it says so. Plain words typed as the address are searched for (`StationAddress`).
- **Featured:** SomaFM, Radio Paradise, FRISKY, NTS, and The Lot Radio, high quality only (MP3 192k+, AAC/Opus/Ogg 128k+, FLAC, video). Each keeps a backup stream if the best one fails.
- **Formats:** MP3, ADTS AAC/HE-AAC, and HLS (TS, packed audio, or fMP4 segments with AAC or MP3; starts 30 s behind live, so a late segment plays through). SomaFM's 320k HLS streams are fMP4. Anything else (FLAC or Opus in fMP4, Ogg), and video stations, plays through libVLC, which holds only a couple of seconds.
- **Live video radio:** The Lot Radio's picture shows in the Video window; stations with video get a camera icon.
- **Now playing:** song titles from the stream (ICY, ID3 in TS segments or fMP4 `emsg` boxes, or SomaFM's song API when the stream says none), cover art from iTunes or the station, who's on (The Lot, NTS), today's schedule in the right-click menus.
- Station website and Support links in the Artwork window's right-click menu.
- Stations go through the player, so the EQ and visuals follow them. The visuals follow the beat by ear (see [Visuals](visuals.md)).

## Windows and Mac

Same on both. All radio logic is in Lucent.Core.

## Parity gaps

None.

## Code

- `Lucent.Core/Radio/` (`RadioDirectory`, `RadioGuide`, `FeaturedStations`, `RadioStream`, `IcyStream`, `HlsStream`, `Mp4Pieces`, `RadioDecoder`, `LiveShows`, `StationArt`, `StationAddress`)
- `Lucent.Core/Video/VideoStream.cs` (`OpenStation`) for libVLC stations
- `RadioWindow.*`, `AddStationWindow.*`, `MainWindow.Radio.cs` in each app
- Settings: `RadioFavorites`, `RadioRecent`, `RadioSection`, `RadioSort`, `RadioMinBitrate`, `RadioCountry`, `RadioDetailsWidth`

## Not supported

FLAC, Opus, or Dolby in fMP4 HLS (falls back to libVLC), encrypted HLS, HLS byte-range segments. SomaFM's fMP4 layout and its `emsg` song titles were tested against ffmpeg-made streams, not the real one (blocked in the cloud). Live show parsing for The Lot and SomaFM song titles were written without network access; verify on a real run after changes. Add a station's page reading is checked against made-up pages (`StationAddressSelfTest`); real station and directory pages weren't reachable from the cloud.

Not planned: browsing radio.net (or another directory's site) inside Lucent. radio.net has no public API, its terms keep its station list for private use on its own site, and an embedded page would bring its ads and play outside Lucent's player, so the EQ and visuals wouldn't follow.
