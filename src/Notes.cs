using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

// Anotações de senhas: um item por linha no arquivo senhas.txt (na pasta de dados do app).
static class Notes
{
    static string FilePath { get { return Path.Combine(Storage.Dir, "senhas.txt"); } }

    public static List<string> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return File.ReadAllLines(FilePath, Encoding.UTF8).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        }
        catch { }
        return new List<string>();
    }

    public static void Save(List<string> lines)
    {
        try
        {
            Directory.CreateDirectory(Storage.Dir);
            File.WriteAllLines(FilePath, lines, Encoding.UTF8);
        }
        catch { }
    }

    public const int MaxShown = 20;

    // Painel do overlay: suas senhas e, se estiver numa sala, as do grupo (últimas MaxShown de cada).
    // Numeração: suas primeiro (1..N), depois as do grupo (N+1..).
    public static Bitmap Render(List<string> lines, List<string> group, string groupTitle, int fontPx, string addKeyText)
    {
        fontPx = Util.Clamp(fontPx, 10, 36);
        // kind: 0 título seu, 1 linha sua, 2 dica, 3 título grupo, 4 linha do grupo
        var rows = new List<KeyValuePair<string, int>>();
        bool hasGroup = groupTitle != null;
        int nl = lines.Count;
        if (nl > 0 || !hasGroup)
        {
            rows.Add(new KeyValuePair<string, int>((hasGroup ? L.T("SUAS SENHAS") : L.T("SENHAS")) + (nl > MaxShown ? L.T("  (últimas ") + MaxShown + L.T(" de ") + nl + ")" : ""), 0));
            if (nl == 0) rows.Add(new KeyValuePair<string, int>(L.T("Nenhuma anotação — ") + addKeyText + L.T(" para anotar"), 2));
            for (int i = Math.Max(0, nl - MaxShown); i < nl; i++) rows.Add(new KeyValuePair<string, int>((i + 1) + ".  " + lines[i], 1));
        }
        if (hasGroup)
        {
            int G = group.Count;
            rows.Add(new KeyValuePair<string, int>(groupTitle + (G > MaxShown ? L.T("  (últimas ") + MaxShown + L.T(" de ") + G + ")" : ""), 3));
            if (G == 0) rows.Add(new KeyValuePair<string, int>(L.T("Nenhuma senha do grupo ainda"), 2));
            for (int j = Math.Max(0, G - MaxShown); j < G; j++) rows.Add(new KeyValuePair<string, int>((nl + j + 1) + ".  " + group[j], 4));
        }

        var th = Themes.A;
        using (var fTitle = new Font(th.Font, fontPx * 0.85f, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var fLine = new Font(th.Font, fontPx, FontStyle.Bold, GraphicsUnit.Pixel))
        {
            float w = 0, lineH = fontPx * 1.45f, titleH = fontPx * 1.5f, gap = fontPx * 0.5f;
            int pad = (int)(fontPx * 0.7f);
            float h = 0;
            using (var tmp = new Bitmap(1, 1)) using (var g0 = Graphics.FromImage(tmp))
                for (int i = 0; i < rows.Count; i++)
                {
                    bool title = rows[i].Value == 0 || rows[i].Value == 3;
                    w = Math.Max(w, g0.MeasureString(rows[i].Key, title ? fTitle : fLine).Width);
                    h += title ? titleH + (i > 0 ? gap : 0) : lineH;
                }
            int W = (int)Math.Ceiling(Math.Max(160, w + pad * 2)), H = (int)Math.Ceiling(h + pad * 1.4f);
            var bmp = new Bitmap(W + 4, H + 4, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                var r = new RectangleF(2, 2, W, H);
                using (var path = new GraphicsPath())
                {
                    float d = 14;
                    path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                    path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                    path.CloseFigure();
                    using (var b = new SolidBrush(Color.FromArgb(th.IsDark ? 195 : 225, th.Bg))) g.FillPath(b, path);
                    using (var p = new Pen(Color.FromArgb(220, th.Accent), 1.5f)) g.DrawPath(p, path);
                }
                // tema com faixa no topo (Windows XP): faixa colorida atrás do primeiro título
                if (!th.HeaderTop.IsEmpty)
                {
                    var band = new RectangleF(r.X + 1, r.Y + 1, r.Width - 2, pad * 0.6f + fontPx * 1.5f);
                    using (var clip = new GraphicsPath())
                    {
                        float d2 = 13;
                        clip.AddArc(band.X, band.Y, d2, d2, 180, 90); clip.AddArc(band.Right - d2, band.Y, d2, d2, 270, 90);
                        clip.AddLine(band.Right, band.Bottom, band.X, band.Bottom); clip.CloseFigure();
                        using (var b = new LinearGradientBrush(band, th.HeaderTop, th.HeaderBottom, 90f)) g.FillPath(b, clip);
                    }
                }
                float y = r.Y + pad * 0.6f;
                using (var shadow = new SolidBrush(th.IsDark ? Color.FromArgb(200, 0, 0, 0) : Color.Transparent))
                    for (int i = 0; i < rows.Count; i++)
                    {
                        int k = rows[i].Value;
                        bool title = k == 0 || k == 3;
                        if (title && i > 0) y += gap;
                        Color c = k == 0 ? (i == 0 && !th.HeaderTop.IsEmpty ? Color.White : th.Accent)
                                : k == 3 ? (i == 0 && !th.HeaderTop.IsEmpty ? Color.White : th.Group)
                                : k == 2 ? th.Dim
                                : k == 4 ? Ui.Mix(th.Text, th.Group, 0.3) : th.Text;
                        var f = title ? fTitle : fLine;
                        using (var b = new SolidBrush(c))
                        {
                            if (!title) g.DrawString(rows[i].Key, f, shadow, r.X + pad + 1, y + 1);
                            g.DrawString(rows[i].Key, f, b, r.X + pad, y);
                        }
                        y += title ? titleH : lineH;
                    }
            }
            return bmp;
        }
    }
}

