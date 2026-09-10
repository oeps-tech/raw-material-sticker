using System.Text.Json;
using System.Text.Json.Serialization;
using Oeps.RawMaterialSticker.Core.Data;

namespace Oeps.RawMaterialSticker.Core.Configuration;

public sealed class UserSettings
{
    private const int CurrentWindowLayoutVersion = 1;
    public const int MinimumWindowHeight = 610;

    public string? LastPrinterName { get; set; }
    public SearchMode SearchMode { get; set; } = SearchMode.OepsPn;
    public bool ExtendedDescription { get; set; }
    public Dictionary<string, PrinterOffsets> PrinterOffsets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int WindowWidth { get; set; } = 620;
    public int WindowHeight { get; set; } = MinimumWindowHeight;
    // Missing in older preference files; zero lets Load apply the migration once.
    public int WindowLayoutVersion { get; set; }
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public bool WindowMaximized { get; set; }
    [JsonIgnore] public string? LoadError { get; private set; }

    public static UserSettings Load(string path)
    {
        if (!File.Exists(path)) return new() { WindowLayoutVersion = CurrentWindowLayoutVersion };
        try
        {
            var settings = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path), JsonStorage.Options) ?? new();
            if (settings.WindowLayoutVersion < CurrentWindowLayoutVersion)
            {
                if (settings.WindowWidth == 740 && settings.WindowHeight == 650)
                {
                    settings.WindowWidth = 620;
                    settings.WindowHeight = MinimumWindowHeight;
                }
                settings.WindowLayoutVersion = CurrentWindowLayoutVersion;
            }
            settings.WindowWidth = Math.Clamp(settings.WindowWidth, 560, 3840);
            settings.WindowHeight = Math.Clamp(settings.WindowHeight, MinimumWindowHeight, 2160);
            if (!Enum.IsDefined(settings.SearchMode)) settings.SearchMode = SearchMode.OepsPn;
            settings.PrinterOffsets = new(settings.PrinterOffsets ?? new(), StringComparer.OrdinalIgnoreCase);
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new()
            {
                WindowLayoutVersion = CurrentWindowLayoutVersion,
                LoadError = $"Saved preferences could not be read: {ex.Message}"
            };
        }
    }

    public void Save(string path)
    {
        WindowLayoutVersion = Math.Max(WindowLayoutVersion, CurrentWindowLayoutVersion);
        JsonStorage.WriteAtomicAsync(path, this).GetAwaiter().GetResult();
    }

    public PrinterOffsets GetPrinterOffsets(string queue) => PrinterOffsets.GetValueOrDefault(queue) ?? new();

    public PrinterConfiguration ApplyPrinterOffsets(PrinterConfiguration profile, string queue)
    {
        var offsets = GetPrinterOffsets(queue);
        offsets.Validate();
        var dpi = profile.Dpi ?? throw new ArgumentException("Configure printer DPI before applying offsets.");
        return profile.WithAdditionalOffsets(offsets.ToDots(offsets.XMm, dpi), offsets.ToDots(offsets.YMm, dpi));
    }
}

public sealed record PrinterOffsets(decimal XMm = 0, decimal YMm = 0)
{
    public void Validate()
    {
        if (XMm is < -10 or > 10 || YMm is < -10 or > 10)
            throw new ArgumentException("Printer offsets must be between -10 and 10 mm.");
    }
    public int ToDots(decimal millimetres, int dpi) => (int)decimal.Round(millimetres * dpi / 25.4m, 0, MidpointRounding.AwayFromZero);
}
