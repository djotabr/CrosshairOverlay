using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

// Visual moderno do menu: cantos arredondados, chaves liga/desliga, sliders finos e cartões.
static class Ui
{
    // Cores do tema atual (definidas pelo SettingsForm ao montar o menu).
    public static Color Bg, Card, Input, Border, Text, Dim, Accent, Nav;

    static string family;
    public static string FamilyOverride;   // fonte do tema (ex.: Tahoma no Windows XP)
    public static Font Font(float size, FontStyle style = FontStyle.Regular)
    {
        if (!string.IsNullOrEmpty(FamilyOverride)) return new Font(FamilyOverride, size, style);
        if (family == null)
        {
            var names = new System.Drawing.Text.InstalledFontCollection().Families.Select(f => f.Name).ToList();
            family = names.Contains("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
        }
        return new Font(family, size, style);
    }

    static Font icons;
    public static Font IconFont
    {
        get
        {
            if (icons == null)
            {
                var names = new System.Drawing.Text.InstalledFontCollection().Families.Select(f => f.Name).ToList();
                icons = new Font(names.Contains("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets", 12f);
            }
            return icons;
        }
    }

    public static Color Mix(Color a, Color b, double t)
    {
        return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static Color ParentBg(Control c) { return c.Parent != null ? c.Parent.BackColor : Bg; }
}

// Botão arredondado, sem borda, com hover suave. Opcional: ícone (Glyph) e barrinha de seleção (Indicator).
class RoundButton : Button
{
    public string Glyph;
    public bool Indicator;
    public Color Surface = Color.Empty;   // cor do que está atrás (quando não é a do controle pai)
    public int Radius = 8;
    bool hover, down;

    public RoundButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var parent = Surface.IsEmpty ? Ui.ParentBg(this) : Surface;
        g.Clear(parent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Color fill = BackColor;
        if (!Enabled) fill = Ui.Mix(fill, parent, 0.5);
        else if (down) fill = Ui.Mix(fill, Ui.Text, 0.16);
        else if (hover) fill = FlatAppearance.MouseOverBackColor.IsEmpty ? Ui.Mix(fill, Ui.Text, 0.08) : FlatAppearance.MouseOverBackColor;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Ui.Round(r, Radius))
        {
            using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            int bs = FlatAppearance.BorderSize;
            if (bs > 0 && !FlatAppearance.BorderColor.IsEmpty)
                using (var pen = new Pen(FlatAppearance.BorderColor, bs)) { pen.Alignment = PenAlignment.Inset; g.DrawPath(pen, path); }
        }
        if (Indicator)
            using (var b = new SolidBrush(Ui.Accent))
            using (var bar = Ui.Round(new RectangleF(3, Height * 0.28f, 3.5f, Height * 0.44f), 1.75f))
                g.FillPath(b, bar);

        Color fc = Enabled ? ForeColor : Ui.Dim;
        var text = new Rectangle(Padding.Left, 0, Width - Padding.Horizontal, Height);
        if (!string.IsNullOrEmpty(Glyph))
        {
            TextRenderer.DrawText(g, Glyph, Ui.IconFont, new Rectangle(12, 0, 24, Height), fc,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            text = new Rectangle(44, 0, Width - 48, Height);
        }
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
                    (TextAlign == ContentAlignment.MiddleLeft || !string.IsNullOrEmpty(Glyph) ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(g, Text.Replace("&&", "&"), Font, text, fc, flags | TextFormatFlags.NoPrefix);
    }
}

// Chave liga/desliga (no lugar da caixinha de marcar).
class ToggleSwitch : CheckBox
{
    bool hover;

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var t = TextRenderer.MeasureText(Text, Font);
        return new Size(48 + t.Width + 4, Math.Max(28, t.Height + 8));
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.ParentBg(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new RectangleF(1, Height / 2f - 10, 38, 20);
        Color on = Ui.Accent, off = Ui.Mix(Ui.Input, Ui.Text, hover ? 0.16 : 0.08);
        using (var path = Ui.Round(track, 10))
        using (var b = new SolidBrush(Checked ? on : off)) g.FillPath(b, path);
        float d = 14, x = Checked ? track.Right - d - 3 : track.X + 3;
        using (var b = new SolidBrush(Checked ? Color.White : Ui.Mix(Ui.Dim, Ui.Text, 0.3)))
            g.FillEllipse(b, x, track.Y + 3, d, d);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(48, 0, Width - 48, Height), Enabled ? ForeColor : Ui.Dim,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        if (Focused && ShowFocusCues)
            using (var pen = new Pen(Color.FromArgb(120, Ui.Accent), 1)) using (var path = Ui.Round(new RectangleF(0, track.Y - 2, 40, 24), 12)) g.DrawPath(pen, path);
    }
}

// Botão de opção redondo.
class ModernRadio : RadioButton
{
    public ModernRadio()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var t = TextRenderer.MeasureText(Text, Font);
        return new Size(28 + t.Width + 4, Math.Max(26, t.Height + 6));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.ParentBg(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var c = new RectangleF(2, Height / 2f - 9, 18, 18);
        using (var pen = new Pen(Checked ? Ui.Accent : Ui.Mix(Ui.Border, Ui.Text, 0.2), 2)) g.DrawEllipse(pen, c);
        if (Checked) using (var b = new SolidBrush(Ui.Accent)) g.FillEllipse(b, c.X + 5, c.Y + 5, 8, 8);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(28, 0, Width - 28, Height), ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }
}

// Slider fino com bolinha (no lugar do TrackBar do Windows).
class ModernSlider : Control
{
    int min, max = 100, val;
    bool drag, hover;
    public event EventHandler ValueChanged;

    public int Minimum { get { return min; } set { min = value; if (max < min) max = min; Value = val; Invalidate(); } }
    public int Maximum { get { return max; } set { max = Math.Max(min, value); Value = val; Invalidate(); } }
    public int Value
    {
        get { return val; }
        set
        {
            int v = Math.Max(min, Math.Min(max, value));
            if (v == val) return;
            val = v; Invalidate();
            var h = ValueChanged; if (h != null) h(this, EventArgs.Empty);
        }
    }

    public ModernSlider()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    const int Pad = 10;
    float Frac { get { return max > min ? (val - min) / (float)(max - min) : 0; } }

    void SetFromX(int x)
    {
        float f = (x - Pad) / (float)Math.Max(1, Width - Pad * 2);
        Value = min + (int)Math.Round(Math.Max(0, Math.Min(1, f)) * (max - min));
    }

    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { drag = true; Focus(); SetFromX(e.X); } base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (drag) SetFromX(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { drag = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || base.IsInputKey(k); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) Value--;
        else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) Value++;
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var parent = Ui.ParentBg(this);
        g.Clear(parent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float w = Width - Pad * 2, cy = Height / 2f, cx = Pad + Frac * w;
        using (var path = Ui.Round(new RectangleF(Pad, cy - 2, w, 4), 2))
        using (var b = new SolidBrush(Ui.Mix(Ui.Input, Ui.Text, 0.1))) g.FillPath(b, path);
        if (cx > Pad + 1)
            using (var path = Ui.Round(new RectangleF(Pad, cy - 2, cx - Pad, 4), 2))
            using (var b = new SolidBrush(Ui.Accent)) g.FillPath(b, path);
        float d = drag || hover ? 18 : 16;
        using (var b = new SolidBrush(Ui.Accent)) g.FillEllipse(b, cx - d / 2, cy - d / 2, d, d);
        using (var b = new SolidBrush(parent)) g.FillEllipse(b, cx - 3.5f, cy - 3.5f, 7, 7);
    }
}

// Cartão arredondado que agrupa uma seção.
class Card : FlowLayoutPanel
{
    public Card()
    {
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16, 12, 16, 14);
        Margin = new Padding(0, 0, 12, 12);
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.ParentBg(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Ui.Round(new RectangleF(0, 0, Width - 1, Height - 1), 12))
        {
            using (var b = new SolidBrush(BackColor)) g.FillPath(b, path);
            using (var pen = new Pen(Ui.Mix(BackColor, Ui.Text, 0.06), 1)) g.DrawPath(pen, path);
        }
    }
}
