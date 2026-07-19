using System.Drawing;

namespace ScreenshotCat.Models;

public sealed record CaptureResult(Bitmap Bitmap, Rectangle MonitorBounds, string PreviewPath) : IDisposable
{
    public void Dispose()
    {
        Bitmap.Dispose();
        TryDelete(PreviewPath);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temporary previews are best-effort cleanup only.
        }
    }
}
