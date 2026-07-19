using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using ScreenshotCat.Models;
using System.Globalization;
using System.Text;

namespace ScreenshotCat.Services;

public sealed class ScreenshotSaveService
{
    private readonly string _outputDirectory;

    public ScreenshotSaveService(string? outputDirectory = null)
    {
        _outputDirectory = outputDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ScreenshotCat");
        Directory.CreateDirectory(_outputDirectory);
    }

    public SaveResult Save(
        CaptureResult capture,
        Rectangle selectionScreenRect,
        IReadOnlyList<Annotation> annotations,
        IReadOnlyList<MarkerNote> markerNotes)
    {
        var selection = Rectangle.Intersect(
            new Rectangle(
                selectionScreenRect.Left - capture.MonitorBounds.Left,
                selectionScreenRect.Top - capture.MonitorBounds.Top,
                selectionScreenRect.Width,
                selectionScreenRect.Height),
            new Rectangle(0, 0, capture.Bitmap.Width, capture.Bitmap.Height));

        if (selection.Width <= 0 || selection.Height <= 0)
        {
            selection = new Rectangle(0, 0, capture.Bitmap.Width, capture.Bitmap.Height);
        }

        using var cropped = capture.Bitmap.Clone(selection, PixelFormat.Format32bppPArgb);
        var cropScreenOrigin = new Point(
            capture.MonitorBounds.Left + selection.Left,
            capture.MonitorBounds.Top + selection.Top);
        using (var graphics = Graphics.FromImage(cropped))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (var annotation in annotations)
            {
                DrawAnnotation(graphics, annotation, cropScreenOrigin);
            }

            DrawMarkerNotes(graphics, markerNotes, cropScreenOrigin);
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var imagePath = Path.Combine(_outputDirectory, $"shot-{stamp}-{Guid.NewGuid().ToString("N")[..6]}.png");
        cropped.Save(imagePath, ImageFormat.Png);

        var noteText = ComposeMarkerNotes(markerNotes);

        var logPath = Path.Combine(_outputDirectory, "annotations.txt");
        File.AppendAllText(logPath, $"{imagePath} 批注:{noteText}{Environment.NewLine}");
        return new SaveResult(imagePath, noteText, logPath);
    }

    private static void DrawAnnotation(Graphics graphics, Annotation annotation, Point cropScreenOrigin)
    {
        var start = new PointF(
            annotation.Start.X - cropScreenOrigin.X,
            annotation.Start.Y - cropScreenOrigin.Y);
        var end = new PointF(
            annotation.End.X - cropScreenOrigin.X,
            annotation.End.Y - cropScreenOrigin.Y);

        var annotationColor = Color.FromArgb(annotation.ColorArgb);
        if (annotation.Kind == AnnotationKind.Arrow)
        {
            using var pen = new Pen(annotationColor, 5)
            {
                StartCap = LineCap.Round
            };
            using var cap = new AdjustableArrowCap(6, 8, true);
            pen.CustomEndCap = cap;
            graphics.DrawLine(pen, start, end);
            return;
        }

        using var markerPen = new Pen(annotationColor, 4);
        using var markerBrush = new SolidBrush(Color.FromArgb(52, annotationColor.R, annotationColor.G, annotationColor.B));
        var rect = RectangleF.FromLTRB(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), Math.Max(start.X, end.X), Math.Max(start.Y, end.Y));
        graphics.FillRectangle(markerBrush, rect);
        graphics.DrawRectangle(markerPen, rect.X, rect.Y, rect.Width, rect.Height);
    }

    private static void DrawMarkerNotes(
        Graphics graphics,
        IReadOnlyList<MarkerNote> markerNotes,
        Point cropScreenOrigin)
    {
        if (markerNotes.Count == 0)
        {
            return;
        }

        using var numberBrush = new SolidBrush(Color.FromArgb(255, 24, 144, 255));
        using var backgroundBrush = new SolidBrush(Color.FromArgb(210, 255, 255, 255));
        using var borderBrush = new Pen(Color.FromArgb(255, 24, 144, 255), 2);
        using var font = new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel);

        foreach (var note in markerNotes)
        {
            var position = new PointF(
                note.Position.X - cropScreenOrigin.X,
                note.Position.Y - cropScreenOrigin.Y);

            var label = note.Index.ToString(CultureInfo.InvariantCulture);
            var textSize = graphics.MeasureString(label, font);
            var size = Math.Max(26, Math.Max((int)textSize.Width, (int)textSize.Height)) + 2;
            var radius = (float)(size / 2.0);
            var rect = new RectangleF(position.X - radius, position.Y - radius, size, size);

            graphics.FillEllipse(backgroundBrush, rect);
            graphics.DrawEllipse(borderBrush, rect);
            graphics.DrawString(
                label,
                font,
                numberBrush,
                rect.X + (rect.Width - textSize.Width) / 2,
                rect.Y + (rect.Height - textSize.Height) / 2);
        }
    }

    private static string ComposeMarkerNotes(IReadOnlyList<MarkerNote> markerNotes)
    {
        if (markerNotes.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < markerNotes.Count; i++)
        {
            var note = markerNotes[i];
            builder.Append(i == 0 ? string.Empty : Environment.NewLine);
            builder.Append(note.Index.ToString(CultureInfo.InvariantCulture));
            builder.Append(". ");
            builder.Append(note.Text.Trim());
        }

        return builder.ToString();
    }
}
