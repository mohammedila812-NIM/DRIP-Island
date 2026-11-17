# IslandWin — Antigravity Master Plan

Goals: (1) realistic liquid, (2) fix Music, (3) full Settings (simple + advanced), (4) remove the ugly white halo, (5) many new features.

Read section 0 first. Then paste **Prompt K** (kickoff), then one phase at a time.

---

## 0. How to work with Antigravity on this repo

1. Put this file in the repo root as `PLAN.md`.
2. Add section 1 as an **Always On** workspace rule: Antigravity → Customizations → Rules → + Workspace. File-based equivalent: `.agents/rules/island.md` (some versions use `.agent/rules/`; check what the Rules tab shows).
3. Use **Planning mode**. For every phase: paste the phase prompt, wait for the *Implementation Plan* artifact, read it, comment on anything outside the phase scope, then approve.
4. After each phase: commit, push, let GitHub Actions build, test the exe, then come back with what you saw (paste screenshots or the Actions log into the chat). Do not start the next phase until the current one is accepted.
5. **Assumption: there is no .NET SDK on your PC.** The agent therefore cannot build or run anything. Code must compile "blind", and CI is the only compiler. That is why Phase 0 adds a snapshot mode that renders PNGs in CI, so you can look at the liquid without installing anything. If you install the SDK later, delete rule 3 below.

---

## 1. Standing rules (paste as the Always-On rule)

```text
PROJECT: IslandWin, a WPF (.NET 8, net8.0-windows10.0.19041.0) "dynamic island" overlay for Windows, rendered with SkiaSharp.Views.WPF 2.88.9 (metaball liquid). Read PLAN.md and README.md before editing.

RULES
1. Do not rewrite from scratch. Make targeted edits. Keep the file map in README.md accurate and update it when files are added.
2. Keep these behaviours exactly: window is click-through unless the cursor is over the visible island (MainWindow.Tick + Native.ClickThrough); idle = zero repaints (`Settled` logic); single instance; tray icon; Esc / click-away collapses.
3. The user has NO .NET SDK and cannot run the app locally. You cannot compile. Therefore: use only APIs you are certain exist in .NET 8 and SkiaSharp 2.88.9. If you are not certain, use a simpler API you are sure of, and list it under "UNVERIFIED APIS" in your summary. Prefer small, low-risk changes. No new NuGet packages unless the phase says so, and never packages that need native installers.
4. Never swallow exceptions silently. Replace empty `catch { }` with a call to `Log.Error(...)` (Phase 0 creates it). Failing UI actions may still be non-fatal.
5. No magic numbers in new code. Every tunable goes through the `Tuning`/`AppSettings` classes (Phase 3) once they exist. Until then, a named `const` with a comment.
6. All WinRT/background events must marshal to the UI thread with Dispatcher before touching UI. Guard async refreshes against out-of-order results (version counter).
7. Do not add telemetry. Any network feature (weather, calendar URL, lyrics) must be opt-in, off by default, with a clear label in Settings.
8. Keep UI style: black island, Apple-like dark controls, Inter/Segoe UI Variable, Segoe Fluent Icons glyphs, no emoji.
9. Work only on the current phase. If you notice something out of scope, add it to the "NOTES FOR LATER" list in your summary instead of changing it.
10. End every phase with: (a) summary of files changed, (b) UNVERIFIED APIS, (c) how the user should test it, (d) self-audit of your diff against the phase's acceptance criteria.
11. Ask before making any decision that changes behaviour the user did not ask for.
```

---

## 2. What is actually wrong (findings from reading the code)

