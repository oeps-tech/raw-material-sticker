using System.Threading.Channels;

namespace Oeps.RawMaterialSticker.App;

internal sealed record AudioOutputDevice(uint Id, string EndpointId);
internal interface IScannerTone : IDisposable { void Play(); }

internal sealed class ScannerReadSound : IDisposable, IAsyncDisposable
{
    private readonly Channel<bool> _requests = Channel.CreateUnbounded<bool>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _playback;
    private int _disposed;

    public ScannerReadSound(Action? playTone = null, Action<string>? onError = null,
        Func<AudioOutputDevice>? getDefaultOutput = null, Func<AudioOutputDevice, IScannerTone>? openOutput = null)
    {
        getDefaultOutput ??= NativeScannerTone.GetDefaultOutput;
        openOutput ??= device => new NativeScannerTone(device.Id);
        _playback = Task.Run(async () =>
        {
            IScannerTone? output = null;
            AudioOutputDevice? currentDevice = null;
            void RefreshOutput()
            {
                if (playTone is not null) return;
                var selected = getDefaultOutput();
                if (output is not null && selected == currentDevice) return;
                output?.Dispose(); output = null; currentDevice = null;
                output = openOutput(selected); currentDevice = selected;
            }
            void ReportFailure(Exception ex)
            {
                output?.Dispose(); output = null; currentDevice = null;
                System.Diagnostics.Trace.TraceError("Camera sound playback: " + ex);
                try { onError?.Invoke(ex.Message); }
                catch (Exception notificationError) { System.Diagnostics.Trace.TraceError("Camera sound notification: " + notificationError); }
            }
            try
            {
                // Prepare the output before the first scan. Also monitor changes while idle.
                try { if (Volatile.Read(ref _disposed) == 0) RefreshOutput(); }
                catch (Exception ex) { ReportFailure(ex); }
                var available = _requests.Reader.WaitToReadAsync().AsTask();
                while (Volatile.Read(ref _disposed) == 0)
                {
                    if (!available.IsCompleted) await Task.WhenAny(available, Task.Delay(250));
                    if (Volatile.Read(ref _disposed) != 0) break;
                    if (!available.IsCompleted)
                    {
                        try { RefreshOutput(); }
                        catch (Exception ex) { ReportFailure(ex); }
                        continue;
                    }
                    if (!await available) break;
                    if (_requests.Reader.TryRead(out _))
                    {
                        try
                        {
                            RefreshOutput(); // Catch a change immediately before a scan as well.
                            if (playTone is not null) playTone();
                            else output!.Play();
                        }
                        catch (Exception ex) { ReportFailure(ex); }
                        await Task.Delay(25); // Keep rapid confirmations distinct.
                    }
                    available = _requests.Reader.WaitToReadAsync().AsTask();
                }
            }
            finally { output?.Dispose(); }
        });
    }

    public void Play()
    {
        if (Volatile.Read(ref _disposed) == 0) _requests.Writer.TryWrite(true);
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _requests.Writer.TryComplete();
        // The worker owns audio resources and releases them after the active beep finishes.
    }
    public ValueTask DisposeAsync() { Dispose(); return new ValueTask(_playback); }
}
