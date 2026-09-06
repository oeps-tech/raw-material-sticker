using Oeps.RawMaterialSticker.Core.Configuration;
using Oeps.RawMaterialSticker.Core.Updates;

namespace Oeps.RawMaterialSticker.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            var dataIndex = Array.IndexOf(args, "--data-dir");
            var paths = new AppPaths(dataIndex >= 0 && dataIndex + 1 < args.Length ? args[dataIndex + 1] : null);
            var config = AppConfiguration.Load(AppContext.BaseDirectory, paths.UserDataRoot);
            if (args.Contains("--sample")) { config.SampleMode = true; config.DryRun = true; }
            if (args.Contains("--dry-run")) config.DryRun = true;
            MainForm? form = null;
            using var instance = AppInstanceCoordinator.TryAcquire(() =>
            {
                if (form is not { IsHandleCreated: true, IsDisposed: false }) return;
                try { form.BeginInvoke(() => { if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal; form.Show(); form.Activate(); }); }
                catch (InvalidOperationException) { }
            });
            if (instance is null) return;
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
            form = new MainForm(config, paths, http, args);
            Application.Run(form);
        }
        catch (Exception e)
        {
            MessageBox.Show($"OEPS Raw Material Sticker could not start.\n\n{e.Message}\n\nCheck appsettings.json or start with --sample after correcting configuration.", "Startup error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
