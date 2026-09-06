using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Oeps.RawMaterialSticker.Core.Configuration;
using Oeps.RawMaterialSticker.Core.Data;
using Oeps.RawMaterialSticker.Core.Updates;

namespace Oeps.RawMaterialSticker.Tests;

internal static class IntegrationChecks
{
    public static async Task<int> VerifyLiveAsync(string directory)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
        var paths = new AppPaths(directory);
        var config = new AppConfiguration();
        var repo = new ComponentRepository(http, config, paths);
        Assert.True(await repo.RefreshAsync(), repo.LastError);
        Console.WriteLine($"PASS anonymous live download: {repo.Components.Count} distinct PN/MPN pairings");
        Assert.True(repo.HasExpensiveData);
        Console.WriteLine($"PASS expensive-item tab joined: {repo.Components.Where(c => c.IsExpensive).Select(c => c.OepsPn).Distinct(StringComparer.OrdinalIgnoreCase).Count()} matching OEPS PNs");
        var stamp = repo.LastSuccessfulSyncUtc;
        var offline = new ComponentRepository(http, config, paths);
        Assert.True(offline.LoadCache());
        Assert.Equal(repo.Components.Count, offline.Components.Count);
        Assert.Equal(stamp, offline.LastSuccessfulSyncUtc);
        Assert.True(offline.HasExpensiveData);
        Assert.Equal(repo.Components.Count(c => c.IsExpensive), offline.Components.Count(c => c.IsExpensive));
        Console.WriteLine("PASS immediate cache startup from real sheet data");
        Assert.True(await repo.RefreshAsync(), repo.LastError);
        Assert.True(repo.NextRefreshUtc > DateTimeOffset.UtcNow.AddSeconds(290));
        Console.WriteLine("PASS manual full refresh and five-minute next-refresh schedule");
        return 0;
    }

    public static async Task<int> VerifyPackageAsync(string packagePath, string dataDirectory)
    {
        packagePath = Path.GetFullPath(packagePath);
        dataDirectory = Path.GetFullPath(dataDirectory);
        using var archive = ZipFile.OpenRead(packagePath);
        using var manifestStream = archive.Entries.Single(e => e.FullName.Replace('\\', '/') == "app/update-manifest.json").Open();
        using var manifest = await JsonDocument.ParseAsync(manifestStream);
        var version = SemanticVersion.Parse(manifest.RootElement.GetProperty("version").GetString()!);
        var checksum = UpdateStore.ParseChecksum(await File.ReadAllTextAsync(packagePath + ".sha256"), Path.GetFileName(packagePath));
        var store = new UpdateStore(dataDirectory);
        var installed = await store.StagePackageAsync(packagePath, checksum, version);
        Assert.True(store.LoadState().Current is null, "Use a fresh package verification directory.");
        Assert.True(store.IsValidInstalled(installed));
        Console.WriteLine("PASS real release ZIP checksum, extraction and manifest validation");
        if (AppInstanceCoordinator.IsAppRunning())
        {
            Console.WriteLine("SKIP packaged UI launch: operator app is already running. Close it and repeat using a fresh verification directory.");
            return 2;
        }
        var eventName = @"Local\OEPS.RawMaterialSticker.Ready." + Guid.NewGuid().ToString("N");
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
        var start = new ProcessStartInfo(store.GetExecutablePath(installed)) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var arg in new[] { "--sample", "--ui-smoke", "--data-dir", dataDirectory, "--startup-ready", eventName }) start.ArgumentList.Add(arg);
        var runtimeRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "..", "..", ".."));
        start.Environment["DOTNET_ROOT_X64"] = runtimeRoot; start.Environment["DOTNET_ROOT"] = runtimeRoot;
        using var child = Process.Start(start)!;
        Assert.True(await Task.Run(() => ready.WaitOne(TimeSpan.FromSeconds(35))), "Packaged app did not signal startup readiness.");
        store.MarkWorking(installed);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        await child.WaitForExitAsync(deadline.Token);
        Assert.Equal(0, child.ExitCode);
        Assert.Equal(installed.DirectoryName, new UpdateStore(dataDirectory).LoadState().Current!.DirectoryName);
        Console.WriteLine("PASS packaged app startup-ready signal, UI dry run and persisted working-version recovery");
        Console.WriteLine(await File.ReadAllTextAsync(Path.Combine(dataDirectory, "ui-smoke.txt")));
        return 0;
    }
}
