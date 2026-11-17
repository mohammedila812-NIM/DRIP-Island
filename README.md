# Island for Windows

A liquid "dynamic island" that lives at the top-centre of the screen.

- Cursor far away: nothing visible, window is fully click-through.
- Cursor approaches: the screen edge bulges, forms a neck and a drop (stages 2-4 of the sketch).
- Cursor touches the drop: it snaps off, falls, and settles as a pill showing the time (stage 5).
- Click the pill: it expands into the Clock suite (Clock, Stopwatch, Timer, Alarm, Reminder).
- Scroll down inside the island: Music page (title, artist, artwork, progress, controls). Scroll up to return.
- Move the cursor away: panel collapses to the pill, then the pill is pulled back into the edge.
- Esc or clicking elsewhere collapses it. Tray icon: Start with Windows / Quit.

## Build and run

Requires Windows 10 (2004+) or 11 and the .NET 8 SDK.

    dotnet run -c Release

Single file for daily use:

    dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true

## Code map

| File | Role |
|---|---|
| `Droplet.cs` | Physics + state machine (Hidden, Hanging, Pill, Expanded, Retract). All feel constants live here. |
| `LiquidRenderer.cs` | Skia metaball rendering (blur + alpha threshold), rim, neck chain. |
| `MainWindow.*` | Transparent topmost window, cursor polling, click-through toggling, paging, alerts. |
| `ClockPage.*` | Clock, stopwatch, timer, alarm, reminder. State saved to `%AppData%\IslandWin\state.json`. |
| `MusicPage.*` | Windows media session (SMTC): title, artist, artwork, progress, seek, transport. |
| `App.*` | Single instance, tray icon, autostart, shared Apple-style control styles. |

## Tuning the feel (Droplet.cs / LiquidRenderer.cs)

- `EnterR` (230): how far away the liquid notices the cursor.
- `Reach.Step(h, 170, 0.38)`: stiffness and damping of the wobble. Lower damping = jellier.
- `Sigma` (4.5, renderer): blur of the metaball field. Larger = gooier, thicker necks.
- `Detach()` impulse (140): how hard the drop falls.
- Pill/Expanded spring values in `Retarget()`: bounce of the landing and the expansion.

## Phase 2 hooks

Pages are plain UserControls stacked in `MainWindow.xaml` (`PageStack`). Add a page, raise the page
count in `GoPage`, add a dot. Alerts go through `AlertInfo`, so any feature can raise one.

## Fonts

San Francisco cannot be redistributed with Windows apps. The UI uses Inter if installed
(https://rsms.me/inter, nearly identical to SF), then Segoe UI Variable. Icons are Segoe Fluent Icons glyphs.
