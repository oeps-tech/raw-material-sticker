using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Oeps.RawMaterialSticker.App;

// Owned exclusively by the sound worker, until the popup closes or output changes.
// SoundPlayer/PlaySound calls cannot replace this dedicated buffer.
internal sealed class NativeScannerTone : IScannerTone
{
    private readonly AutoResetEvent _completed = new(false);
    private static readonly uint HeaderSize = (uint)Marshal.SizeOf<WaveHeader>();
    private const int BlockBytes = 44100 / 50 * 2; // 20 ms of mono PCM
    private readonly List<AudioBuffer> _buffers = [];
    private readonly byte[] _tone = CreatePcm();
    private readonly object _gate = new();
    private readonly Queue<TaskCompletionSource> _pending = new();
    private TaskCompletionSource? _current;
    private int _toneOffset, _stopping;
    private Exception? _failure;
    private Thread? _pump;
    private bool _pumpStarted;
    private nint _device;
    private long _completedBuffers;
    internal long CompletedBuffers => Interlocked.Read(ref _completedBuffers);

    public NativeScannerTone(uint deviceId = uint.MaxValue)
    {
        try
        {
            var format = new WaveFormat { FormatTag = 1, Channels = 1, SamplesPerSecond = 44100,
                AverageBytesPerSecond = 88200, BlockAlign = 2, BitsPerSample = 16 };
            Check(waveOutOpen(out _device, deviceId, ref format, _completed.SafeWaitHandle.DangerousGetHandle(), 0, 0x50000), "Open audio output");
            // Keep three small buffers in flight even when no tone is requested.
            // Merely keeping a waveOut handle open does not keep its stream active.
            for (var i = 0; i < 3; i++)
            {
                var buffer = new AudioBuffer(); _buffers.Add(buffer);
                buffer.Samples = Marshal.AllocHGlobal(BlockBytes);
                buffer.Header = Marshal.AllocHGlobal((int)HeaderSize);
                Marshal.Copy(buffer.Pcm, 0, buffer.Samples, BlockBytes);
                Marshal.StructureToPtr(new WaveHeader { Data = buffer.Samples, BufferLength = BlockBytes }, buffer.Header, false);
                Check(waveOutPrepareHeader(_device, buffer.Header, HeaderSize), "Prepare scanner audio stream");
                buffer.Prepared = true;
                Check(waveOutWrite(_device, buffer.Header, HeaderSize), "Start scanner audio stream");
            }
            _pump = new Thread(Pump) { IsBackground = true, Name = "OEPS scanner audio" };
            _pump.Start(); _pumpStarted = true;
        }
        catch { Dispose(); throw; }
    }

