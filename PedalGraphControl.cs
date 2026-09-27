using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using Vortice;
using Vortice.Direct2D1;

namespace ACEvo_Driver_Input;

// The graph logic mirrors the original WPF control, but draws to a native Direct2D target.
internal sealed class PedalGraphControl
{
    private const double HistorySeconds = 10;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<GraphSample> _samples = [];

    public void AddSample(float throttle, float brake, float clutch, bool tcActive, bool absActive)
    {
        double now = _clock.Elapsed.TotalSeconds;
        _samples.Add(new GraphSample(now, throttle, brake, clutch, tcActive, absActive));
        Trim(now);
    }

    public void Draw(ID2D1HwndRenderTarget target, GraphBrushes brushes, float x, float y, float width, float height)
    {
        if (width <= 1 || height <= 1)
        {
            return;
        }

        RawRectF bounds = new(x, y, x + width, y + height);
        RoundedRectangle roundedBounds = new(new RectangleF(x, y, width, height), 5, 5);
        target.FillRoundedRectangle(roundedBounds, brushes.Background);

        for (int i = 1; i < 4; i++)
        {
            float lineY = y + height * i / 4;
            target.DrawLine(new Vector2(x, lineY), new Vector2(x + width, lineY), brushes.Grid, 1);
        }

        for (int i = 1; i < 5; i++)
        {
            float lineX = x + width * i / 5;
            target.DrawLine(new Vector2(lineX, y), new Vector2(lineX, y + height), brushes.Grid, 1);
        }

        double now = _clock.Elapsed.TotalSeconds;
        Trim(now);
        target.PushAxisAlignedClip(bounds, AntialiasMode.PerPrimitive);
        DrawSeries(target, now, x, y, width, height, static s => s.Throttle,
            static s => s.TcActive, brushes.Throttle, brushes.TcThrottle);
        DrawSeries(target, now, x, y, width, height, static s => s.Brake,
            static s => s.AbsActive, brushes.Brake, brushes.AbsBrake);
        DrawSeries(target, now, x, y, width, height, static s => s.Clutch,
            static _ => false, brushes.Clutch, brushes.Clutch);
        target.PopAxisAlignedClip();
        target.DrawRoundedRectangle(roundedBounds, brushes.Border, 1);
    }

    private void DrawSeries(
        ID2D1HwndRenderTarget target,
        double now,
        float x,
        float y,
        float width,
        float height,
        Func<GraphSample, float> value,
        Func<GraphSample, bool> activity,
        ID2D1SolidColorBrush normalBrush,
        ID2D1SolidColorBrush activeBrush)
    {
        Vector2? previous = null;
        foreach (GraphSample sample in _samples)
        {
            double age = now - sample.Time;
            if (age > HistorySeconds)
            {
                continue;
            }

            float pointX = x + width * (float)(1 - age / HistorySeconds);
            float pointY = y + height * (1 - Math.Clamp(value(sample), 0f, 1f));
            Vector2 point = new(pointX, pointY);
            if (previous is Vector2 start)
            {
                target.DrawLine(start, point, activity(sample) ? activeBrush : normalBrush, 2.5f);
            }

            previous = point;
        }
    }

    private void Trim(double now)
    {
        int removeCount = 0;
        double oldest = now - HistorySeconds - 0.25;
        while (removeCount < _samples.Count && _samples[removeCount].Time < oldest)
        {
            removeCount++;
        }

        if (removeCount > 0)
        {
            _samples.RemoveRange(0, removeCount);
        }
    }

    private readonly record struct GraphSample(
        double Time,
        float Throttle,
        float Brake,
        float Clutch,
        bool TcActive,
        bool AbsActive);
}

internal sealed class GraphBrushes : IDisposable
{
    public ID2D1SolidColorBrush Background { get; }
    public ID2D1SolidColorBrush Grid { get; }
    public ID2D1SolidColorBrush Border { get; }
    public ID2D1SolidColorBrush Throttle { get; }
    public ID2D1SolidColorBrush TcThrottle { get; }
    public ID2D1SolidColorBrush Brake { get; }
    public ID2D1SolidColorBrush AbsBrake { get; }
    public ID2D1SolidColorBrush Clutch { get; }

    public GraphBrushes(ID2D1HwndRenderTarget target)
    {
        Background = target.CreateSolidColorBrush(Colors.FromRgb(20, 20, 23));
        Grid = target.CreateSolidColorBrush(Colors.FromRgb(52, 52, 58));
        Border = target.CreateSolidColorBrush(Colors.FromRgb(72, 72, 78));
        Throttle = target.CreateSolidColorBrush(Colors.FromRgb(46, 208, 110));
        TcThrottle = target.CreateSolidColorBrush(Colors.FromRgb(168, 85, 247));
        Brake = target.CreateSolidColorBrush(Colors.FromRgb(255, 75, 85));
        AbsBrake = target.CreateSolidColorBrush(Colors.FromRgb(255, 214, 10));
        Clutch = target.CreateSolidColorBrush(Colors.FromRgb(76, 166, 255));
    }

    public void Dispose()
    {
        Background.Dispose();
        Grid.Dispose();
        Border.Dispose();
        Throttle.Dispose();
        TcThrottle.Dispose();
        Brake.Dispose();
        AbsBrake.Dispose();
        Clutch.Dispose();
    }
}
