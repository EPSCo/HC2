using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace HC2.App.Interop;

/// <summary>
/// Raises <see cref="DeviceChanged"/> when Windows broadcasts <c>WM_DEVICECHANGE</c> / <c>DBT_DEVNODES_CHANGED</c>,
/// which is what a USB-serial adapter being plugged in or pulled out looks like. The burst of messages a single
/// plug event produces is debounced into one notification.
/// </summary>
public sealed class DeviceChangeNotifier : IDisposable
{
    private const int WM_DEVICECHANGE      = 0x0219;
    private const int DBT_DEVNODES_CHANGED = 0x0007;

    private readonly HwndSource      _source;
    private readonly DispatcherTimer _debounce;

    public DeviceChangeNotifier(Window window)
    {
        _source = (HwndSource)PresentationSource.FromVisual(window)!;
        _source.AddHook(WndProc);

        _debounce      =  new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            DeviceChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    public event EventHandler? DeviceChanged;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DEVICECHANGE && wParam.ToInt32() == DBT_DEVNODES_CHANGED)
        {
            _debounce.Stop();
            _debounce.Start();
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        _debounce.Stop();
        _source.RemoveHook(WndProc);
    }
}