// Caixinha que aparece por cima do jogo para anotar rápido. Enter salva, Esc cancela.
class QuickNoteForm : Form
{
    readonly TextBox tb;
    readonly MenuTheme th = Themes.A;
    IntPtr previous;
    readonly Label head;
    bool groupAvailable, toGroup;
    public event Action<string, bool> Saved;   // texto, vai para o grupo?

    public QuickNoteForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = th.Bg;
        ForeColor = th.Text;
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(440, 74);
        Padding = new Padding(1);
        KeyPreview = true;

        var l = head = new Label
        {
            Text = L.T("Nova anotação   (Enter salva  •  Esc cancela)"), ForeColor = th.Accent,
            Font = new Font(th.Font, 9f, FontStyle.Bold), Location = new Point(12, 9), AutoSize = true
        };
        tb = new TextBox
        {
            Location = new Point(12, 34), Width = 416, BackColor = th.Input, ForeColor = th.Text,
            BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 12f)
        };
        Controls.AddRange(new Control[] { l, tb });
        KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Finish(true); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Finish(false); }
        };
        Deactivate += (s, e) => { Dbg("Deactivate visible=" + Visible); if (Visible) Finish(false); };
    }

    void UpdateHead()
    {
        if (!groupAvailable)
        {
            head.Text = L.T("Nova anotação   (Enter salva  •  Esc cancela)");
            head.ForeColor = th.Accent;
        }
        else if (toGroup)
        {
            head.Text = L.T("Nova anotação  →  GRUPO   (Tab: só minha  •  Enter salva  •  Esc)");
            head.ForeColor = th.Group;
        }
        else
        {
            head.Text = L.T("Nova anotação  →  SÓ MINHA   (Tab: grupo  •  Enter salva  •  Esc)");
            head.ForeColor = th.Accent;
        }
        Invalidate();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Tab && groupAvailable) { toGroup = !toGroup; UpdateHead(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var c = groupAvailable && toGroup ? th.Group : th.Accent;
        using (var pen = new Pen(c, 2)) e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    public static void Dbg(string m) { string f = Environment.GetEnvironmentVariable("CROSSHAIR_DEBUG"); if (!string.IsNullOrEmpty(f)) try { File.AppendAllText(f, DateTime.Now.ToString("HH:mm:ss.fff") + " " + m + "\r\n"); } catch { } }

    public void Open(Point at, bool groupAvailable, bool toGroup)
    {
        this.groupAvailable = groupAvailable; this.toGroup = toGroup;
        UpdateHead();
        Dbg("Open fg=" + Native.GetForegroundWindow());
        previous = Native.GetForegroundWindow();
        tb.Text = "";
        Location = at;
        Show();
        Activate();
        Native.ForceForeground(Handle);
        tb.Focus();
        Dbg("Opened active=" + (Form.ActiveForm == this) + " fg=" + Native.GetForegroundWindow() + " me=" + Handle);
    }

    void Finish(bool save)
    {
        Dbg("Finish save=" + save + " text=" + tb.Text);
        string text = tb.Text.Trim();
        Hide();
        if (save && text.Length > 0 && Saved != null) Saved(text, groupAvailable && toGroup);
        if (previous != IntPtr.Zero) Native.ForceForeground(previous);   // volta para o jogo
    }
}

// Lista rápida por cima do jogo para apagar senhas já usadas.
// 1-9 apaga na hora • ↑↓ + Delete apaga a selecionada • Z desfaz • Esc/Enter fecha.
class DeleteNotesForm : Form
{
    readonly ListBox list;
    readonly MenuTheme th = Themes.A;
    readonly Label hint;
    IntPtr previous;
    readonly Func<List<string>> getNotes;
    readonly Action<int> deleteAt;
    readonly Func<bool> undo;

    public DeleteNotesForm(Func<List<string>> getNotes, Action<int> deleteAt, Func<bool> undo)
    {
        this.getNotes = getNotes; this.deleteAt = deleteAt; this.undo = undo;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = th.Bg;
        ForeColor = th.Text;
        Font = new Font("Segoe UI", 10f);
        Padding = new Padding(1);
        KeyPreview = true;

        var title = new Label
        {
            Text = L.T("APAGAR SENHA"), ForeColor = th.IsDark ? Color.FromArgb(255, 110, 110) : Color.FromArgb(200, 40, 40),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Location = new Point(12, 8), AutoSize = true
        };
        hint = new Label
        {
            Text = L.T("1-9 apaga na hora  •  ↑↓ + Delete  •  Z desfaz  •  Esc fecha"),
            ForeColor = th.Dim, Font = new Font(th.Font, 8.5f), Location = new Point(12, 28), AutoSize = true
        };
        list = new ListBox
        {
            Location = new Point(12, 50), Width = 456, BorderStyle = BorderStyle.None,
            BackColor = Ui.Mix(th.Bg, th.Text, 0.06), ForeColor = th.Text, Font = new Font(th.Font, 12f),
            IntegralHeight = false
        };
        Controls.AddRange(new Control[] { title, hint, list });
        ClientSize = new Size(480, 100);

        KeyDown += OnKey;
        Deactivate += (s, e) => { QuickNoteForm.Dbg("DEL Deactivate visible=" + Visible + " fg=" + Native.GetForegroundWindow()); if (Visible) Close2(); };
    }

    void Fill(int select)
    {
        var notes = getNotes();
        list.Items.Clear();
        for (int i = 0; i < notes.Count; i++) list.Items.Add((i + 1) + ".  " + notes[i]);
        if (notes.Count == 0) list.Items.Add(L.T("(nenhuma senha anotada)"));
        list.Height = Math.Min(12, Math.Max(1, list.Items.Count)) * list.ItemHeight + 6;
        ClientSize = new Size(480, list.Bottom + 12);
        if (notes.Count > 0) list.SelectedIndex = Util.Clamp(select, 0, notes.Count - 1);
    }

    void Remove(int idx)
    {
        if (idx < 0 || idx >= getNotes().Count) return;
        deleteAt(idx);
        Fill(idx);
        Invalidate();
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        e.Handled = true; e.SuppressKeyPress = true;
        Keys k = e.KeyCode;
        if (k >= Keys.D1 && k <= Keys.D9) Remove(k - Keys.D1);
        else if (k >= Keys.NumPad1 && k <= Keys.NumPad9) Remove(k - Keys.NumPad1);
        else if (k == Keys.Delete || k == Keys.Back) Remove(list.SelectedIndex);
        else if (k == Keys.Z) { if (undo()) Fill(getNotes().Count - 1); }
        else if (k == Keys.Escape || k == Keys.Enter) Close2();
        else if (k == Keys.Up && list.SelectedIndex > 0) list.SelectedIndex--;
        else if (k == Keys.Down && list.SelectedIndex < list.Items.Count - 1) list.SelectedIndex++;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using (var pen = new Pen(Color.FromArgb(220, 80, 80), 2)) e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    public void Open(Point at, Rectangle screen)
    {
        previous = Native.GetForegroundWindow();
        Fill(0);
        Location = new Point(Math.Min(at.X, screen.Right - Width), Math.Max(screen.Y, Math.Min(at.Y, screen.Bottom - Height)));
        Show();
        Activate();
        Native.ForceForeground(Handle);
        list.Focus();
    }

    void Close2()
    {
        Hide();
        if (previous != IntPtr.Zero) Native.ForceForeground(previous);   // volta para o jogo
    }
}
