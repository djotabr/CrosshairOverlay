using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

// Desenha o contador do Auto-tecla: uma caixinha por tecla com o nome, o tempo restante e uma barra.
// A caixa fica verde enquanto a tecla está sendo apertada.
static class AutoHud
{
    public static readonly string[] PositionNames = {
        "Abaixo da mira", "Acima da mira", "Canto superior esquerdo", "Canto superior direito",
        "Canto inferior esquerdo", "Canto inferior direito" };

    static readonly Color Box = Color.FromArgb(190, 18, 20, 26);
    static readonly Color Border = Color.FromArgb(220, 80, 85, 98);
    static readonly Color Lit = Color.FromArgb(240, 0, 220, 130);
    static readonly Color Bar = Color.FromArgb(230, 0, 200, 120);

    public static Bitmap Render(KeyStatus[] keys, long now, int scalePct)
    {
        float sc = Util.Clamp(scalePct, 50, 300) / 100f;
        int bw = (int)(78 * sc), bh = (int)(62 * sc), gap = (int)(8 * sc), pad = 2;
        int n = Math.Max(1, keys.Length);
        var bmp = new Bitmap(n * bw + (n - 1) * gap + pad * 2, bh + pad * 2, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        using (var fKey = new Font("Segoe UI", 15f * sc, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var fTime = new Font("Segoe UI", 13f * sc, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            for (int i = 0; i < keys.Length; i++)
            {
                var k = keys[i];
                bool lit = now < k.PressedUntil;
                var r = new RectangleF(pad + i * (bw + gap), pad, bw, bh);
                using (var path = Rounded(r, 8 * sc))
                {
                    using (var b = new SolidBrush(lit ? Lit : Box)) g.FillPath(b, path);
                    using (var p = new Pen(lit ? Color.White : Border, 1.5f * sc)) g.DrawPath(p, path);
                }
                Color txt = lit ? Color.Black : Color.White;
                using (var b = new SolidBrush(txt))
                {
                    g.DrawString(Hotkeys.ToText((int)k.Key), fKey, b, new RectangleF(r.X, r.Y + 4 * sc, r.Width, r.Height * 0.45f), center);
                    double left = Math.Max(0, (k.NextDownAt - now) / 1000.0);
                    string t = lit ? "APERTOU" : left.ToString(left < 10 ? "0.0" : "0") + "s";
                    using (var bt = new SolidBrush(lit ? Color.Black : Color.FromArgb(255, 200, 205, 215)))
                        g.DrawString(t, fTime, bt, new RectangleF(r.X, r.Y + r.Height * 0.45f, r.Width, r.Height * 0.35f), center);
                }
                // barra de progresso até o próximo aperto
                long total = Math.Max(1, k.NextDownAt - k.CountFrom);
                float frac = lit ? 1f : Math.Min(1f, Math.Max(0f, (now - k.CountFrom) / (float)total));
                float bx = r.X + 7 * sc, by = r.Bottom - 9 * sc, bwid = r.Width - 14 * sc, bhgt = 4 * sc;
                using (var b = new SolidBrush(Color.FromArgb(lit ? 90 : 120, lit ? 0 : 255, lit ? 0 : 255, lit ? 0 : 255)))
                    g.FillRectangle(b, bx, by, bwid, bhgt);
                using (var b = new SolidBrush(lit ? Color.Black : Bar))
                    g.FillRectangle(b, bx, by, bwid * frac, bhgt);
            }
        }
        return bmp;
    }

    static GraphicsPath Rounded(RectangleF r, float rad)
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

    // Posição (canto superior esquerdo) do HUD na tela.
    public static Point Place(int pos, Rectangle screen, Point crosshair, Size size)
    {
        const int m = 24;
        switch (pos)
        {
            case 1: return new Point(crosshair.X - size.Width / 2, crosshair.Y - 70 - size.Height);
            case 2: return new Point(screen.Left + m, screen.Top + m);
            case 3: return new Point(screen.Right - m - size.Width, screen.Top + m);
            case 4: return new Point(screen.Left + m, screen.Bottom - m - size.Height);
            case 5: return new Point(screen.Right - m - size.Width, screen.Bottom - m - size.Height);
            default: return new Point(crosshair.X - size.Width / 2, crosshair.Y + 70);
        }
    }
}

// Cartão "Jogo detectado" com o ícone do jogo, mostrado por alguns segundos no overlay.
static class GameToast
{
    public static Bitmap Render(Bitmap icon, string line1, string line2)
    {
        using (var f1 = new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var f2 = new Font("Segoe UI", 13f, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            Size s1, s2;
            using (var tmp = new Bitmap(1, 1)) using (var g0 = Graphics.FromImage(tmp))
            { s1 = Size.Ceiling(g0.MeasureString(line1, f1)); s2 = Size.Ceiling(g0.MeasureString(line2, f2)); }
            int ic = 40, pad = 12;
            int w = pad + ic + 12 + Math.Max(s1.Width, s2.Width) + pad, h = 64;
            var bmp = new Bitmap(w + 4, h + 4, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                var r = new RectangleF(2, 2, w, h);
                using (var path = new GraphicsPath())
                {
                    float d = 18;
                    path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                    path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                    path.CloseFigure();
                    using (var b = new SolidBrush(Color.FromArgb(215, 18, 20, 26))) g.FillPath(b, path);
                    using (var p = new Pen(Color.FromArgb(230, 0, 200, 120), 1.5f)) g.DrawPath(p, path);
                }
                if (icon != null) g.DrawImage(icon, new Rectangle(2 + pad, 2 + (h - ic) / 2, ic, ic));
                float tx = 2 + pad + ic + 12;
                g.DrawString(line1, f1, Brushes.White, tx, 2 + 12);
                using (var b = new SolidBrush(Color.FromArgb(255, 170, 230, 200))) g.DrawString(line2, f2, b, tx, 2 + 34);
            }
            return bmp;
        }
    }
}
