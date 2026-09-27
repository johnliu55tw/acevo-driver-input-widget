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
    private ID2D1HwndRenderTarget? _target;
    private GraphBrushes? _graphBrushes;
    private SteeringBrushes? _wheelBrushes;
    private ID2D1SolidColorBrush? _text;
    private ID2D1SolidColorBrush? _panel;
    private ID2D1SolidColorBrush? _border;

    public Direct2DRenderer(nint hwnd)
    {
        _hwnd = hwnd;
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
        GraphBrushes graphBrushes = _graphBrushes!;
        TelemetrySample? displayed = sample?.Status is ACEvoStatus.Live or ACEvoStatus.Pause or ACEvoStatus.Replay
            ? sample
            : null;

        target.BeginDraw();
        target.Clear(Colors.FromRgb(14, 15, 18));
        target.DrawRoundedRectangle(new RoundedRectangle(new RectangleF(0.5f, 0.5f, width - 1, height - 1), 9, 9),
            _border!, 1);

        const float top = 17;
        const float panelHeight = 180;
        const float graphX = 12;
        float graphWidth = Math.Max(1, width - 427);
        float gaugeX = graphX + graphWidth + 12;
        float gearX = gaugeX + 114 + 12;
        float wheelX = gearX + 97 + 8;

        _graph.Draw(target, graphBrushes, graphX, top, graphWidth, panelHeight);
        DrawGauge(target, gaugeX, top, panelHeight, displayed?.Clutch ?? 0, graphBrushes.Clutch);
        DrawGauge(target, gaugeX + 40, top, panelHeight, displayed?.Brake ?? 0, graphBrushes.Brake);
        DrawGauge(target, gaugeX + 80, top, panelHeight, displayed?.Throttle ?? 0, graphBrushes.Throttle);
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
        _target.SetDpi(96, 96);
        _graphBrushes = new GraphBrushes(_target);
        _wheelBrushes = new SteeringBrushes(_target);
        _text = _target.CreateSolidColorBrush(Colors.FromRgb(246, 246, 248));
        _panel = _target.CreateSolidColorBrush(Colors.FromRgb(15, 16, 19));
        _border = _target.CreateSolidColorBrush(Colors.FromRgb(58, 60, 66));
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
