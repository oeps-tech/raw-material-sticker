namespace Oeps.RawMaterialSticker.Core.Configuration;

public sealed class AppPaths
{
    public static string DefaultUserDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OEPS", "RawMaterialSticker");

    public AppPaths(string? userDataRoot = null)
    {
        UserDataRoot = Path.GetFullPath(userDataRoot ?? DefaultUserDataRoot);
    }

    public string UserDataRoot { get; }
    public string CacheFile => Path.Combine(UserDataRoot, "components-cache.json");
    public string SettingsFile => Path.Combine(UserDataRoot, "user-settings.json");
    public string ConfigurationFile => Path.Combine(UserDataRoot, "appsettings.json");
    public string DryRunDirectory => Path.Combine(UserDataRoot, "dry-runs");
}
