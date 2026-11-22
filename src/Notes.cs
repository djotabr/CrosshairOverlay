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

    // Estilos do painel no jogo (Config.NotesStyle).
    public static readonly string[] StyleNames = { "Cartão", "Só texto", "Legenda", "Etiquetas", "Linha única", "Barra lateral" };

    class Row { public int Kind; public string Num, Text; }   // Kind: 0 título seu, 1 linha sua, 2 dica, 3 título grupo, 4 linha do grupo

    // Painel do overlay: suas senhas e, se estiver numa sala, as do grupo (últimas MaxShown de cada).
    // Numeração: suas primeiro (1..N), depois as do grupo (N+1..).
    public static Bitmap Render(List<string> lines, List<string> group, string groupTitle, int fontPx, string addKeyText, int style = 0, int bgOpacity = 75)
    {
        fontPx = Util.Clamp(fontPx, 10, 36);
        var rows = new List<Row>();
        bool hasGroup = groupTitle != null;
        int nl = lines.Count;
        if (nl > 0 || !hasGroup)
        {
            rows.Add(new Row { Kind = 0, Text = (hasGroup ? L.T("SUAS SENHAS") : L.T("SENHAS")) + (nl > MaxShown ? L.T("  (últimas ") + MaxShown + L.T(" de ") + nl + ")" : "") });
            if (nl == 0) rows.Add(new Row { Kind = 2, Text = L.T("Nenhuma anotação — ") + addKeyText + L.T(" para anotar") });
            for (int i = Math.Max(0, nl - MaxShown); i < nl; i++) rows.Add(new Row { Kind = 1, Num = (i + 1).ToString(), Text = lines[i] });
        }
        if (hasGroup)
        {
            int G = group.Count;
            rows.Add(new Row { Kind = 3, Text = groupTitle + (G > MaxShown ? L.T("  (últimas ") + MaxShown + L.T(" de ") + G + ")" : "") });
            if (G == 0) rows.Add(new Row { Kind = 2, Text = L.T("Nenhuma senha do grupo ainda") });
            for (int j = Math.Max(0, G - MaxShown); j < G; j++) rows.Add(new Row { Kind = 4, Num = (nl + j + 1).ToString(), Text = group[j] });
        }

        var th = Themes.A;
        int alpha = Util.Clamp(bgOpacity, 0, 100) * 255 / 100;
        using (var fTitle = new Font(th.Font, fontPx * 0.85f, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var fLine = new Font(th.Font, fontPx, FontStyle.Bold, GraphicsUnit.Pixel))
        {
            switch (Util.Clamp(style, 0, StyleNames.Length - 1))
            {
                case 1: return TextOnly(rows, th, fTitle, fLine, fontPx);
                case 2: return Subtitles(rows, th, fTitle, fLine, fontPx, alpha);
                case 3: return Tags(rows, th, fTitle, fLine, fontPx, alpha);
                case 4: return OneLine(rows, th, fTitle, fLine, fontPx, alpha);
                case 5: return SideBar(rows, th, fTitle, fLine, fontPx);
                default: return CardStyle(rows, th, fTitle, fLine, fontPx, alpha);
            }
        }
    }

    // ---- ajudantes ----

    static Color RowColor(MenuTheme th, Row r)
    {
        return r.Kind == 0 ? th.Accent : r.Kind == 3 ? th.Group : r.Kind == 2 ? th.Dim
             : r.Kind == 4 ? Ui.Mix(th.Text, th.Group, 0.3) : th.Text;
    }

    static string Full(Row r) { return r.Num == null ? r.Text : r.Num + ".  " + r.Text; }
    static bool IsTitle(Row r) { return r.Kind == 0 || r.Kind == 3; }

    static readonly StringFormat Typo = StringFormat.GenericTypographic;

    static float Width(Graphics g, string s, Font f)
    {
        var fmt = (StringFormat)Typo.Clone();
        fmt.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
        return g.MeasureString(s, f, 4000, fmt).Width;
    }

    static Graphics Measurer(out Bitmap tmp) { tmp = new Bitmap(1, 1); var g = Graphics.FromImage(tmp); g.TextRenderingHint = TextRenderingHint.AntiAlias; return g; }

    static Graphics Canvas(Bitmap bmp)
    {
        var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        return g;
    }

    // texto com contorno: legível em qualquer fundo do jogo
    static void Outlined(Graphics g, string s, Font f, Color c, float x, float y, float width)
    {
        bool bright = c.GetBrightness() > 0.45f;
        using (var path = new GraphicsPath())
        {
            path.AddString(s, f.FontFamily, (int)f.Style, f.Size, new PointF(x, y), Typo);
            using (var pen = new Pen(bright ? Color.FromArgb(230, 0, 0, 0) : Color.FromArgb(230, 255, 255, 255), width) { LineJoin = LineJoin.Round })
                g.DrawPath(pen, path);
            using (var b = new SolidBrush(c)) g.FillPath(b, path);
        }
    }

    static void Shadowed(Graphics g, string s, Font f, Color c, float x, float y, bool shadow)
    {
        if (shadow) using (var b = new SolidBrush(Color.FromArgb(200, 0, 0, 0))) g.DrawString(s, f, b, x + 1, y + 1, Typo);
        using (var b = new SolidBrush(c)) g.DrawString(s, f, b, x, y, Typo);
    }

    static Color Contrast(Color bg) { return bg.GetBrightness() > 0.6f ? Color.Black : Color.White; }

    // ---- 0: Cartão (caixa com fundo e borda) ----
    static Bitmap CardStyle(List<Row> rows, MenuTheme th, Font fTitle, Font fLine, int fontPx, int alpha)
    {
        float w = 0, lineH = fontPx * 1.45f, titleH = fontPx * 1.5f, gap = fontPx * 0.5f, h = 0;
        int pad = (int)(fontPx * 0.7f);
        Bitmap tmp; using (var g0 = Measurer(out tmp))
            for (int i = 0; i < rows.Count; i++)
            {
                w = Math.Max(w, Width(g0, Full(rows[i]), IsTitle(rows[i]) ? fTitle : fLine));
                h += IsTitle(rows[i]) ? titleH + (i > 0 ? gap : 0) : lineH;
            }
        tmp.Dispose();
        int W = (int)Math.Ceiling(Math.Max(160, w + pad * 2)), H = (int)Math.Ceiling(h + pad * 1.4f);
        var bmp = new Bitmap(W + 4, H + 4, PixelFormat.Format32bppArgb);
        using (var g = Canvas(bmp))
        {
            var r = new RectangleF(2, 2, W, H);
            using (var path = Ui.Round(r, 7))
            {
                using (var b = new SolidBrush(Color.FromArgb(alpha, th.Bg))) g.FillPath(b, path);
                using (var p = new Pen(Color.FromArgb(220, th.Accent), 1.5f)) g.DrawPath(p, path);
            }
            bool band = !th.HeaderTop.IsEmpty;   // Windows XP: faixa azul atrás do primeiro título
            if (band)
            {
                var br = new RectangleF(r.X + 1, r.Y + 1, r.Width - 2, pad * 0.6f + titleH);
                using (var clip = new GraphicsPath())
                {
                    clip.AddArc(br.X, br.Y, 13, 13, 180, 90); clip.AddArc(br.Right - 13, br.Y, 13, 13, 270, 90);
                    clip.AddLine(br.Right, br.Bottom, br.X, br.Bottom); clip.CloseFigure();
                    using (var b = new LinearGradientBrush(br, th.HeaderTop, th.HeaderBottom, 90f)) g.FillPath(b, clip);
                }
            }
            float y = r.Y + pad * 0.6f;
            for (int i = 0; i < rows.Count; i++)
            {
                bool title = IsTitle(rows[i]);
                if (title && i > 0) y += gap;
                var c = band && i == 0 ? Color.White : RowColor(th, rows[i]);
                Shadowed(g, Full(rows[i]), title ? fTitle : fLine, c, r.X + pad, y + fontPx * 0.12f, !title && th.IsDark && alpha > 40);
                y += title ? titleH : lineH;
            }
        }
        return bmp;
    }

    // ---- 1: Só texto (sem caixa, texto com contorno) ----
    static Bitmap TextOnly(List<Row> rows, MenuTheme th, Font fTitle, Font fLine, int fontPx)
    {
        float ow = Math.Max(2.5f, fontPx / 5f), lineH = fontPx * 1.4f, titleH = fontPx * 1.35f, gap = fontPx * 0.45f, w = 0, h = 0;
        Bitmap tmp; using (var g0 = Measurer(out tmp))
            for (int i = 0; i < rows.Count; i++)
            {
                w = Math.Max(w, Width(g0, Full(rows[i]), IsTitle(rows[i]) ? fTitle : fLine));
                h += IsTitle(rows[i]) ? titleH + (i > 0 ? gap : 0) : lineH;
            }
        tmp.Dispose();
        var bmp = new Bitmap((int)Math.Ceiling(w + ow * 2 + 8), (int)Math.Ceiling(h + ow * 2 + 6), PixelFormat.Format32bppArgb);
        using (var g = Canvas(bmp))
        {
            float y = ow + 2;
            for (int i = 0; i < rows.Count; i++)
            {
                bool title = IsTitle(rows[i]);
                if (title && i > 0) y += gap;
                Outlined(g, Full(rows[i]), title ? fTitle : fLine, RowColor(th, rows[i]), ow + 2, y, ow);
                y += title ? titleH : lineH;
            }
        }
        return bmp;
    }

    // ---- 2: Legenda (cada linha numa faixa translúcida) ----
    static Bitmap Subtitles(List<Row> rows, MenuTheme th, Font fTitle, Font fLine, int fontPx, int alpha)
    {
        float padX = fontPx * 0.55f, lineH = fontPx * 1.55f, sp = Math.Max(2, fontPx * 0.18f), gap = fontPx * 0.4f, w = 0, h = 0;
        var widths = new float[rows.Count];
        Bitmap tmp; using (var g0 = Measurer(out tmp))
            for (int i = 0; i < rows.Count; i++)
            {
                widths[i] = Width(g0, Full(rows[i]), IsTitle(rows[i]) ? fTitle : fLine) + padX * 2;
                w = Math.Max(w, widths[i]);
                h += lineH + sp + (IsTitle(rows[i]) && i > 0 ? gap : 0);
            }
        tmp.Dispose();
        var bmp = new Bitmap((int)Math.Ceiling(w + 4), (int)Math.Ceiling(h + 4), PixelFormat.Format32bppArgb);
        using (var g = Canvas(bmp))
        {
            float y = 2;
            for (int i = 0; i < rows.Count; i++)
            {
                bool title = IsTitle(rows[i]);
                if (title && i > 0) y += gap;
                var rect = new RectangleF(2, y, widths[i], lineH);
                Color bg = title ? Color.FromArgb(Math.Max(alpha, 170), rows[i].Kind == 3 ? th.Group : th.Accent) : Color.FromArgb(alpha, th.Bg);
                using (var path = Ui.Round(rect, lineH * 0.22f)) using (var b = new SolidBrush(bg)) g.FillPath(b, path);
                var f = title ? fTitle : fLine;
                var c = title ? Contrast(bg) : RowColor(th, rows[i]);
                float ty = y + (lineH - f.Size * 1.15f) / 2f;
                using (var b = new SolidBrush(c)) g.DrawString(Full(rows[i]), f, b, 2 + padX, ty, Typo);
                y += lineH + sp;
            }
        }
        return bmp;
    }

    // ---- 3: Etiquetas (número numa bolinha + etiqueta com a senha) ----
    static Bitmap Tags(List<Row> rows, MenuTheme th, Font fTitle, Font fLine, int fontPx, int alpha)
    {
        float chipH = fontPx * 1.6f, sp = Math.Max(3, fontPx * 0.25f), gap = fontPx * 0.45f, titleH = fontPx * 1.35f, ow = Math.Max(2f, fontPx / 6f);
        float badge = chipH, padX = fontPx * 0.55f, w = 0, h = 0;
        Bitmap tmp; using (var g0 = Measurer(out tmp))
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                if (IsTitle(r) || r.Kind == 2) { w = Math.Max(w, Width(g0, r.Text, IsTitle(r) ? fTitle : fLine) + ow * 2); h += titleH + (i > 0 ? gap : 0); }
                else { w = Math.Max(w, badge + 4 + Width(g0, r.Text, fLine) + padX * 2); h += chipH + sp; }
            }
        tmp.Dispose();
        var bmp = new Bitmap((int)Math.Ceiling(w + 6), (int)Math.Ceiling(h + 6), PixelFormat.Format32bppArgb);
        using (var g = Canvas(bmp))
        using (var fNum = new Font(fLine.FontFamily, fontPx * 0.8f, FontStyle.Bold, GraphicsUnit.Pixel))
        {
            float y = 3;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                if (IsTitle(r) || r.Kind == 2)
                {
                    if (i > 0) y += gap;
                    Outlined(g, r.Text, IsTitle(r) ? fTitle : fLine, RowColor(th, r), 3 + ow, y + 1, ow);
                    y += titleH;
                    continue;
                }
                Color acc = r.Kind == 4 ? th.Group : th.Accent;
                float tw = Width(g, r.Text, fLine);
                // etiqueta
                var body = new RectangleF(3 + badge / 2, y, badge / 2 + 4 + tw + padX * 2, chipH);
                using (var path = Ui.Round(body, chipH / 2)) using (var b = new SolidBrush(Color.FromArgb(alpha, th.Bg))) g.FillPath(b, path);
                using (var path = Ui.Round(body, chipH / 2)) using (var p = new Pen(Color.FromArgb(160, acc), 1.2f)) g.DrawPath(p, path);
                // bolinha com o número
                using (var b = new SolidBrush(acc)) g.FillEllipse(b, 3, y, badge, badge);
                var ns = g.MeasureString(r.Num, fNum, 400, Typo);
                using (var b = new SolidBrush(Contrast(acc))) g.DrawString(r.Num, fNum, b, 3 + (badge - ns.Width) / 2, y + (badge - fNum.Size * 1.15f) / 2, Typo);
                using (var b = new SolidBrush(RowColor(th, r))) g.DrawString(r.Text, fLine, b, 3 + badge + 4 + padX * 0.6f, y + (chipH - fLine.Size * 1.15f) / 2, Typo);
                y += chipH + sp;
            }
        }
        return bmp;
    }

    // ---- 4: Linha única (tudo numa linha, tipo barra de status) ----
    static Bitmap OneLine(List<Row> rows, MenuTheme th, Font fTitle, Font fLine, int fontPx, int alpha)
    {
        // segmentos: (texto, fonte, cor)
        var segs = new List<Tuple<string, Font, Color>>();
        bool any = rows.Any(r => r.Kind == 1 || r.Kind == 4);
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.Kind == 2 && any) continue;
            if (IsTitle(r)) { if (segs.Count > 0) segs.Add(Tuple.Create("   |   ", fLine, th.Dim)); segs.Add(Tuple.Create(r.Text + "   ", fTitle, RowColor(th, r))); continue; }
            if (r.Kind == 2) { segs.Add(Tuple.Create(r.Text, fLine, th.Dim)); continue; }
            if (i > 0 && !IsTitle(rows[i - 1])) segs.Add(Tuple.Create("   •   ", fLine, th.Dim));
            segs.Add(Tuple.Create(r.Num + "  ", fTitle, r.Kind == 4 ? th.Group : th.Accent));
            segs.Add(Tuple.Create(r.Text, fLine, RowColor(th, r)));
        }
        float pad = fontPx * 0.7f, hgt = fontPx * 1.7f, w = 0;
        var ws = new float[segs.Count];
        Bitmap tmp; using (var g0 = Measurer(out tmp)) for (int i = 0; i < segs.Count; i++) { ws[i] = Width(g0, segs[i].Item1, segs[i].Item2); w += ws[i]; }
        tmp.Dispose();
        var bmp = new Bitmap((int)Math.Ceiling(w + pad * 2 + 4), (int)Math.Ceiling(hgt + 4), PixelFormat.Format32bppArgb);
        using (var g = Canvas(bmp))
        {
            var rect = new RectangleF(2, 2, bmp.Width - 4, hgt);
            using (var path = Ui.Round(rect, hgt / 2))
            {
                using (var b = new SolidBrush(Color.FromArgb(alpha, th.Bg))) g.FillPath(b, path);
                using (var p = new Pen(Color.FromArgb(200, th.Accent), 1.3f)) g.DrawPath(p, path);
            }
            float x = 2 + pad;
            for (int i = 0; i < segs.Count; i++)
            {
                var f = segs[i].Item2;
                using (var b = new SolidBrush(segs[i].Item3)) g.DrawString(segs[i].Item1, f, b, x, 2 + (hgt - f.Size * 1.15f) / 2, Typo);
                x += ws[i];
            }
        }
        return bmp;
    }

    // ---- 5: Barra lateral (sem caixa; barrinha colorida à esquerda, estilo HUD) ----
    static Bitmap SideBar(List<Row> rows, MenuTheme th, Font fTitle, Font fLine, int fontPx)
    {
        float lineH = fontPx * 1.4f, titleH = fontPx * 1.4f, gap = fontPx * 0.5f, bar = Math.Max(3, fontPx / 4f), left = bar + fontPx * 0.7f, w = 0, h = 0;
        Bitmap tmp; using (var g0 = Measurer(out tmp))
            for (int i = 0; i < rows.Count; i++)
            {
                w = Math.Max(w, Width(g0, Full(rows[i]), IsTitle(rows[i]) ? fTitle : fLine));
                h += IsTitle(rows[i]) ? titleH + (i > 0 ? gap : 0) : lineH;
            }
        tmp.Dispose();
        var bmp = new Bitmap((int)Math.Ceiling(left + w + 8), (int)Math.Ceiling(h + 6), PixelFormat.Format32bppArgb);
        using (var g = Canvas(bmp))
        {
            float y = 3, sectionTop = 3;
            Color sectionColor = th.Accent;
            Action<float> drawBar = bottom =>
            {
                using (var path = Ui.Round(new RectangleF(1, sectionTop, bar, Math.Max(bar, bottom - sectionTop)), bar / 2))
                using (var b = new SolidBrush(sectionColor)) g.FillPath(b, path);
            };
            for (int i = 0; i < rows.Count; i++)
            {
                bool title = IsTitle(rows[i]);
                if (title && i > 0) { drawBar(y - 2); y += gap; sectionTop = y; }
                if (title) sectionColor = rows[i].Kind == 3 ? th.Group : th.Accent;
                var c = RowColor(th, rows[i]);
                if (title) Outlined(g, Full(rows[i]), fTitle, c, left, y + 1, Math.Max(2f, fontPx / 7f));
                else Outlined(g, Full(rows[i]), fLine, c, left, y + 1, Math.Max(2f, fontPx / 6f));
                y += title ? titleH : lineH;
            }
            drawBar(y - 2);
        }
        return bmp;
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
