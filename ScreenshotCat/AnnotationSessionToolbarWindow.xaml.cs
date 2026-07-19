using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using ScreenshotCat.Interop;
using ScreenshotCat.Services;
using Windows.Graphics;
using Windows.UI;
using WinRT;
using WinRT.Interop;

namespace ScreenshotCat;

public sealed partial class AnnotationSessionToolbarWindow : Window
{
    private const int ToolbarHeightDip = 40;

    private readonly nint _targetHwnd;
    private readonly nint _annotationHwnd;
    private readonly Action _onCancel;
    private readonly Action _onClear;
    private readonly Action _onFinish;
    private readonly Action<bool> _onInteractionModeChanged;
    private readonly DispatcherTimer _followTimer = new();
    private readonly TargetWindowTracker _targetWindowTracker;
    private AppWindow? _appWindow;
    private DesktopAcrylicController? _acrylicController;
    private SystemBackdropConfiguration? _backdropConfiguration;
    private NativeMethods.POINT _dragStartCursor;
    private NativeMethods.RECT _dragStartTargetRect;
    private bool _isDraggingTarget;
    private NativeMethods.RECT? _lastTargetRect;
    private int _lastToolbarY = int.MinValue;
    private int _lastToolbarHeight;
    private bool _isShown;

    public AnnotationSessionToolbarWindow(
        nint targetHwnd,
        nint annotationHwnd,
        Action onCancel,
        Action onClear,
        Action onFinish,
        Action<bool> onInteractionModeChanged)
    {
        _targetHwnd = targetHwnd;
        _annotationHwnd = annotationHwnd;
        _onCancel = onCancel;
        _onClear = onClear;
        _onFinish = onFinish;
        _onInteractionModeChanged = onInteractionModeChanged;

        InitializeComponent();
        ConfigureWindow();
        _targetWindowTracker = new TargetWindowTracker(
            targetHwnd,
            DispatcherQueue,
            () => FollowTarget());
        Activated += AnnotationSessionToolbarWindow_Activated;
        Closed += AnnotationSessionToolbarWindow_Closed;
        _followTimer.Interval = TimeSpan.FromMilliseconds(250);
        _followTimer.Tick += (_, _) => FollowTarget();
    }

    public void StartFollowing()
    {
        if (!FollowTarget())
        {
            return;
        }

        _followTimer.Start();
    }

    public void SetCommentCount(int count)
    {
        StatusText.Text = count == 0 ? "正在批注" : $"正在批注 · {count} 条";
        FinishText.Text = "保存";
        FinishButton.IsEnabled = count > 0;
    }

    public void SetSavingState(bool isSaving, string? message = null)
    {
        StatusText.Text = message ?? (isSaving ? "正在保存批注…" : "正在批注");
        FinishButton.IsEnabled = !isSaving;
        FinishText.Text = isSaving ? "保存中" : "保存";
    }

