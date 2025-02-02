using System.Threading;
using NAPS2.Platform.Windows;
using NAPS2.Remoting.Worker;

namespace NAPS2.Scan.Internal.Twain;

/// <summary>
/// Proxy implementation of ITwainSessionController that interacts with a Twain session in a worker process.
/// </summary>
public class RemoteTwainSessionController : ITwainSessionController
{
    private readonly ScanningContext _scanningContext;

    public RemoteTwainSessionController(ScanningContext scanningContext)
    {
        _scanningContext = scanningContext;
    }

    public async Task<List<ScanDevice>> GetDeviceList(ScanOptions options)
    {
        using var workerContext = CreateWorker(options);
        return await workerContext.Service.TwainGetDeviceList(options);
    }

    public async Task StartScan(ScanOptions options, ITwainEvents twainEvents, CancellationToken cancelToken)
    {
        using var workerContext = CreateWorker(options);
        try
        {
            await workerContext.Service.TwainScan(options, cancelToken, twainEvents);
        }
        finally
        {
            EnableWindow(options);
        }
    }

    private WorkerContext CreateWorker(ScanOptions options)
    {
        // TODO: Allow TWAIN to be used without a worker for SDK users
        if (_scanningContext.WorkerFactory == null)
        {
            throw new InvalidOperationException(
                "ScanningContext.WorkerFactory must be set to use TWAIN.");
        }
        return _scanningContext.WorkerFactory.Create(options.TwainOptions.Dsm == TwainDsm.NewX64
            ? WorkerType.Native
            : WorkerType.WinX86);
    }

    private void EnableWindow(ScanOptions options)
    {
        if (options.DialogParent != IntPtr.Zero && options.UseNativeUI)
        {
            // At the Windows API level, a modal window is implemented by doing two things:
            // 1. Setting the parent on the child window
            // 2. Disabling the parent window
            // The worker is supposed to re-enable the window before returning, but in case the process dies or
            // some other problem occurs, here we make sure that happens.
            Win32.EnableWindow(options.DialogParent, true);
            // We also want to make sure the main NAPS2 window is in the foreground
            Win32.SetForegroundWindow(options.DialogParent);
        }
    }
}