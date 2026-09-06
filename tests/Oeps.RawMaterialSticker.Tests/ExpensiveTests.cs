using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Oeps.RawMaterialSticker.Core;
using Oeps.RawMaterialSticker.Core.Configuration;
using Oeps.RawMaterialSticker.Core.Data;
using Oeps.RawMaterialSticker.Core.Printing;

namespace Oeps.RawMaterialSticker.Tests;

public static class ExpensiveTests
{
    [Test]
    public static void ExpensiveCsvPreservesFirstItemAndValidatesWholeList()
    {
        var parsed = ExpensiveComponentParser.Parse("\uFEFF\"OEPS_PN\"\n\"OEPS070051\"\nOEPS-0012\nOEPS070051\n\n");
        Assert.Equal(2, parsed.Count);
        Assert.True(parsed.Contains("oeps070051"));
        Assert.True(parsed.Contains("OEPS-0012"));
        Assert.False(parsed.Contains("OEPS0012"));
        Assert.Equal(0, ExpensiveComponentParser.Parse("OEPS_PN\n").Count);
        foreach (var invalid in new[] { "", "<html>sign in</html>", "OEPS070051", "WRONG\nOEPS070051", "OEPS_PN\nnot a PN", "OEPS_PN\nOEPS070051,extra", "OEPS_PN\n\"unfinished" })
            Assert.Throws<InvalidDataException>(() => ExpensiveComponentParser.Parse(invalid));
    }

    [Test]
    public static async Task BothSheetsCommitTogetherAndOfflineRetainsExpenseStatus()
    {
        using var folder = new TemporaryDirectory();
        using var handler = new TwoSheetHandler();
        using var http = new HttpClient(handler);
        var paths = new AppPaths(folder.Path);
        var repository = new ComponentRepository(http, new(), paths);
        Assert.True(await repository.RefreshAsync(), repository.LastError);
        Assert.True(repository.HasExpensiveData);
        Assert.Equal(2, repository.Components.Count(c => c.IsExpensive));
        Assert.False(repository.Components.Single(c => c.OepsPn == "OEPS-0012").IsExpensive);
        var session = new OperationSession { Month = "09", Year = "2026", Quantity = "42", DateUnavailable = true };
        session.Select(repository.Components[0]);
        var previous = File.ReadAllBytes(paths.CacheFile);
        var stamp = repository.LastSuccessfulSyncUtc;

        handler.ComponentCsv = "OEPS_PN,MPN\nOEPS070051,CHANGED\n";
        handler.ExpensiveCsv = "<html>Error</html>";
        Assert.False(await repository.RefreshAsync());
        Assert.True(previous.SequenceEqual(File.ReadAllBytes(paths.CacheFile)));
        Assert.Equal(stamp, repository.LastSuccessfulSyncUtc);
        Assert.Equal(2, repository.Components.Count(c => c.IsExpensive));
        var offline = new ComponentRepository(http, new(), paths);
        Assert.True(offline.LoadCache());
        Assert.True(offline.HasExpensiveData);
        Assert.Equal(2, offline.Components.Count(c => c.IsExpensive));

        handler.ComponentCsv = TwoSheetHandler.InitialComponents;
        handler.ExpensiveCsv = "OEPS_PN\n";
        Assert.True(await repository.RefreshAsync());
        Assert.True(repository.Components.All(c => !c.IsExpensive));
        Assert.True(session.Revalidate(repository.Components));
        Assert.False(session.Selected!.IsExpensive);
        Assert.Equal("42", session.Quantity);
        Assert.True(session.DateUnavailable);
    }

    [Test]
    public static void LegacyCacheRemainsSearchableButRequiresExpenseSync()
    {
        using var folder = new TemporaryDirectory();
        var paths = new AppPaths(folder.Path);
        File.WriteAllText(paths.CacheFile, "{\"schemaVersion\":1,\"lastSuccessfulSyncUtc\":\"2026-09-06T10:00:00Z\",\"components\":[{\"oepsPn\":\"OEPS070051\",\"mpn\":\"MPN-A\"}]}");
        using var http = new HttpClient();
        var repo = new ComponentRepository(http, new(), paths);
        Assert.True(repo.LoadCache());
        Assert.True(repo.HasData);
        Assert.False(repo.HasExpensiveData);
        Assert.True(repo.LastError!.Contains("before printing"));
    }

