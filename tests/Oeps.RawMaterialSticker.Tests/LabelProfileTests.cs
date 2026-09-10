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
        var preferences = new UserSettings();
        preferences.PrinterOffsets["Primary"] = new(1, -2);
        preferences.PrinterOffsets["Secondary"] = new(-1, 2);
        secondary.UseSelectedPrinter = false;
        var shifted = renderer.RenderProfiles(request, primary, "Primary", secondary, true, true, preferences);
        Assert.True(Encoding.ASCII.GetString(shifted[0].Bytes).Contains("^LS-12\n^LT-24\n"));
        Assert.True(Encoding.ASCII.GetString(shifted[1].Bytes).Contains("^LS12\n^LT24\n"));
        Assert.True(shifted.All(label => !Encoding.ASCII.GetString(label.Bytes).Contains("^LS0\n")));
        secondary.UseSelectedPrinter = true;
        shifted = renderer.RenderProfiles(request, primary, "Primary", secondary, true, false, preferences);
        Assert.True(shifted.All(label => Encoding.ASCII.GetString(label.Bytes).Contains("^LS-12\n^LT-24\n")));
        Assert.Equal(0, primary.OffsetXDots); Assert.Equal(0, secondary.OffsetYDots);
        var unshifted = renderer.RenderProfiles(request, primary, "Another printer", secondary, true, false, preferences);
        Assert.True(unshifted.All(label => Encoding.ASCII.GetString(label.Bytes).Contains("^LS0\n^LT0\n")));
        primary.OffsetYDots = 110;
        preferences.PrinterOffsets["Primary"] = new(0, 2);
        Assert.Throws<ArgumentException>(() => renderer.RenderProfiles(request, primary, "Primary", secondary, true, false, preferences));
        primary.OffsetYDots = 0;
        secondary.ProductionValidated = false;
        Assert.Throws<InvalidOperationException>(() => renderer.RenderProfiles(request, primary, "Primary", secondary, true, true));
        Assert.Equal(1, renderer.RenderProfiles(request, primary, "Primary", secondary, false, true).Count);
        Assert.Equal(2, renderer.RenderProfiles(request, primary, "Primary", secondary, true, false).Count);
        secondary.Dpi = 203;
        Assert.Throws<ArgumentException>(() => renderer.RenderProfiles(request, primary, "Primary", secondary, true, false));
    }

    [Test]
    public static void PrinterOffsetsPersistPerQueueAndCombineWithoutMutatingReleaseProfiles()
    {
        using var folder = new TestFolder();
        var path = Path.Combine(folder.Path, "user-settings.json");
        File.WriteAllText(path, "{\"lastPrinterName\":\"Production\"}");
        var settings = UserSettings.Load(path);
        Assert.Equal(new PrinterOffsets(), settings.GetPrinterOffsets("Production"));
        settings.PrinterOffsets["Production"] = new(1.25m, -0.5m);
        settings.PrinterOffsets["Logistics"] = new(-2, 0);
        settings.Save(path);
        var reloaded = UserSettings.Load(path);
        Assert.Equal(new PrinterOffsets(1.25m, -0.5m), reloaded.GetPrinterOffsets("PRODUCTION"));
        Assert.Equal(new PrinterOffsets(-2, 0), reloaded.GetPrinterOffsets("Logistics"));
        var profile = new PrinterConfiguration { Dpi = 300, OffsetXDots = 2, OffsetYDots = 3, Darkness = 23 };
        var applied = reloaded.ApplyPrinterOffsets(profile, "production");
        Assert.Equal(17, applied.OffsetXDots); Assert.Equal(-3, applied.OffsetYDots);
        Assert.Equal(2, profile.OffsetXDots); Assert.Equal(3, profile.OffsetYDots);
        profile.Darkness = 22;
        Assert.Equal<int?>(22, reloaded.ApplyPrinterOffsets(profile, "Production").Darkness);
        reloaded.PrinterOffsets["Production"] = new(); reloaded.Save(path);
        Assert.Equal(new PrinterOffsets(), UserSettings.Load(path).GetPrinterOffsets("Production"));
        reloaded.PrinterOffsets["Production"] = new(11, 0);
        Assert.Throws<ArgumentException>(() => reloaded.ApplyPrinterOffsets(profile, "Production"));
    }
}
