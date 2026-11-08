# Contributing to Home Control

Thanks for helping. Bug reports, fixes, design ideas and support for more devices are all welcome.

## Getting started

You need Windows 10 or 11 (x64) and the [.NET 10 SDK](https://dotnet.microsoft.com/download). Visual Studio isn't
needed.

```
cd native
.\build.ps1                     # release build -> %LOCALAPPDATA%\Temp\HomeControlBuild\publish
.\install.ps1                   # install it (Start menu, Settings > Apps)
```

You don't need any devices to work on the app:

- `HomeControl.exe --demo` runs with a simulated light and two simulated air conditioners. Nothing is sent over the
  network and your own settings aren't touched.
- `HomeControl.exe --demo --screenshots <folder>` renders every page and quick panel tab, in light and dark themes,
  without showing a window. Please attach before/after screenshots to pull requests that change the UI.
- `probe\` is a console program for trying protocol code against a real device (`Probe.exe gree` finds, pairs and
  reads Gree air conditioners).

Every push and pull request is built on GitHub (see `.github/workflows/build.yml`); the run's page has the zip.

## How the code is laid out

- `native/Core/`: device protocols and settings, with no UI code. Tuya: `TuyaProtocol.cs`, `TuyaLink.cs`,
  `TuyaCloud.cs`, `LightController.cs`. Gree: `Gree.cs`, `AcState.cs`. Simulated devices: `Demo.cs`.
- `native/LightSession.cs` and `native/AcHub.cs`: what the pages show, kept in step with the devices.
- `native/Pages/`, `native/Shell/` (tray, quick panel, startup), `native/Controls/`, `native/Art/` (drawings).
- `native/Music/`: audio capture, analysis and the music sync engine.

## Adding support for a device

There isn't a general driver interface yet; it's the next big step, and ideas are welcome in an issue. Today:

- **An air conditioner** that isn't Gree: implement `IAcClient` (read the state, apply changes) in `native/Core/`,
  and map the unit's settings to the keys `AcState` uses. `AcUnit` and the Climate page then work with it.
- **A light** that isn't Tuya: `LightController` would need to pick a protocol by device type. Please open an issue
  first so we can agree on the shape.
- **Anything else** (plugs, fans, sensors): open an issue with the device's details first.

Whatever the device:

1. Add a `probe` command that finds the device and reads it, so others with the same device can test without the app.
2. Add it to `Demo.cs` if the UI shows it differently, so it can be screenshotted.
3. Say in the pull request exactly which models and firmware you tested.

### Safety rules for device code

People will run this against hardware they paid for, so:

- Only use what the device's own app does in normal use. Never write to firmware, flash or configuration memory,
  and never send commands you've only guessed.
- Read before you write, and change only what the user asked for. If you have to take control of something (a mode
  the phone app normally runs), give it back when the app stops, errors or quits.
- Keep everything on the local network unless the device has no local control. No telemetry, no third-party servers.
- Keys and passwords stay in the user's settings file (`%APPDATA%\Home Control\config.json`). Never log them, put
  them in error messages or commit them. Use the demo or made-up values in tests, docs and screenshots.

## Pull requests

- One change per pull request, with a short description of what changed and how you tested it.
- Keep the UI text short and plain, in the style the app already uses.
- Match the code style around you. Comments say why, not what.
- By contributing, you agree that your work is released under the [MIT licence](LICENSE).