| Symptom | Root cause in the code | Where |
|---|---|---|
| Drip looks like a small ball on a thin string, little surface area | Reservoir is only an 80 px wide oval that bulges 13 px (`rx` 40→22, `depth` max 17, centre y = -7). Neck radius starts at 7 and is multiplied by up to 0.55 as it stretches, so about 4 px radius. With blur Sigma 4.5, a 4 px radius thread barely clears the 0.5 threshold. The drop is only 22 px across. There is no wide flared base (meniscus). | `LiquidRenderer.DrawShapes`, `Droplet.Retarget` (`dia = 22f * s`) |
| White boundary looks bad while dripping | The "rim" is the same field thresholded at 0.38 and painted white under the black 0.50 layer. Its thickness is the gap between two iso-lines of a blurred field, so it is thin on big flat faces and **thick and glowy around thin necks and small drops**. It is not a constant-width outline. | `LiquidRenderer` ctor (`_rimFx`), `Render` (first `Pass`) |
| Snap looks fake | `Tether` just fades 1→0 with a critically damped spring, so the neck thins uniformly and vanishes. Real liquid pinches near the drop, then the top stub recoils into the reservoir. No satellite bead. | `Droplet.Retarget/Step`, neck drawing |
| Music "doesn't work" | Cannot be confirmed without running it. Likely suspects, in order: (1) `Init()` is triggered by `Loaded` on a control inside a `Visibility.Collapsed` panel, so it may never run until first expand; (2) it uses `GetCurrentSession()` only, which is often a paused browser tab instead of the app that is playing; (3) `TryGetMediaPropertiesAsync` can return null or throw and every catch is empty, so failures are invisible; (4) the WinRT projection may behave differently in the single-file exe than in `dotnet run`; (5) thumbnail reloaded on every event, no race guard. | `MusicPage.xaml.cs` |
| Only 2 pages possible | `GoPage` clamps to `0..1`, dots are hard-coded `Dot0/Dot1`, page height is `Droplet.PanelH`. | `MainWindow.xaml(.cs)` |
| Size cannot be customized | `Droplet` sizes are `const`; XAML has hard-coded 500x260, 440x200 and a clip `Rect="0,0,440,200" RadiusX="34"` (does not even match `PanelR = 38`). | `Droplet.cs`, `MainWindow.xaml` |
| Wrong on multi-monitor / mixed DPI | `Left` uses `PrimaryScreenWidth`; `_dpi` is read once; cursor math is `p.X / _dpi - Left`. | `MainWindow` |
| Alarms can be missed | Alarm fires only if the 50 ms tick sees the exact minute; after sleep/resume the minute is skipped. Timer and stopwatch are not persisted. | `ClockPage.EverySecond` |
| Minor | Tray `HICON` leaked; second launch just exits instead of opening the app; clock uses current culture with no 12/24 h option. | `App.xaml.cs`, `ClockPage` |

---

## PROMPT K: kickoff (paste first, changes nothing)

```text
Read every file in this repo (all .cs, .xaml, .csproj, README.md, PLAN.md). Do NOT edit anything yet.
Produce:
1. A 15-line architecture summary in your own words (render loop, state machine, click-through, paging).
2. A list of every empty catch block and every hard-coded size/constant that PLAN.md section 2 says must become configurable.
3. A list of APIs you plan to use in phases 0-3 with your confidence (high/medium/low) that each exists in .NET 8 / SkiaSharp 2.88.9 / Windows.Media.Control.
4. Any risk or contradiction you see in PLAN.md.
Then stop and wait.
```

---

## PHASE 0: Safety net (logging, snapshots in CI)

### Prompt 0

```text
Phase 0 of PLAN.md. Goal: make the project debuggable without a local SDK. Do ONLY this:

A. Add `Log.cs`: static `Log.Info/Warn/Error(string, Exception = null)` writing to %AppData%\IslandWin\logs\island-YYYYMMDD.log, thread-safe (lock), size-capped (delete files older than 7 days at startup). Replace every empty `catch { }` in the repo with `catch (Exception ex) { Log.Error("<where>", ex); }` keeping the same control flow. Also hook AppDomain.UnhandledException, DispatcherUnhandledException (mark Handled=true, log) and TaskScheduler.UnobservedTaskException.

B. Add a headless snapshot mode: `IslandWin.exe --snapshot <outDir>`. In App.OnStartup, if this arg is present, run `Snapshot.Run(outDir)` BEFORE creating MainWindow/tray/mutex, then Shutdown() and exit code 0 (nonzero on exception, and log it).
   Snapshot.Run must use only SkiaSharp offscreen surfaces (SKSurface.Create) plus Droplet and LiquidRenderer; no WPF windows.
   It renders PNGs at 2x scale (1000x520 px per frame):
   1. hanging_reach_{20,40,60,80,100}.png   (set cursor path so Reach settles at each value; use fixed dt = 1/240 and step until Settled or 3 s)
   2. detach_{00..12}.png every 30 ms starting at the frame Detach() fires (record Phase changes)
   3. pill.png, expanded.png (settled)
   4. A contact sheet `sheet.png` that tiles all frames with their names.
   Render each frame twice side by side: on #0B0B0D and on #D9D9DE backgrounds (so rim/halo problems are visible on both).
   Make Droplet.Update/Step usable with a scripted cursor (they already take mx,my,dt; do not add Windows dependencies to Droplet).

C. Extend .github/workflows/build.yml: build step separate from publish; on failure upload the build log; after publish, run the exe with `Start-Process -Wait -NoNewWindow` (a WinExe does not block PowerShell otherwise) with `--snapshot snapshots`, then `actions/upload-artifact@v4` the `snapshots` folder (name: snapshots). Keep the existing exe artifact.

Do not change any visuals or behaviour. Acceptance: CI produces `snapshots/sheet.png` showing the CURRENT (ugly) liquid; Log file is created on startup.
```

