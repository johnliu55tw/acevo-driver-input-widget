using System.ComponentModel;
using System.Runtime.InteropServices;
using ACEvo_Simple_Telemetry;

namespace ACEvo_Driver_Input;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        // Keep Win32 client pixels and Direct2D coordinates aligned on scaled displays.
        SetProcessDpiAwarenessContext((nint)(-4)); // PER_MONITOR_AWARE_V2
        using InputWindow window = new();
        return window.Run();
    }

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(nint dpiContext);
}

internal sealed class InputWindow : IDisposable
{
    private const string ClassName = "AcevoDriverInputDirect2DWindow";
    private const uint WsPopup = 0x80000000;
    private const uint WsExTopmost = 0x00000008;
    private const uint WsExToolWindow = 0x00000080;
    private const int WindowWidth = 960;
    private const int WindowHeight = 210;
    private const uint WmSize = 0x0005;
    private const uint WmClose = 0x0010;
    private const uint WmDestroy = 0x0002;
    private const uint WmPaint = 0x000F;
    private const uint WmEraseBkgnd = 0x0014;
    private const uint WmTimer = 0x0113;
    private const uint WmNcHitTest = 0x0084;
    private const uint WmNcRightButtonUp = 0x00A5;
    private const nint HitCaption = 2;
    private const nuint TelemetryTimerId = 1;
    private const uint MfPopup = 0x0010;
    private const uint MfChecked = 0x0008;
    private const uint MfSeparator = 0x0800;
    private const uint TpmReturnCmdRightButton = 0x0102;
    private const uint SwpNoMoveNoZOrderNoActivate = 0x0016;
    private const uint ExitCommand = 1;
    private const uint ThrottleCommand = 10;
    private const uint BrakeCommand = 11;
    private const uint ClutchCommand = 12;
    private const uint ZoomInCommand = 20;
    private const uint ZoomOutCommand = 21;
    private const uint ZoomBaseCommand = 100;
    private const uint DarkThemeCommand = 200;
    private const uint LightThemeCommand = 201;
    private const uint GraphSpanBaseCommand = 300;
    private const uint ChartWidthBaseCommand = 400;
    private const uint UpdateRateBaseCommand = 500;

    private static readonly WndProcDelegate WindowProcedureDelegate = WindowProcedure;
    private static InputWindow? s_window;

    private readonly ACEvoTelemetryReader _reader = new();
    private readonly WidgetSettings _settings = WidgetSettings.Load();
    private Direct2DRenderer? _renderer;
    private TelemetrySample? _sample;
    private DateTime _nextConnectAttempt;
    private nint _hwnd;

    public int Run()
    {
        s_window = this;
        nint module = GetModuleHandleW(null);
        WindowClassEx windowClass = new()
        {
            Size = (uint)Marshal.SizeOf<WindowClassEx>(),
            Style = 0x0003, // CS_HREDRAW | CS_VREDRAW
            Procedure = WindowProcedureDelegate,
            Instance = module,
            Cursor = LoadCursorW(0, (nint)32512),
            ClassName = ClassName
        };
        if (RegisterClassExW(ref windowClass) == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to register the window class.");
        }

        _hwnd = CreateWindowExW(WsExTopmost | WsExToolWindow, ClassName, "AC EVO Driver Inputs — Direct2D PoC",
            WsPopup, 100, 100, ScaledWidth, ScaledHeight,
            0, 0, module, 0);
        if (_hwnd == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to create the Win32 window.");
        }

        UpdateCornerRegion();

        _renderer = new Direct2DRenderer(_hwnd, _settings);
        if (SetTimer(_hwnd, TelemetryTimerId, _settings.UpdateIntervalMilliseconds, 0) == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to start the telemetry timer.");
        }

        ShowWindow(_hwnd, 5);
        UpdateWindow(_hwnd);
        while (true)
        {
            int result = GetMessageW(out WindowMessage message, 0, 0, 0);
            if (result == -1)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "The Win32 message loop failed.");
            }
            if (result == 0)
            {
                return unchecked((int)message.WParam);
            }

