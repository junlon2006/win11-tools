using System.Globalization;
using System.Runtime.InteropServices;

namespace CpuCoreVoltageTray;

internal sealed class TaskbarStatusForm : Form
{
    private readonly Label _maximumValue;
    private readonly Label _currentValue;
    private readonly Label _sensorValue;
    private readonly Label _frequencyValue;
    private readonly Label _temperatureValue;
    private readonly Label _powerValue;

    private Icon? _ownedIcon;
    private bool _allowClose;

    public TaskbarStatusForm(Icon initialIcon)
    {
        _ownedIcon = initialIcon;

        Text = "最大 --";
        Icon = initialIcon;
        ClientSize = new Size(360, 228);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = true;
        ShowInTaskbar = true;
        WindowState = FormWindowState.Minimized;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(247, 248, 249);
        ForeColor = Color.FromArgb(30, 34, 38);

        Label heading = new()
        {
            AutoSize = false,
            Location = new Point(18, 14),
            Size = new Size(324, 22),
            Font = new Font("Segoe UI", 10, FontStyle.Regular),
            Text = "本次运行全局最大 VID"
        };

        _maximumValue = new Label
        {
            AutoSize = false,
            Location = new Point(16, 36),
            Size = new Size(328, 49),
            Font = new Font("Segoe UI", 28, FontStyle.Bold),
            ForeColor = Color.FromArgb(16, 112, 79),
            Text = "--",
            TextAlign = ContentAlignment.MiddleLeft
        };

        _currentValue = CreateDetailLabel(18, 91, "当前：正在读取...");
        _sensorValue = CreateDetailLabel(18, 115, "传感器：正在读取...", subdued: true);
        _frequencyValue = CreateDetailLabel(18, 148, "最高核心频率：--");
        _temperatureValue = CreateDetailLabel(18, 172, "最高核心温度：--");
        _powerValue = CreateDetailLabel(18, 196, "最高封装功率：--");

        Controls.AddRange([
            heading,
            _maximumValue,
            _currentValue,
            _sensorValue,
            _frequencyValue,
            _temperatureValue,
            _powerValue
        ]);
        FormClosing += OnFormClosing;
    }

    public void UpdateReading(
        VoltageReading reading,
        float? globalMaximumVoltage,
        float? globalMaximumFrequencyMHz,
        float? globalMaximumTemperatureCelsius,
        float? globalMaximumPowerWatts)
    {
        if (globalMaximumVoltage is float maximumVoltage)
        {
            _maximumValue.Text = $"{maximumVoltage:0.000} V";
            Text = $"最大 {maximumVoltage:0.000} V";
        }
        else
        {
            _maximumValue.Text = "--";
            Text = "最大 --";
        }

        if (reading.IsAvailable)
        {
            _currentValue.Text = $"当前：{reading.Volts:0.000} V    有效核心：{reading.SensorCount}";
            _sensorValue.Text = $"传感器：{reading.SensorName}";
        }
        else
        {
            _currentValue.Text = "当前：--";
            _sensorValue.Text = $"状态：{reading.Error}";
        }

        _frequencyValue.Text = $"最高核心频率：{FormatFrequency(globalMaximumFrequencyMHz)}";
        _temperatureValue.Text = $"最高核心温度：{FormatTemperature(globalMaximumTemperatureCelsius)}";
        _powerValue.Text = $"最高封装功率：{FormatPower(globalMaximumPowerWatts)}";
    }

    public void ShowStatus()
    {
        WindowState = FormWindowState.Normal;
        Show();
        Activate();
        BringToFront();
    }


    public void ReplaceIcon(Icon nextIcon)
    {
        Icon? previousIcon = _ownedIcon;
        _ownedIcon = nextIcon;
        Icon = nextIcon;
        if (IsHandleCreated)
        {
            SendMessage(Handle, 0x0080, IntPtr.Zero, nextIcon.Handle);
            SendMessage(Handle, 0x0080, new IntPtr(1), nextIcon.Handle);
        }
        previousIcon?.Dispose();
    }

    public void PrepareForExit() => _allowClose = true;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Icon = null!;
            _ownedIcon?.Dispose();
            _ownedIcon = null;
        }

        base.Dispose(disposing);
    }

    private static Label CreateDetailLabel(int x, int y, string text, bool subdued = false)
    {
        return new Label
        {
            AutoSize = false,
            Location = new Point(x, y),
            Size = new Size(324, 21),
            Font = new Font("Segoe UI", 9, FontStyle.Regular),
            ForeColor = subdued ? Color.FromArgb(85, 91, 97) : Color.FromArgb(30, 34, 38),
            Text = text,
            AutoEllipsis = true
        };
    }

    private static string FormatFrequency(float? megahertz)
    {
        return megahertz is float value
            ? (value / 1_000f).ToString("0.00", CultureInfo.InvariantCulture) + " GHz"
            : "--";
    }

    private static string FormatTemperature(float? celsius)
    {
        return celsius is float value
            ? value.ToString("0", CultureInfo.InvariantCulture) + " °C"
            : "--";
    }

    private static string FormatPower(float? watts)
    {
        return watts is float value
            ? value.ToString("0.0", CultureInfo.InvariantCulture) + " W"
            : "--";
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern IntPtr SendMessage(
        IntPtr windowHandle,
        uint message,
        IntPtr parameter,
        IntPtr iconHandle);

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (_allowClose || eventArgs.CloseReason != CloseReason.UserClosing)
            return;

        eventArgs.Cancel = true;
        WindowState = FormWindowState.Minimized;
    }
}
