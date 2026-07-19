namespace ScreenshotCat.Models;

public sealed record AnnotationSessionSaveResult(
    IReadOnlyList<string> ImagePaths,
    string SummaryText,
    string TextPath);
