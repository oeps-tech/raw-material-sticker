using System.Text.Json;
using System.Text.Json.Serialization;
using Oeps.RawMaterialSticker.Core.Data;

namespace Oeps.RawMaterialSticker.Core.Configuration;

public sealed class UserSettings
{
    private const int CurrentWindowLayoutVersion = 1;

    public string? LastPrinterName { get; set; }
    public SearchMode SearchMode { get; set; } = SearchMode.OepsPn;
    public bool ExtendedDescription { get; set; }
    public int WindowWidth { get; set; } = 620;
    public int WindowHeight { get; set; } = 530;
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
                    settings.WindowHeight = 530;
                }
                settings.WindowLayoutVersion = CurrentWindowLayoutVersion;
            }
            settings.WindowWidth = Math.Clamp(settings.WindowWidth, 560, 3840);
            settings.WindowHeight = Math.Clamp(settings.WindowHeight, 530, 2160);
            if (!Enum.IsDefined(settings.SearchMode)) settings.SearchMode = SearchMode.OepsPn;
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
}
