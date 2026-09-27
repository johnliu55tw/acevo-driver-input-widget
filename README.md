# AC EVO Driver Inputs Direct2D PoC

A native Win32, always-on-top, borderless window that renders with Vortice.Direct2D1. It has no WPF dependency. The pedal graph remains visible when Assetto Corsa EVO is not running or no session is active.

The PoC copies `ACEvoTelemetryReader.cs` and its `DebugLog.cs` dependency from ACEvo-Simple-Telemetry. `PedalGraphControl.cs` and `SteeringWheelControl.cs` recreate those WPF controls with Direct2D: the graph shows throttle, brake, and clutch history, with purple TC and yellow ABS segments; the wheel shows steering rotation. The compact layout places the graph first, then clutch, brake, and throttle gauges, gear, and steering wheel. Dark and light colors match the theme definitions in ACEvo-Simple-Telemetry.

## Run

Requires Windows and the .NET 10 SDK:

```powershell
dotnet run --project .\ACEvo-Driver-Input.csproj
```

The window opens even without the game and shows an empty graph and neutral gauges. When AC EVO's shared-memory mappings are available, values update automatically. Drag anywhere to move the window. Right-click to choose visible pedals, zoom from 50% to 250%, Dark or Light theme, a 5, 10, 15, 20, or 30-second graph span, Small, Medium, or Large chart width, and a 30 or 60 Hz update rate. The update rate controls both telemetry polling and redraws. The graph's vertical grid marks each second at every width and time span. **Zoom in** and **Zoom out** step through the listed levels. Choose **Exit** to close it. Preferences are saved in `%LOCALAPPDATA%\ACEvoDriverInput\settings.json`.