            TranslateMessage(ref message);
            DispatchMessageW(ref message);
        }
    }

    private static nint WindowProcedure(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        InputWindow? window = s_window;
        if (window is null)
        {
            return DefWindowProcW(hwnd, message, wParam, lParam);
        }

        switch (message)
        {
            case WmTimer when wParam == TelemetryTimerId:
                window.PollTelemetry();
                InvalidateRect(hwnd, 0, false);
                return 0;
            case WmSize:
                window.Resize();
                return 0;
            case WmPaint:
                window.Paint();
                ValidateRect(hwnd, 0);
                return 0;
            case WmEraseBkgnd:
                return 1;
            case WmNcHitTest:
                return HitCaption; // Drag the borderless overlay from any point.
            case WmNcRightButtonUp:
                window.ShowSettingsMenu(hwnd);
                return 0;
            case WmClose:
                DestroyWindow(hwnd);
                return 0;
            case WmDestroy:
                KillTimer(hwnd, TelemetryTimerId);
                PostQuitMessage(0);
                return 0;
            default:
                return DefWindowProcW(hwnd, message, wParam, lParam);
        }
    }

    private int LogicalWindowWidth => WindowWidth + _settings.ChartWidthPixels - WidgetSettings.MediumChartWidthPixels;
    private int ScaledWidth => (int)Math.Round(LogicalWindowWidth * _settings.ZoomPercent / 100.0);
    private int ScaledHeight => (int)Math.Round(WindowHeight * _settings.ZoomPercent / 100.0);

    private void ShowSettingsMenu(nint hwnd)
    {
        nint menu = CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            nint pedals = CreatePopupMenu();
            nint zoom = CreatePopupMenu();
            nint theme = CreatePopupMenu();
            nint graphSpan = CreatePopupMenu();
            nint chartWidth = CreatePopupMenu();
            nint updateRate = CreatePopupMenu();
            if (pedals == 0 || zoom == 0 || theme == 0 || graphSpan == 0 || chartWidth == 0 || updateRate == 0)
            {
                if (pedals != 0) DestroyMenu(pedals);
                if (zoom != 0) DestroyMenu(zoom);
                if (theme != 0) DestroyMenu(theme);
                if (graphSpan != 0) DestroyMenu(graphSpan);
                if (chartWidth != 0) DestroyMenu(chartWidth);
                if (updateRate != 0) DestroyMenu(updateRate);
                return;
            }

            AppendMenuW(pedals, _settings.ShowThrottle ? MfChecked : 0, ThrottleCommand, "Throttle");
            AppendMenuW(pedals, _settings.ShowBrake ? MfChecked : 0, BrakeCommand, "Brake");
            AppendMenuW(pedals, _settings.ShowClutch ? MfChecked : 0, ClutchCommand, "Clutch");
            AppendMenuW(menu, MfPopup, (nuint)pedals, "Pedal inputs");

            AppendMenuW(zoom, 0, ZoomOutCommand, "Zoom out");
            AppendMenuW(zoom, 0, ZoomInCommand, "Zoom in");
            AppendMenuW(zoom, MfSeparator, 0, string.Empty);
            for (int i = 0; i < WidgetSettings.AvailableZoomLevels.Count; i++)
            {
                int percent = WidgetSettings.AvailableZoomLevels[i];
                AppendMenuW(zoom, _settings.ZoomPercent == percent ? MfChecked : 0,
                    ZoomBaseCommand + (uint)i, $"{percent}%");
            }
            AppendMenuW(menu, MfPopup, (nuint)zoom, "Zoom");

            AppendMenuW(theme, _settings.IsLightTheme ? 0 : MfChecked, DarkThemeCommand, "Dark");
            AppendMenuW(theme, _settings.IsLightTheme ? MfChecked : 0, LightThemeCommand, "Light");
            AppendMenuW(menu, MfPopup, (nuint)theme, "Theme");

            for (int i = 0; i < WidgetSettings.AvailableGraphSpans.Count; i++)
            {
                int seconds = WidgetSettings.AvailableGraphSpans[i];
                AppendMenuW(graphSpan, _settings.GraphTimeSpanSeconds == seconds ? MfChecked : 0,
                    GraphSpanBaseCommand + (uint)i, $"{seconds} seconds");
            }
            AppendMenuW(menu, MfPopup, (nuint)graphSpan, "Graph time span");
            for (int i = 0; i < WidgetSettings.AvailableChartWidths.Count; i++)
            {
                string width = WidgetSettings.AvailableChartWidths[i];
                AppendMenuW(chartWidth, _settings.ChartWidth == width ? MfChecked : 0,
                    ChartWidthBaseCommand + (uint)i, width);
            }
            AppendMenuW(menu, MfPopup, (nuint)chartWidth, "Chart width");
            for (int i = 0; i < WidgetSettings.AvailableUpdateRates.Count; i++)
            {
                int rate = WidgetSettings.AvailableUpdateRates[i];
                AppendMenuW(updateRate, _settings.UpdateRateHz == rate ? MfChecked : 0,
                    UpdateRateBaseCommand + (uint)i, $"{rate} Hz");
            }
            AppendMenuW(menu, MfPopup, (nuint)updateRate, "Update rate");
            AppendMenuW(menu, MfSeparator, 0, string.Empty);
            AppendMenuW(menu, 0, ExitCommand, "Exit");
            GetCursorPos(out WindowPoint point);
            SetForegroundWindow(hwnd);
            HandleMenuCommand(TrackPopupMenuEx(menu, TpmReturnCmdRightButton, point.X, point.Y, hwnd, 0));
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private void HandleMenuCommand(uint command)
    {
        if (command == ExitCommand)
        {
            DestroyWindow(_hwnd);
            return;
        }

        int previousWidth = ScaledWidth;
        int previousHeight = ScaledHeight;
        int previousUpdateRate = _settings.UpdateRateHz;
        switch (command)
        {
            case ThrottleCommand: _settings.ShowThrottle = !_settings.ShowThrottle; break;
            case BrakeCommand: _settings.ShowBrake = !_settings.ShowBrake; break;
            case ClutchCommand: _settings.ShowClutch = !_settings.ShowClutch; break;
            case DarkThemeCommand: _settings.Theme = "Dark"; break;
            case LightThemeCommand: _settings.Theme = "Light"; break;
            case ZoomInCommand:
            case ZoomOutCommand:
                int current = 0;
                while (WidgetSettings.AvailableZoomLevels[current] != _settings.ZoomPercent)
                    current++;
                int next = Math.Clamp(current + (command == ZoomInCommand ? 1 : -1),
                    0, WidgetSettings.AvailableZoomLevels.Count - 1);
                _settings.ZoomPercent = WidgetSettings.AvailableZoomLevels[next];
                break;
            default:
                if (command >= ZoomBaseCommand && command < ZoomBaseCommand + (uint)WidgetSettings.AvailableZoomLevels.Count)
                    _settings.ZoomPercent = WidgetSettings.AvailableZoomLevels[(int)(command - ZoomBaseCommand)];
                else if (command >= GraphSpanBaseCommand && command < GraphSpanBaseCommand + (uint)WidgetSettings.AvailableGraphSpans.Count)
                    _settings.GraphTimeSpanSeconds = WidgetSettings.AvailableGraphSpans[(int)(command - GraphSpanBaseCommand)];
                else if (command >= ChartWidthBaseCommand && command < ChartWidthBaseCommand + (uint)WidgetSettings.AvailableChartWidths.Count)
                    _settings.ChartWidth = WidgetSettings.AvailableChartWidths[(int)(command - ChartWidthBaseCommand)];
                else if (command >= UpdateRateBaseCommand && command < UpdateRateBaseCommand + (uint)WidgetSettings.AvailableUpdateRates.Count)
                    _settings.UpdateRateHz = WidgetSettings.AvailableUpdateRates[(int)(command - UpdateRateBaseCommand)];
                else
                    return;
                break;
        }

        if (previousUpdateRate != _settings.UpdateRateHz &&
            SetTimer(_hwnd, TelemetryTimerId, _settings.UpdateIntervalMilliseconds, 0) == 0)
        {
            _settings.UpdateRateHz = previousUpdateRate;
            return;
        }

        bool resize = previousWidth != ScaledWidth || previousHeight != ScaledHeight;
        _renderer?.ApplySettings(_settings);
        if (resize)
        {
            SetWindowPos(_hwnd, 0, 0, 0, ScaledWidth, ScaledHeight, SwpNoMoveNoZOrderNoActivate);
            UpdateCornerRegion();
        }
        InvalidateRect(_hwnd, 0, false);
        _settings.Save();
    }

    private void UpdateCornerRegion()
    {
        int diameter = (int)Math.Round(18 * _settings.ZoomPercent / 100.0);
        nint region = CreateRoundRectRgn(0, 0, ScaledWidth, ScaledHeight, diameter, diameter);
        if (region != 0 && SetWindowRgn(_hwnd, region, true) == 0)
            DeleteObject(region);
    }

    private void PollTelemetry()
    {
        if (!_reader.IsConnected && DateTime.UtcNow >= _nextConnectAttempt)
        {
            _reader.TryConnect();
            _nextConnectAttempt = DateTime.UtcNow.AddSeconds(1);
        }

        if (_reader.TryRead(out TelemetrySample sample))
        {
            _sample = sample;
            if (sample.Status is ACEvoStatus.Live or ACEvoStatus.Pause)
            {
                _renderer?.AddSample(sample);
            }
        }
        else if (!_reader.IsConnected)
        {
            _sample = null;
        }
    }

    private void Resize()
    {
        if (_renderer is not null && GetClientRect(_hwnd, out WindowRect bounds))
        {
            _renderer.Resize(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        }
    }

    private void Paint()
    {
        if (_renderer is not null && GetClientRect(_hwnd, out WindowRect bounds))
        {
            _renderer.Render(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, _sample);
        }
    }

    public void Dispose()
    {
        _renderer?.Dispose();
        _reader.Dispose();
        if (_hwnd != 0 && IsWindow(_hwnd))
        {
            DestroyWindow(_hwnd);
        }
        s_window = null;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WndProcDelegate(nint hwnd, uint message, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClassEx
    {
        public uint Size;
        public uint Style;
        public WndProcDelegate Procedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public nint MenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowMessage
    {
        public nint Hwnd;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public WindowPoint Point;
        public uint Private;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? moduleName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WindowClassEx windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(uint extendedStyle, string className, string windowName,
        uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadCursorW(nint instance, nint cursorName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetMessageW(out WindowMessage message, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref WindowMessage message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DispatchMessageW(ref WindowMessage message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DefWindowProcW(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nuint SetTimer(nint hwnd, nuint timerId, uint intervalMilliseconds, nint callback);
    [DllImport("user32.dll")]
    private static extern bool KillTimer(nint hwnd, nuint timerId);
    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint hwnd, out WindowRect rect);
    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(nint hwnd, nint rect, bool erase);
    [DllImport("user32.dll")]
    private static extern bool ValidateRect(nint hwnd, nint rect);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")]
    private static extern bool UpdateWindow(nint hwnd);
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);
    [DllImport("gdi32.dll")]
    private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(nint hwnd, nint region, bool redraw);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint handle);
    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(nint menu, uint flags, nuint itemId, string text);
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out WindowPoint point);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint parameters);
    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);
}
