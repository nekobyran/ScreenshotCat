using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ScreenshotCat.Models;

namespace ScreenshotCat;

public sealed partial class MainPage : Page
{
    private MainWindow? _mainWindow;

    public MainPage()
    {
        InitializeComponent();
        Loaded += MainPage_Loaded;
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        _mainWindow = ((App)Application.Current).MainWindow;
        if (_mainWindow is null)
        {
            return;
        }

        var hotkeyStatus = _mainWindow.HotkeyRegistered
            ? "截图键 Scroll Lock 与 Ctrl+Alt+N 已启用。"
            : "截图键注册失败，可能已被其他应用占用。";
        var startupStatus = _mainWindow.StartupRegistered
            ? "开机自启已启用。"
            : "开机自启注册失败。";
        HotkeyText.Text = $"{hotkeyStatus} {startupStatus}";
        _mainWindow.ScreenshotSaved += MainWindow_ScreenshotSaved;
    }

    private void MainWindow_ScreenshotSaved(object? sender, SaveResult result)
    {
        CopyButton.IsEnabled = true;
        var status = _mainWindow?.LastSaveCopiedToClipboard == true
            ? "已保存并复制"
            : "已保存；剪贴板暂时不可用，可点击复制重试";
        StatusText.Text = $"{status}: {result.ImagePath}\n批注: {result.AnnotationText}\n记录: {result.LogPath}";
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow?.StartCapture();
    }

    private void HideButton_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow?.HideToBackground();
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow?.ExitApplication();
    }

    private async void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mainWindow is null)
        {
            return;
        }

        var copied = await _mainWindow.CopyLastAsync();
        if (_mainWindow.LastSave is not null)
        {
            var status = copied ? "已复制" : "复制失败，请稍后重试";
            StatusText.Text = $"{status}: {_mainWindow.LastSave.ImagePath}\n批注: {_mainWindow.LastSave.AnnotationText}\n记录: {_mainWindow.LastSave.LogPath}";
        }
    }

    private void GraphicToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_mainWindow is not null)
        {
            _mainWindow.AutoGraphicRecognition = GraphicToggle.IsOn;
        }
    }
}
