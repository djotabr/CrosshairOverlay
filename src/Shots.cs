using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

class Shot { public string Author; public DateTime Time; public Bitmap Image; }

static class Shots
{
    public static string Dir { get { return Path.Combine(Storage.Dir, "prints"); } }

    // Captura o monitor indicado e devolve o JPG.
    public static byte[] Capture(Rectangle screen)
    {
        using (var bmp = new Bitmap(screen.Width, screen.Height, PixelFormat.Format24bppRgb))
        {
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(screen.Location, Point.Empty, screen.Size);
            return Jpeg(bmp, 80);
        }
    }

    static byte[] Jpeg(Image img, long quality)
    {
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using (var ep = new EncoderParameters(1))
        using (var ms = new MemoryStream())
        {
            ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
            img.Save(ms, codec, ep);
            return ms.ToArray();
        }
    }

    public static Bitmap Decode(byte[] jpg)
    {
        try
        {
            using (var ms = new MemoryStream(jpg))
            using (var img = Image.FromStream(ms))
                return new Bitmap(img);
        }
        catch { return null; }
    }

    // Salva na pasta de prints (mantém os 50 mais novos).
    public static void SaveFile(byte[] jpg, string author, DateTime time)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            string safe = new string(author.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
            File.WriteAllBytes(Path.Combine(Dir, time.ToString("yyyy-MM-dd_HH-mm-ss") + "_" + (safe.Length > 0 ? safe : "print") + ".jpg"), jpg);
            foreach (var old in new DirectoryInfo(Dir).GetFiles("*.jpg").OrderByDescending(f => f.Name).Skip(50)) old.Delete();
        }
        catch { }
    }

    public static Bitmap Thumb(Bitmap src, int size)
    {
        var t = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(t))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            int s = Math.Min(src.Width, src.Height);
            g.DrawImage(src, new Rectangle(0, 0, size, size), new Rectangle((src.Width - s) / 2, (src.Height - s) / 2, s, s), GraphicsUnit.Pixel);
        }
        return t;
    }
}

// Mostra os prints por cima do jogo. ←/→ navega, Esc / clique fecha e volta para o jogo.
class ShotViewer : Form
{
    readonly PictureBox pic;
    readonly Label info;
    readonly Func<List<Shot>> getShots;
    int index;
    IntPtr previous;

    public ShotViewer(Func<List<Shot>> getShots)
    {
        this.getShots = getShots;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.Black;
        KeyPreview = true;
        pic = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
        info = new Label
        {
            Dock = DockStyle.Top, Height = 34, TextAlign = ContentAlignment.MiddleCenter, BackColor = Color.FromArgb(22, 23, 28),
            ForeColor = Color.White, Font = new Font("Segoe UI", 11f, FontStyle.Bold)
        };
        Controls.Add(pic);
        Controls.Add(info);
        pic.Click += (s, e) => Close2();
        KeyDown += (s, e) =>
        {
            e.Handled = true; e.SuppressKeyPress = true;
            var l = getShots();
            if (e.KeyCode == Keys.Left && index > 0) { index--; ShowIndex(); }
            else if (e.KeyCode == Keys.Right && index < l.Count - 1) { index++; ShowIndex(); }
            else if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) Close2();
        };
        Deactivate += (s, e) => { if (Visible) Close2(); };
    }

    void ShowIndex()
    {
        var l = getShots();
        if (l.Count == 0) { pic.Image = null; info.Text = L.T("Nenhum print ainda   •   Esc fecha"); return; }
        index = Util.Clamp(index, 0, l.Count - 1);
        var s = l[index];
        pic.Image = s.Image;
        info.Text = L.T("Print de ") + s.Author + "  —  " + s.Time.ToString("HH:mm:ss") + "   (" + (index + 1) + "/" + l.Count + L.T(")   •   ← → outros   •   Esc fecha");
    }

    public void Open(Rectangle screen)
    {
        previous = Native.GetForegroundWindow();
        Bounds = screen;
        index = getShots().Count - 1;   // o mais novo
        ShowIndex();
        Show();
        Activate();
        Native.ForceForeground(Handle);
    }

    public bool IsOpen { get { return Visible; } }
    public void RefreshList() { if (Visible) { index = getShots().Count - 1; ShowIndex(); } }

    void Close2()
    {
        Hide();
        pic.Image = null;
        if (previous != IntPtr.Zero) Native.ForceForeground(previous);
    }
}
