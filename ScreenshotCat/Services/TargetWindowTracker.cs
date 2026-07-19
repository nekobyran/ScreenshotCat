using Microsoft.UI.Dispatching;
using ScreenshotCat.Interop;

namespace ScreenshotCat.Services;

public sealed class TargetWindowTracker : IDisposable
{
    private readonly nint _targetHwnd;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly Action _onLocationChanged;
    private readonly NativeMethods.WinEventProc _callback;
    private nint _locationHook;
    private nint _foregroundHook;
    private int _updateQueued;
    private int _disposed;

    public TargetWindowTracker(
        nint targetHwnd,
        DispatcherQueue dispatcherQueue,
        Action onLocationChanged)
    {
        _targetHwnd = targetHwnd;
        _dispatcherQueue = dispatcherQueue;
        _onLocationChanged = onLocationChanged;
        _callback = HandleWinEvent;
        _locationHook = NativeMethods.SetWinEventHook(
            NativeMethods.EventObjectLocationChange,
            NativeMethods.EventObjectLocationChange,
            0,
            _callback,
            0,
            0,
            NativeMethods.WineventOutOfContext | NativeMethods.WineventSkipOwnProcess);
        _foregroundHook = NativeMethods.SetWinEventHook(
            NativeMethods.EventSystemForeground,
            NativeMethods.EventSystemForeground,
            0,
            _callback,
            0,
            0,
            NativeMethods.WineventOutOfContext | NativeMethods.WineventSkipOwnProcess);
    }

    public void Dispose()
    {
        Volatile.Write(ref _disposed, 1);
        var locationHook = Interlocked.Exchange(ref _locationHook, 0);
        if (locationHook != 0)
        {
            _ = NativeMethods.UnhookWinEvent(locationHook);
        }

        var foregroundHook = Interlocked.Exchange(ref _foregroundHook, 0);
        if (foregroundHook != 0)
        {
            _ = NativeMethods.UnhookWinEvent(foregroundHook);
        }
    }

    private void HandleWinEvent(
        nint winEventHook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime)
    {
        var isForegroundChange = eventType == NativeMethods.EventSystemForeground;
        if (Volatile.Read(ref _disposed) != 0
            || hwnd == 0
            || (!isForegroundChange
                && (hwnd != _targetHwnd
                    || idObject != NativeMethods.ObjidWindow
                    || idChild != NativeMethods.ChildidSelf))
            || Interlocked.Exchange(ref _updateQueued, 1) != 0)
        {
            return;
        }

        if (!_dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    if (Volatile.Read(ref _disposed) == 0)
                    {
                        _onLocationChanged();
                    }
                }
                finally
                {
                    Volatile.Write(ref _updateQueued, 0);
                }
            }))
        {
            Volatile.Write(ref _updateQueued, 0);
        }
    }
}
