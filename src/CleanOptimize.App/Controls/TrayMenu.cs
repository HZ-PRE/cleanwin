using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace CleanOptimize.App.Controls;

internal enum TrayAction { Header, Open, Scan, QuickClean, AutoClean, Exit }

/// <summary>Native tray menu behavior with a small, theme-aware renderer; no images or timers.</summary>
internal sealed class TrayMenu : ContextMenuStrip
{
    private readonly Drawing.Font _textFont = new("Microsoft YaHei UI", 9.5f);
    private readonly Drawing.Font _headingFont = new("Microsoft YaHei UI", 9f, Drawing.FontStyle.Bold);
    private readonly TrayMenuRenderer _renderer = new();

    internal TrayMenu()
    {
        Font = _textFont;
        ShowImageMargin = false;
        ShowCheckMargin = false;
        Renderer = _renderer;
        ApplyTheme();
        AddAction("CleanOptimize", TrayAction.Header, null).Font = _headingFont;
        Items.Add(new ToolStripSeparator());
    }

    internal TrayMenuItem AddAction(string text, TrayAction action, EventHandler? onClick)
    {
        var item = new TrayMenuItem(text, action) { Enabled = action != TrayAction.Header };
        if (onClick != null) item.Click += onClick;
        Items.Add(item);
        return item;
    }

    public override Drawing.Size GetPreferredSize(Drawing.Size proposedSize)
    {
        var size = base.GetPreferredSize(proposedSize);
        // The native drop-down layout measures only text by default, omitting our
        // custom icon and switch columns. Include those columns in the popup width.
        foreach (var item in Items.OfType<TrayMenuItem>())
            size.Width = Math.Max(size.Width, item.GetPreferredSize(proposedSize).Width + Padding.Horizontal);
        return size;
    }

    internal void ApplyTheme()
    {
        if (IsDisposed) return;
        _renderer.Palette = TrayPalette.Current();
        BackColor = _renderer.Palette.Background;
        ForeColor = _renderer.Palette.Text;
        int inset = Math.Max(1, (int)Math.Round(7 * DeviceDpi / 96d));
        Padding = new Forms.Padding(inset);
        Invalidate();
    }

    protected override void OnOpening(CancelEventArgs e)
    {
        ApplyTheme();
        base.OnOpening(e);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) { _textFont.Dispose(); _headingFont.Dispose(); }
    }
}

