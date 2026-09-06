using System.Net;
using System.Text;
using Oeps.RawMaterialSticker.Core.Configuration;
using Oeps.RawMaterialSticker.Core.Data;

namespace Oeps.RawMaterialSticker.Tests;

public static class DataTests
{
    [Test]
    public static void CsvReadsHeadersRegardlessOfOrderAndPreservesIdentifiers()
    {
        var components = CsvComponentParser.Parse("\uFEFFDescription,MPN,Unused,OEPS PN\r\nPart,000042,ignored,OEPS-0012\r\n");
        Assert.Equal(1, components.Count);
        Assert.Equal("OEPS-0012", components[0].OepsPn);
        Assert.Equal("000042", components[0].Mpn);
        Assert.Equal("Part", components[0].Description);
    }

    [Test]
    public static void CsvSupportsQuotedCommasQuotesNewlinesAndUtf8()
    {
        var components = CsvComponentParser.Parse("OEPS_PN,MPN,Description,Manufacturer\n\"OEPSAB1234\",\"MPN,42\",\"Line one\r\n\"\"quoted\"\" café\",\"München\"\n");
        Assert.Equal("MPN,42", components[0].Mpn);
        Assert.Equal("Line one\r\n\"quoted\" café", components[0].Description);
        Assert.Equal("München", components[0].Manufacturer);
    }

    [Test]
    public static void CsvDeduplicatesOnlyIdenticalPairings()
    {
        var components = CsvComponentParser.Parse("OEPS_PN,MPN\nOEPS101234,PART-A\nOEPS101234,PART-B\nOEPS101234,PART-A\n,\n\n");
        Assert.Equal(2, components.Count);
        Assert.Equal(2, ComponentSearch.Search(components, "101234", SearchMode.OepsPn).Count);
        Assert.Equal("PART-B", ComponentSearch.FindPair(components, "OEPS101234", "PART-B")!.Mpn);
        Assert.True(ComponentSearch.FindPair(components, "OEPS101234", "PART-C") is null);
        Assert.True(ComponentSearch.FindPair(components, "oeps101234", "PART-A") is null);
    }

    [Test]
    public static void SearchUsesSelectedColumnSubstringAndPrefixRanking()
    {
        Component[] components = [new("OEPSXYZ123", "23-B"), new("OEPS123456", "123-A"), new("OEPSZZZ123", "23-A")];
        var byMpn = ComponentSearch.Search(components, "23", SearchMode.Mpn);
        Assert.Equal("23-A", byMpn[0].Mpn);
        Assert.Equal("23-B", byMpn[1].Mpn);
        Assert.Equal("123-A", byMpn[2].Mpn);
        Assert.Equal(1, ComponentSearch.Search(components, "oeps123", SearchMode.OepsPn).Count);
        Assert.Equal(0, ComponentSearch.Search(components, "oeps123", SearchMode.Mpn).Count);
        Assert.Equal(2, ComponentSearch.Search(components, "", SearchMode.OepsPn, 2).Count);
        Component[] spaced = [new("OEPS020003", "AB 12"), new("OEPS990001", "XAB12")];
        Assert.Equal("OEPS020003", ComponentSearch.Search(spaced, " OEPS 02 0003 ", SearchMode.OepsPn).Single().OepsPn);
        Assert.Equal("OEPS020003", ComponentSearch.Search(spaced, "oeps\t02\u00a00003", SearchMode.OepsPn).Single().OepsPn);
        var spacedMpn = ComponentSearch.Search(spaced, "A B 1 2", SearchMode.Mpn);
        Assert.Equal("AB 12", spacedMpn[0].Mpn);
        Assert.Equal("XAB12", spacedMpn[1].Mpn);
    }

    [Test]
    public static void CsvRejectsBrokenDownloadsAndRequiredValues()
    {
        foreach (var csv in new[]
        {
            "", "<!DOCTYPE html><html>Sign in</html>", "MPN,Description\npart,description",
            "OEPS_PN,MPN\n", "OEPS_PN,MPN\n,part", "OEPS_PN,MPN\nOEPS101234,",
            "OEPS_PN,MPN\nOEPS101234,\"unfinished", "OEPS_PN,MPN\nOEPS101234,\"part\"oops",
            "OEPS_PN,MPN\nOEPS101234,pa\"rt", "OEPS_PN,MPN\nOEPS101234,part,extra",
            "OEPS_PN,MPN,OEPS PN\nOEPS101234,part,OEPS101234",
            "OEPS_PN,MPN\nOEPS101234,\"part\nwith newline\""
        }) Assert.Throws<InvalidDataException>(() => CsvComponentParser.Parse(csv));
    }

