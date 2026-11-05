using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace SystemMonitor;

public partial class OverlayWindow : Window
{
    private const int HotkeyToggle = 1, HotkeyExit = 2;
    private const uint VkF10 = 0x79, VkF11 = 0x7A;
    private const int TopMarginPx = 8;

    private readonly HardwareSampler _hardware = new();
    private readonly FpsMonitor _fps = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private IntPtr _hwnd;

    public OverlayWindow()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => Tick();
        SizeChanged += (_, _) => PositionTopCenter();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        // Click-through, never takes focus, hidden from Alt+Tab.
        int style = Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE,
            style | Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);

        const uint mods = Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT;
        Native.RegisterHotKey(_hwnd, HotkeyToggle, mods, VkF10);
        Native.RegisterHotKey(_hwnd, HotkeyExit, mods, VkF11);
        HwndSource.FromHwnd(_hwnd)!.AddHook(WndProc);

        _timer.Start();
        Tick();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != Native.WM_HOTKEY) return IntPtr.Zero;

        handled = true;
        if (wParam.ToInt32() == HotkeyToggle)
            Visibility = Visibility == Visibility.Visible ? Visibility.Hidden : Visibility.Visible;
        else if (wParam.ToInt32() == HotkeyExit)
            Close();
        return IntPtr.Zero;
    }

    private void Tick()
    {
        Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out uint pid);

        var hw = _hardware.Read();
        var fps = _fps.Sample((int)pid);

        Readout.Text =
            $"CPU {Fmt(hw.CpuLoad, "%")} {Fmt(hw.CpuTemp, "°C")}  │  " +
            $"GPU {Fmt(hw.GpuLoad, "%")} {Fmt(hw.GpuTemp, "°C")}  │  " +
            $"{Fmt(fps, " FPS")}";

        PositionTopCenter();
        WriteDiag((int)pid, fps);
    }

    // Temporary: dumps what the sensors and ETW see, so missing values can be traced.
    private int _diagTicks;
    private void WriteDiag(int pid, float? fps)
    {
        if (++_diagTicks % 10 != 1) return;
        string name;
        try { name = System.Diagnostics.Process.GetProcessById(pid).ProcessName; } catch { name = "?"; }
        var ids = string.Join(", ", _fps.SeenEventIds.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value}"));
        var top = string.Join(", ", _fps.LastRates.OrderByDescending(k => k.Value).Take(4).Select(k =>
        {
            string n;
            try { n = System.Diagnostics.Process.GetProcessById(k.Key).ProcessName; } catch { n = "?"; }
            return $"{n}({k.Key})={k.Value:0}";
        }));
        var text = $"{DateTime.Now:HH:mm:ss} fg={name}({pid}) fps={fps?.ToString("0.0") ?? "null"} topPresenters=[{top}] dxgiEvents=[{ids}]\n";
        try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sysmon-diag.txt"), text); }
        catch { /* diagnostics must never break the overlay */ }
    }

    private static string Fmt(float? v, string unit) =>
        v is float f ? f.ToString("0") + unit : "--" + unit;

    // Centers on the monitor of the foreground window (the game), in raw pixels so DPI scaling can't skew it.
    private void PositionTopCenter()
    {
        if (_hwnd == IntPtr.Zero) return;

        var target = Native.GetForegroundWindow();
        var monitor = Native.MonitorFromWindow(target == IntPtr.Zero ? _hwnd : target, Native.MONITOR_DEFAULTTONEAREST);
        var info = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(monitor, ref info) || !Native.GetWindowRect(_hwnd, out var self)) return;

        int width = self.Right - self.Left;
        int x = info.rcMonitor.Left + (info.rcMonitor.Right - info.rcMonitor.Left - width) / 2;
        int y = info.rcMonitor.Top + TopMarginPx;

        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        Native.UnregisterHotKey(_hwnd, HotkeyToggle);
        Native.UnregisterHotKey(_hwnd, HotkeyExit);
        _fps.Dispose();
        _hardware.Dispose();
        base.OnClosed(e);
    }
}