    private void ConfigureWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.IsShownInSwitchers = false;
        _appWindow.Hide();
        _isShown = false;
        NativeMethods.ConfigureBorderlessToolWindow(hwnd, _annotationHwnd);
        NativeMethods.DisableDwmBorder(hwnd);
        ConfigureAcrylic();
    }

    private void ConfigureAcrylic()
    {
        if (!DesktopAcrylicController.IsSupported())
        {
            return;
        }

        _backdropConfiguration = new SystemBackdropConfiguration
        {
            IsInputActive = true,
            Theme = SystemBackdropTheme.Default
        };
        _acrylicController = new DesktopAcrylicController
        {
            Kind = DesktopAcrylicKind.Thin
        };
        _acrylicController.SetSystemBackdropConfiguration(_backdropConfiguration);
        _ = _acrylicController.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
    }

    private bool FollowTarget()
    {
        if (_targetHwnd == 0 || !NativeMethods.IsWindow(_targetHwnd))
        {
            Close();
            return false;
        }

        if (!NativeMethods.IsWindowVisible(_targetHwnd)
            || NativeMethods.IsIconic(_targetHwnd)
            || !NativeMethods.GetWindowRect(_targetHwnd, out var rect))
        {
            HideToolbar();
            return true;
        }

        var width = Math.Max(0, rect.Right - rect.Left);
        if (width < 240)
        {
            Close();
            return false;
        }

        var hwnd = WindowNative.GetWindowHandle(this);
        var foreground = NativeMethods.GetForegroundWindow();
        var foregroundRoot = NativeMethods.GetAncestor(foreground, NativeMethods.GaRoot);
        if (foreground != _targetHwnd
            && foregroundRoot != _targetHwnd
            && foreground != _annotationHwnd
            && foregroundRoot != _annotationHwnd)
        {
            HideToolbar();
            return true;
        }

        var monitorPoint = new NativeMethods.POINT { X = rect.Left, Y = rect.Top };
        var monitor = NativeMethods.MonitorFromPoint(monitorPoint, NativeMethods.MonitorDefaultToNearest);
        var monitorInfo = new NativeMethods.MONITORINFO
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>()
        };
        var workTop = NativeMethods.GetMonitorInfo(monitor, ref monitorInfo)
            ? monitorInfo.rcWork.Top
            : 0;
        var toolbarHeight = NativeMethods.DipToPhysicalPixels(hwnd, ToolbarHeightDip);
        var y = rect.Top - toolbarHeight;
        if (y < workTop)
        {
            y = rect.Top;
        }

        var currentBoundsMatch = NativeMethods.GetWindowRect(hwnd, out var currentRect)
            && currentRect.Left == rect.Left
            && currentRect.Top == y
            && currentRect.Right - currentRect.Left == width
            && currentRect.Bottom - currentRect.Top == toolbarHeight;
        var boundsChanged = !currentBoundsMatch
            || !_lastTargetRect.HasValue
            || _lastTargetRect.Value.Left != rect.Left
            || _lastTargetRect.Value.Top != rect.Top
            || _lastTargetRect.Value.Right != rect.Right
            || _lastTargetRect.Value.Bottom != rect.Bottom
            || _lastToolbarY != y
            || _lastToolbarHeight != toolbarHeight;
        if (boundsChanged)
        {
            _appWindow?.MoveAndResize(new RectInt32(rect.Left, y, width, toolbarHeight));
            var settledToolbarHeight = NativeMethods.DipToPhysicalPixels(hwnd, ToolbarHeightDip);
            if (settledToolbarHeight != toolbarHeight)
            {
                toolbarHeight = settledToolbarHeight;
                y = rect.Top - toolbarHeight;
                if (y < workTop)
                {
                    y = rect.Top;
                }
                _appWindow?.MoveAndResize(new RectInt32(rect.Left, y, width, toolbarHeight));
            }
            _lastTargetRect = rect;
            _lastToolbarY = y;
            _lastToolbarHeight = toolbarHeight;
        }

        if (!_isShown)
        {
            _appWindow?.Show(activateWindow: false);
            _isShown = true;
        }
        return true;
    }

    private void HideToolbar()
    {
        if (!_isShown)
        {
            return;
        }

        _appWindow?.Hide();
        _isShown = false;
    }

    private void AnnotationSessionToolbarWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (_backdropConfiguration is not null)
        {
            _backdropConfiguration.IsInputActive = args.WindowActivationState != WindowActivationState.Deactivated;
        }

        NativeMethods.DisableDwmBorder(WindowNative.GetWindowHandle(this));
    }

    private void DragRegion_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(DragRegion);
        if (!point.Properties.IsLeftButtonPressed
            || !NativeMethods.GetCursorPos(out _dragStartCursor)
            || !NativeMethods.GetWindowRect(_targetHwnd, out _dragStartTargetRect))
        {
            return;
        }

        _isDraggingTarget = true;
        DragRegion.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void DragRegion_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDraggingTarget
            || !e.GetCurrentPoint(DragRegion).Properties.IsLeftButtonPressed
            || !NativeMethods.GetCursorPos(out var cursor))
        {
            return;
        }

        var x = _dragStartTargetRect.Left + cursor.X - _dragStartCursor.X;
        var y = _dragStartTargetRect.Top + cursor.Y - _dragStartCursor.Y;
        NativeMethods.SetWindowPos(
            _targetHwnd,
            0,
            x,
            y,
            0,
            0,
            NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
        FollowTarget();
        e.Handled = true;
    }

    private void DragRegion_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDraggingTarget)
        {
            return;
        }

        _isDraggingTarget = false;
        DragRegion.ReleasePointerCaptures();
        e.Handled = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _onCancel();

    private void ClearButton_Click(object sender, RoutedEventArgs e) => _onClear();

    private void FinishButton_Click(object sender, RoutedEventArgs e) => _onFinish();

    private void InteractionToggle_Click(object sender, RoutedEventArgs e)
    {
        var enabled = InteractionToggle.IsChecked == true;
        InteractionToggle.Content = enabled ? "继续批注" : "操作窗口";
        _onInteractionModeChanged(enabled);
    }

    private void LocateButton_Click(object sender, RoutedEventArgs e)
    {
        NativeMethods.BringWindowToTop(_targetHwnd);
        NativeMethods.SetForegroundWindow(_targetHwnd);
    }

    private void AnnotationSessionToolbarWindow_Closed(object sender, WindowEventArgs args)
    {
        _followTimer.Stop();
        _targetWindowTracker.Dispose();
        _isDraggingTarget = false;
        _acrylicController?.Dispose();
        _acrylicController = null;
        _backdropConfiguration = null;
    }
}
