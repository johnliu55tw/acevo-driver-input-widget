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

    private static readonly WndProcDelegate WindowProcedureDelegate = WindowProcedure;
    private static InputWindow? s_window;

    private readonly ACEvoTelemetryReader _reader = new();
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
            WsPopup, 100, 100, WindowWidth, WindowHeight,
            0, 0, module, 0);
        if (_hwnd == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to create the Win32 window.");
        }

        nint cornerRegion = CreateRoundRectRgn(0, 0, WindowWidth, WindowHeight, 18, 18);
        if (cornerRegion != 0 && SetWindowRgn(_hwnd, cornerRegion, true) == 0)
        {
            DeleteObject(cornerRegion);
        }

        _renderer = new Direct2DRenderer(_hwnd);
        if (SetTimer(_hwnd, TelemetryTimerId, 33, 0) == 0)
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
                ShowExitMenu(hwnd);
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

    private static void ShowExitMenu(nint hwnd)
    {
        nint menu = CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            AppendMenuW(menu, 0, 1, "Exit");
            GetCursorPos(out WindowPoint point);
            SetForegroundWindow(hwnd);
            if (TrackPopupMenuEx(menu, 0x0102, point.X, point.Y, hwnd, 0) == 1)
            {
                DestroyWindow(hwnd);
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
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
}
