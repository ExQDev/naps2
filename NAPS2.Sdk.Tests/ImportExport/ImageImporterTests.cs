using System.Runtime.InteropServices;
using System.Threading;
using Moq;
using NAPS2.ImportExport;
using NAPS2.ImportExport.Images;
using NAPS2.Scan;
using NAPS2.Sdk.Tests.Asserts;
using Xunit;

namespace NAPS2.Sdk.Tests.ImportExport;

// TODO: Use StorageConfig to test in-memory import (just need to figure out how to handle storage assertions, maybe delegate to StorageConfig)
public class ImageImporterTests : ContextualTests
{
    private readonly ImageImporter _imageImporter;

    public ImageImporterTests()
    {
        _imageImporter = new ImageImporter(ScanningContext, ImageContext, new ImportPostProcessor());
        ScanningContext.FileStorageManager = FileStorageManager.CreateFolder(Path.Combine(FolderPath, "recovery"));
    }

    [Fact]
    public async Task ImportPngImage()
    {
        var filePath = CopyResourceToFile(ImageResources.skewed_bw, "image.png");

        var source = _imageImporter.Import(filePath, new ImportParams(), (current, max) => { }, CancellationToken.None);
        var result = await source.ToList();

        Assert.Single(result);
        var storage = Assert.IsType<ImageFileStorage>(result[0].Storage);
        Assert.True(File.Exists(storage.FullPath));
        Assert.Equal(Path.Combine(FolderPath, "recovery"), Path.GetDirectoryName(storage.FullPath));
        Assert.True(result[0].Metadata.Lossless);
        Assert.Equal(BitDepth.Color, result[0].Metadata.BitDepth);
        Assert.Null(result[0].PostProcessingData.Thumbnail);
        Assert.False(result[0].PostProcessingData.BarcodeDetection.IsAttempted);
        Assert.True(result[0].TransformState.IsEmpty);

        result[0].Dispose();
        Assert.False(File.Exists(storage.FullPath));
    }

    [Fact]
    public async Task ImportJpegImage()
    {
        var filePath = CopyResourceToFile(ImageResources.dog, "image.jpg");

        var source = _imageImporter.Import(filePath, new ImportParams(), (current, max) => { }, CancellationToken.None);
        var result = await source.ToList();

        Assert.Single(result);
        var storage = Assert.IsType<ImageFileStorage>(result[0].Storage);
        Assert.True(File.Exists(storage.FullPath));
        Assert.Equal(Path.Combine(FolderPath, "recovery"), Path.GetDirectoryName(storage.FullPath));
        Assert.False(result[0].Metadata.Lossless);
        Assert.Equal(BitDepth.Color, result[0].Metadata.BitDepth);
        Assert.Null(result[0].PostProcessingData.Thumbnail);
        Assert.False(result[0].PostProcessingData.BarcodeDetection.IsAttempted);
        Assert.True(result[0].TransformState.IsEmpty);

        result[0].Dispose();
        Assert.False(File.Exists(storage.FullPath));
    }

    [Fact]
    public async Task ImportTiffImage()
    {
        var filePath = CopyResourceToFile(ImageResources.animals_tiff, "image.tiff");

        var source = _imageImporter.Import(filePath, new ImportParams(), (current, max) => { }, CancellationToken.None);
        var result = await source.ToList();

        Assert.Equal(3, result.Count);
        AssertUsesRecoveryStorage(result[0].Storage, "00001.jpg");
        Assert.False(result[0].Metadata.Lossless);
        Assert.Equal(BitDepth.Color, result[0].Metadata.BitDepth);
        ImageAsserts.Similar(ImageResources.dog, result[0]);

        AssertUsesRecoveryStorage(result[2].Storage, "00003.jpg");
        Assert.False(result[2].Metadata.Lossless);
        Assert.Equal(BitDepth.Color, result[2].Metadata.BitDepth);
        ImageAsserts.Similar(ImageResources.stock_cat, result[2]);

        result[0].Dispose();
        AssertRecoveryStorageCleanedUp(result[0].Storage);
        AssertUsesRecoveryStorage(result[2].Storage, "00003.jpg");
        result[2].Dispose();
        AssertRecoveryStorageCleanedUp(result[2].Storage);
    }

    [Fact]
    public async Task ImportWithThumbnailGeneration()
    {
        var filePath = CopyResourceToFile(ImageResources.dog, "image.jpg");

        var source = _imageImporter.Import(filePath, new ImportParams { ThumbnailSize = 256 }, (current, max) => { },
            CancellationToken.None);
        var result = await source.ToList();

        Assert.Single(result);
        Assert.NotNull(result[0].PostProcessingData.Thumbnail);
        Assert.Equal(256, result[0].PostProcessingData.Thumbnail.Width);
    }

