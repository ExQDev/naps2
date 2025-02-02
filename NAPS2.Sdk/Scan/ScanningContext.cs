using System.Collections.Immutable;
using NAPS2.Ocr;
using NAPS2.Remoting.Worker;
using NAPS2.Scan.Internal;

namespace NAPS2.Scan;

public class ScanningContext : IDisposable
{
    private readonly ProcessedImageOwner _processedImageOwner = new();

    // TODO: Make sure properties are initialized by callers (or something equivalent)
    public ScanningContext(ImageContext imageContext)
    {
        ImageContext = imageContext;
    }

    public ScanningContext(ImageContext imageContext, FileStorageManager fileStorageManager)
    {
        ImageContext = imageContext;
        FileStorageManager = fileStorageManager;
    }

    public ScanningContext(ImageContext imageContext, FileStorageManager fileStorageManager, IOcrEngine ocrEngine,
        IWorkerFactory workerFactory)
    {
        ImageContext = imageContext;
        FileStorageManager = fileStorageManager;
        OcrEngine = ocrEngine;
        WorkerFactory = workerFactory;
    }

    // TODO: Figure out initialization etc.
    public ImageContext ImageContext { get; }

    public FileStorageManager? FileStorageManager { get; set; }

    public string TempFolderPath { get; set; } = Path.GetTempPath();

    public string? RecoveryPath { get; set; }

    public IWorkerFactory? WorkerFactory { get; set; }

    public OcrRequestQueue OcrRequestQueue { get; } = new();

    public IOcrEngine? OcrEngine { get; set; }

    internal IScanDriver? LegacyTwainDriver { get; set; }

    public ProcessedImage CreateProcessedImage(IImageStorage storage)
    {
        return CreateProcessedImage(storage, Enumerable.Empty<Transform>());
    }

    public ProcessedImage CreateProcessedImage(IImageStorage storage, IEnumerable<Transform> transforms)
    {
        var bitDepth = storage switch
        {
            IMemoryImage { LogicalPixelFormat: ImagePixelFormat.BW1 } => BitDepth.BlackAndWhite,
            IMemoryImage { LogicalPixelFormat: ImagePixelFormat.Gray8 } => BitDepth.Grayscale,
            _ => BitDepth.Color
        };
        return CreateProcessedImage(storage, bitDepth, false, -1, transforms);
    }

    public ProcessedImage CreateProcessedImage(IImageStorage storage, BitDepth bitDepth, bool lossless, int quality)
    {
        return CreateProcessedImage(storage, bitDepth, lossless, quality, Enumerable.Empty<Transform>());
    }

    public ProcessedImage CreateProcessedImage(IImageStorage storage, BitDepth bitDepth, bool lossless, int quality,
        IEnumerable<Transform> transforms)
    {
        var convertedStorage = ConvertStorageIfNeeded(storage, bitDepth, lossless, quality);
        var metadata = new ImageMetadata(bitDepth, lossless);
        var image = new ProcessedImage(
            ImageContext,
            convertedStorage,
            metadata,
            new PostProcessingData(),
            new TransformState(transforms.ToImmutableList()),
            _processedImageOwner);
        return image;
    }

    private IImageStorage ConvertStorageIfNeeded(IImageStorage storage, BitDepth bitDepth, bool lossless, int quality)
    {
        if (FileStorageManager != null)
        {
            return ConvertToFileStorage(storage, bitDepth, lossless, quality);
        }
        return ConvertToMemoryStorage(storage);
    }

    private IImageStorage ConvertToMemoryStorage(IImageStorage storage)
    {
        switch (storage)
        {
            case IMemoryImage image:
                return image.Clone();
            case ImageFileStorage fileStorage:
                return ImageContext.Load(fileStorage.FullPath);
            case ImageMemoryStorage memoryStorage:
                if (memoryStorage.TypeHint == ".pdf")
                {
                    return memoryStorage;
                }
                return ImageContext.Load(memoryStorage.Stream);
            default:
                // The only case that should hit this is a test with a mock
                return storage;
        }
    }

    private IImageStorage ConvertToFileStorage(IImageStorage storage, BitDepth bitDepth, bool lossless, int quality)
    {
        switch (storage)
        {
            case IMemoryImage image:
                return WriteImageToBackingFile(image, bitDepth, lossless, quality);
            case ImageFileStorage fileStorage:
                return fileStorage;
            case ImageMemoryStorage memoryStorage:
                if (memoryStorage.TypeHint == ".pdf")
                {
                    return WriteDataToBackingFile(memoryStorage.Stream, ".pdf");
                }
                // TODO: Can we just write this to a file directly? Is there any case where SaveSmallestFormat is really needed?
                var loadedImage = ImageContext.Load(memoryStorage.Stream);
                return WriteImageToBackingFile(loadedImage, bitDepth, lossless, quality);
            default:
                // The only case that should hit this is a test with a mock
                return storage;
        }
    }

    private ImageFileStorage WriteDataToBackingFile(MemoryStream stream, string ext)
    {
        if (FileStorageManager == null)
        {
            throw new InvalidOperationException();
        }
        var path = FileStorageManager.NextFilePath() + ext;
        using var fileStream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        stream.WriteTo(fileStream);
        return new ImageFileStorage(path, false);
    }

    private IImageStorage WriteImageToBackingFile(IMemoryImage image, BitDepth bitDepth, bool lossless, int quality)
    {
        if (FileStorageManager == null)
        {
            throw new InvalidOperationException();
        }
        var path = FileStorageManager.NextFilePath();
        var fullPath = new ImageExportHelper()
            .SaveSmallestFormat(path, image, bitDepth, lossless, quality, out _);
        return new ImageFileStorage(fullPath, false);
    }

    public string SaveToTempFile(IMemoryImage image, BitDepth bitDepth = BitDepth.Color)
    {
        var path = Path.Combine(TempFolderPath, Path.GetRandomFileName());
        return new ImageExportHelper()
            .SaveSmallestFormat(path, image, bitDepth, false, -1, out _);
    }

    public string SaveToTempFile(ProcessedImage image, BitDepth bitDepth = BitDepth.Color)
    {
        using var rendered = image.Render();
        return SaveToTempFile(rendered, bitDepth);
    }

    public void Dispose()
    {
        _processedImageOwner.Dispose();
        FileStorageManager?.Dispose();
    }

    private class ProcessedImageOwner : IProcessedImageOwner, IDisposable
    {
        private readonly HashSet<IDisposable> _disposables = new HashSet<IDisposable>();

        public void Register(IDisposable internalDisposable)
        {
            lock (this)
            {
                _disposables.Add(internalDisposable);
            }
        }

        public void Unregister(IDisposable internalDisposable)
        {
            lock (this)
            {
                _disposables.Remove(internalDisposable);
            }
        }

        public void Dispose()
        {
            IEnumerable<IDisposable> list;
            lock (this)
            {
                list = _disposables.ToList();
            }
            foreach (var disposable in list)
            {
                disposable.Dispose();
            }
        }
    }
}