    [Test]
    public static void CsvSupportsConfiguredAliases()
    {
        var aliases = new HeaderAliases { OepsPn = ["Internal code"], Mpn = ["Supplier part"] };
        var components = CsvComponentParser.Parse("Supplier part,Internal code\n00123,OEPS-0012", aliases);
        Assert.Equal("00123", components[0].Mpn);
    }

    [Test]
    public static async Task InvalidDownloadPreservesMemoryDiskAndSyncTimestamp()
    {
        using var folder = new TestFolder();
        var paths = new AppPaths(folder.Path);
        using var handler = new SequenceHandler(Csv("OEPS_PN,MPN\nOEPS101234,PART-A"), Csv("OEPS_PN,MPN\nOEPS101234,"),
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>Sign in</html>", Encoding.UTF8, "text/html") },
            Csv("OEPS_PN,MPN"), new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var client = new HttpClient(handler);
        var time = new FakeTimeProvider();
        var repository = new ComponentRepository(client, new(), paths, time);
        Assert.True(await repository.RefreshAsync());
        var original = await File.ReadAllBytesAsync(paths.CacheFile);
        var syncedAt = repository.LastSuccessfulSyncUtc;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            time.Advance(TimeSpan.FromMinutes(1));
            Assert.False(await repository.RefreshAsync());
            Assert.Equal(1, repository.Components.Count);
            Assert.Equal("PART-A", repository.Components[0].Mpn);
            Assert.Equal(syncedAt, repository.LastSuccessfulSyncUtc);
            var persisted = await File.ReadAllBytesAsync(paths.CacheFile);
            Assert.True(original.SequenceEqual(persisted));
            Assert.True(repository.LastError is not null);
            Assert.Equal(time.GetUtcNow() + ComponentRepository.RefreshInterval, repository.NextRefreshUtc);
            Assert.False(repository.IsRefreshing);
        }
    }

