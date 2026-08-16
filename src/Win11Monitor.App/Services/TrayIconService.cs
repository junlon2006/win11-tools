using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;
using Win11Monitor.Core;

namespace Win11Monitor.App.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _widgetMenuItem;
    private readonly Forms.ContextMenuStrip _contextMenu;
    private Icon? _renderedIcon;
    private int _lastRenderedTemperature = int.MinValue;
    private int _lastRenderedSeverity = int.MinValue;

    public TrayIconService(Action showWindow, Action toggleWidget, Action exit)
    {
        _widgetMenuItem = new Forms.ToolStripMenuItem("显示任务栏监控条")
        {
            Checked = true,
            CheckOnClick = false
        };
        _widgetMenuItem.Click += (_, _) => toggleWidget();

        var openMenuItem = new Forms.ToolStripMenuItem("打开监控面板");
        openMenuItem.Click += (_, _) => showWindow();
        var exitMenuItem = new Forms.ToolStripMenuItem("退出");
        exitMenuItem.Click += (_, _) => exit();

        _contextMenu = new Forms.ContextMenuStrip();
        _contextMenu.Items.Add(openMenuItem);
        _contextMenu.Items.Add(_widgetMenuItem);
        _contextMenu.Items.Add(new Forms.ToolStripSeparator());
        _contextMenu.Items.Add(exitMenuItem);

        _notifyIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = _contextMenu,
            Text = "Z690 Monitor 正在启动",
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => showWindow();
        _notifyIcon.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
            {
                showWindow();
            }
        };

        SetIcon(null);
    }

    public void Update(MonitorSnapshot snapshot)
    {
        SetIcon(snapshot.CpuTemperature?.Maximum);
        _notifyIcon.Text = BuildTooltip(snapshot);
    }

    public void SetWidgetChecked(bool isChecked)
    {
        _widgetMenuItem.Checked = isChecked;
    }

    private void SetIcon(double? temperature)
    {
        var roundedTemperature = temperature is null
            ? -1
            : Math.Clamp((int)Math.Round(temperature.Value), 0, 999);
        var severity = GetTemperatureSeverity(temperature);
        if (roundedTemperature == _lastRenderedTemperature && severity == _lastRenderedSeverity)
        {
            return;
        }

        var nextIcon = RenderIcon(temperature);
        _notifyIcon.Icon = nextIcon;
        _renderedIcon?.Dispose();
        _renderedIcon = nextIcon;
        _lastRenderedTemperature = roundedTemperature;
        _lastRenderedSeverity = severity;
    }

    private static Icon RenderIcon(double? temperature)
    {
        using var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var background = GetTemperatureSeverity(temperature) switch
        {
            3 => Color.FromArgb(211, 64, 73),
            2 => Color.FromArgb(220, 147, 31),
            1 => Color.FromArgb(27, 150, 104),
            _ => Color.FromArgb(92, 100, 108)
        };
        using var backgroundBrush = new SolidBrush(background);
        graphics.FillRoundedRectangle(backgroundBrush, new Rectangle(1, 1, 30, 30), 6);

        var label = temperature is null ? "--" : Math.Clamp((int)Math.Round(temperature.Value), 0, 999).ToString("00");
        var fontSize = label.Length > 2 ? 12 : 15;
        using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(Color.White);
        var size = graphics.MeasureString(label, font);
        graphics.DrawString(label, font, textBrush, (32 - size.Width) / 2, (32 - size.Height) / 2 - 1);

        var iconHandle = bitmap.GetHicon();
        try
        {
            using var borrowedIcon = Icon.FromHandle(iconHandle);
            return (Icon)borrowedIcon.Clone();
        }
        finally
        {
            _ = DestroyIcon(iconHandle);
        }
    }

    private static string BuildTooltip(MonitorSnapshot snapshot)
    {
        static string Number(double? value, string format) => value?.ToString(format) ?? "--";

        var voltageLabel = snapshot.CpuVoltageSource switch
        {
            CpuVoltageSource.Vid => "VID",
            CpuVoltageSource.Vcore => "Vcore",
            _ => "V"
        };
        var tooltip = $"CPU {Number(snapshot.CpuTemperature?.Maximum, "F0")}/{Number(snapshot.CpuTemperature?.Average, "F0")}C | " +
                      $"{voltageLabel} {Number(snapshot.CpuVoltage?.Maximum, "F2")}/{Number(snapshot.CpuVoltage?.Average, "F2")}V | " +
                      $"PCH {Number(snapshot.PchTemperature?.Value, "F0")}C";
        return tooltip.Length <= 63 ? tooltip : tooltip[..63];
    }

    private static int GetTemperatureSeverity(double? temperature) => temperature switch
    {
        null => 0,
        >= 90 => 3,
        >= 75 => 2,
        _ => 1
    };

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
        _renderedIcon?.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}

internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle bounds, int radius)
    {
        using var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }
}