**Hints for Phase 0**

- `LiquidRenderer.Render(SKCanvas target, SKImageInfo info, float scale, Droplet d)` already takes everything needed. Create `new SKImageInfo(1000, 520)` and `scale = 2f`.
- Add a `SKColor Background` pass in Snapshot only (renderer clears to transparent).
- A WinExe launched from PowerShell returns immediately, which is why `Start-Process -Wait` is required. Check `$LASTEXITCODE`/`(Start-Process ... -PassThru).ExitCode`.
- Stepping: `sim.Update(dt, mx, my, false); sim.Step(dt);` Note `Step` snaps when `Settled`.
- Snippet for the CI step:

```yaml
      - name: Snapshots
        shell: pwsh
        run: |
          $p = Start-Process -FilePath .\publish\IslandWin.exe -ArgumentList '--snapshot','snapshots' -Wait -PassThru
          if ($p.ExitCode -ne 0) { throw "snapshot failed: $($p.ExitCode)" }
      - uses: actions/upload-artifact@v4
        with: { name: snapshots, path: snapshots }
```

---

## PHASE 1: Realistic liquid

### Prompt 1

```text
Phase 1 of PLAN.md. Goal: the island must look and move like real, viscous black liquid (think a drop of oil hanging from a ceiling, then pinching off and landing as a pill). Use ONLY Droplet.cs, LiquidRenderer.cs and Snapshot.cs. Do not touch pages or settings.

Problems to fix (see PLAN.md section 2):
 1. Too little surface area: the hanging drop reads as a small ball on a thin thread.
 2. The white rim is uneven and glowy around the neck. Remove it by default.
 3. The snap is a fade, not a pinch-off.

Required changes, in this order. After each numbered change the code must still be a complete, compiling state:

1. RIM. Delete the second (0.38) pass. Replace with a constant-width, optional edge treatment built from the FINAL thresholded shape: `SKImageFilter.CreateDropShadow(0, 0, 0.8f, 0.8f, <white alpha ~50>, <main filter>)` (verify the signature; if unsure, use the "offset white copy behind black copy by -1 px in Y" trick for a top-lit edge). Expose `RimMode { Off, Subtle, Strong }` as a static property on the renderer, default Off. Make sure nothing is drawn along the screen-edge junction at y = 0.

2. MENISCUS + NECK. Redraw the neck as a flared, tapered profile:
   r(f) = rMin + (rBase - rMin) * pow(1 - f, p)   with f = 0 at the screen edge, f = 1 at the drop.
   rBase (radius at the edge) grows with Reach from ~18 to ~34 DIP; rMin shrinks from ~11 to ~3.5 as the drop stretches; p ~ 2.4 to 2.8. Sample at <= 3 DIP spacing. Make the reservoir wider and lower (rx up to ~70, depth up to ~24) so the base merges into the edge with a wide concave fillet. Neck must stay above the metaball threshold: compensate for blur shrinkage (radius += sigma * 0.35 for thin parts) and clamp rMin >= 2.5.

3. VOLUME. Make the drop bigger and heavier: diameter 22 -> up to ~34 DIP at full reach, teardrop shaped: stretch factor k = 1 + 0.35 * stretch; Ht = dia * sqrt(k), Wd = dia / sqrt(k) (area conserved). The drop's centre should lag the cursor-driven sway with its own spring for a pendulum feel.

4. FILLETS. Increase blur Sigma from 4.5 to a range of 6-8 and make it scale with stretch (rounder fillets while stretched). Cache SKImageFilter instances per quantized sigma (steps of 0.5); never allocate filters per frame. Keep render cost sane: render the metaball field at 0.5 resolution and upscale (blur is low-frequency) behind a `FieldScale` constant, while keeping the final edge crisp (apply the threshold AFTER upscaling if you can do so with the filter API you are sure of; otherwise keep full-res and tell me).

5. PINCH-OFF. Replace the uniform Tether fade with: (a) during the last ~120 ms before Detach the neck radius at f ~ 0.75 collapses to ~1 DIP; (b) at Detach, the top stub retracts into the reservoir (Reach gets a small negative velocity kick so it recoils and overshoots) and the bottom stub is absorbed by the drop; (c) a small satellite bead (radius 3-5) may spawn at the pinch point and fall with gravity, merging into the pill on landing (metaball merge). Satellite is optional behind a `Satellite` bool, default true.

6. LANDING. On landing, squash and stretch: Wd/Ht springs should visibly overshoot (width wider, height shorter, then settle to the pill). Keep existing spring structure; adjust values, do not add per-frame allocations.

Do not change public method names used by MainWindow (Update, Step, Expand, Collapse, ForceExpand, Contains, Settled, Phase, Wd, Y, Rad...). Keep `Settled` correct for every new spring/state, otherwise idle repaints will break.
At the end, regenerate expectations: list which snapshot frames I should look at to judge each of changes 1-6.
```

