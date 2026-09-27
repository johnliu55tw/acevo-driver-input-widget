using Vortice.Mathematics;

namespace ACEvo_Driver_Input;

internal static class Colors
{
    public static Color4 FromRgb(byte r, byte g, byte b) => new(r / 255f, g / 255f, b / 255f, 1);
}
