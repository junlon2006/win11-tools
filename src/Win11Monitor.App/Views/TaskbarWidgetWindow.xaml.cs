using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Win11Monitor.App.ViewModels;

namespace Win11Monitor.App.Views;

public partial class TaskbarWidgetWindow : Window
{
    private readonly MonitorViewModel _viewModel;
    private readonly DispatcherTimer _visibilityTimer;

    public TaskbarWidgetWindow(MonitorViewModel viewModel, Action showMainWindow)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        SourceInitialized += (_, _) => DockToTaskbar();
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        MouseLeftButtonUp += (_, _) => showMainWindow();

        _visibilityTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(750)
        };
        _visibilityTimer.Tick += (_, _) => RefreshVisibility();
        _visibilityTimer.Start();
    }

    public void DockToTaskbar()
    {
        var screen = Forms.Screen.PrimaryScreen;
        if (screen is null)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var work = screen.WorkingArea;
        var bounds = screen.Bounds;
        var margin = 8d;
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;

        if (work.Top > bounds.Top)
        {
            Top = work.Top / dpi.DpiScaleY + margin;
        }
        else
        {
            Top = work.Bottom / dpi.DpiScaleY - height - margin;
        }

        Left = work.Left > bounds.Left
            ? work.Left / dpi.DpiScaleX + margin
            : work.Right / dpi.DpiScaleX - width - margin;
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.WorkArea))
        {
            DockToTaskbar();
        }
    }

    public void RefreshVisibility()
    {
        var shouldShow = _viewModel.ShowTaskbarWidget &&
                         !IsTaskbarAutoHidden() &&
                         !IsForegroundWindowFullScreen();

        if (shouldShow && !IsVisible)
        {
            Show();
            DockToTaskbar();
        }
        else if (!shouldShow && IsVisible)
        {
            Hide();
        }
    }

    private static bool IsTaskbarAutoHidden()
    {
        var data = new AppBarData { Size = (uint)Marshal.SizeOf<AppBarData>() };
        var state = ShellAppBarMessage(AppBarGetState, ref data).ToInt64();
        return (state & AppBarStateAutoHide) != 0;
    }

    private bool IsForegroundWindowFullScreen()
    {
        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero ||
            foregroundWindow == new System.Windows.Interop.WindowInteropHelper(this).Handle ||
            IsShellDesktopWindow(foregroundWindow) ||
            !GetWindowRect(foregroundWindow, out var windowBounds))
        {
            return false;
        }

        var screenBounds = Forms.Screen.FromHandle(foregroundWindow).Bounds;
        const int tolerance = 2;
        return windowBounds.Left <= screenBounds.Left + tolerance &&
               windowBounds.Top <= screenBounds.Top + tolerance &&
               windowBounds.Right >= screenBounds.Right - tolerance &&
               windowBounds.Bottom >= screenBounds.Bottom - tolerance;
    }

    private static bool IsShellDesktopWindow(IntPtr windowHandle)
    {
        if (windowHandle == GetShellWindow() || windowHandle == GetDesktopWindow())
        {
            return true;
        }

        var className = new StringBuilder(32);
        _ = GetClassName(windowHandle, className, className.Capacity);
        return className.ToString() is "Progman" or "WorkerW";
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _visibilityTimer.Stop();
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        base.OnClosing(e);
    }

    private const uint AppBarGetState = 0x00000004;
    private const long AppBarStateAutoHide = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public uint Size;
        public IntPtr WindowHandle;
        public uint CallbackMessage;
        public uint Edge;
        public NativeRect Rectangle;
        public IntPtr Parameter;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("shell32.dll", EntryPoint = "SHAppBarMessage")]
    private static extern IntPtr ShellAppBarMessage(uint message, ref AppBarData data);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr windowHandle, StringBuilder className, int maximumCount);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect rectangle);
}
