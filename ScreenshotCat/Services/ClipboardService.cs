using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using System.Runtime.InteropServices;

namespace ScreenshotCat.Services;

public sealed class ClipboardService
{
    private const int ClipboardBusyHResult = unchecked((int)0x800401D0);

    public async Task CopyImageAsync(string imagePath)
    {
        if (!File.Exists(imagePath))
        {
            return;
        }

        var file = await StorageFile.GetFileFromPathAsync(imagePath);
        var package = new DataPackage();
        package.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));
        package.SetText(imagePath);
        await SetContentWithRetryAsync(package);
    }

    public async Task CopyFilesAndTextAsync(IReadOnlyList<string> filePaths, string text)
    {
        var files = new List<StorageFile>();
        foreach (var filePath in filePaths)
        {
            if (File.Exists(filePath))
            {
                files.Add(await StorageFile.GetFileFromPathAsync(filePath));
            }
        }

        var package = new DataPackage();
        package.SetText(text);
        if (files.Count > 0)
        {
            package.SetStorageItems(files);
        }

        await SetContentWithRetryAsync(package);
    }

    private static async Task SetContentWithRetryAsync(DataPackage package)
    {
        const int maxAttempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Clipboard.SetContent(package);
                Clipboard.Flush();
                return;
            }
            catch (COMException exception) when (
                exception.HResult == ClipboardBusyHResult && attempt < maxAttempts)
            {
                await Task.Delay(attempt * 40);
            }
        }
    }
}
