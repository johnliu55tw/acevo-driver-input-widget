using System.Numerics;
using Vortice.Direct2D1;

namespace ACEvo_Driver_Input;

internal sealed class SteeringWheelControl
{
    public void Draw(
        ID2D1HwndRenderTarget target,
        SteeringBrushes brushes,
        float x,
        float y,
        float size,
        float angleRadians)
    {
        Vector2 center = new(x + size / 2, y + size / 2);
        float radius = Math.Max(0, size / 2 - 7);
        if (radius <= 0)
        {
            return;
        }

        target.FillEllipse(new Ellipse(center, radius, radius), brushes.Background);
        target.DrawEllipse(new Ellipse(center, radius, radius), brushes.Rim, 8);

        target.DrawLine(center, RotatedPoint(center, radius - 7, -90, angleRadians), brushes.Spoke, 7);
        target.DrawLine(center, RotatedPoint(center, radius - 7, 150, angleRadians), brushes.Spoke, 7);
        target.DrawLine(center, RotatedPoint(center, radius - 7, 30, angleRadians), brushes.Spoke, 7);
        target.DrawLine(
            RotatedPoint(center, radius + 1, -90, angleRadians),
            RotatedPoint(center, radius - 16, -90, angleRadians),
            brushes.Marker,
            8);
        target.FillEllipse(new Ellipse(center, 15, 15), brushes.Hub);
    }

    private static Vector2 RotatedPoint(Vector2 center, float radius, double degrees, float angleRadians)
    {
        double radians = degrees * Math.PI / 180 + angleRadians;
        return center + new Vector2((float)Math.Cos(radians) * radius, (float)Math.Sin(radians) * radius);
    }
}

internal sealed class SteeringBrushes : IDisposable
{
    public ID2D1SolidColorBrush Background { get; }
    public ID2D1SolidColorBrush Rim { get; }
    public ID2D1SolidColorBrush Spoke { get; }
    public ID2D1SolidColorBrush Marker { get; }
    public ID2D1SolidColorBrush Hub { get; }

    public SteeringBrushes(ID2D1HwndRenderTarget target, ThemePalette palette)
    {
        Background = target.CreateSolidColorBrush(palette.WheelBackground);
        Rim = target.CreateSolidColorBrush(palette.WheelRim);
        Spoke = target.CreateSolidColorBrush(palette.WheelSpoke);
        Marker = target.CreateSolidColorBrush(Colors.FromRgb(46, 208, 110));
        Hub = target.CreateSolidColorBrush(palette.WheelHub);
    }

    public void Dispose()
    {
        Background.Dispose();
        Rim.Dispose();
        Spoke.Dispose();
        Marker.Dispose();
        Hub.Dispose();
    }
}
