using System.Drawing;
using System.Globalization;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using ACEvo_Simple_Telemetry;

namespace ACEvo_Driver_Input;

internal sealed class Direct2DRenderer : IDisposable
{
    private readonly nint _hwnd;
    private readonly PedalGraphControl _graph = new();
    private readonly SteeringWheelControl _wheel = new();
    private readonly ID2D1Factory _factory;
    private readonly IDWriteFactory _writeFactory;
    private readonly IDWriteTextFormat _gaugeFormat;
    private readonly IDWriteTextFormat _gearFormat;
    private readonly IDWriteTextFormat _angleFormat;
    private ThemePalette _palette;
    private float _zoomScale;
    private ID2D1HwndRenderTarget? _target;
    private GraphBrushes? _graphBrushes;
    private SteeringBrushes? _wheelBrushes;
    private ID2D1SolidColorBrush? _text;
    private ID2D1SolidColorBrush? _panel;
    private ID2D1SolidColorBrush? _border;

    public Direct2DRenderer(nint hwnd, WidgetSettings settings)
    {
        _hwnd = hwnd;
        _palette = settings.IsLightTheme ? ThemePalette.Light : ThemePalette.Dark;
        _zoomScale = settings.ZoomPercent / 100f;
        ApplySettings(settings);
        _factory = D2D1.D2D1CreateFactory<ID2D1Factory>(Vortice.Direct2D1.FactoryType.SingleThreaded, DebugLevel.None);
        _writeFactory = DWrite.DWriteCreateFactory<IDWriteFactory>(Vortice.DirectWrite.FactoryType.Shared);
        _gaugeFormat = CenteredFormat(16);
        _gearFormat = CenteredFormat(70);
        _angleFormat = CenteredFormat(21);
    }

    private IDWriteTextFormat CenteredFormat(float size)
    {
        IDWriteTextFormat format = _writeFactory.CreateTextFormat("Segoe UI", null, FontWeight.Bold,
            FontStyle.Normal, FontStretch.Normal, size, "en-US");
        format.TextAlignment = TextAlignment.Center;
        format.ParagraphAlignment = ParagraphAlignment.Center;
        format.WordWrapping = WordWrapping.NoWrap;
        return format;
    }

    public void AddSample(TelemetrySample sample) =>
        _graph.AddSample(sample.Throttle, sample.Brake, sample.Clutch, sample.TcActive, sample.AbsActive);

    public void ApplySettings(WidgetSettings settings)
    {
        _graph.ShowThrottle = settings.ShowThrottle;
        _graph.ShowBrake = settings.ShowBrake;
        _graph.ShowClutch = settings.ShowClutch;
        _graph.TimeSpanSeconds = settings.GraphTimeSpanSeconds;
        ThemePalette palette = settings.IsLightTheme ? ThemePalette.Light : ThemePalette.Dark;
        if (_palette != palette)
        {
            _palette = palette;
            ReleaseTarget();
        }
        _zoomScale = settings.ZoomPercent / 100f;
        _target?.SetDpi(96 * _zoomScale, 96 * _zoomScale);
    }

    public void Resize(int width, int height)
    {
        if (_target is not null && width > 0 && height > 0)
        {
            _target.Resize(new SizeI(width, height));
        }
    }

    public void Render(int width, int height, TelemetrySample? sample)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        EnsureTarget(width, height);
        ID2D1HwndRenderTarget target = _target!;
        float logicalWidth = width / _zoomScale;
        float logicalHeight = height / _zoomScale;
        GraphBrushes graphBrushes = _graphBrushes!;
        TelemetrySample? displayed = sample?.Status is ACEvoStatus.Live or ACEvoStatus.Pause or ACEvoStatus.Replay
            ? sample
            : null;

        target.BeginDraw();
        target.Clear(_palette.OverlayBackground);
        target.DrawRoundedRectangle(new RoundedRectangle(new RectangleF(0.5f, 0.5f, logicalWidth - 1, logicalHeight - 1), 9, 9),
            _border!, 1);

        const float top = 17;
        const float panelHeight = 180;
        const float graphX = 12;
        int visiblePedals = (_graph.ShowClutch ? 1 : 0) + (_graph.ShowBrake ? 1 : 0) + (_graph.ShowThrottle ? 1 : 0);
        float gearX = logicalWidth - 277;
        float graphWidth = Math.Max(1, visiblePedals == 0
            ? gearX - graphX - 12
            : gearX - graphX - 24 - (visiblePedals * 40 - 6));
        float gaugeX = graphX + graphWidth + 12;
        float wheelX = gearX + 97 + 8;

