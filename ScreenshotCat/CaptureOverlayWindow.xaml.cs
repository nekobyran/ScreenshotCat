using System.Globalization;
using Windows.UI;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ScreenshotCat.Interop;
using ScreenshotCat.Models;
using ScreenshotCat.Services;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;
using DPoint = System.Drawing.Point;
using DRectangle = System.Drawing.Rectangle;
using WPoint = Windows.Foundation.Point;

namespace ScreenshotCat;

public sealed partial class CaptureOverlayWindow : Window
{
    private const nuint OverlaySubclassId = 3401;
    private static readonly Color[] AnnotationPalette =
    [
        Color.FromArgb(245, 255, 214, 64),
        Color.FromArgb(245, 46, 204, 113),
        Color.FromArgb(245, 255, 112, 67),
        Color.FromArgb(245, 171, 71, 188),
        Color.FromArgb(245, 38, 198, 218)
    ];

    private readonly CaptureResult _capture;
    private readonly bool _autoGraphicRecognition;
    private readonly Func<SaveResult, Task> _onSavedAsync;
    private readonly Action<nint>? _onPersistentWindowRequested;
    private readonly DPoint _initialCursorPoint;
    private readonly NativeMethods.SubclassProc _overlaySubclassProc;
    private readonly SelectionDetectionService _selectionDetection = new();
    private readonly ScreenshotSaveService _saveService = new();
    private readonly List<Annotation> _annotations = [];
    private readonly List<MarkerNote> _markerNotes = [];
    private readonly DispatcherTimer _longPressTimer = new();
    private readonly DispatcherTimer _rightLongPressTimer = new();
    private DRectangle? _selectionScreenRect;
    private DRectangle? _lastAnnotationScreenRect;
    private WPoint _dragStartDip;
    private WPoint _currentDip;
    private WPoint _rightPressStartDip;
    private bool _isDragging;
    private bool _isRightPressing;
    private WPoint? _noteAnchorDip;
    private int _annotationColorIndex;
    private nint _hwnd;
    private bool _subclassed;
    private bool _wrappedInteractionMode;
    private bool _annotationMode;
    private bool _disposed;
    private AppWindow? _appWindow;

    public CaptureOverlayWindow(
        CaptureResult capture,
        bool autoGraphicRecognition,
        Func<SaveResult, Task> onSavedAsync,
        DPoint initialCursorPoint,
        DRectangle? initialSelectionScreenRect,
        Action<nint>? onPersistentWindowRequested = null,
        bool startInAnnotationMode = false)
    {
        _capture = capture;
        _autoGraphicRecognition = autoGraphicRecognition;
        _onSavedAsync = onSavedAsync;
        _onPersistentWindowRequested = onPersistentWindowRequested;
        _initialCursorPoint = initialCursorPoint;
        _selectionScreenRect = ClampSelectionToMonitor(initialSelectionScreenRect);
        // A detected window is already a stable capture target. Let the first drag
        // inside it create an annotation instead of forcing a second mode switch.
        _annotationMode = _selectionScreenRect.HasValue || startInAnnotationMode;
        _overlaySubclassProc = OverlayWndProc;

        InitializeComponent();
        ConfigureWindow();
        UpdateAnnotationModeUi();
        ScreenPreview.Source = new BitmapImage(new Uri(_capture.PreviewPath));
        Root.Loaded += Root_Loaded;
        Activated += (_, _) => EnsureOverlayBounds();
        Closed += CaptureOverlayWindow_Closed;

        _longPressTimer.Interval = TimeSpan.FromMilliseconds(520);
        _longPressTimer.Tick += LongPressTimer_Tick;
        _rightLongPressTimer.Interval = TimeSpan.FromMilliseconds(520);
        _rightLongPressTimer.Tick += RightLongPressTimer_Tick;
    }