    internal static byte[] CreatePcm()
    {
        const int sampleRate = 44100, lead = sampleRate / 20, tone = sampleRate / 10, tail = sampleRate / 25;
        var pcm = new byte[(lead + tone + tail) * 2];
        for (var i = 0; i < tone; i++)
        {
            var envelope = Math.Min(1.0, Math.Min(i, tone - 1 - i) / (sampleRate * 0.003));
            var value = (short)(short.MaxValue * 0.55 * envelope * Math.Sin(2 * Math.PI * 2800 * i / sampleRate));
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan((lead + i) * 2, 2), value);
        }
        return pcm;
    }

    internal static AudioOutputDevice GetDefaultOutput()
    {
        // WinMM mapper and endpoint queries from mmddk.h. The endpoint identity
        // detects hot-plug changes even if Windows reuses the numeric device ID.
        Check(waveOutPreferred(unchecked((nint)(nuint)uint.MaxValue), 0x2015, out var id, out _), "Find default audio output");
        if (id == uint.MaxValue) throw new InvalidOperationException("No default audio output is available.");
        Check(waveOutEndpointSize((nint)id, 0x0812, out var size, 0), "Read audio endpoint size");
        if (size < 2 || size > 65536 || size % 2 != 0) throw new InvalidOperationException("Invalid audio endpoint identity size.");
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            Check(waveOutMessage((nint)id, 0x0811, buffer, size), "Read audio endpoint identity");
            var endpoint = Marshal.PtrToStringUni(buffer, (int)size / 2).TrimEnd('\0');
            if (endpoint.Length == 0) throw new InvalidOperationException("Audio endpoint identity is empty.");
            return new(id, endpoint);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public void Play()
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _stopping) != 0, this);
            if (_failure is not null) throw new InvalidOperationException("Scanner audio stream failed.", _failure);
            _pending.Enqueue(finished);
        }
        // Completion means that the buffer containing the end of this particular
        // beep has returned from the driver, not just that a beep was queued.
        finished.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
    }

    private void Pump()
    {
        try
        {
            var next = 0;
            var progress = Stopwatch.StartNew();
            while (Volatile.Read(ref _stopping) == 0)
            {
                var buffer = _buffers[next];
                if ((Marshal.PtrToStructure<WaveHeader>(buffer.Header).Flags & 1) == 0)
                {
                    if (progress.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Scanner audio output stopped returning buffers.");
                    _completed.WaitOne(100); continue;
                }
                buffer.ToneFinished?.TrySetResult(); buffer.ToneFinished = null;
                Interlocked.Increment(ref _completedBuffers);
                Fill(buffer);
                Marshal.Copy(buffer.Pcm, 0, buffer.Samples, BlockBytes);
                Check(waveOutWrite(_device, buffer.Header, HeaderSize), "Continue scanner audio stream");
                next = (next + 1) % _buffers.Count;
                progress.Restart();
            }
        }
        catch (Exception ex)
        {
            lock (_gate) _failure = ex;
        }
        finally
        {
            lock (_gate)
            {
                var error = _failure ?? new ObjectDisposedException(nameof(NativeScannerTone));
                _current?.TrySetException(error);
                while (_pending.TryDequeue(out var pending)) pending.TrySetException(error);
                foreach (var buffer in _buffers) buffer.ToneFinished?.TrySetException(error);
            }
        }
    }

    private void Fill(AudioBuffer buffer)
    {
        Array.Clear(buffer.Pcm);
        lock (_gate)
        {
            if (_current is null && _pending.TryDequeue(out var pending)) { _current = pending; _toneOffset = 0; }
            if (_current is null) return; // Stream silence without closing or starving the device.
            var length = Math.Min(BlockBytes, _tone.Length - _toneOffset);
            _tone.AsSpan(_toneOffset, length).CopyTo(buffer.Pcm);
            _toneOffset += length;
            if (_toneOffset == _tone.Length) { buffer.ToneFinished = _current; _current = null; }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0) return;
        _completed.Set();
        if (_pumpStarted) _pump!.Join();
        if (_device != 0)
        {
            // Reset returns outstanding buffers before their native memory is released.
            var reset = waveOutReset(_device);
            uint unprepare = 0;
            foreach (var buffer in _buffers)
                if (buffer.Prepared) unprepare |= waveOutUnprepareHeader(_device, buffer.Header, HeaderSize);
            var close = waveOutClose(_device);
            if (reset != 0 || unprepare != 0 || close != 0)
            {
                // Keep buffers/event alive if a broken driver still owns them.
                Trace.TraceError($"Audio cleanup failed: reset={reset}, unprepare={unprepare}, close={close}.");
                GC.KeepAlive(_completed);
                _completed.SafeWaitHandle.SetHandleAsInvalid();
                _device = 0;
                return;
            }
            _device = 0;
        }
        foreach (var buffer in _buffers)
        {
            if (buffer.Header != 0) Marshal.FreeHGlobal(buffer.Header);
            if (buffer.Samples != 0) Marshal.FreeHGlobal(buffer.Samples);
        }
        _buffers.Clear();
        _completed.Dispose();
    }

    private sealed class AudioBuffer
    {
        public nint Samples, Header;
        public bool Prepared;
        public byte[] Pcm { get; } = new byte[BlockBytes];
        public TaskCompletionSource? ToneFinished;
    }

    private static void Check(uint result, string operation)
    {
        if (result != 0) throw new InvalidOperationException($"{operation} failed (Windows audio code {result}).");
    }
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormat
    {
        public ushort FormatTag, Channels;
        public uint SamplesPerSecond, AverageBytesPerSecond;
        public ushort BlockAlign, BitsPerSample, ExtraSize;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public nint Data;
        public uint BufferLength, BytesRecorded;
        public nuint User;
        public uint Flags, Loops;
        public nint Next;
        public nuint Reserved;
    }
    [DllImport("winmm.dll")] private static extern uint waveOutOpen(out nint device, uint deviceId, ref WaveFormat format, nint callback, nuint instance, uint flags);
    [DllImport("winmm.dll")] private static extern uint waveOutPrepareHeader(nint device, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutWrite(nint device, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutReset(nint device);
    [DllImport("winmm.dll")] private static extern uint waveOutUnprepareHeader(nint device, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutClose(nint device);
    [DllImport("winmm.dll", EntryPoint = "waveOutMessage")] private static extern uint waveOutPreferred(nint device, uint message, out uint id, out uint flags);
    [DllImport("winmm.dll", EntryPoint = "waveOutMessage")] private static extern uint waveOutEndpointSize(nint device, uint message, out nuint size, nuint unused);
    [DllImport("winmm.dll")] private static extern uint waveOutMessage(nint device, uint message, nint buffer, nuint size);
}
