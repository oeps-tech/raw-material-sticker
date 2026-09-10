using System.Text;
using Oeps.RawMaterialSticker.Core.Configuration;
using Oeps.RawMaterialSticker.Core.Printing;

namespace Oeps.RawMaterialSticker.Tests;

public static class LabelProfileTests
{
    private sealed class TestFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "oeps-profile-test-" + Guid.NewGuid().ToString("N"));
        public TestFolder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }

    [Test]
    public static void ReleaseProfilesReplaceSettingsAndIgnoreLocalOverrides()
    {
        using var package = new TestFolder();
        using var user = new TestFolder();
        File.WriteAllText(Path.Combine(package.Path, "appsettings.json"), "{\"normalProfilePath\":\"label.json\",\"expensiveProfilePath\":\"label.json\"}");
        File.WriteAllText(Path.Combine(package.Path, "label.json"), "{\"darkness\":23,\"printSpeedIps\":2}");
        File.WriteAllText(Path.Combine(user.Path, "label.json"), "{\"darkness\":10}");
        File.WriteAllText(Path.Combine(user.Path, "appsettings.json"), "{\"normalProfilePath\":null,\"expensiveProfilePath\":\"missing.json\",\"printer\":{\"darkness\":5}}");
        var config = AppConfiguration.Load(package.Path, user.Path);
        Assert.Equal<int?>(23, config.Printer.Darkness);
        Assert.Equal<int?>(23, config.ExpensivePrinter!.Darkness);
        File.WriteAllText(Path.Combine(package.Path, "label.json"), "{\"darkness\":22,\"printSpeedIps\":3}");
        config = AppConfiguration.Load(package.Path, user.Path);
        Assert.Equal<int?>(22, config.Printer.Darkness);
        Assert.Equal<int?>(3, config.ExpensivePrinter!.PrintSpeedIps);
    }

    [Test]
    public static void ProfilesApplyIndependentSettingsAndQueuesBeforeSubmission()
    {
        var normal = new LabelRenderer(LabelRenderer.BuiltInTemplate);
        var extra = new ExpensiveLabelRenderer(ExpensiveLabelRenderer.BuiltInTemplate);
        var renderer = new LabelJobRenderer(normal, extra);
        PrinterConfiguration Profile(int darkness, string queue, string hash) => new()
        {
            Model = "Zebra ZD421", Dpi = 300, Connection = "network", QueueName = queue,
            PrintSpeedIps = 2, Darkness = darkness, MediaTracking = "web", PrintMethod = "thermal-transfer",
            ProductionValidated = true, ValidatedTemplateSha256 = hash,
            LongestValidatedOepsPn = "OEPS101234", ValidationNotes = "Test fixture only"
        };
        var primary = Profile(23, "Primary", normal.TemplateSha256);
        var secondary = Profile(22, "Secondary", extra.TemplateSha256);
        secondary.UseSelectedPrinter = false;
        var request = new LabelRequest("OEPS101234", "MPN", 9, 2026, 10);
        var labels = renderer.RenderProfiles(request, primary, "Primary", secondary, true, true);
        Assert.Equal(2, labels.Count);
        Assert.Equal("Primary", labels[0].Queue); Assert.Equal("Secondary", labels[1].Queue);
        secondary.UseSelectedPrinter = true;
        var logisticsLabels = renderer.RenderProfiles(request, primary, "Zebra ZD421 Logistics", secondary, true, true);
        Assert.True(logisticsLabels.All(label => label.Queue == "Zebra ZD421 Logistics"));
        Assert.True(Encoding.ASCII.GetString(labels[0].Bytes).Contains("~SD23\n"));
        Assert.True(Encoding.ASCII.GetString(labels[1].Bytes).Contains("~SD22\n"));
        foreach (var label in labels)
        {
            var zpl = Encoding.ASCII.GetString(label.Bytes);
            Assert.True(zpl.Contains("^PR2\n")); Assert.True(zpl.Contains("^MNW\n")); Assert.True(zpl.Contains("^MTT\n"));
        }
        secondary.ProductionValidated = false;
        Assert.Throws<InvalidOperationException>(() => renderer.RenderProfiles(request, primary, "Primary", secondary, true, true));
        Assert.Equal(1, renderer.RenderProfiles(request, primary, "Primary", secondary, false, true).Count);
        Assert.Equal(2, renderer.RenderProfiles(request, primary, "Primary", secondary, true, false).Count);
        secondary.Dpi = 203;
        Assert.Throws<ArgumentException>(() => renderer.RenderProfiles(request, primary, "Primary", secondary, true, false));
    }
}
