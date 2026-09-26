using Oeps.RawMaterialSticker.App;

namespace Oeps.RawMaterialSticker.Tests;

public static class ScannerSoundTests
{
    public static int VerifyDevicePlayback()
    {
        try
        {
            var device = NativeScannerTone.GetDefaultOutput();
            using var output = new NativeScannerTone(device.Id);
            Console.WriteLine($"Opened default audio output {device.Id} once for five beeps.");
            for (var i = 0; i < 5; i++)
            {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                output.Play();
                Assert.True(timer.ElapsedMilliseconds >= 150, "Audio driver completed before the full buffer duration.");
                Console.WriteLine($"PASS native beep {i + 1}: driver completed after {timer.ElapsedMilliseconds} ms");
                var beforeIdle = output.CompletedBuffers;
                Thread.Sleep(i == 1 ? 2000 : 250);
                Assert.True(output.CompletedBuffers > beforeIdle + 3, "The audio stream stopped between beeps.");
            }
            Console.WriteLine("PASS audio stream remained active with silence between every beep, including a two-second pause.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    [Test]
    public static async Task OutputOpensBeforeFirstScanIsReusedAndTracksIdleEndpointChanges()
    {
        var selected = new AudioOutputDevice(0, "monitor");
        var opened = new System.Collections.Concurrent.ConcurrentQueue<FakeTone>();
        var sound = new ScannerReadSound(getDefaultOutput: () => Volatile.Read(ref selected), openOutput: device =>
        {
            var tone = new FakeTone(); opened.Enqueue(tone); return tone;
        });
        try
        {
            await UntilAsync(() => opened.Count == 1);
            var first = opened.First();
            Assert.Equal(0, first.Plays);
            sound.Play(); sound.Play(); sound.Play();
            await UntilAsync(() => first.Plays == 3);
            Assert.Equal(1, opened.Count); Assert.Equal(0, first.Disposals);
            // Numeric WinMM IDs may be reused: the endpoint identity must also be compared.
            Volatile.Write(ref selected, new AudioOutputDevice(0, "laptop"));
            await UntilAsync(() => opened.Count == 2);
            Assert.Equal(1, first.Disposals);
            var second = opened.Last();
            Assert.Equal(0, second.Plays);
            sound.Play(); await UntilAsync(() => second.Plays == 1);
            await sound.DisposeAsync();
            Assert.Equal(1, second.Disposals);
        }
        finally { await sound.DisposeAsync(); }
    }

    [Test]
    public static async Task OutputChangeDuringBeepWaitsForCompletionBeforeClosingDevice()
    {
        using var release = new ManualResetEventSlim();
        var selected = new AudioOutputDevice(0, "monitor");
        var first = new FakeTone(() => release.Wait(TimeSpan.FromSeconds(5)));
        var second = new FakeTone();
        var sound = new ScannerReadSound(getDefaultOutput: () => Volatile.Read(ref selected),
            openOutput: device => device.Id == 0 ? first : second);
        try
        {
            sound.Play(); await UntilAsync(() => first.Plays == 1);
            Volatile.Write(ref selected, new AudioOutputDevice(1, "laptop"));
            sound.Play();
            await Task.Delay(300);
            Assert.Equal(0, first.Disposals); Assert.Equal(0, second.Plays);
            release.Set(); await UntilAsync(() => second.Plays == 1);
            Assert.Equal(1, first.Disposals);
            await sound.DisposeAsync(); Assert.Equal(1, second.Disposals);
        }
        finally { release.Set(); await sound.DisposeAsync(); }
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Audio lifecycle check timed out.");
            await Task.Delay(10);
        }
    }

    private sealed class FakeTone(Action? duringPlayback = null) : IScannerTone
    {
        public int Plays, Disposals;
        public void Play() { Interlocked.Increment(ref Plays); duringPlayback?.Invoke(); }
        public void Dispose() => Interlocked.Increment(ref Disposals);
    }

    [Test]
    public static async Task PlaybackErrorsAreReportedAndDoNotStopSubsequentBeeps()
    {
        var attempts = 0;
        var errors = new List<string>();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var sound = new ScannerReadSound(() =>
        {
            if (++attempts == 1) throw new InvalidOperationException("Disconnected audio output");
            finished.TrySetResult();
        }, error => errors.Add(error));
        sound.Play(); sound.Play();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, attempts); Assert.Equal("Disconnected audio output", errors.Single());
    }

    [Test]
    public static void NativeToneContainsFullDurationAndSilentLeadAndTail()
    {
        var pcm = NativeScannerTone.CreatePcm();
        Assert.Equal(44100 * 190 / 1000 * 2, pcm.Length);
        Assert.True(pcm.Take(44100 / 20 * 2).All(b => b == 0));
        Assert.True(pcm.Skip(pcm.Length - 44100 / 25 * 2).All(b => b == 0));
        Assert.True(pcm.Skip(44100 / 20 * 2).Take(44100 / 10 * 2).Any(b => b != 0));
    }

    [Test]
    public static async Task RapidScansPlayEveryToneWithoutOverlap()
    {
        var count = 0; var active = 0; var overlap = 0;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var sound = new ScannerReadSound(() =>
        {
            if (Interlocked.Increment(ref active) != 1) Interlocked.Increment(ref overlap);
            Thread.Sleep(10);
            Interlocked.Decrement(ref active);
            if (Interlocked.Increment(ref count) == 20) completed.TrySetResult();
        });
        for (var i = 0; i < 20; i++) sound.Play();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(20, count); Assert.Equal(0, overlap);
    }

    [Test]
    public static async Task ClosingDuringPlaybackDiscardsPendingBeepsAndWaitsForActiveTone()
    {
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var sound = new ScannerReadSound(() =>
        {
            Interlocked.Increment(ref count); started.TrySetResult(); release.Wait(TimeSpan.FromSeconds(5));
        });
        try
        {
            sound.Play(); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            sound.Play(); sound.Play();
            var disposing = sound.DisposeAsync().AsTask();
            Assert.False(disposing.IsCompleted);
            sound.Play(); release.Set();
            await disposing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, count);
        }
        finally { release.Set(); await sound.DisposeAsync(); }
    }
}