internal sealed class TrayMenuItem(string text, TrayAction action) : ToolStripMenuItem(text)
{
    internal TrayAction Action { get; } = action;
    public override Drawing.Size GetPreferredSize(Drawing.Size constrainingSize)
    {
        float scale = (Owner?.DeviceDpi ?? 96) / 96f;
        var label = TextRenderer.MeasureText(Text, Font, Drawing.Size.Empty,
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        return new Drawing.Size(Math.Max((int)(224 * scale), label.Width + (int)(96 * scale)),
            Math.Max((int)((Action == TrayAction.Header ? 32 : 38) * scale), label.Height + (int)(12 * scale)));
    }
}

internal sealed record TrayPalette(Drawing.Color Background, Drawing.Color Text, Drawing.Color Muted,
    Drawing.Color Accent, Drawing.Color Hover, Drawing.Color Border, Drawing.Color Danger, Drawing.Color DangerHover, Drawing.Color Toggle)
{
    internal static TrayPalette Current()
    {
        if (SystemInformation.HighContrast)
            return new(SystemColors.Menu, SystemColors.MenuText, SystemColors.GrayText, SystemColors.Highlight,
                SystemColors.Highlight, SystemColors.MenuText, SystemColors.MenuText, SystemColors.Highlight, SystemColors.GrayText);
        Drawing.Color Read(string key, Drawing.Color fallback)
        {
            if (System.Windows.Application.Current?.TryFindResource(key) is System.Windows.Media.SolidColorBrush brush)
                return Drawing.Color.FromArgb(brush.Color.A, brush.Color.R, brush.Color.G, brush.Color.B);
            return fallback;
        }
        return new(Read("CardBgBrush", SystemColors.Menu), Read("TextPrimaryBrush", SystemColors.MenuText),
            Read("TextSecondaryBrush", SystemColors.GrayText), Read("PrimaryBrush", SystemColors.Highlight),
            Read("PrimarySoftBrush", SystemColors.ControlLight), Read("CardBorderBrush", SystemColors.ControlDark),
            Read("DangerBrush", Drawing.Color.Firebrick), Read("DangerSoftBrush", SystemColors.ControlLight),
            Read("TrackBrush", SystemColors.ControlDark));
    }
}

internal sealed class TrayMenuRenderer : ToolStripRenderer
{
    internal TrayPalette Palette { get; set; } = TrayPalette.Current();

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Palette.Background);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Palette.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        float scale = (e.ToolStrip?.DeviceDpi ?? 96) / 96f;
        using var pen = new Pen(Palette.Border);
        e.Graphics.DrawLine(pen, 10 * scale, e.Item.Height / 2f, e.Item.Width - 10 * scale, e.Item.Height / 2f);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item is not TrayMenuItem item) return;
        float scale = (item.Owner?.DeviceDpi ?? 96) / 96f;
        var state = e.Graphics.Save();
        try
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (item.Selected && item.Enabled)
            {
                using var fill = new SolidBrush(item.Action == TrayAction.Exit ? Palette.DangerHover : Palette.Hover);
                using var shape = Rounded(new RectangleF(1, 1, item.Width - 2, item.Height - 2), 7 * scale);
                e.Graphics.FillPath(fill, shape);
            }
            var color = SystemInformation.HighContrast && item.Selected ? SystemColors.HighlightText
                : item.Action == TrayAction.Exit ? Palette.Danger : Palette.Accent;
            var glyph = e.Graphics.Save();
            e.Graphics.TranslateTransform(13 * scale, (item.Height - 18 * scale) / 2);
            e.Graphics.ScaleTransform(scale, scale);
            using (var pen = new Pen(color, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                DrawGlyph(e.Graphics, pen, item.Action);
            e.Graphics.Restore(glyph);
            if (item.Action == TrayAction.AutoClean)
            {
                var bounds = new RectangleF(item.Width - 42 * scale, (item.Height - 16 * scale) / 2, 29 * scale, 16 * scale);
                using var track = new SolidBrush(item.Checked ? Palette.Accent : Palette.Toggle);
                using var shape = Rounded(bounds, 8 * scale);
                e.Graphics.FillPath(track, shape);
                using var thumb = new SolidBrush(item.Checked ? Palette.Background : Palette.Muted);
                e.Graphics.FillEllipse(thumb, bounds.X + (item.Checked ? 15 : 3) * scale, bounds.Y + 3 * scale, 10 * scale, 10 * scale);
            }
        }
        finally { e.Graphics.Restore(state); }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item is not TrayMenuItem item) { base.OnRenderItemText(e); return; }
        float scale = (item.Owner?.DeviceDpi ?? 96) / 96f;
        int left = (int)(42 * scale), right = (int)((item.Action == TrayAction.AutoClean ? 49 : 13) * scale);
        var color = SystemInformation.HighContrast && item.Selected ? SystemColors.HighlightText
            : item.Action == TrayAction.Header ? Palette.Muted : item.Action == TrayAction.Exit ? Palette.Danger : Palette.Text;
        TextRenderer.DrawText(e.Graphics, item.Text, item.Font, new Rectangle(left, 0, Math.Max(0, item.Width - left - right), item.Height), color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }

    private static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void DrawGlyph(Graphics g, Pen pen, TrayAction action)
    {
        switch (action)
        {
            case TrayAction.Header:
                g.DrawArc(pen, 2, 2, 14, 14, 45, 270);
                g.DrawLines(pen, [new PointF(7, 9), new PointF(10, 12), new PointF(16, 5)]);
                break;
            case TrayAction.Open:
                g.DrawRectangle(pen, 2, 3, 14, 12); g.DrawLine(pen, 2, 7, 16, 7); break;
            case TrayAction.Scan:
                g.DrawEllipse(pen, 2, 2, 10, 10); g.DrawLine(pen, 11, 11, 16, 16); break;
            case TrayAction.QuickClean:
                g.DrawLines(pen, [new PointF(10, 1), new PointF(3, 10), new PointF(9, 10), new PointF(7, 17), new PointF(15, 7), new PointF(9, 7), new PointF(10, 1)]);
                break;
            case TrayAction.AutoClean:
                g.DrawEllipse(pen, 2, 2, 14, 14); g.DrawLines(pen, [new PointF(9, 5), new PointF(9, 9), new PointF(12, 11)]); break;
            case TrayAction.Exit:
                g.DrawArc(pen, 2, 3, 14, 13, -55, 290); g.DrawLine(pen, 9, 1, 9, 8); break;
        }
    }
}