**Hints for Phase 1**

- Blur math: a straight band of half-width `w` blurred by sigma `s` has centre alpha `erf(w / (s * sqrt2))`. For `w = 3.9`, `s = 4.5` that is about 0.61. Barely above the 0.5 threshold, so the neck is razor thin or breaks. This is the real reason it looks like a string.
- Blurring also shrinks small blobs (a 22 px circle loses its edge). If a shape looks smaller than specified, inflate it by about `0.3 * sigma`.
- Realistic drips are not round: stretch the drop vertically, keep area constant, and let the neck pinch just under the reservoir fillet, not in the middle.
- Quantize sigma and cache filters. `SKImageFilter.CreateBlur` per frame will thrash the GC.
- Let `stretch = clamp((dropY - restY) / 40, 0, 1)` drive neck radius, drop elongation and sigma together, so the look is one coherent function.
- Keep constants in one `LiquidLook` static class now; Phase 3 turns it into settings.
- Best-quality alternative (only if the metaball version still looks cheap): replace the blur-threshold trick with an SDF + smooth-min in an `SKRuntimeEffect` (SkSL) shader. It gives exact, resolution-independent fillets, uniform rims and lighting normals. Cost: the effect runs on the CPU inside a layered (AllowsTransparency) window, so restrict it to the island's bounding box. Check that `SKRuntimeEffect` exists in the pinned SkiaSharp; if not, do **not** upgrade packages without asking.
- Optional "gloss": an inner highlight along upward-facing edges at 20-35 % alpha, never an outer halo.

**Acceptance (judge on `sheet.png`)**: the hanging drop has a clearly wide flared base; no white glow on either background (Rim Off); the neck visibly pinches before the drop leaves; frames after detach show the stub retracting; idle repaint behaviour unchanged.

---

## PHASE 2: Music, fixed and better

### Prompt 2

