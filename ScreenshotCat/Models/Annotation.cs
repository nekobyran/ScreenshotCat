using System.Drawing;

namespace ScreenshotCat.Models;

public enum AnnotationKind
{
    Arrow,
    Marker
}

public sealed record Annotation(AnnotationKind Kind, PointF Start, PointF End, int ColorArgb);