    [Fact]
    public async Task SingleFrameProgress()
    {
        var filePath = CopyResourceToFile(ImageResources.dog, "image.jpg");

        var progressMock = new Mock<ProgressHandler>();

        var source = _imageImporter.Import(filePath, new ImportParams(), progressMock.Object, CancellationToken.None);

        progressMock.VerifyNoOtherCalls();
        await source.ToList();
        progressMock.Verify(x => x(0, 1));
        progressMock.Verify(x => x(1, 1));
        progressMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MultiFrameProgress()
    {
        var filePath = CopyResourceToFile(ImageResources.animals_tiff, "image.tiff");

        var progressMock = new Mock<ProgressHandler>();
        var source = _imageImporter.Import(filePath, new ImportParams(), progressMock.Object, CancellationToken.None);

        progressMock.VerifyNoOtherCalls();
        Assert.NotNull(await source.Next());
        progressMock.Verify(x => x(0, 3));
        progressMock.Verify(x => x(1, 3));
        progressMock.VerifyNoOtherCalls();
        Assert.NotNull(await source.Next());
        progressMock.Verify(x => x(2, 3));
        progressMock.VerifyNoOtherCalls();
        Assert.NotNull(await source.Next());
        progressMock.Verify(x => x(3, 3));
        progressMock.VerifyNoOtherCalls();
        Assert.Null(await source.Next());
        progressMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SingleFrameCancellation()
    {
        var filePath = CopyResourceToFile(ImageResources.dog, "image.jpg");

        var cts = new CancellationTokenSource();
        var source = _imageImporter.Import(filePath, new ImportParams(), (current, max) => { }, cts.Token);

        cts.Cancel();
        Assert.Null(await source.Next());
    }

    // This test doesn't work on Mac as the full file is loaded first, making per-frame loading instant
    [PlatformFact(exclude: PlatformFlags.Mac)]
    public async Task MultiFrameCancellation()
    {
        var filePath = CopyResourceToFile(ImageResources.animals_tiff, "image.tiff");

        var cts = new CancellationTokenSource();
        var source = _imageImporter.Import(filePath, new ImportParams(), (current, max) => { }, cts.Token);

        Assert.NotNull(await source.Next());
        Assert.NotNull(await source.Next());
        cts.Cancel();
        Assert.Null(await source.Next());
    }

    private void AssertUsesRecoveryStorage(IImageStorage storage, string expectedFileName)
    {
        var fileStorage = Assert.IsType<ImageFileStorage>(storage);
        Assert.EndsWith(expectedFileName, fileStorage.FullPath);
        Assert.True(File.Exists(fileStorage.FullPath));
        Assert.Equal(Path.Combine(FolderPath, "recovery"), Path.GetDirectoryName(fileStorage.FullPath));
    }

    private void AssertRecoveryStorageCleanedUp(IImageStorage storage)
    {
        var fileStorage = Assert.IsType<ImageFileStorage>(storage);
        Assert.False(File.Exists(fileStorage.FullPath));
    }

    [Fact]
    public async Task ImportMissingFile()
    {
        var filePath = Path.Combine(FolderPath, "missing.png");
        var source = _imageImporter.Import(filePath, new ImportParams(), (current, max) => { }, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<FileNotFoundException>(async () => await source.ToList());
        Assert.Contains("Could not find", ex.Message);
    }

    [Fact]
    public async Task ImportInUseFile()
    {
        var filePath = CopyResourceToFile(ImageResources.dog, "image.png");
        using var stream = File.OpenWrite(filePath);
        var source = _imageImporter.Import(filePath, new ImportParams(), (current, max) => { }, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<IOException>(async () => await source.ToList());
        Assert.Contains("being used by another process", ex.Message);
    }

    [Fact]
    public async Task ImportWithBarcodeDetection()
    {
        var filePath = CopyResourceToFile(ImageResources.patcht, "image.jpg");

        var importParams = new ImportParams
        {
            BarcodeDetectionOptions = new BarcodeDetectionOptions { DetectBarcodes = true }
        };
        var source = _imageImporter.Import(filePath, importParams, (current, max) => { }, CancellationToken.None);
        var result = await source.ToList();

        Assert.Single(result);
        Assert.True(result[0].PostProcessingData.BarcodeDetection.IsAttempted);
        Assert.True(result[0].PostProcessingData.BarcodeDetection.IsBarcodePresent);
        Assert.True(result[0].PostProcessingData.BarcodeDetection.IsPatchT);
        Assert.Equal("PATCHT", result[0].PostProcessingData.BarcodeDetection.DetectedText);
    }
}