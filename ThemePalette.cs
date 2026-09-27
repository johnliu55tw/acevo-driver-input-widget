using Vortice.Mathematics;

namespace ACEvo_Driver_Input;

// Colors copied from ACEvo-Simple-Telemetry's MainWindow theme resources.
internal sealed class ThemePalette
{
    public Color4 PrimaryText { get; }
    public Color4 SecondaryText { get; }
    public Color4 MutedText { get; }
    public Color4 OverlayBackground { get; }
    public Color4 SettingsBackground { get; }
    public Color4 PanelBackground { get; }
    public Color4 GaugeTrack { get; }
    public Color4 ControlBorder { get; }
    public Color4 ControlHover { get; }
    public Color4 GraphBackground { get; }
    public Color4 GraphGrid { get; }
    public Color4 WheelBackground { get; }
    public Color4 WheelRim { get; }
    public Color4 WheelSpoke { get; }
    public Color4 WheelHub { get; }

    private ThemePalette(bool light)
    {
        PrimaryText = light ? Rgb(0x16, 0x16, 0x1A) : Rgb(0xF4, 0xF4, 0xF4);
        SecondaryText = light ? Rgb(0x5F, 0x62, 0x68) : Rgb(0x85, 0x85, 0x8C);
        MutedText = light ? Rgb(0x73, 0x77, 0x80) : Rgb(0x77, 0x77, 0x7E);
        OverlayBackground = light ? Rgb(0xF2, 0xF4, 0xF7) : Argb(0xEC, 0x0A, 0x0A, 0x0C);
        SettingsBackground = light ? Rgb(0xE5, 0xE7, 0xEB) : Rgb(0x18, 0x18, 0x1C);
        PanelBackground = GaugeTrack = GraphBackground = WheelBackground = Argb(0, 0, 0, 0);
        ControlBorder = light ? Rgb(0xB9, 0xBE, 0xC8) : Rgb(0x34, 0x34, 0x3A);
        ControlHover = light ? Rgb(0xDD, 0xE1, 0xE6) : Rgb(0x29, 0x29, 0x2E);
        GraphGrid = light ? Argb(0x50, 0x6B, 0x72, 0x80) : Argb(0x46, 0x5A, 0x5A, 0x60);
        WheelRim = light ? Rgb(0x24, 0x26, 0x2A) : Rgb(0xE1, 0xE1, 0xE4);
        WheelSpoke = light ? Rgb(0x5A, 0x5E, 0x66) : Rgb(0xB6, 0xB6, 0xBC);
        WheelHub = light ? Rgb(0xAE, 0xB3, 0xBC) : Rgb(0x2D, 0x2D, 0x32);
    }

    public static ThemePalette Dark { get; } = new(false);
    public static ThemePalette Light { get; } = new(true);

    private static Color4 Rgb(byte r, byte g, byte b) => Argb(255, r, g, b);
    private static Color4 Argb(byte a, byte r, byte g, byte b) => new(r / 255f, g / 255f, b / 255f, a / 255f);
}
