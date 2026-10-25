# Windows and layout

## What it does

Lucent is a set of small windows, like Winamp: player, visuals (W), queue (Q), library (L), equalizer (E), analyzer (Y), artwork (C), video (P), plus the editor, radio, record, and stems windows.

- **Snapping and docking:** windows snap to each other and to the screen. Docked windows move with the player. Dock all (D), fill (G), shrink (K).
- **Link (J):** undocked windows move with the player too.
- **Locks:** a padlock in each title bar pins that window's place and size. The player's lock pins only the player.
- **Remembered places:** every window reopens where it was, docked or not. Analyzer and artwork reopen if they were open.
- **Swap:** drag a window well inside another and they trade places and sizes (an outline shows "Swap with …"). Not for the player, the equalizer, or locked windows.
- **Lucent full screen (Shift+F11,** right-click "Full screen Lucent", or double-click the player's title bar): every open window tiles one screen, at any scaling. Swaps are kept while in full screen.
- **Tidy layout (Shift+D):** cycles symmetric layouts (as they are, centred, centred mirrored, classic, mirrored). Works in and out of full screen.
- **Right-click > Close** on every window; the player's reads "Close Lucent".
- **Toast** messages over the queue or player for quick confirmations.
- Layouts use the player's own screen, never the primary screen.

## Keyboard shortcuts

| Key | Does |
| --- | --- |
| Space | Play or pause |
| Z / X | Previous, next |
| S / R / H | Shuffle, repeat, favorite |
| Ctrl+I | Edit sound info |
| W Q L E Y C P | Visuals, queue, library, equalizer, analyzer, artwork, video |
| D G K | Dock, fill, shrink |
| J / Shift+D | Link windows, tidy layout |
| O / M | Options, Sources |
| V / N / B / A / F | Picker, next visual, back, Auto, star the visual |
| F11 / Shift+F11 | Fullscreen visuals, Lucent full screen |
| T / Shift+T | Thumbs up/down (Pandora) |

## Windows

Own window snapper on Win32 messages (`Windows/WindowSnapper.cs`); locks block move and size there. Handles mixed-scale monitors (a carried window at another scale is centred on its place). Menus are kept clear of the taskbar.

## Mac

Avalonia windows with custom chrome (`Windows/Chrome.cs`). Locks make the window non-resizable. A menu bar carries the same commands (Window > Tidy Layout has no key, since ⇧D would eat typing). Positions are in points, so mixed scaling needs no special handling.

## Parity gaps

Mac-only menu bar (Windows has the same commands in right-click menus and keys). See [PARITY.md](../PARITY.md).

## Code

- `Lucent.Core/Layout/FullScreenLayout.cs`, `WindowSwap.cs`
- `Windows/` in each app (`WindowSnapper`, `WindowLocks`, `LockButton`, `LucentFullScreen`, `SwapHighlight`, `CloseItem`, `ToolWindow`, `Toast`)
- `MainWindow.WindowPlaces.cs`, `MainWindow.FullScreen.cs`, `MainWindow.PlayerSize.cs` in each app
- Settings: `Windows`, `LockedWindows`, `LinkWindows`, `LucentFullScreen`, `WindowsBeforeFullScreen`, `FullScreenSwaps`, `FullScreenStyle`

## Adding a window

Add a `TileKind`, map it in `TileKindOf()`, add it to `TiledWindows()`, and call `LucentFullScreen.AddTo(menu, this)` on its right-click menu (gives "Full screen Lucent" and Close).

## Checks

`tools/LayoutCheck` (parity job) checks every full-screen layout at 100 to 200% scaling and on Mac screens: no overlaps, nothing off screen, minimum sizes, locks, swaps, and every tidy style.