```text
Phase 2 of PLAN.md. Goal: Music must work reliably with Spotify, Apple Music, YouTube/Chrome/Edge, VLC, and survive apps opening/closing. Do NOT assume the cause. Instrument first.

1. Create `MediaService.cs` (singleton, owns the GlobalSystemMediaTransportControlsSessionManager). It is started explicitly from MainWindow after Show() (NOT from a Loaded event of a collapsed control) and keeps running while the island is collapsed.
   - Subscribe to manager.SessionsChanged AND CurrentSessionChanged. Enumerate GetSessions().
   - Session choice policy: prefer a session whose PlaybackStatus is Playing (newest wins); else the last one that was playing; else manager.GetCurrentSession(). Provide a settings-ready `IgnoredApps` list and `PreferredApp` string matched against SourceAppUserModelId (hard-coded defaults empty).
   - Raise a single `NowPlayingChanged` event with an immutable record: Title, Artist, Album, SourceApp (friendly name, strip ".exe"), IsPlaying, Position, Duration, CanSeek, CanNext, CanPrev, Shuffle, Repeat, Art (frozen ImageSource or null), AccentColor.
   - Handle null from TryGetMediaPropertiesAsync, missing thumbnail, zero duration (live streams), and apps that never send timeline updates. Use a version counter so slow async results never overwrite newer ones. Cache art per (Title|Artist|Album) key.
   - Log every attach/detach/choice and every failure with Log.*.
2. Refactor MusicPage to only render NowPlaying and send commands via MediaService (play/pause, next, prev, seek, shuffle, repeat). Disable buttons when the session says the control is disabled; hide the seek bar when CanSeek is false.
3. Add a diagnostics line to the Music page footer, visible only when `ShowDebug` is true: "<SourceApp> | Playing | session 2 of 3".
4. UX: draggable seek bar (mouse capture) with hover time; marquee scrolling for long titles; shuffle/repeat buttons; art accent colour used for the progress fill (extract a dominant colour from a 32x32 downscale, clamp brightness); empty state shows the hint "Play something to see it here" plus the list of detected apps if any.
5. Pill integration (compact live activity): when something is playing and the island is a Pill, show a 22 px rounded album art on the left and a 4-bar animated equaliser on the right INSTEAD of the clock; when paused fall back to the clock. Respect a `PillShowsMusic` bool (default true). The pill width may grow slightly (spring) when showing art.
6. Make page switching discoverable: clicking a page dot switches page; horizontal two-finger swipe (MouseWheel with Shift / Tilt) also switches.

Acceptance: with Spotify playing AND a paused Chrome tab open, the island shows Spotify. Pause Spotify, play YouTube: island switches within 1 s. Closing the player returns to the empty state without an exception in the log.
```

**Hints for Phase 2**

- `SessionsChanged` fires when apps appear or disappear. Each session needs its own subscriptions, and they must be removed when it disappears, or you leak handlers.
- `session.SourceAppUserModelId` looks like `Spotify.exe`, `Chrome`, `MSEdge`, `AppleInc.AppleMusicWin_...!App`. Map known ones to friendly names.
- Timeline: many apps update `Position` rarely. Extrapolate: `pos = Position + (now - LastUpdatedTime)` only while Playing (already done in the old code; keep it).
- `TryChangeShuffleActiveAsync`, `TryChangeAutoRepeatModeAsync`, `GetPlaybackInfo().Controls.IsNextEnabled/IsPreviousEnabled/IsPlaybackPositionEnabled` exist on the session / playback info. Log if any is missing at runtime instead of crashing.
- `AsStreamForRead()` needs `using System.IO;` (already there). Freeze the `BitmapImage` before handing it across threads.
- Verify the single-file exe, not just `dotnet run`: the CI snapshot job can also run `IslandWin.exe --media-selftest` that calls `RequestAsync()` and logs the result. Add this flag.
- Optional, opt-in only: synced lyrics line in the pill using lrclib.net (free, no key). Off by default, clearly labelled as a network feature.

---

## PHASE 3: Settings system and Settings window

### Prompt 3A: settings backbone

