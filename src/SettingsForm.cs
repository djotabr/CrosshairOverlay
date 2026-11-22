using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

class BufferedPanel : Panel
{
    public BufferedPanel() { DoubleBuffered = true; ResizeRedraw = true; }
}

class SettingsForm : Form
{
    static readonly Color BG = Color.FromArgb(22, 23, 28);
    static readonly Color PANEL = Color.FromArgb(32, 34, 41);
    static readonly Color INPUT = Color.FromArgb(46, 49, 58);
    static readonly Color BORDER = Color.FromArgb(72, 76, 88);
    static readonly Color FG = Color.FromArgb(232, 234, 240);
    static readonly Color DIM = Color.FromArgb(150, 155, 170);
    static readonly Color ACCENT = Color.FromArgb(0, 210, 130);
    static readonly Color ACCENT_DARK = Color.FromArgb(0, 140, 90);
    const int RowW = 505;

    readonly CrosshairApp app;
    bool suppress;
    readonly List<Action> refreshers = new List<Action>();
    readonly List<Button> tabButtons = new List<Button>();
    readonly List<Control> pages = new List<Control>();
    BufferedPanel preview;
    ComboBox profileCombo;
    Label hotkeyStatus;
    int zoom = 2, bgMode = 2, currentPage;
    public bool AllowClose;

    Config C { get { return app.Cfg; } }

    public SettingsForm(CrosshairApp app)
    {
        this.app = app;
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Crosshair Overlay — by " + AppInfo.Author;
        FormBorderStyle = FormBorderStyle.None;   // menu sem bordas, semitransparente, por cima do jogo
        MaximizeBox = false;
        Opacity = Util.Clamp(app.Settings.MenuOpacity, 40, 100) / 100.0;
        Padding = new Padding(1);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(900, 640);
        BackColor = BG;
        ForeColor = FG;
        Font = new Font("Segoe UI", 9f);
        TopMost = true;
        Icon = app.AppIcon;

        BuildLeft();
        BuildTabs();
        var close = Btn("✕", 30, (s, e) => Hide());
        close.Location = new Point(ClientSize.Width - 44, 14);
        close.Height = 34;
        close.FlatAppearance.MouseOverBackColor = Color.FromArgb(190, 50, 50);
        Controls.Add(close);
        MakeDraggable(this);
        ResumeLayout(false);
        PerformLayout();

        FormClosing += (s, e) =>
        {
            if (!AllowClose && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        };
    }

    // Sem barra de título: arrastar pelo fundo ou pelos textos do cabeçalho move o menu.
    void MakeDraggable(Control c)
    {
        c.MouseDown += (s, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            Native.ReleaseCapture();
            Native.SendMessage(Handle, 0xA1 /* WM_NCLBUTTONDOWN */, (IntPtr)2 /* HTCAPTION */, IntPtr.Zero);
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using (var pen = new Pen(ACCENT_DARK, 1)) e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    // ---------- helpers de UI ----------

    Button Btn(string text, int w, EventHandler click)
    {
        var b = new Button
        {
            Text = text, Width = w, Height = 28, FlatStyle = FlatStyle.Flat,
            BackColor = INPUT, ForeColor = FG, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 6, 6),
            UseVisualStyleBackColor = false
        };
        b.FlatAppearance.BorderColor = BORDER;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(62, 66, 78);
        if (click != null) b.Click += click;
        return b;
    }

    Label Lbl(string text, int w)
    {
        return new Label { Text = text, Width = w, Height = 28, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 0, 6, 0) };
    }

    FlowLayoutPanel NewPage()
    {
        var p = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
            Location = new Point(336, 54), Size = new Size(550, 574), BackColor = PANEL,
            Padding = new Padding(14, 4, 8, 14), Visible = false
        };
        Controls.Add(p);
        pages.Add(p);
        return p;
    }

    void Header(Control p, string text)
    {
        p.Controls.Add(new Label
        {
            Text = text.ToUpperInvariant(), Width = RowW, Height = 30, TextAlign = ContentAlignment.BottomLeft,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = ACCENT, Margin = new Padding(0, 8, 0, 4)
        });
    }

    void Note(Control p, string text)
    {
        p.Controls.Add(new Label
        {
            Text = text, AutoSize = true, MaximumSize = new Size(RowW, 0), ForeColor = DIM, Margin = new Padding(0, 4, 0, 4)
        });
    }

