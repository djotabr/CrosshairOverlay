using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

// Janela transparente, sempre no topo, que ignora o mouse (click-through).
class OverlayForm : Form
{
    public OverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(1, 1);
        Location = new Point(-100, -100);
        Text = "Crosshair Overlay";
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW
                        | Native.WS_EX_TOPMOST | Native.WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    public void SetBitmap(Bitmap bmp, int x, int y)
    {
        IntPtr screenDc = Native.GetDC(IntPtr.Zero);
        IntPtr memDc = Native.CreateCompatibleDC(screenDc);
        IntPtr hBmp = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            hBmp = bmp.GetHbitmap(Color.FromArgb(0));
            old = Native.SelectObject(memDc, hBmp);
            var size = new Native.SIZE(bmp.Width, bmp.Height);
            var src = new Native.POINT(0, 0);
            var dst = new Native.POINT(x, y);
            var blend = new Native.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, Native.ULW_ALPHA);
        }
        finally
        {
            Native.ReleaseDC(IntPtr.Zero, screenDc);
            if (hBmp != IntPtr.Zero) { Native.SelectObject(memDc, old); Native.DeleteObject(hBmp); }
            Native.DeleteDC(memDc);
        }
    }

    public void EnsureTopMost()
    {
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }
}

static class Util
{
    public static int Clamp(int v, int min, int max) { return v < min ? min : (v > max ? max : v); }

    public static Color FromHue(double h)
    {
        h = ((h % 360) + 360) % 360;
        double x = 1 - Math.Abs((h / 60) % 2 - 1);
        double r = 0, g = 0, b = 0;
        if (h < 60) { r = 1; g = x; }
        else if (h < 120) { r = x; g = 1; }
        else if (h < 180) { g = 1; b = x; }
        else if (h < 240) { g = x; b = 1; }
        else if (h < 300) { r = x; b = 1; }
        else { r = 1; b = x; }
        return Color.FromArgb(255, (int)(r * 255), (int)(g * 255), (int)(b * 255));
    }
}

// Desenha a mira em um bitmap com transparência. O centro da mira fica no pixel (size/2, size/2).
static class Renderer
{
    static string imgPath;
    static Bitmap img;

    static Bitmap GetImage(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        if (path != imgPath)
        {
            if (img != null) { img.Dispose(); img = null; }
            imgPath = path;
            try { using (var tmp = new Bitmap(path)) img = new Bitmap(tmp); } catch { img = null; }
        }
        return img;
    }

    public static Bitmap Render(Config c, Color main)
    {
        Bitmap image = c.UseImage ? GetImage(c.ImagePath) : null;
        float t = Math.Max(1, c.Thickness);
        float o = c.Outline ? c.OutlineThickness : 0;

        // Raio máximo ocupado (independe da rotação)
        float e = 2;
        if (c.ShowCross) e = Math.Max(e, c.Gap + c.Length + t);
        if (c.ShowSolidCross) e = Math.Max(e, c.Length + t);
        if (c.ShowChevron) e = Math.Max(e, (c.Gap + c.Length) * 1.42f + t);
        if (c.ShowCircle) e = Math.Max(e, c.Radius + t);
        if (c.ShowSquare) e = Math.Max(e, (c.Radius + t) * 1.42f);
        if (c.ShowDot) e = Math.Max(e, c.DotSize);
        int iw = 0, ih = 0;
        if (image != null)
        {
            iw = Math.Max(1, image.Width * c.ImageScale / 100);
            ih = Math.Max(1, image.Height * c.ImageScale / 100);
            e = Math.Max(e, (float)Math.Sqrt(iw * iw + ih * ih) / 2f);
        }
        e += 2 * o + 3;
        int size = Math.Min((int)Math.Ceiling(e) * 2 + 4, 2400);

        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = c.AntiAlias ? SmoothingMode.AntiAlias : SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            float origin = size / 2 + ((c.Thickness % 2 == 1) ? 0.5f : 0f);
            g.TranslateTransform(origin, origin);
            if (c.Rotation != 0) g.RotateTransform(c.Rotation);

            if (image != null)
                g.DrawImage(image, new RectangleF(-iw / 2f, -ih / 2f, iw, ih));

            Color oc = Color.FromArgb(c.OutlineColor);
            if (o > 0) DrawShapes(g, c, Color.FromArgb(255, oc.R, oc.G, oc.B), t, o);
            DrawShapes(g, c, Color.FromArgb(255, main.R, main.G, main.B), t, 0);
        }

