using System.Net;
using System.Text;
using System.Text.Json;
using Oeps.RawMaterialSticker.Core.Configuration;

namespace Oeps.RawMaterialSticker.Core.Data;

/// <summary>Validated snapshots and a five-minute schedule, independent of software updates.</summary>
public sealed class ComponentRepository
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(300);
    private const int MaximumDownloadBytes = 20 * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly AppConfiguration _configuration;
    private readonly AppPaths _paths;
    private readonly TimeProvider _timeProvider;
    private int _refreshing;
    private RepositoryState _state;

    public ComponentRepository(HttpClient httpClient, AppConfiguration configuration, AppPaths paths, TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _paths = paths;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _state = new([], null, _timeProvider.GetUtcNow(), null);
    }

    public IReadOnlyList<Component> Components => Volatile.Read(ref _state).Components;
    public DateTimeOffset? LastSuccessfulSyncUtc => Volatile.Read(ref _state).LastSuccessfulSyncUtc;
    public DateTimeOffset NextRefreshUtc => Volatile.Read(ref _state).NextRefreshUtc;
    public string? LastError => Volatile.Read(ref _state).LastError;
    public bool HasData => Components.Count > 0;
    public bool HasExpensiveData => Volatile.Read(ref _state).ExpensiveListKnown;
    public bool IsRefreshing => Volatile.Read(ref _refreshing) != 0;
    public bool IsSampleData => _configuration.SampleMode;
    public bool IsRefreshDue => !IsRefreshing && !IsSampleData && _timeProvider.GetUtcNow() >= NextRefreshUtc;

    public bool LoadCache()
    {
        if (_configuration.SampleMode) { UseSampleData(); return true; }
        if (!File.Exists(_paths.CacheFile)) return false;
        try
        {
            var cache = JsonSerializer.Deserialize<ComponentCacheSnapshot>(File.ReadAllText(_paths.CacheFile), JsonStorage.Options)
                ?? throw new InvalidDataException("Cache contains no snapshot.");
            if (cache.SchemaVersion is not (1 or 2) || cache.LastSuccessfulSyncUtc == default || cache.Components is not { Length: > 0 })
                throw new InvalidDataException("Cache has an unsupported format or no validated records.");
            if (cache.Components.Any(component => component is null || string.IsNullOrWhiteSpace(component.OepsPn)
                || string.IsNullOrWhiteSpace(component.Mpn) || component.OepsPn.Any(char.IsControl) || component.Mpn.Any(char.IsControl)))
                throw new InvalidDataException("Cache contains invalid component identifiers.");
            var components = cache.Components.DistinctBy(component => (component.OepsPn, component.Mpn)).ToArray();
            Volatile.Write(ref _state, new(Array.AsReadOnly(components), cache.LastSuccessfulSyncUtc.ToUniversalTime(), _timeProvider.GetUtcNow(),
                cache.SchemaVersion == 1 ? "Update database to load expensive-item status before printing." : null, cache.SchemaVersion == 2));
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            Volatile.Write(ref _state, _state with { LastError = $"Local database could not be loaded: {ex.Message}" });
            return false;
        }
    }

    public void UseSampleData()
    {
        if (!_configuration.SampleMode)
            throw new InvalidOperationException("Set sampleMode=true explicitly before loading demonstration data.");
        Component[] samples =
        [
            new("OEPS101234", "DEMO-CRYSTAL-12MHZ", "Demonstration only", "SAMPLE DATA — expensive crystal", IsExpensive: true),
            new("OEPS101234", "DEMO-ALTERNATE-12MHZ", "Demonstration only", "SAMPLE DATA — alternate MPN for same expensive OEPS PN", IsExpensive: true),
            new("OEPS-AB1234", "DEMO-RESISTOR-10K", "Demonstration only", "SAMPLE DATA — letter suffix"),
            new("OEPS-0012", "DEMO-CAPACITOR-100NF", "Demonstration only", "SAMPLE DATA — leading zeros")
        ];
        Volatile.Write(ref _state, new(Array.AsReadOnly(samples), null, DateTimeOffset.MaxValue, null, true));
    }

    /// <summary>Returns true only when a fresh online snapshot was validated and persisted.</summary>
    public async Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsSampleData || Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0) return false;
        try
        {
            var csv = await DownloadCsvAsync(_configuration.SpreadsheetCsvUrl, cancellationToken).ConfigureAwait(false);
            // A large sheet should never make an awaiting Windows Forms caller parse on its UI thread.
            var parsed = await Task.Run(() => CsvComponentParser.Parse(csv, _configuration.HeaderAliases), cancellationToken).ConfigureAwait(false);
            var expensiveCsv = await DownloadCsvAsync(_configuration.ExpensiveSpreadsheetCsvUrl, cancellationToken).ConfigureAwait(false);
            var expensive = await Task.Run(() => ExpensiveComponentParser.Parse(expensiveCsv), cancellationToken).ConfigureAwait(false);
            var components = Array.AsReadOnly(parsed.Select(c => c with { IsExpensive = expensive.Contains(c.OepsPn) }).ToArray());
            var successfulAt = _timeProvider.GetUtcNow();
            var snapshot = new ComponentCacheSnapshot(2, successfulAt, components.ToArray());
            await JsonStorage.WriteAtomicAsync(_paths.CacheFile, snapshot, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _state, new(components, successfulAt, successfulAt + RefreshInterval, null, true));
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Volatile.Write(ref _state, _state with { LastError = "Database refresh was canceled; the last valid copy has been kept." });
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException
            or JsonException or DecoderFallbackException or OperationCanceledException)
        {
            Volatile.Write(ref _state, _state with { LastError = $"Database refresh failed: {ex.Message}" });
            return false;
        }
        finally
        {
            Volatile.Write(ref _state, _state with { NextRefreshUtc = _timeProvider.GetUtcNow() + RefreshInterval });
            Volatile.Write(ref _refreshing, 0);
        }
    }

    private async Task<string> DownloadCsvAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("Configure a publicly readable HTTPS spreadsheet CSV export URL for both tabs.");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd("text/csv");
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidDataException("The spreadsheet is not anonymously readable. Enable public view access for both tabs.");
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri?.Scheme is { } responseScheme && responseScheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("The spreadsheet export redirected to a non-HTTPS address.");
        if (response.Content.Headers.ContentType?.MediaType is "text/html" or "application/xhtml+xml")
            throw new InvalidDataException("Google returned HTML instead of CSV. Check public view access and both tab export URLs.");
        if (response.Content.Headers.ContentLength > MaximumDownloadBytes)
            throw new InvalidDataException("The spreadsheet download exceeds the 20 MiB limit.");
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int received;
        while ((received = await body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length + received > MaximumDownloadBytes) throw new InvalidDataException("The spreadsheet download exceeds the 20 MiB limit.");
            buffer.Write(chunk, 0, received);
        }
        return new UTF8Encoding(false, true).GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private sealed record RepositoryState(IReadOnlyList<Component> Components, DateTimeOffset? LastSuccessfulSyncUtc, DateTimeOffset NextRefreshUtc, string? LastError, bool ExpensiveListKnown = false);
    private sealed record ComponentCacheSnapshot(int SchemaVersion, DateTimeOffset LastSuccessfulSyncUtc, Component[] Components);
}