    private void ConfigureWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        _hwnd = hwnd;
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow = appWindow;
        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsAlwaysOnTop = true;
        appWindow.SetPresenter(presenter);
        appWindow.MoveAndResize(new RectInt32(
            _capture.MonitorBounds.Left,
            _capture.MonitorBounds.Top,
            _capture.MonitorBounds.Width,
            _capture.MonitorBounds.Height));
        NativeMethods.DisableDwmBorder(hwnd);
        NativeMethods.ApplyBorderlessRegion(
            hwnd,
            _capture.MonitorBounds.Width,
            _capture.MonitorBounds.Height,
            inset: 1);
        _subclassed = NativeMethods.SetWindowSubclass(hwnd, _overlaySubclassProc, OverlaySubclassId, 0);
    }

    private void Root_Loaded(object sender, RoutedEventArgs e)
    {
        EnsureOverlayBounds();
        var hwnd = WindowNative.GetWindowHandle(this);
        NativeMethods.BringWindowToTop(hwnd);
        NativeMethods.SetForegroundWindow(hwnd);
        NativeMethods.DisableDwmBorder(hwnd);
        Root.Focus(FocusState.Programmatic);
        UpdateSelectionVisual();
    }

    private void EnsureOverlayBounds()
    {
        if (_appWindow is null)
        {
            return;
        }

        var hwnd = WindowNative.GetWindowHandle(this);
        var expected = _capture.MonitorBounds;
        if (NativeMethods.GetWindowRect(hwnd, out var current)
            && current.Left == expected.Left
            && current.Top == expected.Top
            && current.Right - current.Left == expected.Width
            && current.Bottom - current.Top == expected.Height)
        {
            return;
        }

        _appWindow.MoveAndResize(new RectInt32(
            expected.Left,
            expected.Top,
            expected.Width,
            expected.Height));
    }

    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Root.Focus(FocusState.Pointer);
        var point = e.GetCurrentPoint(Root);
        if (point.Properties.IsRightButtonPressed)
        {
            if (_annotationMode && CanAnnotateAtDip(point.Position))
            {
                ShowNotePanel(point.Position);
                e.Handled = true;
                return;
            }

            _rightPressStartDip = point.Position;
            _isRightPressing = true;
            _rightLongPressTimer.Start();
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (NotePanel.Visibility == Visibility.Visible)
        {
            HideNotePanel();
        }

        _dragStartDip = point.Position;
        _currentDip = _dragStartDip;
        _isDragging = true;
        e.Handled = true;
    }

    private void Root_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Root);
        if (_isRightPressing)
        {
            if (!point.Properties.IsRightButtonPressed || Distance(_rightPressStartDip, point.Position) > 6)
            {
                _rightLongPressTimer.Stop();
            }

            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _currentDip = point.Position;
        if (Distance(_dragStartDip, _currentDip) > 6)
        {
            _longPressTimer.Stop();
        }

        if (_isDragging && !IsNestedAnnotationDrag(_dragStartDip))
        {
            SetSelectionFromDip(_dragStartDip, _currentDip);
        }

        e.Handled = true;
    }

    private void Root_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _longPressTimer.Stop();
        _rightLongPressTimer.Stop();
        if (_isRightPressing)
        {
            _isRightPressing = false;
            e.Handled = true;
            return;
        }

        _currentDip = e.GetCurrentPoint(Root).Position;

        var movedDistance = Distance(_dragStartDip, _currentDip);
        if (_isDragging && movedDistance > 6)
        {
            if (!TryAddNestedAnnotation(_dragStartDip, _currentDip))
            {
                SetSelectionFromDip(_dragStartDip, _currentDip);
                _lastAnnotationScreenRect = null;
            }
        }
        else if (_isDragging)
        {
            SelectWindowAtDip(_currentDip);
        }

        _isDragging = false;
        e.Handled = true;
    }

    private void LongPressTimer_Tick(object? sender, object e)
    {
        _longPressTimer.Stop();
        var screenPoint = DipToScreenPoint(_dragStartDip);
        var detectedWindow = _selectionDetection.DetectTopLevelWindowAtPoint(
            screenPoint,
            _capture.MonitorBounds,
            Environment.ProcessId);
        if (detectedWindow.HasValue)
        {
            _selectionScreenRect = detectedWindow.Value;
            EnterWrappedInteractionMode();
            UpdateSelectionVisual();
            _isDragging = false;
            return;
        }

        DRectangle? detected = null;
        if (_autoGraphicRecognition)
        {
            detected = _selectionDetection.DetectGraphicCard(_capture, screenPoint);
        }
        if (detected.HasValue)
        {
            _selectionScreenRect = detected.Value;
            UpdateSelectionVisual();
            _isDragging = false;
        }
    }

    private void RightLongPressTimer_Tick(object? sender, object e)
    {
        _rightLongPressTimer.Stop();
        if (!_isRightPressing)
        {
            return;
        }

        EnterPersistentAnnotationWindowAtDip(_rightPressStartDip);
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            Close();
            return;
        }

        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        if (NotePanel.Visibility == Visibility.Visible)
        {
            SaveCurrentMarker();
            e.Handled = true;
            return;
        }

        _ = SaveAndCloseAsync();
        e.Handled = true;
    }

    private void NoteBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            SaveCurrentMarker();
            e.Handled = true;
        }
    }

    private void AnnotationModeButton_Click(object sender, RoutedEventArgs e)
    {
        SetAnnotationMode(!_annotationMode);
    }

    private void SaveCurrentMarker()
    {
        if (_noteAnchorDip is null)
        {
            return;
        }

        var text = NoteBox.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            HideNotePanel();
            return;
        }

        var markerIndex = _markerNotes.Count + 1;
        var screenPoint = DipToScreenPoint(_noteAnchorDip.Value);
        _markerNotes.Add(new MarkerNote(markerIndex, screenPoint, text));

        DrawMarkerBadge(_noteAnchorDip.Value, markerIndex);
        RefreshMarkerList();
        HideNotePanel();
    }

    private void DrawMarkerBadge(WPoint pointDip, int index)
    {
        var badge = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            Background = new SolidColorBrush(Color.FromArgb(240, 3, 169, 244)),
            BorderBrush = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = index.ToString(CultureInfo.InvariantCulture),
                Foreground = new SolidColorBrush(Colors.White),
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            IsHitTestVisible = false
        };

        var left = Math.Clamp(pointDip.X - 13, 0, Math.Max(0, Root.ActualWidth - badge.Width));
        var top = Math.Clamp(pointDip.Y - 13, 0, Math.Max(0, Root.ActualHeight - badge.Height));
        Canvas.SetLeft(badge, left);
        Canvas.SetTop(badge, top);
        AnnotationCanvas.Children.Add(badge);
    }

    private void RefreshMarkerList()
    {
        SavedNotesList.Children.Clear();
        if (_markerNotes.Count == 0)
        {
            SavedNotesPanel.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (var marker in _markerNotes)
        {
            SavedNotesList.Children.Add(new TextBlock
            {
                Text = $"{marker.Index}. {marker.Text}",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Foreground = new SolidColorBrush(Colors.White),
                Width = 280
            });
        }

        SavedNotesPanel.Visibility = Visibility.Visible;
        SavedNotesScroller.ChangeView(null, SavedNotesScroller.ScrollableHeight, null);
    }

    private async Task SaveAndCloseAsync()
    {
        if (!_selectionScreenRect.HasValue)
        {
            _selectionScreenRect = _capture.MonitorBounds;
            UpdateSelectionVisual();
        }

        var selection = _selectionScreenRect.Value;
        var result = _saveService.Save(_capture, selection, _annotations, _markerNotes);
        await _onSavedAsync(result);
        Close();
    }

    private void ShowNotePanel(WPoint anchor)
    {
        var x = Math.Clamp(anchor.X - 210, 12, Math.Max(12, Root.ActualWidth - 420 - 12));
        var y = Math.Clamp(anchor.Y - 18, 12, Math.Max(12, Root.ActualHeight - 104 - 12));
        NotePanel.Margin = new Thickness(x, y, 0, 0);
        NotePanel.Visibility = Visibility.Visible;
        NoteBox.Text = string.Empty;
        _noteAnchorDip = anchor;
        NoteBox.Focus(FocusState.Programmatic);
    }

    private void HideNotePanel()
    {
        NotePanel.Visibility = Visibility.Collapsed;
        NoteBox.Text = string.Empty;
        _noteAnchorDip = null;
    }

    private void SetSelectionFromDip(WPoint start, WPoint end)
    {
        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        var right = Math.Max(start.X, end.X);
        var bottom = Math.Max(start.Y, end.Y);
        _selectionScreenRect = DipRectToScreen(left, top, right - left, bottom - top);
        UpdateSelectionVisual();
    }

    private bool TryAddNestedAnnotation(WPoint start, WPoint end)
    {
        if (!_selectionScreenRect.HasValue || !IsNestedAnnotationDrag(start) || !IsDipPointInsideSelection(end))
        {
            return false;
        }

        var parentSelection = _selectionScreenRect.Value;
        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        var right = Math.Max(start.X, end.X);
        var bottom = Math.Max(start.Y, end.Y);
        var annotationRect = DipRectToScreen(left, top, right - left, bottom - top);
        var clamped = DRectangle.Intersect(annotationRect, parentSelection);
        if (clamped.Width < 12 || clamped.Height < 12)
        {
            return false;
        }

        var color = AnnotationPalette[_annotationColorIndex % AnnotationPalette.Length];
        _annotationColorIndex++;
        _annotations.Add(new Annotation(
            AnnotationKind.Marker,
            new System.Drawing.PointF(clamped.Left, clamped.Top),
            new System.Drawing.PointF(clamped.Right, clamped.Bottom),
            ToArgb(color)));
        _lastAnnotationScreenRect = clamped;
        DrawAnnotationRect(clamped, color);
        return true;
    }

    private void DrawAnnotationRect(DRectangle screenRect, Color color)
    {
        var rect = ScreenRectToDip(screenRect);
        var shape = new Microsoft.UI.Xaml.Shapes.Rectangle
        {
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 4,
            Fill = new SolidColorBrush(Color.FromArgb(44, color.R, color.G, color.B)),
            IsHitTestVisible = false
        };
        SetRect(shape, rect.X, rect.Y, rect.Width, rect.Height);
        AnnotationCanvas.Children.Add(shape);
    }

    private bool IsDipPointInsideSelection(WPoint point)
    {
        if (!_selectionScreenRect.HasValue)
        {
            return false;
        }

        return _selectionScreenRect.Value.Contains(DipToScreenPoint(point));
    }

    private bool IsNestedAnnotationDrag(WPoint point)
    {
        return _annotationMode && _selectionScreenRect.HasValue && IsDipPointInsideSelection(point);
    }

    private bool CanAnnotateAtDip(WPoint point)
    {
        if (!_annotationMode || !_selectionScreenRect.HasValue)
        {
            return false;
        }

        var screenPoint = DipToScreenPoint(point);
        if (!_selectionScreenRect.Value.Contains(screenPoint))
        {
            return false;
        }

        foreach (var annotation in _annotations)
        {
            if (annotation.Kind != AnnotationKind.Marker)
            {
                continue;
            }

            var left = Math.Min(annotation.Start.X, annotation.End.X);
            var top = Math.Min(annotation.Start.Y, annotation.End.Y);
            var right = Math.Max(annotation.Start.X, annotation.End.X);
            var bottom = Math.Max(annotation.Start.Y, annotation.End.Y);
            if (screenPoint.X >= left && screenPoint.X <= right && screenPoint.Y >= top && screenPoint.Y <= bottom)
            {
                return true;
            }
        }

        return false;
    }

    private void EnterPersistentAnnotationWindowAtDip(WPoint point)
    {
        var screenPoint = DipToScreenPoint(point);
        var detected = _selectionDetection.DetectTopLevelWindowHandleAtPoint(
            screenPoint,
            _capture.MonitorBounds,
            Environment.ProcessId);
        var clamped = ClampSelectionToMonitor(detected?.Bounds);
        if (!clamped.HasValue)
        {
            return;
        }

        _selectionScreenRect = clamped.Value;
        _lastAnnotationScreenRect = null;
        UpdateSelectionVisual();
        if (detected is not null && _onPersistentWindowRequested is not null)
        {
            _onPersistentWindowRequested(detected.Hwnd);
            Close();
            return;
        }

        _wrappedInteractionMode = false;
        SetAnnotationMode(true);
    }

    private void UpdateSelectionVisual()
    {
        var width = Math.Max(1, Root.ActualWidth);
        var height = Math.Max(1, Root.ActualHeight);
        if (!_selectionScreenRect.HasValue)
        {
            SetRect(TopDim, 0, 0, 0, 0);
            SetRect(LeftDim, 0, 0, 0, 0);
            SetRect(RightDim, 0, 0, 0, 0);
            SetRect(BottomDim, 0, 0, 0, 0);
            SelectionBorder.Visibility = Visibility.Collapsed;
            return;
        }

        var rect = ScreenRectToDip(_selectionScreenRect.Value);
        SetRect(TopDim, 0, 0, width, rect.Y);
        SetRect(LeftDim, 0, rect.Y, rect.X, rect.Height);
        SetRect(RightDim, rect.X + rect.Width, rect.Y, Math.Max(0, width - rect.X - rect.Width), rect.Height);
        SetRect(BottomDim, 0, rect.Y + rect.Height, width, Math.Max(0, height - rect.Y - rect.Height));
        SetRect(SelectionBorder, rect.X, rect.Y, rect.Width, rect.Height);
        SelectionBorder.Visibility = Visibility.Visible;
    }

    private DPoint DipToScreenPoint(WPoint point)
    {
        var scaleX = _capture.MonitorBounds.Width / Math.Max(1.0, Root.ActualWidth);
        var scaleY = _capture.MonitorBounds.Height / Math.Max(1.0, Root.ActualHeight);
        return new DPoint(
            _capture.MonitorBounds.Left + (int)Math.Round(point.X * scaleX),
            _capture.MonitorBounds.Top + (int)Math.Round(point.Y * scaleY));
    }

    private DRectangle DipRectToScreen(double left, double top, double width, double height)
    {
        var start = DipToScreenPoint(new WPoint(left, top));
        var end = DipToScreenPoint(new WPoint(left + width, top + height));
        return DRectangle.FromLTRB(
            Math.Min(start.X, end.X),
            Math.Min(start.Y, end.Y),
            Math.Max(start.X, end.X),
            Math.Max(start.Y, end.Y));
    }

    private static double Distance(WPoint a, WPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private DRectangle? ClampSelectionToMonitor(DRectangle? selection)
    {
        if (!selection.HasValue)
        {
            return null;
        }

        var clamped = DRectangle.Intersect(selection.Value, _capture.MonitorBounds);
        return clamped.Width >= 24 && clamped.Height >= 24 ? clamped : null;
    }

    private void SelectWindowAtDip(WPoint point)
    {
        var screenPoint = DipToScreenPoint(point);
        var detected = _selectionDetection.DetectTopLevelWindowAtPoint(
            screenPoint,
            _capture.MonitorBounds,
            Environment.ProcessId);
        var clamped = ClampSelectionToMonitor(detected);
        if (!clamped.HasValue)
        {
            return;
        }

        _selectionScreenRect = clamped.Value;
        UpdateSelectionVisual();
    }

    private bool CanAnnotate()
    {
        return !_wrappedInteractionMode || _annotationMode;
    }

    private void EnterWrappedInteractionMode()
    {
        _wrappedInteractionMode = true;
        SetAnnotationMode(false);
    }

    private void SetAnnotationMode(bool enabled)
    {
        _annotationMode = enabled;
        if (!_annotationMode && NotePanel.Visibility == Visibility.Visible)
        {
            HideNotePanel();
        }

        UpdateAnnotationModeUi();
    }

    private void UpdateAnnotationModeUi()
    {
        if (ModeToolbar is null || AnnotationModeButton is null || WrapModeText is null)
        {
            return;
        }

        ModeToolbar.Visibility = Visibility.Collapsed;
        AnnotationModeButton.Content = _annotationMode ? "关闭批注" : "开启批注";
        WrapModeText.Text = _annotationMode
            ? "常批注窗口：框内圈选后右键批注"
            : "右键长按窗口设为常批注窗口";
        ScreenPreview.Visibility = Visibility.Visible;
    }

    private nint OverlayWndProc(nint hwnd, uint msg, nuint wParam, nint lParam, nuint idSubclass, nuint refData)
    {
        if (msg == NativeMethods.WmNcHitTest && ShouldPassThrough(lParam))
        {
            return NativeMethods.HtTransparent;
        }

        return NativeMethods.DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    private bool ShouldPassThrough(nint lParam)
    {
        if (!_wrappedInteractionMode || _annotationMode)
        {
            return false;
        }

        var screenPoint = GetScreenPointFromLParam(lParam);
        return !IsScreenPointInsideElement(ModeToolbar, screenPoint);
    }

    private bool IsScreenPointInsideElement(FrameworkElement element, DPoint screenPoint)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return false;
        }

        try
        {
            var topLeftDip = element.TransformToVisual(Root).TransformPoint(new WPoint(0, 0));
            var bottomRightDip = element.TransformToVisual(Root).TransformPoint(new WPoint(element.ActualWidth, element.ActualHeight));
            var topLeft = DipToScreenPoint(topLeftDip);
            var bottomRight = DipToScreenPoint(bottomRightDip);
            var rect = DRectangle.FromLTRB(
                Math.Min(topLeft.X, bottomRight.X),
                Math.Min(topLeft.Y, bottomRight.Y),
                Math.Max(topLeft.X, bottomRight.X),
                Math.Max(topLeft.Y, bottomRight.Y));
            return rect.Contains(screenPoint);
        }
        catch
        {
            return false;
        }
    }

    private static DPoint GetScreenPointFromLParam(nint lParam)
    {
        var value = lParam.ToInt64();
        var x = unchecked((short)(value & 0xFFFF));
        var y = unchecked((short)((value >> 16) & 0xFFFF));
        return new DPoint(x, y);
    }

    private static int ToArgb(Color color)
    {
        return color.A << 24 | color.R << 16 | color.G << 8 | color.B;
    }

    private static void SetRect(FrameworkElement element, double left, double top, double width, double height)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        element.Width = Math.Max(0, width);
        element.Height = Math.Max(0, height);
    }

    private Windows.Foundation.Rect ScreenRectToDip(DRectangle rect)
    {
        var scaleX = Math.Max(1.0, Root.ActualWidth) / _capture.MonitorBounds.Width;
        var scaleY = Math.Max(1.0, Root.ActualHeight) / _capture.MonitorBounds.Height;
        return new Windows.Foundation.Rect(
            (rect.Left - _capture.MonitorBounds.Left) * scaleX,
            (rect.Top - _capture.MonitorBounds.Top) * scaleY,
            rect.Width * scaleX,
            rect.Height * scaleY);
    }

    private void CaptureOverlayWindow_Closed(object sender, WindowEventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _longPressTimer.Stop();
        if (_subclassed && _hwnd != 0)
        {
            NativeMethods.RemoveWindowSubclass(_hwnd, _overlaySubclassProc, OverlaySubclassId);
            _subclassed = false;
        }

        _capture.Dispose();
    }
}