```text
Phase 3A of PLAN.md. No new UI yet. Goal: every tunable lives in one persisted settings object.

1. Add `AppSettings.cs` (plain classes, System.Text.Json) with nested groups: General, Appearance, Behavior, Pages, Music, Clock, Hotkeys, Liquid (advanced), Physics (advanced), Performance (advanced), Privacy, Advanced. Include `int SchemaVersion` and a `Migrate()` hook. Every property has a documented default equal to TODAY's hard-coded value (so nothing changes visually). Use `[JsonIgnore]` for computed values.
2. Add `SettingsService` (singleton): Load(), Save() debounced 300 ms, atomic write (temp file + File.Replace), on corrupt JSON copy to settings.bad.json and fall back to defaults, `Changed` event carrying the property path. File: %AppData%\IslandWin\settings.json. Keep state.json for alarms/reminders.
3. Replace `Droplet` consts (W, H, PillW, PillH, PillY, PanelW, PanelH, PanelTop, PanelR, EnterR) and all spring constants in Retarget/Step with reads from a `Tuning` object derived from settings. Keep `Droplet` free of WPF types. Keep a `Tuning.Defaults` that reproduces current behaviour exactly.
4. Make MainWindow size, Panel size and the clip geometry code-driven from Tuning (remove the hard-coded 500x260 / 440x200 / Rect="0,0,440,200" RadiusX="34" in XAML; clip radius must equal the panel radius). Add UiScale support: window size * scale, SKCanvas scale, ScaleTransform on content, and mouse coordinates divided by scale.
5. Make the page list dynamic: an `IIslandPage` interface (Id, Title, Icon glyph, Enabled, Order, CreateView(), optional compact view). `GoPage`, the dots and the vertical shift use the live page count. Clock and Music implement it. Pages can be enabled/disabled and reordered from settings.
6. Multi-monitor and DPI: position by chosen screen (Screen.AllScreens), recompute on DisplaySettingsChanged and WM_DPICHANGED, per-window DPI instead of one cached value. Re-assert Topmost every 2 s.
7. Fix: store alarm "next fire" as a DateTime (not just hour/minute) and fire when now >= next, so sleep/resume does not skip alarms. Persist running timers and stopwatch across restarts. Add 12/24 h and show-seconds options. Destroy the tray HICON properly.

Everything must apply LIVE (no restart) except those marked "restart required" in a comment.
```

### Prompt 3B: Settings window

```text
Phase 3B of PLAN.md. Build the Settings window on top of 3A.

UI
- A normal (non-transparent, non-click-through) WPF window, ~860x620, dark, Apple-style, same control styles as App.xaml (add Toggle, Slider, ComboBox, ColorSwatch, Section header styles there).
- Left sidebar categories: General, Appearance, Behavior, Pages, Music, Clock, Hotkeys, Privacy, Liquid Lab (advanced), Performance (advanced), About.
- Top-right segmented control: [Simple | Advanced]. Simple hides advanced categories and advanced controls. Persist the choice.
- Every control: label, one-line description, reset-to-default button (visible when changed), tooltip with the allowed range. Section footer: "Reset section". Window footer: "Reset all", "Export...", "Import...", "Open settings folder", "Open log".
- Presets dropdown (Liquid, Snappy, Subtle, Honey, Mercury, Minimal/No-physics) that sets a batch of values; show "Custom" when edited.
- Live preview strip at the top of Appearance and Liquid Lab: a second SKElement running its own Droplet+LiquidRenderer on a scripted looping cursor path (drop forms -> falls -> pill -> expands -> retracts) using the CURRENT settings, so every slider is visible immediately.
- Entry points: tray menu "Settings..." (and double-click tray), a small gear on the expanded panel, hotkey, and `--settings`. A second launch of the exe must signal the running instance (named EventWaitHandle or pipe) to open Settings instead of silently exiting.
- Use data binding to the settings objects (INotifyPropertyChanged) rather than code-behind per control. Validate and clamp inputs.

Required settings

SIMPLE
 General: start with Windows; show tray icon; language/culture for dates; check-for-updates link (no auto network).
 Appearance: island colour (default black), accent colour, UI scale (80-150 %), pill size (small/medium/large), corner roundness, rim (Off/Subtle/Strong), gloss on/off, shadow on/off, font.
 Behavior: trigger distance (near/normal/far), open on (click | hover | hotkey), auto-collapse delay, hide when a fullscreen app/game is active, show on monitor (primary/each/cursor monitor), vertical offset, horizontal offset, animation preset, follow Windows "reduce animations".
 Pages: enable/disable and drag-reorder; default page; remember last page.
 Music: show in pill; preferred app; ignored apps; show lyrics (opt-in); art shape (rounded/circle).
 Clock: 12/24 h; show seconds; date format; alarm sound (system sounds or custom .wav); volume; snooze minutes.
 Hotkeys: toggle island (default Win+Alt+I, change if it conflicts), next page, play/pause. Record-a-key control with conflict detection.
 Privacy: which features may use network (weather, calendar, lyrics); clipboard history on/off + clear; "never store" list.

ADVANCED
 Liquid Lab: Sigma min/max; threshold; edge slope; neck rBase/rMin/profile exponent p; reservoir width/depth; drop diameter; drop elongation; pinch timing; satellite bead on/off; field resolution scale; rim width/alpha/colour; gloss strength; background-preview colour.
 Physics: EnterR; Reach stiffness/damping; Tether stiffness; detach impulse; sway amount; pill spring k/zeta; panel spring k/zeta; retract k/zeta; auto-collapse thresholds (away distance/time for Pill and Expanded).
 Performance: max FPS; field render scale; pause rendering when settled; low-power mode on battery; GPU/CPU note; log level; "show FPS and field overlay" debug toggle.
 Advanced: edit-as-JSON box with validation; per-app rules (hide/never expand when process X is foreground); multi-instance guard off switch.

Acceptance: changing any slider updates the preview within one frame and the real island within 300 ms; Reset-all restores today's look exactly; deleting settings.json starts cleanly; corrupt JSON does not crash.
```

