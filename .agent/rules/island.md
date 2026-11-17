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
