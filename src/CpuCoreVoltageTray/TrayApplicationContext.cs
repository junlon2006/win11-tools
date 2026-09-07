using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;

namespace CpuCoreVoltageTray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const int RefreshIntervalMilliseconds = 1_000;
    private const int MaximumTooltipLength = 63;

    private readonly CpuVoltageMonitor _monitor = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _currentItem;
    private readonly ToolStripMenuItem _peakItem;
    private readonly ToolStripMenuItem _frequencyPeakItem;
    private readonly ToolStripMenuItem _temperaturePeakItem;
    private readonly ToolStripMenuItem _powerPeakItem;
    private readonly ToolStripMenuItem _sourceItem;
    private readonly ToolStripMenuItem _autoStartItem;
    private readonly ToolStripMenuItem _driverItem;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly TaskbarStatusForm _taskbarForm;

    private Icon? _currentIcon;
    private float? _globalMaximumVoltage;
    private float? _globalMaximumFrequencyMHz;
    private float? _globalMaximumTemperatureCelsius;
    private float? _globalMaximumPowerWatts;
    private VoltageReading _lastReading = VoltageReading.Unavailable("正在读取...");
    private string _lastIconKey = string.Empty;
    private nint _shellTrayWindow;
    private bool _disposed;

    public TrayApplicationContext(bool startedAutomatically)
    {
        _currentItem = new ToolStripMenuItem("当前最高核心 VID：--") { Enabled = false };
        _peakItem = new ToolStripMenuItem("全局最大电压：--") { Enabled = false };
        _frequencyPeakItem = new ToolStripMenuItem("最高核心频率：--") { Enabled = false };
        _temperaturePeakItem = new ToolStripMenuItem("最高核心温度：--") { Enabled = false };
        _powerPeakItem = new ToolStripMenuItem("最高封装功率：--") { Enabled = false };
        _sourceItem = new ToolStripMenuItem("传感器：正在读取...") { Enabled = false };
        _autoStartItem = new ToolStripMenuItem("开机自动启动");
        _driverItem = new ToolStripMenuItem("安装传感器驱动...");
        ToolStripMenuItem createShortcutItem = new("创建或更新桌面快捷方式");
        ToolStripMenuItem resetMaximumItem = new("重置全部最大值");
        ToolStripMenuItem rescanItem = new("重新扫描传感器");
        ToolStripMenuItem exitItem = new("退出");

        _autoStartItem.Click += ToggleAutoStart;
        _driverItem.Click += InstallDriver;
        createShortcutItem.Click += CreateDesktopShortcut;
        resetMaximumItem.Click += (_, _) => ResetGlobalMaximum();
        rescanItem.Click += (_, _) => RescanSensors();
        exitItem.Click += (_, _) => ExitThread();

        _menu = new ContextMenuStrip();
        _menu.Items.AddRange([
            _currentItem,
            _peakItem,
            _frequencyPeakItem,
            _temperaturePeakItem,
            _powerPeakItem,
            _sourceItem,
            new ToolStripSeparator(),
            _autoStartItem,
            createShortcutItem,
            _driverItem,
            rescanItem,
            resetMaximumItem,
            new ToolStripSeparator(),
            exitItem
        ]);
        _menu.Opening += UpdateMenuState;

        _currentIcon = TrayIconRenderer.Create(null, isError: false);
        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = _menu,
            Icon = _currentIcon,
            Text = "CPU 全局最大 VID：正在读取",
            Visible = false
        };
        _notifyIcon.MouseClick += OnNotifyIconMouseClick;

        _taskbarForm = new TaskbarStatusForm((Icon)_currentIcon!.Clone());
        MainForm = _taskbarForm;
        _taskbarForm.Show();

        _timer = new System.Windows.Forms.Timer
        {
            Interval = RefreshIntervalMilliseconds,
            Enabled = false
        };
        _timer.Tick += (_, _) => RefreshReading();

        EnsureDriver(startedAutomatically);
        RefreshReading();
        _timer.Start();
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _notifyIcon.Visible = false;
        _taskbarForm.PrepareForExit();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            MainForm = null;
            _taskbarForm.Dispose();
            _timer.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _currentIcon?.Dispose();
            _menu.Dispose();
            _monitor.Dispose();
        }

        base.Dispose(disposing);
    }

    private void EnsureDriver(bool startedAutomatically)
    {
        DriverStatus status = DriverManager.GetStatus();
        if (status.IsReady || startedAutomatically)
            return;

        string message = status.IsInstalled
            ? $"读取 CPU 核心电压需要 PawnIO 2.2 或更高版本。\n\n当前版本：{status.Version}\n是否现在更新？"
            : "读取 CPU 核心电压需要安装 PawnIO 传感器驱动。\n\n是否现在安装？";

        DialogResult result = MessageBox.Show(
            message,
            "CPU 核心电压",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information,
            MessageBoxDefaultButton.Button1);

        if (result == DialogResult.Yes)
            InstallDriverCore();
    }

    private void RefreshReading()
    {
        EnsureTrayIconRegistered();

        _lastReading = _monitor.Read();
        _globalMaximumFrequencyMHz = Maximum(
            _globalMaximumFrequencyMHz,
            _lastReading.MaximumCoreClockMHz);
        _globalMaximumTemperatureCelsius = Maximum(
            _globalMaximumTemperatureCelsius,
            _lastReading.MaximumCoreTemperatureCelsius);
        _globalMaximumPowerWatts = Maximum(
            _globalMaximumPowerWatts,
            _lastReading.PackagePowerWatts);

        if (_lastReading.IsAvailable)
        {
            _globalMaximumVoltage = Maximum(_globalMaximumVoltage, _lastReading.Volts);
            float globalMaximumVoltage = _globalMaximumVoltage!.Value;

            _currentItem.Text = $"当前最高核心 VID：{_lastReading.Volts:0.000} V";
            _peakItem.Text = $"全局最大电压：{globalMaximumVoltage:0.000} V";
            _sourceItem.Text = $"传感器：{_lastReading.SensorName} / {_lastReading.SensorCount} 核有效";
            UpdateIcon(globalMaximumVoltage, isError: false);
        }
        else
        {
            _currentItem.Text = "当前最高核心 VID：--";
            _sourceItem.Text = $"状态：{_lastReading.Error}";

            if (_globalMaximumVoltage is float globalMaximumVoltage)
            {
                _peakItem.Text = $"全局最大电压：{globalMaximumVoltage:0.000} V";
                UpdateIcon(globalMaximumVoltage, isError: false);
            }
            else
            {
                _peakItem.Text = "全局最大电压：--";
                UpdateIcon(null, isError: true);
            }
        }

        _frequencyPeakItem.Text = $"最高核心频率：{FormatFrequency(_globalMaximumFrequencyMHz)}";
        _temperaturePeakItem.Text = $"最高核心温度：{FormatTemperature(_globalMaximumTemperatureCelsius)}";
        _powerPeakItem.Text = $"最高封装功率：{FormatPower(_globalMaximumPowerWatts)}";
        _notifyIcon.Text = CreateTooltipText();
        _taskbarForm.UpdateReading(
            _lastReading,
            _globalMaximumVoltage,
            _globalMaximumFrequencyMHz,
            _globalMaximumTemperatureCelsius,
            _globalMaximumPowerWatts);
        UpdateDriverItem();
    }

    private void EnsureTrayIconRegistered()
    {
        nint shellTrayWindow = FindWindow("Shell_TrayWnd", null);
        if (shellTrayWindow == nint.Zero)
        {
            _shellTrayWindow = nint.Zero;
            _notifyIcon.Visible = false;
            return;
        }

        if (shellTrayWindow == _shellTrayWindow)
            return;

        _notifyIcon.Visible = false;
        _notifyIcon.Visible = true;
        _shellTrayWindow = shellTrayWindow;
    }

    private void UpdateIcon(float? volts, bool isError)
    {
        string key = isError
            ? "error"
            : volts is float value
                ? value.ToString("0.00", CultureInfo.InvariantCulture)
                : "empty";
        if (string.Equals(key, _lastIconKey, StringComparison.Ordinal))
            return;

        Icon nextIcon = TrayIconRenderer.Create(volts, isError);
        Icon? previousIcon = _currentIcon;
        _currentIcon = nextIcon;
        _notifyIcon.Icon = nextIcon;
        _taskbarForm.ReplaceIcon((Icon)nextIcon.Clone());
        _lastIconKey = key;
        previousIcon?.Dispose();
    }

    private void UpdateMenuState(object? sender, CancelEventArgs eventArgs)
    {
        try
        {
            _autoStartItem.Checked = AutoStartManager.IsEnabled();
            _autoStartItem.Enabled = true;
            _autoStartItem.ToolTipText = string.Empty;
        }
        catch (Exception exception)
        {
            _autoStartItem.Checked = false;
            _autoStartItem.Enabled = false;
            _autoStartItem.ToolTipText = exception.Message;
        }

        UpdateDriverItem();
    }

    private void UpdateDriverItem()
    {
        DriverStatus status = DriverManager.GetStatus();
        _driverItem.Visible = !status.IsReady || !_lastReading.IsAvailable;
        _driverItem.Text = status.IsReady
            ? "修复传感器驱动..."
            : status.IsInstalled
                ? "更新传感器驱动..."
                : "安装传感器驱动...";
    }

    private void ToggleAutoStart(object? sender, EventArgs eventArgs)
    {
        try
        {
            if (AutoStartManager.IsEnabled())
                AutoStartManager.Disable();
            else
                AutoStartManager.Enable();

            _autoStartItem.Checked = AutoStartManager.IsEnabled();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"无法修改开机自动启动。\n\n{exception.Message}",
                "CPU 核心电压",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void CreateDesktopShortcut(object? sender, EventArgs eventArgs)
    {
        try
        {
            string shortcutPath = ShortcutManager.CreateDesktopShortcut();
            MessageBox.Show(
                $"已创建桌面快捷方式。\n\n{shortcutPath}",
                "CPU 核心电压",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"无法创建桌面快捷方式。\n\n{exception.Message}",
                "CPU 核心电压",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void InstallDriver(object? sender, EventArgs eventArgs) => InstallDriverCore();

    private void InstallDriverCore()
    {
        DriverStatus currentStatus = DriverManager.GetStatus();
        if (currentStatus.IsReady &&
            MessageBox.Show(
                "将重新安装 PawnIO 传感器驱动。是否继续？",
                "CPU 核心电压",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        _timer.Stop();
        try
        {
            DriverInstallResult result = DriverManager.Install();
            if (!result.Succeeded)
            {
                MessageBox.Show(
                    $"驱动安装失败。\n\n{result.Error}",
                    "CPU 核心电压",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            if (result.RestartRequired)
            {
                MessageBox.Show(
                    "驱动安装完成。请重启 Windows 后再读取 CPU 核心电压。",
                    "CPU 核心电压",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            _monitor.Reset();
            RefreshReading();
        }
        finally
        {
            _timer.Start();
        }
    }

    private void RescanSensors()
    {
        _monitor.Reset();
        RefreshReading();
    }

    private void ResetGlobalMaximum()
    {
        _globalMaximumVoltage = null;
        _globalMaximumFrequencyMHz = null;
        _globalMaximumTemperatureCelsius = null;
        _globalMaximumPowerWatts = null;
        _peakItem.Text = "全局最大电压：--";
        _frequencyPeakItem.Text = "最高核心频率：--";
        _temperaturePeakItem.Text = "最高核心温度：--";
        _powerPeakItem.Text = "最高封装功率：--";
        _notifyIcon.Text = "-- | -- | -- | --";
        UpdateIcon(null, isError: !_lastReading.IsAvailable);
        _taskbarForm.UpdateReading(_lastReading, null, null, null, null);
    }

    private void OnNotifyIconMouseClick(object? sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.Button == MouseButtons.Left)
            _taskbarForm.ShowStatus();
    }

    private string CreateTooltipText()
    {
        bool hasAnyPeak = _globalMaximumVoltage.HasValue ||
                          _globalMaximumFrequencyMHz.HasValue ||
                          _globalMaximumTemperatureCelsius.HasValue ||
                          _globalMaximumPowerWatts.HasValue;
        if (!hasAnyPeak)
            return LimitTooltip($"CPU 监测：{_lastReading.Error}");

        string text = $"{FormatVoltage(_globalMaximumVoltage, compact: true)} | " +
                      $"{FormatFrequency(_globalMaximumFrequencyMHz, compact: true)} | " +
                      $"{FormatTemperature(_globalMaximumTemperatureCelsius, compact: true)} | " +
                      $"{FormatPower(_globalMaximumPowerWatts, compact: true)}";
        return LimitTooltip(text);
    }

    private static float? Maximum(float? current, float? candidate)
    {
        if (candidate is not float value)
            return current;

        return current is float existing
            ? Math.Max(existing, value)
            : value;
    }

    private static string FormatVoltage(float? volts, bool compact)
    {
        return volts is float value
            ? value.ToString("0.000", CultureInfo.InvariantCulture) + (compact ? "V" : " V")
            : "--";
    }

    private static string FormatFrequency(float? megahertz, bool compact = false)
    {
        return megahertz is float value
            ? (value / 1_000f).ToString("0.00", CultureInfo.InvariantCulture) +
              (compact ? "GHz" : " GHz")
            : "--";
    }

    private static string FormatTemperature(float? celsius, bool compact = false)
    {
        return celsius is float value
            ? value.ToString("0", CultureInfo.InvariantCulture) + (compact ? "°C" : " °C")
            : "--";
    }

    private static string FormatPower(float? watts, bool compact = false)
    {
        return watts is float value
            ? value.ToString(compact ? "0" : "0.0", CultureInfo.InvariantCulture) + (compact ? "W" : " W")
            : "--";
    }

    private static string LimitTooltip(string value)
    {
        return value.Length <= MaximumTooltipLength
            ? value
            : value[..MaximumTooltipLength];
    }

    [DllImport("user32.dll", EntryPoint = "FindWindowW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string? className, string? windowName);
}
