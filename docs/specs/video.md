# Video

## What it does

- Plays video files (MP4, MOV, MKV, WebM, and anything libVLC reads) and video radio stations.
- Pictures show in the **Video window** (P), a small modular window like the analyzer. It opens by itself for a video and closes 1.5 s after if it opened itself. Click toggles play/pause there (on purpose, unlike the visuals).
- Sound goes through Lucent's player, so the EQ, visuals, and bit-perfect output still apply.
- In the editor: trim and delete on MP4/MOV without re-encoding, export frames and GIFs, and save a smaller copy (H.264/HEVC MP4, or sound as MP3/FLAC/WAV). See [Editor](editor.md).

## Windows and Mac

Same on both: libVLC 3 with LibVLCSharp, bundled. Software decoding is forced. The Smaller copy encoder is Media Foundation on Windows (HEVC needs a GPU encoder or HEVC Video Extensions) and AVFoundation on Mac.

## Parity gaps

None.

## Code

- `Lucent.Core/Video/` (`VideoEngine`, `VideoStream`, `VideoPreview`, `VideoPictureReader`, `Mp4Cutter`, `Mp4Sound`)
- `Lucent.Core/Editor/VideoEdit.cs`, `VideoExport.cs`, `VideoCopy.cs`
- `VideoWindow.*`, `VideoCopyWindow.*`, `VideoExportWindow.*`, `FrameExportWindow.*`, `MainWindow.Video.cs`, `Export/Mp4Writer.cs` in each app

## libVLC rules

- amem only delivers 16-bit samples; Lucent converts to float.
- Both project files strip unused libVLC plugins. Keep `stream_out_smem`, `stream_out_transcode`, `stream_out_standard`, `access_output_file`, and `mux_wav` on the keep lists.
- MP4/MOV sound in the editor is decoded by Lucent (`Mp4Sound`), not libVLC.

## Checks

`VideoSelfTest` plays, cuts, and copies `sync-test.mp4/.mov` on both CI platforms and times flashes against beeps. Sync only warns on CI's virtual Macs.

## Not done

MKV/WebM trim, subtitles, track choice, speed, and floating linked video windows.
