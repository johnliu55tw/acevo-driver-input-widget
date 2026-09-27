using System.Text.Json;
using System.Text.Json.Serialization;

namespace ACEvo_Driver_Input;

internal sealed class WidgetSettings
{
    private static readonly int[] ZoomLevels = [50, 60, 75, 100, 125, 150, 175, 200, 250];
    private static readonly int[] GraphSpans = [5, 10, 15, 20, 30];
    private static readonly int[] UpdateRates = [30, 60];
    private static readonly string[] ChartWidths = ["Small", "Medium", "Large"];
    public const int MediumChartWidthPixels = 533;
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ACEvoDriverInput", "settings.json");

    public bool ShowThrottle { get; set; } = true;
    public bool ShowBrake { get; set; } = true;
    public bool ShowClutch { get; set; } = true;
    public int ZoomPercent { get; set; } = 100;
    public int GraphTimeSpanSeconds { get; set; } = 10;
    public int UpdateRateHz { get; set; } = 30;
    public string ChartWidth { get; set; } = "Medium";
    public string Theme { get; set; } = "Dark";

    public static IReadOnlyList<int> AvailableZoomLevels => ZoomLevels;
    public static IReadOnlyList<int> AvailableGraphSpans => GraphSpans;
    public static IReadOnlyList<int> AvailableUpdateRates => UpdateRates;
    public static IReadOnlyList<string> AvailableChartWidths => ChartWidths;
    [JsonIgnore]
    public int ChartWidthPixels => ChartWidth switch
    {
        "Small" => 400,
        "Large" => 700,
        _ => MediumChartWidthPixels
    };
    [JsonIgnore]
    public bool IsLightTheme => Theme == "Light";
    [JsonIgnore]
    public uint UpdateIntervalMilliseconds => (uint)Math.Round(1000.0 / UpdateRateHz);

    public static WidgetSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                WidgetSettings settings = JsonSerializer.Deserialize<WidgetSettings>(File.ReadAllText(SettingsPath)) ?? new();
                if (!ZoomLevels.Contains(settings.ZoomPercent)) settings.ZoomPercent = 100;
                if (!GraphSpans.Contains(settings.GraphTimeSpanSeconds)) settings.GraphTimeSpanSeconds = 10;
                if (!UpdateRates.Contains(settings.UpdateRateHz)) settings.UpdateRateHz = 30;
                settings.ChartWidth = ChartWidths.FirstOrDefault(width =>
                    string.Equals(width, settings.ChartWidth, StringComparison.OrdinalIgnoreCase)) ?? "Medium";
                settings.Theme = string.Equals(settings.Theme, "Light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";
                return settings;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Preferences must not prevent the overlay from starting.
        }

        return new WidgetSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The overlay remains usable when settings cannot be saved.
        }
    }
}