**Hints for Phase 3**

- Cheapest way to get a form UI: an `ItemsControl` driven by a list of `SettingDescriptor { Path, Label, Description, Kind, Min, Max, Step, Default, Advanced, RestartRequired }` and a `DataTemplateSelector` (Toggle, Slider, Choice, Color, Hotkey, Text). Then adding a setting is one line, and Simple/Advanced is a filter on `Advanced`.
- A hotkey needs `RegisterHotKey` through an `HwndSource` hook on the main window handle, and `UnregisterHotKey` on exit. Registration can fail if another app owns the combo, so show that in the UI.
- Fullscreen detection: `SHQueryUserNotificationState` (P/Invoke, shell32) returns busy / D3D fullscreen / presentation mode. Poll every 1-2 s, not per frame.
- "Reduce animations": `SystemParameters.ClientAreaAnimation`.
- The window is `ShowActivated=False`. Give the Settings window its own normal activation so text boxes work.
- Window geometry for UiScale: allocate `Width = baseW * maxScale` once and scale content inside, to avoid resizing a layered window during animation.

---

## PHASE 4: Features (pick in order, one prompt each)

### Prompt template (reuse for each feature)

```text
Phase 4, feature: <NAME>. Follow PLAN.md rules 1-11. Implement it as an IIslandPage (and optionally a compact pill activity), with its own settings group registered in the Settings window (Simple + Advanced descriptors), enabled/disabled in Pages, and persisted. No network unless the feature table says opt-in. Provide: data model, service, page UI, pill/compact behaviour, settings, and a test checklist.
```

### Feature roadmap

