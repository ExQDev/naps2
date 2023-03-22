﻿using System.Threading;
using NAPS2.Ocr;
using NAPS2.Scan.Internal;

namespace NAPS2.Scan;

public class ScanController
{
    private readonly ScanningContext _scanningContext;
    private readonly ILocalPostProcessor _localPostProcessor;
    private readonly ScanOptionsValidator _scanOptionsValidator;
    private readonly IScanBridgeFactory _scanBridgeFactory;
    public static EventHandler<ProcessedImage>? OnProcess;
    public static EventHandler? OnStart;
    public static EventHandler? OnEnd;
    public static EventHandler<string>? OnError;

    public ScanController(ScanningContext scanningContext)
        : this(scanningContext, new LocalPostProcessor(scanningContext, new OcrController(scanningContext)),
            new ScanOptionsValidator(),
            new ScanBridgeFactory(scanningContext))
    {
    }

    public ScanController(ScanningContext scanningContext, OcrController ocrController)
        : this(scanningContext, new LocalPostProcessor(scanningContext, ocrController), new ScanOptionsValidator(),
            new ScanBridgeFactory(scanningContext))
    {
    }

    internal ScanController(ScanningContext scanningContext, ILocalPostProcessor localPostProcessor,
        ScanOptionsValidator scanOptionsValidator, IScanBridgeFactory scanBridgeFactory)
    {
        _scanningContext = scanningContext;
        _localPostProcessor = localPostProcessor;
        _scanOptionsValidator = scanOptionsValidator;
        _scanBridgeFactory = scanBridgeFactory;
    }

    public Task<List<ScanDevice>> GetDeviceList() => GetDeviceList(new ScanOptions());

    public Task<List<ScanDevice>> GetDeviceList(Driver driver) => GetDeviceList(new ScanOptions { Driver = driver });

    public async Task<List<ScanDevice>> GetDeviceList(ScanOptions options)
    {
        options = _scanOptionsValidator.ValidateAll(options, _scanningContext, false);
        var bridge = _scanBridgeFactory.Create(options);
        var devices = new List<ScanDevice>();
        await bridge.GetDevices(options, CancellationToken.None, devices.Add);
        return devices;
    }

    public IAsyncEnumerable<ScanDevice> GetDevices(CancellationToken cancelToken = default) =>
        GetDevices(new ScanOptions(), cancelToken);

    public IAsyncEnumerable<ScanDevice> GetDevices(Driver driver, CancellationToken cancelToken = default) =>
        GetDevices(new ScanOptions { Driver = driver }, cancelToken);

    public IAsyncEnumerable<ScanDevice> GetDevices(ScanOptions options, CancellationToken cancelToken = default)
    {
        options = _scanOptionsValidator.ValidateAll(options, _scanningContext, false);
        var bridge = _scanBridgeFactory.Create(options);
        return AsyncProducers.RunProducer<ScanDevice>(async produce =>
        {
            await bridge.GetDevices(options, cancelToken, produce);
        });
    }

    public IAsyncEnumerable<ProcessedImage> Scan(ScanOptions options, CancellationToken cancelToken = default)
    {
        options = _scanOptionsValidator.ValidateAll(options, _scanningContext, true);
        int pageNumber = 0;

        void ScanStartCallback() => ScanStart?.Invoke(this, EventArgs.Empty);
        void ScanEndCallback() => ScanEnd?.Invoke(this, EventArgs.Empty);
        void ScanErrorCallback(Exception ex) => ScanError?.Invoke(this, new ScanErrorEventArgs(ex));
        void PageStartCallback() => PageStart?.Invoke(this, new PageStartEventArgs(++pageNumber));

        void PageProgressCallback(double progress) =>
            PageProgress?.Invoke(this, new PageProgressEventArgs(pageNumber, progress));

        void PageEndCallback(ProcessedImage image) => PageEnd?.Invoke(this, new PageEndEventArgs(pageNumber, image));

        ScanStartCallback();
        if (OnStart != null)
        {
            OnStart.Invoke(this, null);
        }
        return AsyncProducers.RunProducer<ProcessedImage>(async produceImage =>
        {
            try
            {
                var bridge = _scanBridgeFactory.Create(options);
                await bridge.Scan(options, cancelToken, new ScanEvents(PageStartCallback, PageProgressCallback),
                    (image, postProcessingContext) =>
                    {
                        image = _localPostProcessor.PostProcess(image, options, postProcessingContext);
                        produceImage(image);
                        PageEndCallback(image);
                        if (OnProcess != null)
                        {
                            OnProcess.Invoke(this, image);
                        }
                    });
            }
            catch (Exception ex)
            {
                ScanErrorCallback(ex);
                if (OnError != null)
                {
                    OnError.Invoke(this, ex.Message);
                }
                if (PropagateErrors)
                {
                    throw;
                }
            }
            finally
            {
                ScanEndCallback();
                if (OnEnd != null)
                {
                    OnEnd.Invoke(this, null);
                }
            }
        });
    }

    /// <summary>
    /// Whether scan errors should be thrown when enumerating the IAsyncEnumerable result. If false (the default), you
    /// will need to listen for the ScanError event to handle errors.
    /// </summary>
    public bool PropagateErrors { get; set; }

    public event EventHandler? ScanStart;

    public event EventHandler? ScanEnd;

    public event EventHandler<ScanErrorEventArgs>? ScanError;

    public event EventHandler<PageStartEventArgs>? PageStart;

    public event EventHandler<PageProgressEventArgs>? PageProgress;

    public event EventHandler<PageEndEventArgs>? PageEnd;
}