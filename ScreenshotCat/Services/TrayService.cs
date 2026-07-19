using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ScreenshotCat.Services;

public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;

    public event EventHandler? ShowRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? CaptureRequested;

    public TrayService(string iconPath)
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = LoadIcon(iconPath),
            Text = "ScreenshotCat",
            Visible = true,
            ContextMenuStrip = BuildContextMenu()
        };

        _notifyIcon.DoubleClick += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    public void SetToolTip(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            _notifyIcon.Text = text;
        }
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开界面", null, (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("立即截图", null, (_, _) => CaptureRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        return menu;
    }

    private static Icon LoadIcon(string iconPath)
    {
        try
        {
            if (File.Exists(iconPath))
            {
                return new Icon(iconPath);
            }
        }
        catch
        {
        }

        return SystemIcons.Application;
    }
}
