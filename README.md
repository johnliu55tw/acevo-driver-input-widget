# AC EVO Driver Inputs Direct2D PoC

A native Win32, always-on-top, borderless window that renders with Vortice.Direct2D1. It has no WPF dependency or settings. The pedal graph remains visible when Assetto Corsa EVO is not running or no session is active.

The PoC copies `ACEvoTelemetryReader.cs` and its `DebugLog.cs` dependency from ACEvo-Simple-Telemetry. `PedalGraphControl.cs` and `SteeringWheelControl.cs` recreate those WPF controls with Direct2D: the graph shows throttle, brake, and clutch history, with purple TC and yellow ABS segments; the wheel shows steering rotation. The compact layout places the graph first, then clutch, brake, and throttle gauges, gear, and steering wheel. The graph uses a fixed ten-second history.

## Run

Requires Windows and the .NET 10 SDK:

```powershell
dotnet run --project .\ACEvo-Driver-Input.csproj
```

The window opens even without the game and shows an empty graph and neutral gauges. When AC EVO's shared-memory mappings are available, values update automatically. Drag anywhere to move the window; right-click and choose **Exit** to close it.
