using Oeps.RawMaterialSticker.Core.Camera;
using Oeps.RawMaterialSticker.Core.Data;

namespace Oeps.RawMaterialSticker.App;

internal sealed partial class CameraForm
{
    // Called only by the application's --sample --ui-smoke verification path.
    internal static async Task RunSmokeAsync(Form owner, string output, Func<CameraReading, Component> resolve,
        Func<CameraReading, decimal, Task<string>> print, Action<CameraReading, decimal> apply)
    {
        const string first = """{"label_version":1,"oeps_pn":"OEPS101234","lot":"0926-TRAY-Q5R2","quantity":"000042"}""";
        var second = first.Replace("Q5R2", "ABCD").Replace("000042", "000008");
        var third = first.Replace("Q5R2", "EFGH").Replace("000042", "000006");
        var connection = new SmokeConnection();
        var printGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CameraReading? printed = null, applied = null;
        decimal printedQuantity = 0;
        var readingSounds = 0;
        Exception? failure = null;
        using (var dialog = new CameraForm(null, 8765, true, resolve, async (reading, quantity) =>
        {
            printed = reading; printedQuantity = quantity;
            await printGate.Task;
            return await print(reading, quantity);
        }, (reading, quantity) => { applied = reading; apply(reading, quantity); }, connection, () => readingSounds++) { Opacity = 0 })
        {
            dialog.Shown += async (_, _) =>
            {
                try
                {
                    await connection.EmitAsync(first); await dialog.DrainAsync();
                    Check(dialog._pn.ReadOnly && dialog._mpn.ReadOnly && dialog._lot.ReadOnly, "Identifiers are editable.");
                    Check(dialog._quantity.ContainsFocus && dialog._print.Enabled, "Quantity lacks focus or print is disabled.");
                    dialog._quantity.Value = 17;
                    await connection.EmitAsync(first); await dialog.DrainAsync();
                    Check(dialog._quantity.Value == 17, "Duplicate overwrote edited quantity.");
                    Check(readingSounds == 1, "A duplicate reading played a sound, or the first reading did not.");
                    await connection.EmitAsync(second); await dialog.DrainAsync();
                    Check(dialog._quantity.Value == 8 && dialog._lot.Text == "0926-TRAY-ABCD", "New JSON did not immediately replace the reading.");
                    for (var repeat = 0; repeat < 3; repeat++)
                    {
                        var before = readingSounds;
                        await connection.EmitAsync(first); await dialog.DrainAsync();
                        Check(dialog._lot.Text == "0926-TRAY-Q5R2" && readingSounds == before + 1, "Repeated A did not update and request a beep.");
                        await connection.EmitAsync(second); await dialog.DrainAsync();
                        Check(dialog._lot.Text == "0926-TRAY-ABCD" && readingSounds == before + 2, "Repeated B did not update and request a beep.");
                        await connection.EmitAsync(second); await dialog.DrainAsync();
                        Check(readingSounds == before + 2, "Consecutive B incorrectly requested another beep.");
                    }
                    dialog._quantity.Value = 0;
                    Check(!dialog._print.Enabled && !dialog._update.Enabled, "Zero quantity allows actions.");
                    dialog._quantity.Value = 12.5m;
                    dialog.Shortcut(Keys.Enter);
                    Check(dialog._busy && printed?.Lot == "0926-TRAY-ABCD" && printedQuantity == 12.5m, "Enter did not capture the confirmed values.");
                    await connection.EmitAsync(third); await dialog.DrainAsync();
                    Check(dialog._lot.Text == printed!.Lot, "An in-flight print was changed by a new reading.");
                    printGate.SetResult();
                    await WaitUntilAsync(() => !dialog._busy);
                    Check(dialog._lot.Text == "0926-TRAY-EFGH" && dialog._quantity.Value == 6 && dialog.Visible, "Next reading is missing after printing.");
                    Check(dialog._printStatus.Text.StartsWith("Saved 2 label(s):"), "Camera did not produce both expensive labels: " + dialog._printStatus.Text);
                    await connection.EmitAsync(third.Replace("EFGH", "IJKL")); await dialog.DrainAsync();
                    Check(dialog._quantity.Text == "6" && dialog._print.Enabled, "A different label with the same quantity left an empty field.");
                    var soundsBeforeRescan = readingSounds;
                    await connection.EmitAsync(first); await dialog.DrainAsync();
                    Check(dialog._lot.Text == "0926-TRAY-Q5R2" && dialog._quantity.Text == "42" && readingSounds == soundsBeforeRescan + 1,
                        "Returning to an earlier label did not update the fields and play a sound.");
                    await connection.EmitAsync(third.Replace("EFGH", "IJKL")); await dialog.DrainAsync();
                    using (var snapshot = new Bitmap(dialog.Width, dialog.Height))
                    {
                        dialog.DrawToBitmap(snapshot, new Rectangle(Point.Empty, snapshot.Size));
                        snapshot.Save(Path.Combine(output, "ui-smoke-camera.png"));
                    }
                    dialog._quantity.Value = 9.125m;
                    dialog.Shortcut(Keys.Space);
                }
                catch (Exception ex) { failure = ex; printGate.TrySetResult(); dialog.Close(); }
            };
            dialog.ShowDialog(owner);
        }
        if (failure is not null) throw failure;
        Check(applied?.Lot == "0926-TRAY-IJKL" && connection.Closed && connection.Disposed, "Space did not apply fields and disconnect.");

        var leaving = new SmokeConnection();
        using (var dialog = new CameraForm(null, 8765, true, resolve, print,
            (_, _) => throw new Exception("Escape changed main fields."), leaving, () => { }) { Opacity = 0 })
        {
            dialog.Shown += async (_, _) =>
            {
                try
                {
                    await leaving.EmitAsync(first); await dialog.DrainAsync();
                    Check(dialog._print.Enabled, "Reopened popup did not receive a reading.");
                    dialog.Shortcut(Keys.Escape);
                }
                catch (Exception ex) { failure = ex; dialog.Close(); }
            };
            dialog.ShowDialog(owner);
        }
        if (failure is not null) throw failure;
        Check(leaving.Closed && leaving.Disposed, "Escape did not disconnect.");

        var connecting = new SmokeConnection { WaitForCancellation = true };
        using (var dialog = new CameraForm(null, 8765, true, resolve, print, apply, connecting) { Opacity = 0 })
        {
            dialog.Shown += (_, _) => dialog.Shortcut(Keys.Escape);
            dialog.ShowDialog(owner);
        }
        Check(connecting.Cancelled && connecting.Closed && connecting.Disposed, "Closing during connection did not cancel and disconnect.");
    }
    private Task DrainAsync()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        BeginInvoke(() => ready.SetResult()); return ready.Task;
    }
    private void Shortcut(Keys key) { var message = new Message(); ProcessCmdKey(ref message, key); }
    private static void Check(bool valid, string error) { if (!valid) throw new Exception(error); }
    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Camera UI check timed out.");
            await Task.Delay(10);
        }
    }
    private sealed class SmokeConnection : ICameraConnection
    {
        public event Action<string>? JsonReceived;
        public event Action<string>? StateChanged;
        public bool Closed, Disposed, Cancelled;
        public bool WaitForCancellation;
        public async Task ConnectAsync(string? ip, int port, bool local, CancellationToken token)
        {
            StateChanged?.Invoke("Connected to sample camera");
            if (WaitForCancellation)
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException) { Cancelled = true; throw; }
        }
        public Task EmitAsync(string json) => Task.Run(() => JsonReceived?.Invoke(json));
        public Task CloseAsync() { Closed = true; return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
