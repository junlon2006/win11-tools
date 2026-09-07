using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;

namespace CpuCoreVoltageTray;

internal static class TrayIconRenderer
{
    private const int CanvasSize = 64;

    public static Icon Create(float? volts, bool isError)
    {
        using Bitmap bitmap = new(CanvasSize, CanvasSize);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Color.Transparent);

        Color background = isError
            ? Color.FromArgb(92, 45, 48)
            : Color.FromArgb(24, 28, 31);
        Color accent = isError
            ? Color.FromArgb(232, 104, 108)
            : Color.FromArgb(52, 201, 151);

        using GraphicsPath tile = CreateRoundedRectangle(new RectangleF(1, 1, 62, 62), 9);
        using SolidBrush backgroundBrush = new(background);
        using Pen borderPen = new(Color.FromArgb(225, accent), 2);
        graphics.FillPath(backgroundBrush, tile);
        graphics.DrawPath(borderPen, tile);

        string text = volts is float value
            ? value.ToString("0.00", CultureInfo.InvariantCulture)
            : "--";
        using Font font = CreateFittedFont(graphics, text);
        using SolidBrush textBrush = new(Color.White);
        using StringFormat format = new()
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.None,
            FormatFlags = StringFormatFlags.NoWrap
        };

        graphics.DrawString(text, font, textBrush, new RectangleF(1, 1, 62, 62), format);

        IntPtr handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static Font CreateFittedFont(Graphics graphics, string text)
    {
        for (float size = 34; size >= 16; size--)
        {
            Font candidate = new("Bahnschrift SemiCondensed", size, FontStyle.Bold, GraphicsUnit.Pixel);
            SizeF measured = graphics.MeasureString(text, candidate);
            if (measured.Width <= 60 && measured.Height <= 58)
                return candidate;

            candidate.Dispose();
        }

        return new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel);
    }

    private static GraphicsPath CreateRoundedRectangle(RectangleF bounds, float radius)
    {
        float diameter = radius * 2;
        GraphicsPath path = new();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
