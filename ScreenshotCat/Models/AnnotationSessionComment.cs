using Windows.Foundation;

namespace ScreenshotCat.Models;

public sealed record AnnotationSessionComment(int Index, string Text, Point Anchor, Rect Selection);
