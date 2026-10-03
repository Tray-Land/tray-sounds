@AGENTS.md

## Tray app conventions

This is a tray-only WinUI 3 app built from the `winui-tray-app` skill scaffold on top of `winapp new`. Load that skill before changing app lifetime, the flyout, the tray icon, polling, settings, or secrets.

- Launch with `winapp run` (or `dotnet run`); never register the package by hand. Use `winapp run --clean` to test first-run behavior.
- `App.xaml.cs` owns the tray icon, single instance, and the only exit path (the Exit menu). There is no main window.
- The flyout (`Views/TrayFlyoutWindow`) is hidden on dismiss and closed after a minute hidden. Anything it starts, it must stop in `FlyoutPage.OnHidden` / `Dispose`.
- The app makes no network calls and declares no `internetClient` capability; the scaffold's ForegroundPoller, Connectivity, Secrets, and CredentialStore were removed. If networking is ever added, bring them back from the `winui-tray-app` skill rather than polling while hidden.
- Playback (`Services/PlaybackService`) is deliberately window-independent so sounds outlive the flyout; each player is disposed when its sound ends. The flyout only subscribes to its events while shown.
- Settings are a flyout page (`Views/SettingsPage`). Adding a sound is a real window (`Views/AddSoundWindow`) because the file picker and mic consent take focus and would light-dismiss the flyout.
- Stock sounds are generated: edit `scripts/GenerateStockSounds.cs` and rerun `dotnet run --file scripts\GenerateStockSounds.cs`. Never hand-edit the WAVs.
- The repo is MIT licensed (`LICENSE`). When the app's features are working, write a descriptive `README.md` (what it does, screenshot, install, setup/keys, privacy, build from source, license); the `winui-tray-app` skill lists the sections.
- Put pure decision logic (parsing, policies, formatting) in classes with no WinRT dependency, so a plain `net10.0` test project can cover it.
