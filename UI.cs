using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace LanGames;

public static class AppInfo
{
    public const string Name = "LAN Games Arena";

    // Sir's instruction: write your name and Sir Muhammad Abdullah's name FIRST.
    // Add your team mates after these names if you have any.
    public const string Developers = "Aisha & Sir Muhammad Abdullah";
    public const string Credits = "Developed by " + Developers + "   |   Visual Programming Project";
}

public static class UI
{
    public static readonly Color Bg = Color.FromArgb(15, 23, 42);
    public static readonly Color Panel = Color.FromArgb(30, 41, 59);
    public static readonly Color Panel2 = Color.FromArgb(51, 65, 85);
    public static readonly Color Text = Color.FromArgb(241, 245, 249);
    public static readonly Color Muted = Color.FromArgb(148, 163, 184);
    public static readonly Color Purple = Color.FromArgb(124, 58, 237);
    public static readonly Color Pink = Color.FromArgb(236, 72, 153);
    public static readonly Color Cyan = Color.FromArgb(6, 182, 212);
    public static readonly Color Green = Color.FromArgb(34, 197, 94);
    public static readonly Color Orange = Color.FromArgb(249, 115, 22);

    public static Font F(float pt, FontStyle st = FontStyle.Regular) => new("Segoe UI", pt, st);

    public static Color Lighten(Color c, float f) =>
        Color.FromArgb(c.A, (int)(c.R + (255 - c.R) * f), (int)(c.G + (255 - c.G) * f), (int)(c.B + (255 - c.B) * f));

    public static Color Darken(Color c, float f) =>
        Color.FromArgb(c.A, (int)(c.R * (1 - f)), (int)(c.G * (1 - f)), (int)(c.B * (1 - f)));

    public static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

/// <summary>Double-buffered panel for custom drawing.</summary>
public class Canvas : Panel
{
    public Canvas()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
    }
}

public class ModernButton : Button
{
    private bool hover, down;
    public Color Accent { get; set; } = UI.Purple;

    public ModernButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        ForeColor = Color.White;
        Font = UI.F(10.5f, FontStyle.Bold);
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnClick(EventArgs e) { Sfx.Play("click"); base.OnClick(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        if (Width < 4 || Height < 4) return;
        var back = Parent?.BackColor ?? UI.Bg;
        if (back.A < 255) back = UI.Bg;
        g.Clear(back);   // fixes the black corners

        var r = new RectangleF(1, 1, Width - 3, Height - 3);
        Color a = Enabled ? Accent : Color.FromArgb(71, 85, 105);
        if (Enabled && down) a = UI.Darken(a, .25f);
        else if (Enabled && hover) a = UI.Lighten(a, .18f);

        using var path = UI.Round(r, Height / 2.6f);
        using var br = new LinearGradientBrush(r, UI.Lighten(a, .12f), UI.Darken(a, .15f), 90f);
        g.FillPath(br, path);

        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var tb = new SolidBrush(Enabled ? ForeColor : Color.FromArgb(148, 163, 184));
        g.DrawString(Text, Font, tb, new RectangleF(0, 0, Width, Height), sf);
    }
}