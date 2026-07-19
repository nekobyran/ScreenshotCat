using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ScreenshotCat.Interop;
using ScreenshotCat.Models;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace ScreenshotCat.Services;

public sealed class ScreenCaptureService
{
    internal NativeMethods.POINT GetCursorPosition()
    {
        NativeMethods.GetCursorPos(out var point);
        return point;
    }

    public Rectangle GetMonitorBoundsAtCursor()
    {
        var point = GetCursorPosition();
        return GetMonitorBoundsAtPoint(new Point(point.X, point.Y));
    }

    public Rectangle GetMonitorBoundsAtPoint(Point point)
    {
        var nativePoint = new NativeMethods.POINT { X = point.X, Y = point.Y };
        var monitor = NativeMethods.MonitorFromPoint(nativePoint, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MONITORINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>()
        };

        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return new Rectangle(0, 0, 1920, 1080);
        }

        return Rectangle.FromLTRB(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right, info.rcMonitor.Bottom);
    }

    public CaptureResult CaptureMonitorAtCursor()
    {
        var point = GetCursorPosition();
        return CaptureMonitorAtPoint(new Point(point.X, point.Y));
    }

    public CaptureResult CaptureMonitorAtPoint(Point point)
    {
        var bounds = GetMonitorBoundsAtPoint(point);
        Bitmap bitmap;

        try
        {
            bitmap = CaptureMonitorByDxgi(bounds);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DXGI capture失败，回退到GDI：{ex}");
            bitmap = CaptureMonitorByGdi(bounds);
        }

        var previewPath = Path.Combine(Path.GetTempPath(), $"ScreenshotCat-preview-{Guid.NewGuid():N}.png");
        bitmap.Save(previewPath, ImageFormat.Png);
        return new CaptureResult(bitmap, bounds, previewPath);
    }

    private static Bitmap CaptureMonitorByDxgi(Rectangle monitorBounds)
    {
        using var dxgiFactory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        if (!TryFindOutputForMonitor(dxgiFactory, monitorBounds, out var adapter, out var output1))
        {
            throw new InvalidOperationException("No DXGI output found for current monitor.");
        }

        using var retainedAdapter = adapter;
        using var retainedOutput1 = output1;
        var featureLevels = Array.Empty<FeatureLevel>();

        D3D11.D3D11CreateDevice(
            adapter: retainedAdapter,
            driverType: DriverType.Unknown,
            flags: DeviceCreationFlags.BgraSupport,
            featureLevels: featureLevels,
            device: out var d3dDevice,
            featureLevel: out _,
            immediateContext: out var d3dContext);
        ArgumentNullException.ThrowIfNull(d3dDevice);
        ArgumentNullException.ThrowIfNull(d3dContext);

        using var device = d3dDevice!;
        using var context = d3dContext!;
        using var duplication = retainedOutput1!.DuplicateOutput(device);

        OutduplFrameInfo frameInfo;
        IDXGIResource desktopResource;
        duplication.AcquireNextFrame(1000, out frameInfo, out desktopResource);
        _ = frameInfo;

        try
        {
            using var resource = desktopResource;
            using var sourceTexture = resource.QueryInterface<ID3D11Texture2D>();
            var sourceDesc = sourceTexture.Description;
            using var stagingTexture = CreateCpuReadableTexture(device, sourceDesc);
            context.CopyResource(stagingTexture, sourceTexture);

            var mapped = context.Map(stagingTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            var sourceStride = (int)mapped.RowPitch;
            try
            {
                var width = (int)sourceDesc.Width;
                var height = (int)sourceDesc.Height;
                var rowLength = width * 4;
                var rawData = new byte[height * rowLength];
                for (var y = 0; y < height; y++)
                {
                    var sourceOffset = y * sourceStride;
                    var targetOffset = y * rowLength;
                    Marshal.Copy((IntPtr)(mapped.DataPointer + sourceOffset), rawData, targetOffset, rowLength);
                    ForceOpaqueAlpha(rawData, targetOffset, rowLength);
                }

                using var pinned = new PinnedByteArray(rawData);
                using var fullBitmap = new Bitmap(width, height, rowLength, PixelFormat.Format32bppArgb, pinned.Pointer);
                if (IsLikelyBlankCapture(fullBitmap))
                {
                    throw new InvalidOperationException("DXGI capture返回空白内容");
                }

                return new Bitmap(fullBitmap);
            }
            finally
            {
                context.Unmap(stagingTexture, 0);
            }
        }
        finally
        {
            duplication.ReleaseFrame();
        }
    }

    private static ID3D11Texture2D CreateCpuReadableTexture(ID3D11Device device, Texture2DDescription sourceDesc)
    {
        var stagingDesc = new Texture2DDescription
        {
            Width = sourceDesc.Width,
            Height = sourceDesc.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = sourceDesc.Format,
            SampleDescription = sourceDesc.SampleDescription,
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None
        };

        return device.CreateTexture2D(stagingDesc, (SubresourceData[]?)null);
    }

    private static bool TryFindOutputForMonitor(
        IDXGIFactory1 factory,
        Rectangle monitorBounds,
        out IDXGIAdapter1? adapter,
        out IDXGIOutput1? output1)
    {
        adapter = null;
        output1 = null;
        var cursor = new Point(monitorBounds.Left + Math.Max(0, monitorBounds.Width / 2), monitorBounds.Top + Math.Max(0, monitorBounds.Height / 2));

        for (uint adapterIndex = 0; ; adapterIndex++)
        {
            IDXGIAdapter1 currentAdapter = null!;
            try
            {
                factory.EnumAdapters1(adapterIndex, out currentAdapter);
            }
            catch
            {
                break;
            }

            var foundForAdapter = false;
            try
            {
                for (uint outputIndex = 0; ; outputIndex++)
                {
                    IDXGIOutput currentOutput = null!;
                    try
                    {
                        currentAdapter.EnumOutputs(outputIndex, out currentOutput);
                    }
                    catch
                    {
                        break;
                    }

                    using (currentOutput)
                    {
                        var desktop = currentOutput.Description.DesktopCoordinates;
                        var desktopRect = Rectangle.FromLTRB(desktop.Left, desktop.Top, desktop.Right, desktop.Bottom);
                        if (!desktopRect.Contains(cursor))
                        {
                            continue;
                        }

                        adapter = currentAdapter;
                        output1 = currentOutput.QueryInterface<IDXGIOutput1>();
                        foundForAdapter = true;
                        return true;
                    }
                }
            }
            finally
            {
                if (!foundForAdapter)
                {
                    currentAdapter.Dispose();
                }
            }
        }

        return false;
    }

    private static Bitmap CaptureMonitorByGdi(Rectangle bounds)
    {
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        }

        return bitmap;
    }

    private static void ForceOpaqueAlpha(byte[] bgraData, int rowOffset, int rowLength)
    {
        for (var offset = rowOffset + 3; offset < rowOffset + rowLength; offset += 4)
        {
            bgraData[offset] = 255;
        }
    }

    private static bool IsLikelyBlankCapture(Bitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        if (width <= 0 || height <= 0)
        {
            return true;
        }

        var stepX = Math.Max(1, width / 28);
        var stepY = Math.Max(1, height / 28);

        var firstSet = false;
        byte firstR = 0;
        byte firstG = 0;
        byte firstB = 0;
        int sampleCount = 0;
        int sameColorCount = 0;
        int darkCount = 0;
        long totalLuma = 0;

        var rect = new Rectangle(0, 0, width, height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytesPerPixel = 4;
            var rowBytes = Math.Abs(data.Stride);
            var row = new byte[rowBytes];
            for (var y = 0; y < height; y += stepY)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, rowBytes);
                for (var x = 0; x < width; x += stepX)
                {
                    var offset = x * bytesPerPixel;
                    if (offset + 2 >= rowBytes)
                    {
                        continue;
                    }

                    var b = row[offset];
                    var g = row[offset + 1];
                    var r = row[offset + 2];
                    var a = row[offset + 3];

                    sampleCount++;
                    if (a < 16)
                    {
                        darkCount++;
                        continue;
                    }

                    var luma = r + g + b;
                    totalLuma += luma;
                    if (luma < 8)
                    {
                        darkCount++;
                    }

                    if (!firstSet)
                    {
                        firstSet = true;
                        firstR = r;
                        firstG = g;
                        firstB = b;
                        continue;
                    }

                    if (Math.Abs(r - firstR) < 3 && Math.Abs(g - firstG) < 3 && Math.Abs(b - firstB) < 3)
                    {
                        sameColorCount++;
                    }
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        if (sampleCount == 0)
        {
            return true;
        }

        var avgLuma = (double)totalLuma / sampleCount;
        var sameRatio = (double)sameColorCount / sampleCount;
        var darkRatio = (double)darkCount / sampleCount;
        return avgLuma < 12 && sameRatio > 0.98 && darkRatio > 0.96;
    }

    private sealed class PinnedByteArray : IDisposable
    {
        private GCHandle _handle;

        public PinnedByteArray(byte[] bytes)
        {
            _handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        }

        public IntPtr Pointer => _handle.AddrOfPinnedObject();

        public void Dispose()
        {
            if (_handle.IsAllocated)
            {
                _handle.Free();
            }
        }
    }
}
