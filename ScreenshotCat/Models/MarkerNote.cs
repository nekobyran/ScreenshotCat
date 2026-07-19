using System.Drawing;

namespace ScreenshotCat.Models;

public sealed record MarkerNote(int Index, Point Position, string Text);
