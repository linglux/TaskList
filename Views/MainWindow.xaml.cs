using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TaskList.Helpers;
using TaskList.Services;
using TaskList.ViewModels;

namespace TaskList;

public partial class MainWindow : Window
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private readonly MainViewModel _viewModel;
    private TrayService? _tray;
    private bool _reallyExit;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        CelebrationService.PlayHandler = (weight, title, origin) =>
            Celebration.PlayAsync(weight, title, origin, CelebrationService.Effect);

        _tray = new TrayService(
            _viewModel,
            ShowFromTray,
            QuickAddFromTray,
            SettingsFromTray,
            ExitFromTray);

        SourceInitialized += OnSourceInitialized;
        StateChanged += OnStateChanged;
        Closing += OnClosing;
    }

    public void ShowFromTray()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        if (!IsVisible) Show();
        Activate();
        Topmost = true;
        Topmost = false;
        _tray?.RefreshStatus();
    }

    public void Cleanup()
    {
        CelebrationService.PlayHandler = null;
        Celebration.Cleanup();
        _viewModel.Settings.Dispose();
        SoundService.Cleanup();
        _tray?.Dispose();
        _tray = null;
    }

    private void QuickAddFromTray()
    {
        ShowFromTray();
        Dispatcher.BeginInvoke(new Action(() => _viewModel.QuickAdd()), DispatcherPriority.Loaded);
    }

    private void SettingsFromTray()
    {
        ShowFromTray();
        Dispatcher.BeginInvoke(new Action(() => _viewModel.NavigateSettingsCommand.Execute(null)), DispatcherPriority.Loaded);
    }

    private void ExitFromTray()
    {
        _reallyExit = true;
        Application.Current.Shutdown();
    }

    private void HideToTray()
    {
        Hide();
        _tray?.ShowFirstTimeTip();
        _tray?.RefreshStatus();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_reallyExit) return;
        e.Cancel = true;
        HideToTray();
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void OnStateChanged(object? sender, EventArgs e)
    {
        MaximizeIcon.Data = (Geometry)FindResource(
            WindowState == WindowState.Maximized ? "IconRestore" : "IconMaximize");
        if (WindowState == WindowState.Minimized) HideToTray();
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            VisualTree.FindAncestor<Button>(source) is not null) return;

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }
        try { DragMove(); }
        catch (InvalidOperationException) { }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook(OnWindowMessage);
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmGetMinMaxInfo) return IntPtr.Zero;

        var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor != IntPtr.Zero)
        {
            var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(monitor, ref monitorInfo))
            {
                info.ptMaxPosition.X = Math.Abs(monitorInfo.rcWork.Left - monitorInfo.rcMonitor.Left);
                info.ptMaxPosition.Y = Math.Abs(monitorInfo.rcWork.Top - monitorInfo.rcMonitor.Top);
                info.ptMaxSize.X = Math.Abs(monitorInfo.rcWork.Right - monitorInfo.rcWork.Left);
                info.ptMaxSize.Y = Math.Abs(monitorInfo.rcWork.Bottom - monitorInfo.rcWork.Top);
                info.ptMinTrackSize.X = (int)MinWidth;
                info.ptMinTrackSize.Y = (int)MinHeight;
            }
        }
        Marshal.StructureToPtr(info, lParam, true);
        handled = true;
        return IntPtr.Zero;
    }
}
