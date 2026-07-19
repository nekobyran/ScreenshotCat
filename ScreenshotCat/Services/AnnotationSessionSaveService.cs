using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text;
using ScreenshotCat.Models;

namespace ScreenshotCat.Services;

public sealed class AnnotationSessionSaveService
{
    private readonly string _outputDirectory;

    public AnnotationSessionSaveService(string? outputDirectory = null)
    {
        _outputDirectory = outputDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "ScreenshotCat");
        Directory.CreateDirectory(_outputDirectory);
    }

    public AnnotationSessionSaveResult Save(
        string snapshotPath,
        IReadOnlyList<AnnotationSessionComment> comments)
    {
        if (!File.Exists(snapshotPath))
        {
            throw new FileNotFoundException("批注底图不存在。", snapshotPath);
        }

        if (comments.Count == 0)
        {
            throw new InvalidOperationException("请先添加至少一条批注。");
        }

        var sessionName = $"annotation-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var sessionDirectory = Path.Combine(_outputDirectory, sessionName);
        Directory.CreateDirectory(sessionDirectory);

        var imagePaths = new List<string>(comments.Count);
        using var source = new Bitmap(snapshotPath);
        foreach (var comment in comments)
        {
            using var annotated = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(annotated))
            {
                graphics.DrawImageUnscaled(source, 0, 0);
                DrawComment(graphics, comment, annotated.Size);
            }

            var imagePath = Path.Combine(
                sessionDirectory,
                $"annotation-{comment.Index.ToString("D2", CultureInfo.InvariantCulture)}.png");
            annotated.Save(imagePath, ImageFormat.Png);
            imagePaths.Add(imagePath);
        }

        var summary = ComposeSummary(imagePaths, comments);
        var textPath = Path.Combine(sessionDirectory, "annotations.txt");
        File.WriteAllText(textPath, summary, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return new AnnotationSessionSaveResult(imagePaths, summary, textPath);
    }

    private static void DrawComment(Graphics graphics, AnnotationSessionComment comment, Size imageSize)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var selection = new RectangleF(
            (float)Math.Clamp(comment.Selection.X, 0, imageSize.Width),
            (float)Math.Clamp(comment.Selection.Y, 0, imageSize.Height),
            (float)Math.Clamp(comment.Selection.Width, 1, imageSize.Width),
            (float)Math.Clamp(comment.Selection.Height, 1, imageSize.Height));
        selection.Width = Math.Min(selection.Width, imageSize.Width - selection.X);
        selection.Height = Math.Min(selection.Height, imageSize.Height - selection.Y);

        var blue = Color.FromArgb(255, 22, 139, 250);
        using var selectionFill = new SolidBrush(Color.FromArgb(58, blue));
        using var selectionPen = new Pen(blue, 4);
        graphics.FillRectangle(selectionFill, selection);
        graphics.DrawRectangle(selectionPen, selection.X, selection.Y, selection.Width, selection.Height);

        const float badgeSize = 34;
        var badgeX = (float)Math.Clamp(comment.Anchor.X - badgeSize / 2, 0, imageSize.Width - badgeSize);
        var badgeY = (float)Math.Clamp(comment.Anchor.Y - badgeSize / 2, 0, imageSize.Height - badgeSize);
        var badge = new RectangleF(badgeX, badgeY, badgeSize, badgeSize);
        using var badgeBrush = new SolidBrush(blue);
        using var badgeBorder = new Pen(Color.White, 2);
        graphics.FillEllipse(badgeBrush, badge);
        graphics.DrawEllipse(badgeBorder, badge);

        var tail = new[]
        {
            new PointF(badgeX + badgeSize * 0.36f, badgeY + badgeSize - 3),
            new PointF(badgeX + badgeSize * 0.64f, badgeY + badgeSize - 3),
            new PointF(badgeX + badgeSize * 0.50f, badgeY + badgeSize + 7)
        };
        graphics.FillPolygon(badgeBrush, tail);

        using var font = new Font("Segoe UI", 15, FontStyle.Bold, GraphicsUnit.Pixel);
        using var textFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        graphics.DrawString(
            comment.Index.ToString(CultureInfo.InvariantCulture),
            font,
            Brushes.White,
            badge,
            textFormat);
    }

    private static string ComposeSummary(
        IReadOnlyList<string> imagePaths,
        IReadOnlyList<AnnotationSessionComment> comments)
    {
        var builder = new StringBuilder();
        builder.AppendLine("图片路径：");
        for (var index = 0; index < imagePaths.Count; index++)
        {
            builder.Append(index + 1);
            builder.Append('：');
            builder.AppendLine(imagePaths[index]);
        }

        builder.AppendLine();
        builder.AppendLine("批注内容：");
        foreach (var comment in comments)
        {
            builder.Append(comment.Index);
            builder.Append('：');
            builder.AppendLine(comment.Text.Trim());
        }

        return builder.ToString().TrimEnd();
    }
}
