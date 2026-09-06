using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Oeps.RawMaterialSticker.Core.Configuration;

public sealed class AppConfiguration
{
    public const string DefaultSpreadsheetCsvUrl = "https://docs.google.com/spreadsheets/d/1c0Wh_HY_bz6y2l1Jr0D6rPmvKgyRPSEPphRE5ZAqndk/gviz/tq?tqx=out:csv&sheet=components%20list&headers=0&tq=select%20A%2CB%2CD%20where%20B%20is%20not%20null%20and%20D%20is%20not%20null%20label%20A%20%27Description%27%2C%20B%20%27OEPS_PN%27%2C%20D%20%27MPN%27";

    public string SpreadsheetCsvUrl { get; set; } = DefaultSpreadsheetCsvUrl;
    public const string DefaultExpensiveCsvUrl = "https://docs.google.com/spreadsheets/d/1c0Wh_HY_bz6y2l1Jr0D6rPmvKgyRPSEPphRE5ZAqndk/gviz/tq?tqx=out:csv&sheet=expensive%20components&headers=0&tq=select%20A%20where%20A%20is%20not%20null%20label%20A%20%27OEPS_PN%27";
    public string ExpensiveSpreadsheetCsvUrl { get; set; } = DefaultExpensiveCsvUrl;
    public HeaderAliases HeaderAliases { get; set; } = new();
    public bool DryRun { get; set; } = true;
    public bool SampleMode { get; set; }
    public string GitHubOwner { get; set; } = "oeps-tech";
    public string GitHubRepository { get; set; } = "raw-material-sticker";
    public string PackagePrefix { get; set; } = "Oeps.RawMaterialSticker";
    public PrinterConfiguration Printer { get; set; } = new();

    /// <summary>User appsettings.json values override the packaged configuration.</summary>
    public static AppConfiguration Load(string baseDirectory, string userDataRoot)
    {
        JsonObject merged = new();
        var packagePath = Path.Combine(baseDirectory, "appsettings.json");
        var userPath = Path.Combine(userDataRoot, "appsettings.json");
        foreach (var path in new[] { packagePath, userPath }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path)) continue;
            try
            {
                var parsed = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                }) as JsonObject ?? throw new InvalidDataException("Configuration must contain a JSON object.");
                Merge(merged, parsed);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"Cannot read configuration '{path}': {ex.Message}", ex);
            }
        }

        var configuration = merged.Deserialize<AppConfiguration>(JsonStorage.Options) ?? new();
        configuration.HeaderAliases ??= new();
        configuration.Printer ??= new();
        return configuration;
    }

    private static void Merge(JsonObject target, JsonObject source)
    {
        foreach (var pair in source)
        {
            var key = target.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, pair.Key, StringComparison.OrdinalIgnoreCase)) ?? pair.Key;
            if (pair.Value is JsonObject sourceChild && target[key] is JsonObject targetChild)
                Merge(targetChild, sourceChild);
            else
                target[key] = pair.Value?.DeepClone();
        }
    }
}

public sealed class HeaderAliases
{
    public string[] OepsPn { get; set; } = ["OEPS_PN", "OEPS PN"];
    public string[] Mpn { get; set; } = ["MPN"];
    public string[] Manufacturer { get; set; } = ["Manufacturer"];
    public string[] Description { get; set; } = ["Description"];
}

public sealed class PrinterConfiguration
{
    public string? Model { get; set; }
    public int? Dpi { get; set; }
    public string? QueueName { get; set; }
    public string? Connection { get; set; }
    public bool ProductionValidated { get; set; }
    public string TemplatePath { get; set; } = "templates/production-label.zpl";
    public string ExpensiveTemplatePath { get; set; } = "templates/expensive-label.zpl";
    public string? ValidatedExpensiveTemplateSha256 { get; set; }
    public string? ValidatedTemplateSha256 { get; set; }
    public int? PrintSpeedIps { get; set; }
    public int? Darkness { get; set; }
    public string? MediaTracking { get; set; }
    public string? PrintMethod { get; set; }
    public string? LongestValidatedOepsPn { get; set; }
    public string? ValidationNotes { get; set; }
}

internal static class JsonStorage
{
    internal static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() }
    };

    internal static async Task WriteAtomicAsync<T>(string path, T value, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(path)) File.Replace(temporaryPath, path, null);
            else File.Move(temporaryPath, path);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
