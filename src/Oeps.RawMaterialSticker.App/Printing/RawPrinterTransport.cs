using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Oeps.RawMaterialSticker.App.Printing;

public sealed record PrintSubmission(uint JobId, int ByteCount);

public sealed class PrintSubmissionException : IOException
{
    public PrintSubmissionException(string message, bool submissionUncertain, uint? jobId, Exception? innerException = null)
        : base(message, innerException)
    {
        SubmissionUncertain = submissionUncertain;
        JobId = jobId;
    }

    public bool SubmissionUncertain { get; }
    public uint? JobId { get; }
}

/// <summary>One RAW spooler document per request. Never retries a job, including uncertain submissions.</summary>
public sealed class RawPrinterTransport
{
    private int submitting;

    public async Task<PrintSubmission> SendAsync(string queueName, byte[] bytes, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0)
            throw new ArgumentException("The printer job is empty.", nameof(bytes));
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref submitting, 1, 0) != 0)
            throw new InvalidOperationException("A print job is already being submitted. Wait for its result.");
        try
        {
            // Own the bytes; callers cannot mutate a job while native WritePrinter is using it.
            var jobBytes = (byte[])bytes.Clone();
            return await Task.Run(() => Send(queueName, jobBytes, cancellationToken), CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref submitting, 0);
        }
    }

    private static PrintSubmission Send(string queueName, byte[] bytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Native.OpenPrinter(queueName, out var printer, IntPtr.Zero))
        {
            var error = Marshal.GetLastWin32Error();
            printer?.Dispose();
            throw Error("OpenPrinter", error, false, null);
        }
        using (printer)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new Native.DocumentInfo
            {
                DocumentName = "OEPS Raw Material Sticker — one sticker",
                DataType = "RAW"
            };
            var jobId = Native.StartDocPrinter(printer, 1, ref info);
            if (jobId == 0)
                throw Error("StartDocPrinter", Marshal.GetLastWin32Error(), false, null);
            var committed = false;
            var writeAttempted = false;
            try
            {
                // After StartDocPrinter, resolve the submission even if the caller cancels.
                // Cancellation cannot retract bytes already accepted by the queue or device.
                if (!Native.StartPagePrinter(printer))
                    throw Error("StartPagePrinter", Marshal.GetLastWin32Error(), false, jobId);
                var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                try
                {
                    var offset = 0;
                    while (offset < bytes.Length)
                    {
                        writeAttempted = true;
                        if (!Native.WritePrinter(printer, IntPtr.Add(pinned.AddrOfPinnedObject(), offset), (uint)(bytes.Length - offset), out var written))
                            throw Error("WritePrinter", Marshal.GetLastWin32Error(), true, jobId);
                        if (written == 0 || written > bytes.Length - offset)
                            throw new PrintSubmissionException("WritePrinter returned an invalid byte count. Submission is uncertain; inspect the queue and printer before another click. The app will not retry.", true, jobId);
                        // A short successful write continues with only the remaining bytes.
                        offset += checked((int)written);
                    }
                }
                finally
                {
                    pinned.Free();
                }
                if (!Native.EndPagePrinter(printer))
                    throw Error("EndPagePrinter", Marshal.GetLastWin32Error(), true, jobId);
                if (!Native.EndDocPrinter(printer))
                    throw Error("EndDocPrinter", Marshal.GetLastWin32Error(), true, jobId);
                committed = true;
                return new PrintSubmission(jobId, bytes.Length);
            }
            catch (PrintSubmissionException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new PrintSubmissionException("The print submission failed. " + (writeAttempted ? "Submission is uncertain; inspect the queue and printer before another click. " : "") + "The app will not retry.", writeAttempted, jobId, ex);
            }
            finally
            {
                // Best effort only: aborting a spool document does not prove no sticker was printed.
                if (!committed)
                    _ = Native.AbortPrinter(printer);
            }
        }
    }

    private static PrintSubmissionException Error(string operation, int error, bool uncertain, uint? jobId)
    {
        var native = new Win32Exception(error);
        var outcome = uncertain ? " Submission is uncertain; inspect the queue and printer before another click." : " No successful print submission was reported.";
        return new PrintSubmissionException($"{operation} failed ({error}): {native.Message}.{outcome} The app will not retry.", uncertain, jobId, native);
    }

    private sealed class SafePrinterHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafePrinterHandle() : base(true) { }
        protected override bool ReleaseHandle() => Native.ClosePrinter(handle);
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct DocumentInfo
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string DocumentName;
            [MarshalAs(UnmanagedType.LPWStr)] public string? OutputFile;
            [MarshalAs(UnmanagedType.LPWStr)] public string DataType;
        }

        [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool OpenPrinter(string printerName, out SafePrinterHandle printer, IntPtr defaults);

        [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        internal static extern uint StartDocPrinter(SafePrinterHandle printer, uint level, ref DocumentInfo document);

        [DllImport("winspool.drv", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool StartPagePrinter(SafePrinterHandle printer);

        [DllImport("winspool.drv", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WritePrinter(SafePrinterHandle printer, IntPtr bytes, uint count, out uint written);

        [DllImport("winspool.drv", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EndPagePrinter(SafePrinterHandle printer);

        [DllImport("winspool.drv", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EndDocPrinter(SafePrinterHandle printer);

        [DllImport("winspool.drv", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AbortPrinter(SafePrinterHandle printer);

        [DllImport("winspool.drv", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ClosePrinter(IntPtr printer);
    }
}