    [Test]
    public static void ExpensiveJobContainsTwoVariableLabelsAndPreservesGraphic()
    {
        var normal = new LabelRenderer(LabelRenderer.BuiltInTemplate);
        var expensive = new ExpensiveLabelRenderer(ExpensiveLabelRenderer.BuiltInTemplate);
        var renderer = new LabelJobRenderer(normal, expensive);
        var request = new LabelRequest("OEPS-0012", "MPN^FS^XZ", 9, 2026, 100, DateUnavailable: true);
        var ordinary = renderer.Render(request, false);
        Assert.Equal(1, ordinary.LabelCount);
        Assert.Equal(1, Regex.Matches(Encoding.ASCII.GetString(ordinary.Bytes), @"\^XA").Count);
        var result = renderer.Render(request, true);
        var zpl = Encoding.ASCII.GetString(result.Bytes);
        Assert.Equal(2, result.LabelCount);
        Assert.Equal(2, Regex.Matches(zpl, @"\^XA").Count);
        Assert.Equal(2, Regex.Matches(zpl, @"\^XZ").Count);
        Assert.Equal(2, Regex.Matches(zpl, @"\^PQ1,0,1,Y").Count);
        var pnField = "^FD" + LabelValues.EscapeField(Code128Encoder.Encode(request.OepsPn).ZplFieldData) + "^FS";
        Assert.Equal(2, Regex.Matches(zpl, Regex.Escape(pnField)).Count);
        var mpnField = "^FD" + LabelValues.EscapeField(request.Mpn) + "^FS";
        Assert.Equal(2, Regex.Matches(zpl, Regex.Escape(mpnField)).Count);
        Assert.True(zpl.Contains("^FD" + LabelValues.EscapeField("0000") + "^FS"));
        var graphic = ExpensiveLabelRenderer.BuiltInTemplate.Split('\n').Single(line => line.Contains("^GFA,")).TrimEnd('\r');
        Assert.True(zpl.Contains(graphic));
        Assert.True(zpl.Contains("^FDATTENTION^FS"));
        Assert.True(zpl.Contains("^FDVery Expensive^FS"));
        Assert.False(zpl.Contains("~JA")); Assert.False(zpl.Contains("^JUS")); Assert.False(zpl.Contains("{{"));
        Assert.False(zpl.Contains("EXPENSIVE ITEM"));
    }

    [Test]
    public static void MissingOrUnvalidatedExpensiveTemplatePreventsIncompleteJob()
    {
        var normal = new LabelRenderer(LabelRenderer.BuiltInTemplate);
        var expensive = new ExpensiveLabelRenderer(ExpensiveLabelRenderer.BuiltInTemplate);
        var request = new LabelRequest("OEPS070051", "MPN-A", 9, 2026, 100);
        var incomplete = new LabelJobRenderer(normal, null);
        Assert.Equal(1, incomplete.Render(request, false).LabelCount);
        Assert.Throws<InvalidOperationException>(() => incomplete.Render(request, true));
        var renderer = new LabelJobRenderer(normal, expensive);
        var config = new PrinterConfiguration
        {
            Model = "TEST ONLY", Dpi = 203, Connection = "TEST ONLY", QueueName = "TEST QUEUE",
            ProductionValidated = true, ValidatedTemplateSha256 = normal.TemplateSha256,
            PrintSpeedIps = 4, Darkness = 25, MediaTracking = "web", PrintMethod = "thermal-transfer",
            LongestValidatedOepsPn = "OEPS070051", ValidationNotes = "Synthetic test, not physical validation."
        };
        Assert.Equal(1, renderer.RenderProduction(request, config, "TEST QUEUE", false).LabelCount);
        Assert.Throws<InvalidOperationException>(() => renderer.RenderProduction(request, config, "TEST QUEUE", true));
        config.ValidatedExpensiveTemplateSha256 = expensive.TemplateSha256;
        var job = renderer.RenderProduction(request, config, "TEST QUEUE", true);
        Assert.Equal(2, job.LabelCount);
        Assert.Equal(2, Regex.Matches(Encoding.ASCII.GetString(job.Bytes), @"\^XA").Count);
        Assert.Throws<ArgumentException>(() => new ExpensiveLabelRenderer(ExpensiveLabelRenderer.BuiltInTemplate.Replace("^PW354", "^PW999")));
    }

    private sealed class TwoSheetHandler : HttpMessageHandler
    {
        public const string InitialComponents = "OEPS_PN,MPN\nOEPS070051,MPN-A\nOEPS070051,MPN-B\nOEPS-0012,MPN-C\n";
        public string ComponentCsv { get; set; } = InitialComponents;
        public string ExpensiveCsv { get; set; } = "OEPS_PN\noeps070051\n";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri!.AbsoluteUri.Contains("sheet=expensive") ? ExpensiveCsv : ComponentCsv, Encoding.UTF8, "text/csv")
            });
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OepsExpensiveTests", Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