    FlowLayoutPanel Flow(Control p, params Control[] cs)
    {
        var f = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
            MaximumSize = new Size(RowW, 0), MinimumSize = new Size(RowW, 0), Margin = new Padding(0, 2, 0, 2)
        };
        f.Controls.AddRange(cs);
        p.Controls.Add(f);
        return f;
    }

    CheckBox Chk(string text, Func<bool> get, Action<bool> set)
    {
        var c = new CheckBox { Text = text, AutoSize = true, ForeColor = FG, Margin = new Padding(0, 4, 16, 4), Cursor = Cursors.Hand };
        c.CheckedChanged += (s, e) => { if (suppress) return; set(c.Checked); Changed(); };
        refreshers.Add(() => c.Checked = get());
        return c;
    }

    void Slider(Control p, string label, int min, int max, Func<int> get, Action<int> set)
    {
        var row = new Panel { Width = RowW, Height = 34, Margin = new Padding(0) };
        var l = new Label { Text = label, Location = new Point(0, 8), Size = new Size(165, 20) };
        var tb = new TrackBar
        {
            Minimum = min, Maximum = max, TickStyle = TickStyle.None, AutoSize = false,
            Location = new Point(165, 3), Size = new Size(262, 28), BackColor = PANEL,
            SmallChange = 1, LargeChange = Math.Max(1, (max - min) / 20)
        };
        var nud = new NumericUpDown
        {
            Minimum = min, Maximum = max, Location = new Point(433, 6), Width = 70,
            BackColor = INPUT, ForeColor = FG, BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Right
        };
        tb.ValueChanged += (s, e) =>
        {
            if (suppress) return;
            suppress = true; nud.Value = tb.Value; suppress = false;
            set(tb.Value); Changed();
        };
        nud.ValueChanged += (s, e) =>
        {
            if (suppress) return;
            suppress = true; tb.Value = (int)nud.Value; suppress = false;
            set((int)nud.Value); Changed();
        };
        refreshers.Add(() => { int v = Util.Clamp(get(), min, max); tb.Value = v; nud.Value = v; });
        row.Controls.AddRange(new Control[] { l, tb, nud });
        p.Controls.Add(row);
    }

    void ColorRow(Control p, string label, Func<int> get, Action<int> set)
    {
        var b = new Button { Width = 80, Height = 26, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 8, 0), UseVisualStyleBackColor = false };
        b.FlatAppearance.BorderColor = BORDER;
        var hex = new Label { Width = 90, Height = 26, TextAlign = ContentAlignment.MiddleLeft, ForeColor = DIM };
        b.Click += (s, e) =>
        {
            using (var cd = new ColorDialog { Color = Color.FromArgb(get()), FullOpen = true, AnyColor = true })
            {
                if (cd.ShowDialog(this) == DialogResult.OK)
                {
                    set(Color.FromArgb(255, cd.Color).ToArgb());
                    RefreshControls(); Changed();
                }
            }
        };
        refreshers.Add(() =>
        {
            var c = Color.FromArgb(get());
            b.BackColor = c;
            b.FlatAppearance.MouseOverBackColor = c;
            hex.Text = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        });
        Flow(p, Lbl(label, 159), b, hex);
    }

    Button Swatch(int argb)
    {
        var b = new Button { Width = 30, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(argb), Margin = new Padding(0, 0, 6, 6), Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
        b.FlatAppearance.BorderColor = BORDER;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(argb);
        b.Click += (s, e) => { C.MainColor = argb; C.Rainbow = false; RefreshControls(); Changed(); };
        return b;
    }

    void Changed()
    {
        if (suppress) return;
        app.ConfigChanged();
    }

    public void RefreshControls()
    {
        suppress = true;
        try { foreach (var r in refreshers) r(); }
        finally { suppress = false; }
        preview.Invalidate();
    }

    public void ReloadAll()
    {
        suppress = true;
        profileCombo.Items.Clear();
        foreach (var p in app.Profiles) profileCombo.Items.Add(p);
        profileCombo.SelectedItem = app.Settings.ActiveProfile;
        suppress = false;
        if (hotkeyStatus != null) hotkeyStatus.Text = app.HotkeyStatus;
        RefreshControls();
    }

    public void RefreshPreview() { preview.Invalidate(); }

    // ---------- coluna esquerda: prévia + perfis ----------

    void BuildLeft()
    {
        var title = new Label { Text = "CROSSHAIR OVERLAY", Font = new Font("Segoe UI", 14f, FontStyle.Bold), ForeColor = ACCENT, Location = new Point(14, 10), AutoSize = true, Cursor = Cursors.SizeAll };
        Controls.Add(title);
        MakeDraggable(title);
        var credit = new Label { Text = "Feito por", ForeColor = DIM, Location = new Point(16, 40), AutoSize = true };
        var author = new Label { Text = AppInfo.Author, ForeColor = FG, Font = new Font("Segoe UI", 9f, FontStyle.Bold), AutoSize = true };
        var ver = new Label { Text = "v" + AppInfo.Version, ForeColor = DIM, AutoSize = true };
        Controls.AddRange(new Control[] { credit, author, ver });
        MakeDraggable(credit); MakeDraggable(author); MakeDraggable(ver);
        author.Location = new Point(credit.Right, 40);
        ver.Location = new Point(author.Right + 6, 40);
        Load += (s, e) => { author.Left = credit.Right; ver.Left = author.Right + 6; };

        preview = new BufferedPanel { Location = new Point(16, 64), Size = new Size(300, 300) };
        preview.Paint += PaintPreview;
        Controls.Add(preview);

        int x = 16;
        foreach (int z in new[] { 1, 2, 4 })
        {
            int zz = z;
            var b = Btn(z + "x", 40, (s, e) => { zoom = zz; preview.Invalidate(); });
            b.Location = new Point(x, 370);
            Controls.Add(b);
            x += 44;
        }
        string[] bgs = { "Escuro", "Claro", "Cena" };
        x = 146;
        for (int i = 0; i < 3; i++)
        {
            int ii = i;
            var b = Btn(bgs[i], 54, (s, e) => { bgMode = ii; preview.Invalidate(); });
            b.Location = new Point(x, 370);
            Controls.Add(b);
            x += 58;
        }

        Controls.Add(new Label { Text = "PERFIL ATIVO", Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = ACCENT, Location = new Point(16, 414), AutoSize = true });
        profileCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
            BackColor = INPUT, ForeColor = FG, Location = new Point(16, 436), Width = 300
        };
        profileCombo.SelectedIndexChanged += (s, e) =>
        {
            if (!suppress && profileCombo.SelectedItem != null) app.SwitchProfile((string)profileCombo.SelectedItem);
        };
        Controls.Add(profileCombo);

        var bNew = Btn("Novo", 72, (s, e) => NewProfile(false)); bNew.Location = new Point(16, 470);
        var bDup = Btn("Duplicar", 72, (s, e) => NewProfile(true)); bDup.Location = new Point(92, 470);
        var bRen = Btn("Renomear", 72, (s, e) => RenameProfile()); bRen.Location = new Point(168, 470);
        var bDel = Btn("Excluir", 72, (s, e) => DeleteProfile()); bDel.Location = new Point(244, 470);
        var bExp = Btn("Exportar código", 148, (s, e) => ExportCode()); bExp.Location = new Point(16, 504);
        var bImp = Btn("Importar código", 148, (s, e) => ImportCode()); bImp.Location = new Point(168, 504);
        Controls.AddRange(new Control[] { bNew, bDup, bRen, bDel, bExp, bImp });

        var help = new Label { ForeColor = DIM, Location = new Point(16, 548), Size = new Size(300, 84) };
        refreshers.Add(() => help.Text =
            "Abrir/fechar este menu:  " + Hotkeys.ToText(app.Settings.MenuKey) +
            "\nMostrar/ocultar mira:  " + Hotkeys.ToText(app.Settings.ToggleKey) +
            "\nPróximo perfil:  " + Hotkeys.ToText(app.Settings.NextProfileKey) +
            "\nLigar/desligar auto-tecla:  " + Hotkeys.ToText(app.Settings.AutoToggleKey) +
            "\nAnotar / mostrar senhas:  " + Hotkeys.ToText(app.Settings.NotesAddKey) + " / " + Hotkeys.ToText(app.Settings.NotesToggleKey) +
            "");
        Controls.Add(help);
    }

    void PaintPreview(object sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = preview.ClientRectangle;
        if (bgMode == 0)
        {
            using (var b = new LinearGradientBrush(r, Color.FromArgb(52, 56, 66), Color.FromArgb(16, 18, 22), 90f)) g.FillRectangle(b, r);
        }
        else if (bgMode == 1)
        {
            using (var b = new LinearGradientBrush(r, Color.FromArgb(240, 242, 245), Color.FromArgb(185, 192, 200), 90f)) g.FillRectangle(b, r);
        }
        else
        {
            int horizon = r.Height * 56 / 100;
            var sky = new Rectangle(0, 0, r.Width, horizon);
            var ground = new Rectangle(0, horizon, r.Width, r.Height - horizon);
            using (var b = new LinearGradientBrush(sky, Color.FromArgb(105, 155, 210), Color.FromArgb(205, 220, 232), 90f)) g.FillRectangle(b, sky);
            using (var b = new LinearGradientBrush(ground, Color.FromArgb(150, 135, 105), Color.FromArgb(85, 75, 58), 90f)) g.FillRectangle(b, ground);
            using (var b = new SolidBrush(Color.FromArgb(170, 160, 145))) g.FillRectangle(b, 0, horizon - 90, 80, 90);
            using (var b = new SolidBrush(Color.FromArgb(140, 128, 112))) g.FillRectangle(b, 210, horizon - 120, 90, 120);
            using (var b = new SolidBrush(Color.FromArgb(120, 110, 96))) g.FillRectangle(b, 90, horizon - 40, 110, 40);
            using (var b = new SolidBrush(Color.FromArgb(60, 55, 50))) g.FillRectangle(b, 236, horizon - 80, 22, 40);
        }

        using (var bmp = Renderer.Render(app.Cfg, app.CurrentColor()))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            int w = bmp.Width * zoom;
            int cx = r.Width / 2, cy = r.Height / 2;
            g.DrawImage(bmp, new Rectangle(cx - (bmp.Width / 2) * zoom, cy - (bmp.Height / 2) * zoom, w, w));
        }
        using (var b = new SolidBrush(Color.FromArgb(150, 0, 0, 0))) g.FillRectangle(b, 6, 6, 74, 20);
        g.DrawString("Prévia " + zoom + "x", Font, Brushes.White, 10, 8);
    }

    void NewProfile(bool dup)
    {
        string name = Prompt.Show(this, dup ? "Duplicar perfil" : "Novo perfil", "Nome do perfil:",
            dup ? app.Settings.ActiveProfile + " (cópia)" : "Novo perfil");
        if (name == null) return;
        if (!app.CreateProfile(name, dup ? app.Cfg.Clone() : new Config()))
            MessageBox.Show(this, "Nome inválido ou já existente.", "Perfil", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    void RenameProfile()
    {
        string name = Prompt.Show(this, "Renomear perfil", "Novo nome:", app.Settings.ActiveProfile);
        if (name == null || name == app.Settings.ActiveProfile) return;
        if (!app.RenameProfile(name))
            MessageBox.Show(this, "Nome inválido ou já existente.", "Perfil", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    void DeleteProfile()
    {
        if (app.Profiles.Count <= 1)
        {
            MessageBox.Show(this, "É preciso ter pelo menos um perfil.", "Perfil", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this, "Excluir o perfil \"" + app.Settings.ActiveProfile + "\"?", "Excluir perfil",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            app.DeleteActiveProfile();
    }

    void ExportCode()
    {
        try
        {
            Clipboard.SetText(Storage.ToShareCode(app.Cfg));
            MessageBox.Show(this, "Código da mira copiado para a área de transferência.\nCompartilhe com amigos — eles usam \"Importar código\".",
                "Exportar", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Erro"); }
    }

    void ImportCode()
    {
        string def = "";
        try
        {
            if (Clipboard.ContainsText())
            {
                string t = Clipboard.GetText().Trim();
                if (t.StartsWith("XH1-")) def = t;
            }
        }
        catch { }
        string code = Prompt.Show(this, "Importar código", "Cole o código da mira (será aplicado ao perfil atual):", def);
        if (code == null) return;
        var c = Storage.FromShareCode(code);
        if (c == null)
        {
            MessageBox.Show(this, "Código inválido.", "Importar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        c.Monitor = app.Cfg.Monitor;
        app.Cfg = c;
        RefreshControls();
        Changed();
    }

    // ---------- abas ----------

    void BuildTabs()
    {
        string[] names = { "Forma", "Cores", "Posição", "Atalhos && Extras", "Auto-tecla", "Senhas" };
        int[] widths = { 70, 66, 74, 122, 88, 68 };
        int tx = 336;
        for (int i = 0; i < names.Length; i++)
        {
            int idx = i;
            var b = Btn(names[i], widths[i], (s, e) => ShowPage(idx));
            b.Height = 34;
            b.Location = new Point(tx, 14);
            b.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            Controls.Add(b);
            tabButtons.Add(b);
            tx += widths[i] + 6;
        }
        BuildShapePage(NewPage());
        BuildColorPage(NewPage());
        BuildPositionPage(NewPage());
        BuildExtrasPage(NewPage());
        BuildAutoPage(NewPage());
        BuildNotesPage(NewPage());
        ShowPage(0);
    }

    void ShowPage(int idx)
    {
        currentPage = idx;
        for (int i = 0; i < pages.Count; i++)
        {
            pages[i].Visible = i == idx;
            tabButtons[i].BackColor = i == idx ? ACCENT_DARK : INPUT;
            tabButtons[i].FlatAppearance.BorderColor = i == idx ? ACCENT : BORDER;
        }
    }

    void BuildShapePage(FlowLayoutPanel p)
    {
        Header(p, "Predefinições rápidas");
        var btns = new List<Control>();
        for (int i = 0; i < Presets.Names.Length; i++)
        {
            int ii = i;
            btns.Add(Btn(Presets.Names[i], 118, (s, e) => { Presets.Apply(C, ii); RefreshControls(); Changed(); }));
        }
        Flow(p, btns.ToArray());

        Header(p, "Componentes (combine à vontade)");
        Flow(p,
            Chk("Cruz", () => C.ShowCross, v => C.ShowCross = v),
            Chk("Cruz sem cortes", () => C.ShowSolidCross, v => C.ShowSolidCross = v),
            Chk("Ponto central", () => C.ShowDot, v => C.ShowDot = v),
            Chk("Ponto redondo", () => C.DotRound, v => C.DotRound = v),
            Chk("Círculo", () => C.ShowCircle, v => C.ShowCircle = v),
            Chk("Quadrado", () => C.ShowSquare, v => C.ShowSquare = v),
            Chk("Chevron ( ^ )", () => C.ShowChevron, v => C.ShowChevron = v));

        Header(p, "Braços da cruz");
        Flow(p,
            Chk("Cima", () => C.ArmTop, v => C.ArmTop = v),
            Chk("Baixo", () => C.ArmBottom, v => C.ArmBottom = v),
            Chk("Esquerda", () => C.ArmLeft, v => C.ArmLeft = v),
            Chk("Direita", () => C.ArmRight, v => C.ArmRight = v));

        Header(p, "Dimensões");
        Slider(p, "Comprimento", 0, 200, () => C.Length, v => C.Length = v);
        Slider(p, "Espessura", 1, 30, () => C.Thickness, v => C.Thickness = v);
        Slider(p, "Espaço central (gap)", 0, 100, () => C.Gap, v => C.Gap = v);
        Slider(p, "Tamanho do ponto", 1, 30, () => C.DotSize, v => C.DotSize = v);
        Slider(p, "Raio círculo/quadrado", 2, 200, () => C.Radius, v => C.Radius = v);
        Slider(p, "Rotação (graus)", 0, 359, () => C.Rotation, v => C.Rotation = v);
        Flow(p, Chk("Suavização (anti-aliasing) — bordas mais suaves", () => C.AntiAlias, v => C.AntiAlias = v));

        Header(p, "Imagem personalizada (PNG com transparência)");
        var imgLabel = new Label { AutoSize = true, ForeColor = DIM, Margin = new Padding(0, 7, 0, 0) };
        refreshers.Add(() => imgLabel.Text = string.IsNullOrEmpty(C.ImagePath) ? "(nenhuma imagem)" : Path.GetFileName(C.ImagePath));
        Flow(p,
            Chk("Usar imagem", () => C.UseImage, v => C.UseImage = v),
            Btn("Escolher...", 90, (s, e) => ChooseImage()),
            Btn("Remover", 80, (s, e) => { C.ImagePath = ""; C.UseImage = false; RefreshControls(); Changed(); }),
            imgLabel);
        Slider(p, "Escala da imagem (%)", 10, 500, () => C.ImageScale, v => C.ImageScale = v);
    }

    void ChooseImage()
    {
        using (var d = new OpenFileDialog { Filter = "Imagens|*.png;*.gif;*.bmp;*.jpg;*.jpeg|Todos|*.*", Title = "Escolher imagem da mira" })
        {
            if (d.ShowDialog(this) != DialogResult.OK) return;
            C.ImagePath = d.FileName;
            C.UseImage = true;
            RefreshControls();
            Changed();
        }
    }

    void BuildColorPage(FlowLayoutPanel p)
    {
        Header(p, "Cor principal");
        ColorRow(p, "Cor da mira", () => C.MainColor, v => C.MainColor = v);
        int[] sw = {
            unchecked((int)0xFF00FF00), unchecked((int)0xFFAFFF00), unchecked((int)0xFF00FFFF), unchecked((int)0xFF1E90FF),
            unchecked((int)0xFFFF00FF), unchecked((int)0xFFFF69B4), unchecked((int)0xFFFF0000), unchecked((int)0xFFFF8C00),
            unchecked((int)0xFFFFFF00), unchecked((int)0xFFFFFFFF), unchecked((int)0xFF808080), unchecked((int)0xFF000000) };
        var sws = new List<Control>();
        foreach (int c in sw) sws.Add(Swatch(c));
        Flow(p, sws.ToArray());
        Slider(p, "Opacidade (%)", 10, 100, () => C.Opacity, v => C.Opacity = v);

        Header(p, "Contorno (outline)");
        Flow(p, Chk("Ativar contorno", () => C.Outline, v => C.Outline = v));
        Slider(p, "Espessura do contorno", 1, 6, () => C.OutlineThickness, v => C.OutlineThickness = v);
        ColorRow(p, "Cor do contorno", () => C.OutlineColor, v => C.OutlineColor = v);

        Header(p, "Efeitos");
        Flow(p, Chk("Modo RGB (arco-íris animado)", () => C.Rainbow, v => C.Rainbow = v));
        Slider(p, "Velocidade do RGB", 1, 20, () => C.RainbowSpeed, v => C.RainbowSpeed = v);
        Note(p, "Dica: ciano, magenta e verde-limão se destacam na maioria dos mapas. " +
                "O contorno preto mantém a mira visível em fundos claros.");
    }

    void Nudge(int dx, int dy)
    {
        C.OffsetX += dx; C.OffsetY += dy;
        RefreshControls(); Changed();
    }

    void BuildPositionPage(FlowLayoutPanel p)
    {
        Header(p, "Monitor");
        var mc = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = INPUT, ForeColor = FG, Width = 330 };
        refreshers.Add(() =>
        {
            mc.Items.Clear();
            var sc = Screen.AllScreens;
            for (int i = 0; i < sc.Length; i++)
                mc.Items.Add("Monitor " + (i + 1) + "  —  " + sc[i].Bounds.Width + "x" + sc[i].Bounds.Height + (sc[i].Primary ? "  (principal)" : ""));
            mc.SelectedIndex = Util.Clamp(C.Monitor, 0, sc.Length - 1);
        });
        mc.SelectedIndexChanged += (s, e) => { if (suppress) return; C.Monitor = mc.SelectedIndex; Changed(); };
        Flow(p, Lbl("Exibir no", 80), mc);

        Header(p, "Deslocamento a partir do centro (pixels)");
        Slider(p, "Horizontal (X)", -1500, 1500, () => C.OffsetX, v => C.OffsetX = v);
        Slider(p, "Vertical (Y)", -1500, 1500, () => C.OffsetY, v => C.OffsetY = v);

        Header(p, "Ajuste fino");
        Flow(p,
            Btn("↑", 40, (s, e) => Nudge(0, -1)),
            Btn("↓", 40, (s, e) => Nudge(0, 1)),
            Btn("←", 40, (s, e) => Nudge(-1, 0)),
            Btn("→", 40, (s, e) => Nudge(1, 0)),
            Btn("Centralizar", 100, (s, e) => { C.OffsetX = 0; C.OffsetY = 0; RefreshControls(); Changed(); }),
            Btn("Posicionar clicando na tela", 200, (s, e) => PickPosition()));
        Note(p, "Dentro do jogo: Ctrl+Alt+Shift + setas move a mira 1 pixel; Ctrl+Alt+Shift+Home centraliza " +
                "(pode ser desativado na aba Atalhos & Extras).\n\n" +
                "Útil para jogos cuja mira não fica exatamente no centro da tela, ou para a terceira pessoa.");
    }

    void PickPosition()
    {
        var screens = Screen.AllScreens;
        var s = screens[Util.Clamp(C.Monitor, 0, screens.Length - 1)];
        Hide();
        using (var pp = new PlacePicker(s))
        {
            pp.ShowDialog();
            if (pp.Picked.HasValue)
            {
                C.OffsetX = pp.Picked.Value.X - (s.Bounds.X + s.Bounds.Width / 2);
                C.OffsetY = pp.Picked.Value.Y - (s.Bounds.Y + s.Bounds.Height / 2);
            }
        }
        Show();
        Activate();
        RefreshControls();
        Changed();
    }

    TextBox HotkeyBox(Func<int> get, Action<int> set)
    {
        var tb = new TextBox
        {
            ReadOnly = true, Width = 220, BackColor = INPUT, ForeColor = FG, BorderStyle = BorderStyle.FixedSingle,
            Cursor = Cursors.Hand, TextAlign = HorizontalAlignment.Center, ShortcutsEnabled = false, Margin = new Padding(0, 3, 6, 3)
        };
        refreshers.Add(() => { if (!tb.Focused) tb.Text = Hotkeys.ToText(get()); });
        tb.Enter += (s, e) =>
        {
            app.SuspendHotkeys();
            tb.Text = "Pressione as teclas...";
            tb.BackColor = Color.FromArgb(18, 70, 50);
        };
        tb.Leave += (s, e) =>
        {
            tb.BackColor = INPUT;
            tb.Text = Hotkeys.ToText(get());
            app.RegisterHotkeys();
            hotkeyStatus.Text = app.HotkeyStatus;
        };
        tb.PreviewKeyDown += (s, e) => { e.IsInputKey = true; };
        tb.KeyDown += (s, e) =>
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            Keys k = e.KeyCode;
            if (k == Keys.ControlKey || k == Keys.ShiftKey || k == Keys.Menu || k == Keys.LWin || k == Keys.RWin)
            {
                tb.Text = Hotkeys.ModText(e.Modifiers) + "...";
                return;
            }
            if (k != Keys.Escape)
            {
                set((k == Keys.Back || k == Keys.Delete) ? 0 : (int)e.KeyData);
                app.SaveSettings();
            }
            tabButtons[currentPage].Focus();
            RefreshControls();
        };
        return tb;
    }

    void BuildExtrasPage(FlowLayoutPanel p)
    {
        Header(p, "Atalhos globais (funcionam dentro do jogo)");
        hotkeyStatus = new Label { AutoSize = true, MaximumSize = new Size(RowW, 0), ForeColor = DIM, Margin = new Padding(0, 4, 0, 4) };
        Flow(p, Lbl("Abrir/fechar menu", 170), HotkeyBox(() => app.Settings.MenuKey, v => app.Settings.MenuKey = v));
        Flow(p, Lbl("Mostrar/ocultar mira", 170), HotkeyBox(() => app.Settings.ToggleKey, v => app.Settings.ToggleKey = v));
        Flow(p, Lbl("Próximo perfil", 170), HotkeyBox(() => app.Settings.NextProfileKey, v => app.Settings.NextProfileKey = v));
        p.Controls.Add(hotkeyStatus);
        Note(p, "Clique no campo e pressione a nova combinação. Backspace remove o atalho, Esc cancela.");
        Flow(p, Chk("Ajuste fino com Ctrl+Alt+Shift + setas (Home centraliza)", () => app.Settings.NudgeKeys,
            v => { app.Settings.NudgeKeys = v; app.SaveSettings(); app.RegisterHotkeys(); hotkeyStatus.Text = app.HotkeyStatus; }));

        Header(p, "Jogo detectado");
        var gameIcon = new PictureBox { Size = new Size(40, 40), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 4, 10, 4) };
        var gameLbl = new Label { AutoSize = true, MaximumSize = new Size(RowW - 56, 0), ForeColor = FG, Margin = new Padding(0, 4, 0, 4) };
        var mapLbl = new Label { AutoSize = true, MaximumSize = new Size(RowW, 0), ForeColor = DIM, Margin = new Padding(0, 4, 0, 4) };
        refreshers.Add(() =>
        {
            string g = app.CurrentGame;
            string linked = app.LinkedProfile(g);
            gameIcon.Image = app.CurrentGameIcon;
            gameIcon.Visible = app.CurrentGameIcon != null;
            gameLbl.Text = g.Length == 0 ? "Nenhum jogo detectado ainda — abra o jogo e volte para ele uma vez."
                : "Em execução: " + g + ".exe" + (app.CurrentGameTitle.Length > 0 ? "   (\"" + app.CurrentGameTitle + "\")" : "") +
                  "\nPerfil vinculado: " + (linked ?? "nenhum");
            var map = app.GameMap();
            mapLbl.Text = map.Count == 0 ? "Nenhum jogo vinculado." :
                "Vínculos:\n" + string.Join("\n", System.Linq.Enumerable.Select(map, kv => "   " + kv.Key + ".exe  →  " + kv.Value));
        });
        Flow(p, gameIcon, gameLbl);
        Flow(p,
            Btn("Vincular perfil atual a este jogo", 230, (s, e) =>
            {
                if (app.CurrentGame.Length == 0) return;
                var m = app.GameMap(); m[app.CurrentGame] = app.Settings.ActiveProfile; app.SetGameMap(m); RefreshControls();
            }),
            Btn("Desvincular", 110, (s, e) =>
            {
                var m = app.GameMap(); m.Remove(app.CurrentGame); app.SetGameMap(m); RefreshControls();
            }));
        var appsCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = INPUT, ForeColor = FG, Width = 330 };
        var apps = new List<Process>();
        Action fillApps = () =>
        {
            foreach (var x in apps) x.Dispose();
            apps = app.RunningApps();
            appsCombo.Items.Clear();
            foreach (var x in apps) appsCombo.Items.Add(x.ProcessName + "  —  " + x.MainWindowTitle);
        };
        appsCombo.DropDown += (s, e) => fillApps();
        appsCombo.SelectionChangeCommitted += (s, e) =>
        {
            int i = appsCombo.SelectedIndex;
            if (i >= 0 && i < apps.Count) { app.SelectGameManually(apps[i]); RefreshControls(); }
        };
        Flow(p, Lbl("Não é esse? Escolher:", 160), appsCombo);
        p.Controls.Add(mapLbl);
        Flow(p, Chk("Trocar de perfil automaticamente quando o jogo vinculado estiver em foco", () => app.Settings.AutoSwitchProfiles,
            v => { app.Settings.AutoSwitchProfiles = v; app.SaveSettings(); }));
        Flow(p, Chk("Só mostrar o overlay com o jogo em foco (some ao minimizar ou sair do jogo)", () => app.Settings.OnlyInGame,
            v => app.SetOnlyInGame(v)));
        Note(p, "O jogo é identificado pelo programa da janela em foco. Escolha o perfil de mira, volte ao jogo, " +
                "abra este menu e clique em \"Vincular\".");

        Header(p, "Atualização (GitHub e grupo)");
        var ver = new Label { AutoSize = true, MaximumSize = new Size(RowW, 0), Margin = new Padding(0, 4, 0, 4), ForeColor = FG };
        refreshers.Add(() =>
        {
            long staged = Updater.StagedBuild;
            ver.Text = "Esta versão: " + AppInfo.Version + "  (" + Updater.VersionText + ")" +
                (staged > BuildInfo.Build ? "\nVersão nova baixada — instala ao fechar o app." : "") +
                (app.UpdateStatus.Length > 0 ? "\n" + app.UpdateStatus : "");
        });
        p.Controls.Add(ver);
        Flow(p, Chk("Baixar versões novas automaticamente (GitHub e grupo)", () => app.Settings.AutoUpdate, v => { app.Settings.AutoUpdate = v; app.SaveSettings(); }));
        var upNow = Btn("Atualizar agora", 130, (s, e) => app.UpdateNow());
        var check = Btn("Procurar atualização", 160, (s, e) => app.CheckGitHubUpdate(true));
        refreshers.Add(() => upNow.Enabled = Updater.StagedBuild > BuildInfo.Build);
        if (Updater.IsAuthor)
            Flow(p, check, upNow, Btn("Enviar esta versão ao grupo", 200, (s, e) => app.ShareUpdate(true)));
        else
            Flow(p, check, upNow);
        Note(p, "O app confere o GitHub (" + AppInfo.Repo + ") ao abrir e a cada 3 horas, e baixa sozinho quando sai versão nova. " +
                "Pelo grupo é ainda mais rápido: quando o autor clica em \"Enviar esta versão ao grupo\", quem está na sala recebe na hora. " +
                "A versão só é aceita se estiver assinada pelo autor, então ninguém consegue mandar um programa falso. " +
                "Quem entra depois recebe a versão de quem já tem (o servidor guarda o arquivo por ~3 horas).");

        Header(p, "Menu");
        Slider(p, "Opacidade do menu (%)", 40, 100, () => app.Settings.MenuOpacity,
            v => { app.Settings.MenuOpacity = v; Opacity = v / 100.0; app.SaveSettings(); });
        Note(p, "Arraste o menu pelo título ou por qualquer área vazia. Feche no ✕ ou pelo atalho.");

        Header(p, "Comportamento");
        Flow(p, Chk("Ocultar a mira enquanto segura o botão direito (mirar / ADS)", () => C.HideOnRightClick, v => C.HideOnRightClick = v));
        Flow(p, Chk("Iniciar com o Windows", () => Storage.GetAutoStart(), v => Storage.SetAutoStart(v)));

        Header(p, "Sobre");
        Note(p, AppInfo.Name + " v" + AppInfo.Version + "\nFeito por " + AppInfo.Author + " — © 2026 " + AppInfo.Author + ". Todos os direitos reservados.");

        Header(p, "Dicas");
        Note(p, "• Use o jogo em modo Janela ou Janela sem bordas (Borderless). Em Tela cheia exclusiva o Windows " +
                "não permite desenhar nada por cima do jogo.\n" +
                "• A mira não interage com o mouse nem com o jogo — é apenas uma camada visual.\n" +
                "• Clique com o botão direito no ícone da bandeja para trocar de perfil ou sair.");
        Flow(p,
            Btn("Abrir pasta de perfis", 160, (s, e) => { try { Directory.CreateDirectory(Storage.Dir); Process.Start(Storage.Dir); } catch { } }),
            Btn("Ocultar/mostrar mira", 160, (s, e) => app.ToggleVisible()),
            Btn("Sair do programa", 140, (s, e) => app.Exit()));
    }

    // ---------- Auto-tecla ----------

    // Campo que captura UMA tecla (sem combinação). Backspace/Delete limpa, Esc cancela.
    TextBox KeyBox(Func<int> get, Action<int> set)
    {
        var tb = new TextBox
        {
            ReadOnly = true, Width = 160, BackColor = INPUT, ForeColor = FG, BorderStyle = BorderStyle.FixedSingle,
            Cursor = Cursors.Hand, TextAlign = HorizontalAlignment.Center, ShortcutsEnabled = false, Margin = new Padding(0, 3, 6, 3)
        };
        refreshers.Add(() => { if (!tb.Focused) tb.Text = Hotkeys.ToText(get()); });
        tb.Enter += (s, e) => { app.SuspendHotkeys(); tb.Text = "Pressione uma tecla..."; tb.BackColor = Color.FromArgb(18, 70, 50); };
        tb.Leave += (s, e) => { tb.BackColor = INPUT; tb.Text = Hotkeys.ToText(get()); app.RegisterHotkeys(); };
        tb.PreviewKeyDown += (s, e) => { e.IsInputKey = true; };
        tb.KeyDown += (s, e) =>
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            Keys k = e.KeyCode;
            if (k == Keys.Back || k == Keys.Delete) set(0);
            else if (k != Keys.Escape) set((int)k);
            app.SaveSettings();
            tabButtons[currentPage].Focus();
            RefreshControls();
        };
        return tb;
    }

    // Linha de tempo (guardado em milissegundos). Mostra em segundos ou em ms.
    Panel TimeRow(Control p, string label, bool seconds, Func<int> get, Action<int> set)
    {
        var row = new Panel { Width = RowW, Height = 34, Margin = new Padding(0) };
        var l = new Label { Text = label, Location = new Point(0, 8), Size = new Size(200, 20) };
        var nud = new NumericUpDown
        {
            Minimum = seconds ? 0.01m : 1m, Maximum = seconds ? 3600m : 10000m,
            DecimalPlaces = seconds ? 2 : 0, Increment = seconds ? 0.1m : 10m,
            Location = new Point(200, 5), Width = 90,
            BackColor = INPUT, ForeColor = FG, BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Right
        };
        decimal mul = seconds ? 1000m : 1m;
        var unit = new Label { Text = seconds ? "segundos" : "ms", Location = new Point(296, 8), AutoSize = true, ForeColor = DIM };
        nud.ValueChanged += (s, e) => { if (suppress) return; set((int)Math.Round(nud.Value * mul)); app.SaveSettings(); };
        refreshers.Add(() => nud.Value = Math.Min(nud.Maximum, Math.Max(nud.Minimum, get() / mul)));
        row.Controls.AddRange(new Control[] { l, nud, unit });
        p.Controls.Add(row);
        return row;
    }

    // Bloco "Fixo / Aleatório" com valor fixo ou mínimo-máximo.
    void FixedOrRandom(Control p, bool seconds, Func<bool> getRandom, Action<bool> setRandom,
        string fixedLabel, Func<int> getFixed, Action<int> setFixed,
        Func<int> getMin, Action<int> setMin, Func<int> getMax, Action<int> setMax)
    {
        var rbFixed = new RadioButton { Text = "Fixo", AutoSize = true, ForeColor = FG, Margin = new Padding(0, 4, 16, 4) };
        var rbRand = new RadioButton { Text = "Aleatório (entre mínimo e máximo)", AutoSize = true, ForeColor = FG, Margin = new Padding(0, 4, 16, 4) };
        Flow(p, rbFixed, rbRand);
        var fixedRow = TimeRow(p, fixedLabel, seconds, getFixed, setFixed);
        var minRow = TimeRow(p, "Mínimo", seconds, getMin, setMin);
        var maxRow = TimeRow(p, "Máximo", seconds, getMax, setMax);
        rbRand.CheckedChanged += (s, e) =>
        {
            fixedRow.Visible = !rbRand.Checked; minRow.Visible = maxRow.Visible = rbRand.Checked;
            if (suppress) return;
            setRandom(rbRand.Checked); app.SaveSettings();
        };
        refreshers.Add(() =>
        {
            bool r = getRandom();
            rbRand.Checked = r; rbFixed.Checked = !r;
            fixedRow.Visible = !r; minRow.Visible = maxRow.Visible = r;
        });
    }

    void BuildAutoPage(FlowLayoutPanel p)
    {
        var S = app.Settings;

        Header(p, "Status");
        var status = new Label { Width = 200, Height = 28, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
        var startBtn = Btn("Ligar", 120, (s, e) => app.ToggleAuto());
        refreshers.Add(() =>
        {
            bool on = app.Auto.Running;
            status.Text = on ? "● LIGADO" : "○ Desligado";
            status.ForeColor = on ? ACCENT : DIM;
            startBtn.Text = on ? "Parar" : "Ligar";
        });
        Flow(p, status, startBtn);

        Header(p, "Teclas (até 3)");
        Flow(p, Lbl("Tecla 1", 80), KeyBox(() => S.AutoKey1, v => S.AutoKey1 = v));
        Flow(p, Lbl("Tecla 2", 80), KeyBox(() => S.AutoKey2, v => S.AutoKey2 = v));
        Flow(p, Lbl("Tecla 3", 80), KeyBox(() => S.AutoKey3, v => S.AutoKey3 = v));
        Note(p, "Clique no campo e pressione a tecla. Backspace deixa o campo vazio (\"Nenhum\").");
        var rbSeq = new RadioButton { Text = "Em sequência (1 → 2 → 3)", AutoSize = true, ForeColor = FG, Margin = new Padding(0, 4, 16, 4) };
        var rbTog = new RadioButton { Text = "Todas juntas (combinação)", AutoSize = true, ForeColor = FG, Margin = new Padding(0, 4, 16, 4) };
        rbTog.CheckedChanged += (s, e) => { if (suppress) return; S.AutoTogether = rbTog.Checked; app.SaveSettings(); };
        refreshers.Add(() => { rbTog.Checked = S.AutoTogether; rbSeq.Checked = !S.AutoTogether; });
        Flow(p, rbSeq, rbTog);

        Header(p, "Intervalo entre cada pressionamento");
        FixedOrRandom(p, true, () => S.AutoRandom, v => S.AutoRandom = v,
            "Intervalo", () => S.AutoIntervalMs, v => S.AutoIntervalMs = v,
            () => S.AutoMinMs, v => S.AutoMinMs = v, () => S.AutoMaxMs, v => S.AutoMaxMs = v);
        Note(p, "Em sequência, o intervalo vale entre cada tecla: 1, espera, 2, espera, 3, espera, 1...\n" +
                "Com \"Todas juntas\", vale entre cada combinação.");

        Header(p, "Tempo segurando cada tecla");
        FixedOrRandom(p, false, () => S.AutoHoldRandom, v => S.AutoHoldRandom = v,
            "Duração", () => S.AutoHoldMs, v => S.AutoHoldMs = v,
            () => S.AutoHoldMinMs, v => S.AutoHoldMinMs = v, () => S.AutoHoldMaxMs, v => S.AutoHoldMaxMs = v);

        Header(p, "Aviso sonoro");
        Flow(p, Chk("Tocar um bipe a cada aperto (tom diferente para cada tecla)", () => S.AutoSound, v => { S.AutoSound = v; app.SaveSettings(); }));
        Slider(p, "Volume do bipe (%)", 0, 100, () => S.AutoVolume, v => { S.AutoVolume = v; app.SaveSettings(); });
        Flow(p, Btn("Testar som", 110, (s, e) => { Beeper.Play(0, S.AutoVolume); })); 

        Header(p, "Contador no overlay");
        Flow(p, Chk("Mostrar contador regressivo de cada tecla na tela", () => S.AutoHud, v => { S.AutoHud = v; app.SaveSettings(); }));
        var posCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = INPUT, ForeColor = FG, Width = 240 };
        posCombo.Items.AddRange(AutoHud.PositionNames);
        posCombo.SelectedIndexChanged += (s, e) => { if (suppress) return; S.AutoHudPos = posCombo.SelectedIndex; app.SaveSettings(); };
        refreshers.Add(() => posCombo.SelectedIndex = Util.Clamp(S.AutoHudPos, 0, AutoHud.PositionNames.Length - 1));
        Flow(p, Lbl("Posição", 80), posCombo);
        Slider(p, "Tamanho do contador (%)", 50, 300, () => S.AutoHudScale, v => { S.AutoHudScale = v; app.SaveSettings(); });
        Note(p, "Cada tecla aparece numa caixinha com o tempo até o próximo aperto. A caixa fica verde quando a tecla é apertada.");

        Header(p, "Atalho para ligar/desligar");
        Flow(p, Lbl("Ligar/desligar", 170), HotkeyBox(() => S.AutoToggleKey, v => S.AutoToggleKey = v));
        Note(p, "• Sempre começa desligado ao abrir o programa.\n" +
                "• As teclas vão para a janela em foco (o jogo). Enquanto esta janela de configurações " +
                "estiver em foco, nada é enviado.\n" +
                "• Atenção: alguns jogos com anti-cheat proíbem automação de teclas — use por sua conta e risco.");
    }

    // ---------- Senhas ----------

    TextBox Input(int w)
    {
        return new TextBox { Width = w, BackColor = INPUT, ForeColor = FG, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10f), Margin = new Padding(0, 3, 6, 3) };
    }

    void BuildGroupSection(FlowLayoutPanel p)
    {
        var S = app.Settings;
        Header(p, "Senhas do grupo (online, grátis)");
        Flow(p, Chk("Sincronizar senhas com o grupo", () => S.SyncEnabled, v => { S.SyncEnabled = v; app.SaveSettings(); app.RestartSync(); }));

        var name = Input(200);
        name.TextChanged += (s, e) => { if (suppress) return; S.SyncName = name.Text.Trim(); app.SaveSettings(); };
        refreshers.Add(() => { if (!name.Focused) name.Text = S.SyncName.Length > 0 ? S.SyncName : app.SyncName; });
        Flow(p, Lbl("Seu nome", 120), name);

        var room = Input(250);
        room.Font = new Font("Consolas", 11f);
        refreshers.Add(() => { if (!room.Focused) room.Text = S.SyncRoom; });
        Flow(p, Lbl("Código da sala", 120), room);

        Action join = () =>
        {
            string code = GroupSync.Normalize(room.Text);
            if (!GroupSync.ValidRoom(code)) { MessageBox.Show(this, "Cole o código da sala (o que o amigo te mandou) ou crie uma sala nova.", "Grupo"); return; }
            S.SyncRoom = code; S.SyncEnabled = true;
            if (name.Text.Trim().Length > 0) S.SyncName = name.Text.Trim();
            app.SaveSettings(); app.RestartSync(); RefreshControls();
        };
        Flow(p,
            Btn("Entrar na sala", 120, (s, e) => join()),
            Btn("Criar sala nova", 130, (s, e) =>
            {
                if (S.SyncRoom.Length > 0 && MessageBox.Show(this, "Criar uma sala nova? Você sai da sala atual (os amigos precisam do código novo).",
                        "Grupo", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                room.Text = GroupSync.NewRoomCode(); join();
            }),
            Btn("Copiar código", 120, (s, e) => { try { if (S.SyncRoom.Length > 0) Clipboard.SetText(S.SyncRoom); } catch { } }));

        var status = new Label { AutoSize = true, MaximumSize = new Size(RowW, 0), Margin = new Padding(0, 4, 0, 4), Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        refreshers.Add(() =>
        {
            if (app.Sync == null)
            {
                status.Text = S.SyncEnabled ? "● Sem sala — crie uma ou cole o código do amigo" : "● Desligado";
                status.ForeColor = DIM;
            }
            else
            {
                status.Text = "● " + (app.Sync.Connected ? "Conectado" : app.Sync.Status) + "   •   " + app.Sync.Notes.Count + " senha(s) do grupo";
                status.ForeColor = app.Sync.Connected ? Color.FromArgb(0, 220, 130) : Color.FromArgb(255, 170, 60);
            }
        });
        p.Controls.Add(status);

        Flow(p, Lbl("Tirar print e enviar", 170), HotkeyBox(() => S.ShotKey, v => S.ShotKey = v));
        Flow(p, Lbl("Ver prints recebidos", 170), HotkeyBox(() => S.ShotViewKey, v => S.ShotViewKey = v));
        Flow(p,
            Btn("Ver prints", 100, (s, e) => app.ViewShots()),
            Btn("Abrir pasta de prints", 160, (s, e) =>
            {
                try { System.IO.Directory.CreateDirectory(Shots.Dir); System.Diagnostics.Process.Start("explorer.exe", "\"" + Shots.Dir + "\""); } catch { }
            }));
        Note(p, "O print é da tela onde está a mira. Use o jogo em modo janela sem borda (borderless): em tela cheia exclusiva o print pode sair preto. " +
                "Os amigos veem um aviso com a miniatura e abrem com o atalho de \"Ver prints\" (← → passa, Esc fecha).");
        Flow(p, Chk("Anotações novas vão para o grupo (na caixinha, Tab troca)", () => S.NotesDefaultGroup,
            v => { S.NotesDefaultGroup = v; app.SaveSettings(); }));
        Flow(p, Btn("Apagar todas do grupo", 170, (s, e) =>
        {
            if (app.Sync == null || app.Sync.Notes.Count == 0) return;
            if (MessageBox.Show(this, "Apagar todas as senhas do grupo? Elas somem para todos da sala.", "Grupo",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) app.Sync.DeleteAll();
        }));
        Note(p, "Como usar: um de vocês clica em \"Criar sala nova\" e manda o código para os outros (Discord, WhatsApp). " +
                "Cada um cola o código e clica em \"Entrar na sala\". Pronto: o que alguém anotar no grupo aparece para todos em 1-2 segundos, " +
                "e o que for apagado some para todos.\n\n" +
                "Usa o serviço gratuito ntfy.sh. As senhas vão criptografadas com o código da sala — sem o código ninguém consegue ler. " +
                "Não compartilhe o código fora do grupo.");
    }

    void NotesCorner(int c)
    {
        var sc = app.NotesScreen();
        const int m = 16;
        app.Settings.NotesX = (c == 1 || c == 3) ? Math.Max(0, sc.Width - app.notesWidth - m) : m;
        app.Settings.NotesY = (c == 2 || c == 3) ? Math.Max(0, sc.Height - app.notesHeight - m) : m;
        app.SaveSettings(); app.UpdateNotes(); RefreshControls();
    }

    void PickNotesPosition()
    {
        var sc = app.NotesScreen();
        Hide();
        var s = Screen.FromRectangle(sc);
        using (var pp = new PlacePicker(s))
        {
            pp.ShowDialog();
            if (pp.Picked.HasValue)
            {
                app.Settings.NotesX = Math.Max(0, pp.Picked.Value.X - sc.X);
                app.Settings.NotesY = Math.Max(0, pp.Picked.Value.Y - sc.Y);
                app.SaveSettings(); app.UpdateNotes();
            }
        }
        Show(); Activate(); RefreshControls();
    }

    void BuildNotesPage(FlowLayoutPanel p)
    {
        var S = app.Settings;
        Header(p, "Suas senhas (só neste PC)");
        Flow(p, Chk("Mostrar as senhas na tela", () => S.NotesShow, v => { S.NotesShow = v; app.SaveSettings(); app.UpdateNotes(); }));
        Note(p, "Uma anotação por linha. Tudo é salvo automaticamente. As senhas do grupo ficam na seção abaixo.");
        var box = new TextBox
        {
            Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, Width = RowW, Height = 230,
            BackColor = INPUT, ForeColor = FG, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 11f),
            Margin = new Padding(0, 2, 0, 6)
        };
        box.TextChanged += (s, e) =>
        {
            if (suppress) return;
            var lines = new List<string>();
            foreach (var l in box.Lines) if (l.Trim().Length > 0) lines.Add(l.Trim());
            app.SetNotes(lines);
        };
        refreshers.Add(() => { if (!box.Focused) box.Text = string.Join("\r\n", app.NoteLines); });
        p.Controls.Add(box);
        Flow(p,
            Btn("Apagar senha…", 120, (s, e) => app.OpenDeleteNotes()),
            Btn("Apagar a última", 130, (s, e) =>
            {
                if (app.NoteLines.Count == 0) return;
                var l = new List<string>(app.NoteLines); l.RemoveAt(l.Count - 1); app.SetNotes(l); RefreshControls();
            }),
            Btn("Apagar todas", 120, (s, e) =>
            {
                if (app.NoteLines.Count == 0) return;
                if (MessageBox.Show(this, "Apagar todas as anotações?", "Senhas", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                { app.SetNotes(new List<string>()); RefreshControls(); }
            }),
            Btn("Copiar tudo", 110, (s, e) => { try { if (app.NoteLines.Count > 0) Clipboard.SetText(string.Join("\r\n", app.NoteLines)); } catch { } }));
        Slider(p, "Tamanho do texto", 10, 36, () => S.NotesFontSize, v => { S.NotesFontSize = v; app.SaveSettings(); app.UpdateNotes(); });

        BuildGroupSection(p);

        Header(p, "Posição do bloco");
        Flow(p,
            Btn("Posicionar clicando na tela", 200, (s, e) => PickNotesPosition()),
            Btn("↖", 36, (s, e) => NotesCorner(0)), Btn("↗", 36, (s, e) => NotesCorner(1)),
            Btn("↙", 36, (s, e) => NotesCorner(2)), Btn("↘", 36, (s, e) => NotesCorner(3)));
        Slider(p, "Horizontal (X)", 0, 7680, () => S.NotesX, v => { S.NotesX = v; app.SaveSettings(); app.UpdateNotes(); });
        Slider(p, "Vertical (Y)", 0, 4320, () => S.NotesY, v => { S.NotesY = v; app.SaveSettings(); app.UpdateNotes(); });
        Note(p, "\"Posicionar clicando\": clique onde deve ficar o canto superior esquerdo do bloco. As setas levam o bloco para um dos cantos.");

        Header(p, "Atalhos (funcionam dentro do jogo)");
        Flow(p, Lbl("Anotar rápido", 170), HotkeyBox(() => S.NotesAddKey, v => { S.NotesAddKey = v; app.UpdateNotes(); }));
        Flow(p, Lbl("Mostrar/ocultar senhas", 170), HotkeyBox(() => S.NotesToggleKey, v => S.NotesToggleKey = v));
        Flow(p, Lbl("Apagar senha usada", 170), HotkeyBox(() => S.NotesDelKey, v => S.NotesDelKey = v));
        Note(p, "No jogo, aperte o atalho de \"Anotar rápido\", digite (ex.: Porta do laboratório 4521) e aperte Enter. " +
                "A caixinha some e você volta direto para o jogo. Esc cancela.\n\n" +
                "Para apagar uma senha já usada: aperte o atalho de \"Apagar senha usada\" e depois o número dela (1-9). " +
                "Z desfaz se apagar a errada. Também funciona digitar -3 na caixinha de anotar para apagar a senha 3.\n\n" +
                "Numa sala de grupo, a caixinha mostra para onde vai a anotação (GRUPO ou SÓ MINHA). Aperte Tab para trocar.\n\n" +
                "As anotações ficam no arquivo senhas.txt, na pasta de perfis.");
    }
}