    [Test]
    public static async Task CacheLoadsImmediatelyAndSupportsOfflineStartup()
    {
        using var folder = new TestFolder();
        var paths = new AppPaths(folder.Path);
        using var handler = new SequenceHandler(Csv("OEPS_PN,MPN\nOEPS101234,PART-A"), new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var client = new HttpClient(handler);
        var time = new FakeTimeProvider();
        var online = new ComponentRepository(client, new(), paths, time);
        Assert.False(online.LoadCache());
        Assert.True(await online.RefreshAsync());
        time.Advance(TimeSpan.FromHours(3));
        var offline = new ComponentRepository(client, new(), paths, time);
        Assert.True(offline.LoadCache());
        Assert.Equal(online.LastSuccessfulSyncUtc, offline.LastSuccessfulSyncUtc);
        Assert.True(offline.IsRefreshDue);
        Assert.False(await offline.RefreshAsync());
        Assert.True(offline.HasData);
        Assert.False(offline.IsRefreshDue);
        time.Advance(TimeSpan.FromMinutes(6));
        Assert.True(offline.IsRefreshDue);
    }

    [Test]
    public static async Task ConcurrentRefreshesUseOnlyOneDownload()
    {
        using var folder = new TestFolder();
        var completion = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new DeferredHandler(completion.Task);
        using var client = new HttpClient(handler);
        var repository = new ComponentRepository(client, new(), new(folder.Path));
        var first = repository.RefreshAsync();
        Assert.True(repository.IsRefreshing);
        Assert.False(await repository.RefreshAsync());
        Assert.Equal(1, handler.RequestCount);
        completion.SetResult(Csv("OEPS_PN,MPN\nOEPS101234,PART-A"));
        Assert.True(await first);
        Assert.False(repository.IsRefreshing);
    }

    [Test]
    public static async Task NoCacheFailureNeverInventsSampleData()
    {
        using var folder = new TestFolder();
        using var handler = new SequenceHandler(new HttpResponseMessage(HttpStatusCode.Forbidden));
        using var client = new HttpClient(handler);
        var repository = new ComponentRepository(client, new(), new(folder.Path));
        Assert.False(repository.LoadCache());
        Assert.False(await repository.RefreshAsync());
        Assert.False(repository.HasData);
        Assert.True(repository.LastError!.Contains("anonymously readable", StringComparison.Ordinal));
    }

    [Test]
    public static void CorruptCacheIsReportedWithoutCrashing()
    {
        using var folder = new TestFolder();
        var paths = new AppPaths(folder.Path);
        using var client = new HttpClient();
        foreach (var content in new[] { "{", "{}", "{\"schemaVersion\":1,\"lastSuccessfulSyncUtc\":\"2026-09-06T10:00:00Z\",\"components\":[{\"oepsPn\":null,\"mpn\":\"PART\"}]}" })
        {
            File.WriteAllText(paths.CacheFile, content);
            var repository = new ComponentRepository(client, new(), paths);
            Assert.False(repository.LoadCache());
            Assert.False(repository.HasData);
            Assert.True(repository.LastError is not null);
        }
    }

    [Test]
    public static async Task SampleModeIsExplicitAndDoesNotPolluteCache()
    {
        using var folder = new TestFolder();
        var paths = new AppPaths(folder.Path);
        using var client = new HttpClient();
        var repository = new ComponentRepository(client, new() { SampleMode = true }, paths);
        Assert.True(repository.LoadCache());
        Assert.True(repository.IsSampleData);
        Assert.True(repository.Components.All(component => component.Mpn.StartsWith("DEMO-", StringComparison.Ordinal)));
        Assert.False(repository.IsRefreshDue);
        Assert.False(await repository.RefreshAsync());
        Assert.False(File.Exists(paths.CacheFile));
    }

    [Test]
    public static void ConfigurationOverridesAreMergedAndSettingsRetainPreferencesOnly()
    {
        using var package = new TestFolder();
        using var user = new TestFolder();
        File.WriteAllText(System.IO.Path.Combine(package.Path, "appsettings.json"), "{\"printer\":{\"model\":\"Zebra\",\"dpi\":203},\"gitHubOwner\":\"oeps-tech\"}");
        File.WriteAllText(System.IO.Path.Combine(user.Path, "appsettings.json"), "{\"Printer\":{\"Dpi\":300},\"sampleMode\":true}");
        var config = AppConfiguration.Load(package.Path, user.Path);
        Assert.Equal("Zebra", config.Printer.Model);
        Assert.Equal<int?>(300, config.Printer.Dpi);
        Assert.True(config.SampleMode);
        Assert.True(config.DryRun);
        Assert.False(config.Printer.ProductionValidated);
        var paths = new AppPaths(user.Path);
        new UserSettings { LastPrinterName = "Queue A", SearchMode = SearchMode.Mpn, WindowWidth = 820 }.Save(paths.SettingsFile);
        var settings = UserSettings.Load(paths.SettingsFile);
        Assert.Equal("Queue A", settings.LastPrinterName);
        Assert.Equal(SearchMode.Mpn, settings.SearchMode);
        Assert.Equal(820, settings.WindowWidth);
        Assert.False(File.ReadAllText(paths.SettingsFile).Contains("quantity", StringComparison.OrdinalIgnoreCase));
    }

    private static HttpResponseMessage Csv(string csv) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(csv, Encoding.UTF8, "text/csv")
    };

    private sealed class SequenceHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(request.RequestUri!.AbsoluteUri.Contains("sheet=expensive", StringComparison.Ordinal) ? Csv("OEPS_PN\n") : _responses.Dequeue());
    }

    private sealed class DeferredHandler(Task<HttpResponseMessage> response) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsoluteUri.Contains("sheet=expensive", StringComparison.Ordinal)) return Task.FromResult(Csv("OEPS_PN\n"));
            RequestCount++;
            return response;
        }
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 6, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }

    private sealed class TestFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OepsStickerTests", Guid.NewGuid().ToString("N"));
        public TestFolder() => Directory.CreateDirectory(Path);
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
    }
}