        int a = Util.Clamp(c.Opacity * 255 / 100, 0, 255);
        if (a < 255)
        {
            var outBmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g2 = Graphics.FromImage(outBmp))
            using (var ia = new ImageAttributes())
            {
                var cm = new ColorMatrix();
                cm.Matrix33 = a / 255f;
                ia.SetColorMatrix(cm);
                g2.DrawImage(bmp, new Rectangle(0, 0, size, size), 0, 0, size, size, GraphicsUnit.Pixel, ia);
            }
            bmp.Dispose();
            bmp = outBmp;
        }
        return bmp;
    }

    static void Fill(Graphics g, Brush br, float x, float y, float w, float h, float o)
    {
        g.FillRectangle(br, x - o, y - o, w + 2 * o, h + 2 * o);
    }

    static void DrawShapes(Graphics g, Config c, Color col, float t, float o)
    {
        using (var br = new SolidBrush(col))
        using (var pen = new Pen(col, t + 2 * o))
        {
            pen.LineJoin = LineJoin.Miter;
            if (c.ShowCross && c.Length > 0)
            {
                float h = t / 2f, gap = c.Gap, len = c.Length;
                if (c.ArmRight) Fill(g, br, gap, -h, len, t, o);
                if (c.ArmLeft) Fill(g, br, -gap - len, -h, len, t, o);
                if (c.ArmBottom) Fill(g, br, -h, gap, t, len, o);
                if (c.ArmTop) Fill(g, br, -h, -gap - len, t, len, o);
            }
            if (c.ShowSolidCross && c.Length > 0)
            {
                // Duas barras contínuas que se cruzam no centro (sem espaço). O "Comprimento" é o tamanho de cada braço.
                float h = t / 2f, L = c.Length + h;
                bool horiz = c.ArmLeft || c.ArmRight, vert = c.ArmTop || c.ArmBottom;
                float x0 = c.ArmLeft ? -L : -h, x1 = c.ArmRight ? L : h;
                float y0 = c.ArmTop ? -L : -h, y1 = c.ArmBottom ? L : h;
                if (horiz) Fill(g, br, x0, -h, x1 - x0, t, o);
                if (vert) Fill(g, br, -h, y0, t, y1 - y0, o);
            }
            if (c.ShowCircle) g.DrawEllipse(pen, -c.Radius, -c.Radius, c.Radius * 2, c.Radius * 2);
            if (c.ShowSquare) g.DrawRectangle(pen, -c.Radius, -c.Radius, c.Radius * 2, c.Radius * 2);
            if (c.ShowChevron && c.Length > 0)
            {
                g.DrawLines(pen, new[] {
                    new PointF(-c.Length, c.Gap + c.Length), new PointF(0, c.Gap), new PointF(c.Length, c.Gap + c.Length) });
            }
            if (c.ShowDot)
            {
                float d = c.DotSize;
                float shift = ((c.DotSize % 2 == 1) ? 0.5f : 0f) - ((c.Thickness % 2 == 1) ? 0.5f : 0f);
                var r = new RectangleF(-d / 2f - o + shift, -d / 2f - o + shift, d + 2 * o, d + 2 * o);
                if (c.DotRound) g.FillEllipse(br, r); else g.FillRectangle(br, r);
            }
        }
    }
}