| # | Feature | Notes / hints |
|---|---|---|
| 1 | **Live activities in the pill** (music art + equaliser, timer countdown ring, stopwatch, focus session) | Central priority list: highest-priority active activity owns the pill; others queue. Needs `Activity` model in Phase 3's page interface. |
| 2 | **Split bubbles**: with two live activities the pill splits into two bubbles that merge/separate (metaball showcase) | Generalise `Droplet` body to `List<Body>`; renderer already fuses nearby shapes. Do this after Phase 1 is stable. |
| 3 | **System HUDs**: volume, caps/num lock, battery plug/unplug and low battery, headphones connected | Volume via NAudio `CoreAudioApi` (small, ok to add after asking); lock keys via `GetKeyState`; battery via `SystemInformation.PowerStatus` (WinForms is already referenced); devices via `Windows.Devices.Enumeration.DeviceWatcher`. Reuse `AlertInfo` with a short auto-dismiss. |
| 4 | **Global hotkeys** + right-click menu + middle-click actions | See Phase 3 hints. |
| 5 | **Auto-hide in fullscreen** and per-app rules | `SHQueryUserNotificationState` + foreground process name. |
| 6 | **Mic / camera in use indicator** (green/orange dot) | Registry `HKCU\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{microphone,webcam}\...`; a subkey with `LastUsedTimeStop == 0` means in use. Poll every 2 s. |
| 7 | **Quick launcher page** (user-defined buttons: app, URL, folder, command) | Settings editor with icon picker (Segoe glyphs). Run via `ProcessStartInfo{UseShellExecute=true}`. |
| 8 | **Weather page** (opt-in) | Open-Meteo, no API key; user enters a city (geocoding endpoint) or lat/lon; cache 30 min; unit setting. |
| 9 | **Calendar: next event** (opt-in) | ICS URL feed (parse VEVENT, handle RRULE minimally or ignore recurring with a note). Show countdown in the pill 10 min before start. |
| 10 | **Pomodoro / focus timer** | Reuse timer infra; ring progress in pill; configurable work/break/long-break; optional "Do not disturb" by muting the island sounds. |
| 11 | **System stats page** (CPU, RAM, network, GPU if available) | `PerformanceCounter` or `System.Diagnostics`; sample at 1 Hz only while visible. |
| 12 | **Clipboard history** (opt-in, text only, last 10) | Clipboard listener via `AddClipboardFormatListener`; skip items flagged `ExcludeClipboardContentFromMonitorProcessing`; Privacy settings with clear-all and a "never store" window-title list; never persisted to disk by default. |
| 13 | **File shelf**: drag files onto the island, drag them out later | Window is click-through, so detect a button-down drag near the island (`GetAsyncKeyState(VK_LBUTTON)` + cursor proximity) and make it interactive; hard feature, schedule late. |
| 14 | **Quick notes** page | Plain text, autosave, a few slots. |
| 15 | **Better alarms/timers** | Repeat days, labels, custom .wav, multiple concurrent timers, "ring in X" summary. |
| 16 | **Themes + import/export** | Colour, rim, gloss, springs bundled as a `.islandtheme` JSON. |
| 17 | **Notification mirroring** | Research spike only. `UserNotificationListener` generally needs package identity, so it may be infeasible for an unpackaged exe. Report findings, do not build blindly. |
| 18 | **Plugin API** | Load page DLLs from `%AppData%\IslandWin\plugins`. Only after the page interface is stable; mention the security implications in the UI. |

---

## PHASE 5: Polish, performance, release

### Prompt 5

```text
Phase 5 of PLAN.md.
1. Performance: measure and log average frame time of the liquid pass in Snapshot mode (CI artifact `perf.txt`). Add the low-power mode (cap FPS, FieldScale 0.35, no satellite) when on battery or when the user selects it. Confirm idle = zero repaints and near-zero CPU with the island hidden.
2. Robustness: handle Explorer restart (re-create tray icon on TaskbarCreated), display changes, RDP sessions, sleep/resume (re-sync clock, alarms, media session).
3. Accessibility: keyboard navigation inside the expanded panel (Tab/Arrows/Enter), Narrator names via AutomationProperties, high-contrast fallback (no rim off, solid colours), respect reduce-animations.
4. Release: GitHub Actions on tag `v*` creates a Release with IslandWin.exe and a SHA256 file; version number from the tag shown in About. Keep framework-dependent single-file. Also publish a `self-contained` variant as a second asset.
5. Docs: update README (new file map, settings reference, troubleshooting: where logs and settings live, how to run --snapshot and --media-selftest).
```

---

## Review prompt (paste after every phase)

```text
Audit your own diff for this phase against its acceptance criteria in PLAN.md and the standing rules. List: (1) every criterion with PASS / FAIL / CANNOT VERIFY, (2) every API you were not 100% sure exists in .NET 8 / SkiaSharp 2.88.9, (3) any behaviour change outside the phase, (4) any new empty catch or magic number. Fix FAILs that are within scope, then report.
```

## Prompt for when CI fails to build

```text
The GitHub Actions build failed. Below is the log. Find the FIRST error (later ones are usually cascades), explain the cause in one sentence, fix only that, and do not refactor anything else. If the cause is an API that does not exist in SkiaSharp 2.88.9 or .NET 8, replace it with a simpler API you are certain about and add it to UNVERIFIED APIS.
<paste log>
```