        _graph.Draw(target, graphBrushes, graphX, top, graphWidth, panelHeight);
        if (_graph.ShowClutch)
        {
            DrawGauge(target, gaugeX, top, panelHeight, displayed?.Clutch ?? 0, graphBrushes.Clutch);
            gaugeX += 40;
        }
        if (_graph.ShowBrake)
        {
            DrawGauge(target, gaugeX, top, panelHeight, displayed?.Brake ?? 0, graphBrushes.Brake);
            gaugeX += 40;
        }
        if (_graph.ShowThrottle)
            DrawGauge(target, gaugeX, top, panelHeight, displayed?.Throttle ?? 0, graphBrushes.Throttle);
        DrawGear(target, gearX, top, panelHeight, displayed);
        DrawWheel(target, wheelX, top, panelHeight, displayed);

        if (target.EndDraw().Failure)
        {
            ReleaseTarget();
        }
    }

    private void DrawGauge(ID2D1HwndRenderTarget target, float x, float y, float height,
        float value, ID2D1SolidColorBrush color)
    {
        const float gaugeWidth = 34;
        target.FillRectangle(new RawRectF(x, y, x + gaugeWidth, y + height), _panel!);
        float filled = Math.Clamp(value, 0, 1) * (height - 2);
        if (filled > 0)
        {
            target.FillRectangle(new RawRectF(x + 1, y + height - 1 - filled, x + gaugeWidth - 1, y + height - 1), color);
        }
        target.DrawRectangle(new RawRectF(x, y, x + gaugeWidth, y + height), _border!, 1);
        DrawText(target, MathF.Round(value * 100).ToString(CultureInfo.InvariantCulture),
            x, y, gaugeWidth, height, _gaugeFormat);
    }

    private void DrawGear(ID2D1HwndRenderTarget target, float x, float y, float height, TelemetrySample? sample)
    {
        DrawPanel(target, x, y, 97, height);
        DrawText(target, FormatGear(sample), x, y, 97, height, _gearFormat);
    }

    private void DrawWheel(ID2D1HwndRenderTarget target, float x, float y, float height, TelemetrySample? sample)
    {
        const float panelWidth = 160;
        DrawPanel(target, x, y, panelWidth, height);
        float angle = sample?.SteeringRadians ?? 0;
        _wheel.Draw(target, _wheelBrushes!, x + 20, y + 10, 120, angle);
        float degrees = angle * 180 / MathF.PI;
        DrawText(target, $"{degrees:+0;-0;0}°", x + 4, y + 142, panelWidth - 8, 30, _angleFormat);
    }

    private void DrawPanel(ID2D1HwndRenderTarget target, float x, float y, float width, float height)
    {
        RoundedRectangle panel = new(new RectangleF(x, y, width, height), 5, 5);
        target.FillRoundedRectangle(panel, _panel!);
        target.DrawRoundedRectangle(panel, _border!, 1);
    }

    private static string FormatGear(TelemetrySample? sample) => sample?.Gear switch
    {
        0 => "R",
        1 => "N",
        > 1 => (sample.Value.Gear - 1).ToString(CultureInfo.InvariantCulture),
        _ => "N"
    };

    private void DrawText(ID2D1HwndRenderTarget target, string text, float x, float y, float width,
        float height, IDWriteTextFormat format) =>
        target.DrawText(text, format, new Rect(x, y, width, height), _text!);

    private void EnsureTarget(int width, int height)
    {
        if (_target is not null)
        {
            return;
        }

        HwndRenderTargetProperties hwndProperties = new()
        {
            Hwnd = _hwnd,
            PixelSize = new SizeI(width, height)
        };
        _target = _factory.CreateHwndRenderTarget(new RenderTargetProperties(), hwndProperties);
        _target.SetDpi(96 * _zoomScale, 96 * _zoomScale);
        _graphBrushes = new GraphBrushes(_target, _palette);
        _wheelBrushes = new SteeringBrushes(_target, _palette);
        _text = _target.CreateSolidColorBrush(_palette.PrimaryText);
        _panel = _target.CreateSolidColorBrush(_palette.PanelBackground);
        _border = _target.CreateSolidColorBrush(_palette.ControlBorder);
    }

    private void ReleaseTarget()
    {
        _border?.Dispose();
        _panel?.Dispose();
        _text?.Dispose();
        _wheelBrushes?.Dispose();
        _graphBrushes?.Dispose();
        _target?.Dispose();
        _border = null;
        _panel = null;
        _text = null;
        _wheelBrushes = null;
        _graphBrushes = null;
        _target = null;
    }

    public void Dispose()
    {
        ReleaseTarget();
        _angleFormat.Dispose();
        _gearFormat.Dispose();
        _gaugeFormat.Dispose();
        _writeFactory.Dispose();
        _factory.Dispose();
    }
}
