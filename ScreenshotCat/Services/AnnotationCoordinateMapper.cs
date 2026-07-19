using System.Drawing;

namespace ScreenshotCat.Services;

public static class AnnotationCoordinateMapper
{
    public static RectangleF ScaleRectangle(
        RectangleF value,
        double sourceWidth,
        double sourceHeight,
        double targetWidth,
        double targetHeight)
    {
        var scaleX = targetWidth / Math.Max(1.0, sourceWidth);
        var scaleY = targetHeight / Math.Max(1.0, sourceHeight);
        return new RectangleF(
            (float)(value.X * scaleX),
            (float)(value.Y * scaleY),
            (float)(value.Width * scaleX),
            (float)(value.Height * scaleY));
    }

    public static PointF ScalePoint(
        PointF value,
        double sourceWidth,
        double sourceHeight,
        double targetWidth,
        double targetHeight)
    {
        var scaleX = targetWidth / Math.Max(1.0, sourceWidth);
        var scaleY = targetHeight / Math.Max(1.0, sourceHeight);
        return new PointF(
            (float)(value.X * scaleX),
            (float)(value.Y * scaleY));
    }
}
