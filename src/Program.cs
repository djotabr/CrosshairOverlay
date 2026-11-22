using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // depois de uma atualização: espera a versão antiga fechar
        int ai = Array.IndexOf(args, "--after-update");
        int oldPid;
        if (ai >= 0 && ai + 1 < args.Length && int.TryParse(args[ai + 1], out oldPid))
            try { using (var old = Process.GetProcessById(oldPid)) old.WaitForExit(15000); } catch { }
        try { System.Net.ServicePointManager.SecurityProtocol |= (System.Net.SecurityProtocolType)3072; } catch { }   // TLS 1.2
        Updater.CleanupOld();
        // versão nova baixada e ainda não instalada (ex.: o app foi fechado à força): instala agora
        if (Updater.StagedBuild > BuildInfo.Build && Updater.Apply(true)) return;
        bool created;
        // com CROSSHAIR_DATA (pasta de teste) pode rodar uma segunda cópia separada
        string data = Environment.GetEnvironmentVariable("CROSSHAIR_DATA");
        string mutexName = "CrosshairOverlay_SingleInstance_7f3a" + (string.IsNullOrEmpty(data) ? "" : "_" + (data.ToLowerInvariant().GetHashCode() & 0x7fffffff));
        using (var m = new Mutex(true, mutexName, out created))
        {
            if (!created)
            {
                MessageBox.Show("O Crosshair Overlay já está em execução.\nProcure o ícone na bandeja do sistema (perto do relógio).",
                    "Crosshair Overlay", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new CrosshairApp());
        }
    }
}

static class Hotkeys
{
    public static string ModText(Keys m)
    {
        string s = "";
        if ((m & Keys.Control) != 0) s += "Ctrl+";
        if ((m & Keys.Alt) != 0) s += "Alt+";
        if ((m & Keys.Shift) != 0) s += "Shift+";
        return s;
    }

    public static string ToText(int v)
    {
        if (v == 0) return "Nenhum";
        Keys k = (Keys)v;
        Keys code = k & Keys.KeyCode;
        string name;
        if (code >= Keys.D0 && code <= Keys.D9) name = ((int)(code - Keys.D0)).ToString();
        else if (code >= Keys.NumPad0 && code <= Keys.NumPad9) name = "Num" + (int)(code - Keys.NumPad0);
        else name = code.ToString();
        return ModText(k & Keys.Modifiers) + name;
    }

    public static void Split(int v, out uint mods, out uint vk)
    {
        Keys k = (Keys)v;
        mods = 0;
        if ((k & Keys.Control) != 0) mods |= Native.MOD_CONTROL;
        if ((k & Keys.Alt) != 0) mods |= Native.MOD_ALT;
        if ((k & Keys.Shift) != 0) mods |= Native.MOD_SHIFT;
        vk = (uint)(k & Keys.KeyCode);
    }
}

class HotkeyWindow : NativeWindow, IDisposable
{
    public event Action<int> HotkeyPressed;
    readonly List<int> ids = new List<int>();

    public HotkeyWindow() { CreateHandle(new CreateParams()); }

    public bool Register(int id, int keys, bool repeat)
    {
        if (keys == 0) return true;
        uint mods, vk;
        Hotkeys.Split(keys, out mods, out vk);
        if (vk == 0) return true;
        bool ok = Native.RegisterHotKey(Handle, id, mods | (repeat ? 0 : Native.MOD_NOREPEAT), vk);
        if (ok) ids.Add(id);
        return ok;
    }

    public void UnregisterAll()
    {
        foreach (int id in ids) Native.UnregisterHotKey(Handle, id);
        ids.Clear();
    }

    // ---- botão direito do mouse via Raw Input (ativado só quando a opção está ligada) ----
    public event Action<bool> RightButton;
    bool rawOn;
    IntPtr rawBuf = IntPtr.Zero;
    const int RawBufSize = 128;

    public void SetMouseListening(bool on)
    {
        if (on == rawOn) return;
        var dev = new Native.RAWINPUTDEVICE
        {
            UsagePage = 0x01, Usage = 0x02,
            Flags = on ? Native.RIDEV_INPUTSINK : Native.RIDEV_REMOVE,
            Target = on ? Handle : IntPtr.Zero
        };
        if (Native.RegisterRawInputDevices(new[] { dev }, 1, (uint)Marshal.SizeOf(typeof(Native.RAWINPUTDEVICE))))
            rawOn = on;
    }

    void HandleRawInput(IntPtr hRaw)
    {
        if (rawBuf == IntPtr.Zero) rawBuf = Marshal.AllocHGlobal(RawBufSize);
        uint header = (uint)(8 + 2 * IntPtr.Size);
        uint size = RawBufSize;
        if (Native.GetRawInputData(hRaw, Native.RID_INPUT, rawBuf, ref size, header) == unchecked((uint)-1)) return;
        if (Marshal.ReadInt32(rawBuf, 0) != Native.RIM_TYPEMOUSE) return;
        ushort flags = (ushort)Marshal.ReadInt16(rawBuf, (int)header + 4);
        if (RightButton == null) return;
        if ((flags & Native.RI_MOUSE_RIGHT_DOWN) != 0) RightButton(true);
        if ((flags & Native.RI_MOUSE_RIGHT_UP) != 0) RightButton(false);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY && HotkeyPressed != null) HotkeyPressed(m.WParam.ToInt32());
        else if (m.Msg == Native.WM_INPUT) HandleRawInput(m.LParam);
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterAll();
        SetMouseListening(false);
        DestroyHandle();
        if (rawBuf != IntPtr.Zero) { Marshal.FreeHGlobal(rawBuf); rawBuf = IntPtr.Zero; }
    }
}

// Tela semi-transparente para escolher a posição da mira com um clique.
class PlacePicker : Form
{
    public Point? Picked { get; private set; }

    public PlacePicker(Screen screen)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = screen.Bounds;
        BackColor = Color.Black;
        Opacity = 0.55;
        TopMost = true;
        ShowInTaskbar = false;
        Cursor = Cursors.Cross;
        KeyPreview = true;
        DoubleBuffered = true;

        var l = new Label
        {
            Text = "Clique onde você quer a mira   •   Esc para cancelar",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            AutoSize = true,
            BackColor = Color.Transparent
        };
        Controls.Add(l);
        Load += (s, e) => { l.Location = new Point((ClientSize.Width - l.Width) / 2, 60); };
        MouseDown += OnPick;
        l.MouseDown += OnPick;
        MouseMove += (s, e) => Invalidate();
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        Paint += (s, e) =>
        {
            var p = PointToClient(Cursor.Position);
            using (var pen = new Pen(Color.Lime, 1))
            {
                e.Graphics.DrawLine(pen, 0, p.Y, ClientSize.Width, p.Y);
                e.Graphics.DrawLine(pen, p.X, 0, p.X, ClientSize.Height);
            }
            var c = new Point(ClientSize.Width / 2, ClientSize.Height / 2);
            using (var pen = new Pen(Color.FromArgb(160, Color.White), 1) { DashStyle = DashStyle.Dash })
            {
                e.Graphics.DrawLine(pen, c.X - 20, c.Y, c.X + 20, c.Y);
                e.Graphics.DrawLine(pen, c.X, c.Y - 20, c.X, c.Y + 20);
            }
        };
    }

    void OnPick(object sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) Picked = Cursor.Position;
        Close();
    }
}

static class Prompt
{
    public static string Show(IWin32Window owner, string title, string text, string def)
    {
        using (var f = new Form())
        {
            f.Text = title;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(440, 120);
            f.MinimizeBox = false; f.MaximizeBox = false; f.ShowInTaskbar = false;
            f.TopMost = true;
            f.Font = new Font("Segoe UI", 9f);
            var l = new Label { Text = text, Location = new Point(12, 12), AutoSize = true };
            var tb = new TextBox { Text = def, Location = new Point(12, 38), Width = 416 };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(272, 80), Width = 75 };
            var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Location = new Point(353, 80), Width = 75 };
            f.AcceptButton = ok; f.CancelButton = cancel;
            f.Controls.AddRange(new Control[] { l, tb, ok, cancel });
            return f.ShowDialog(owner) == DialogResult.OK ? tb.Text.Trim() : null;
        }
    }
}

class CrosshairApp : ApplicationContext
{
    public Config Cfg;
    public AppSettings Settings;
    public List<string> Profiles;
    public string HotkeyStatus = "";
    public List<string> NoteLines;
    readonly OverlayForm notesOverlay = new OverlayForm();
    QuickNoteForm quickNote;
    DeleteNotesForm deleteNotes;
    string lastDeleted; int lastDeletedAt = -1;
    GroupNote lastGroupDeleted;
    public readonly List<Shot> ShotList = new List<Shot>();
    ShotViewer shotViewer;
    readonly Dictionary<long, DateTime> updSeen = new Dictionary<long, DateTime>();   // atualizações vistas na sala
    bool updateRestart, shareScheduled;
    public string UpdateStatus = "";
    public GroupSync Sync;
    static readonly List<GroupNote> NoGroup = new List<GroupNote>();
    public List<GroupNote> GroupNotes { get { return Sync != null ? Sync.Notes : NoGroup; } }
    public string SyncName { get { return string.IsNullOrWhiteSpace(Settings.SyncName) ? Environment.UserName : Settings.SyncName.Trim(); } }

    // Liga/desliga/troca a sala do grupo conforme as configurações.
    public void RestartSync()
    {
        if (Sync != null) { Sync.Dispose(); Sync = null; }
        lastGroupDeleted = null;
        if (Settings.SyncEnabled && GroupSync.ValidRoom(Settings.SyncRoom))
        {
            Sync = new GroupSync(Settings.SyncRoom, Storage.Dir, a => { try { overlay.BeginInvoke(a); } catch { } }, BuildInfo.Build);
            var mine = Sync;
            Sync.FileOffered += f => { if (mine == Sync) OnFileOffered(f); };
            Sync.PeerVersion += (b, t) => { if (mine == Sync) OnPeerVersion(b, t); };
            Sync.Changed += () =>
            {
                if (mine != Sync) return;
                UpdateNotes();
                if (settingsForm != null && !settingsForm.IsDisposed && settingsForm.Visible) settingsForm.RefreshControls();
            };
        }
        UpdateNotes();
        if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.RefreshControls();
    }

    // ---------- prints ----------

    public void TakeShot()
    {
        byte[] jpg;
        try { jpg = Shots.Capture(CrossScreen()); }
        catch (Exception ex) { ShowToast(null, "Não consegui tirar o print", ex.Message, 3000); return; }
        var now = DateTime.Now;
        Shots.SaveFile(jpg, "eu", now);
        var bmp = Shots.Decode(jpg);
        if (bmp != null) AddShot(new Shot { Author = "você", Time = now, Image = bmp });
        if (Sync == null) { ShowToast(ThumbOf(bmp), "Print salvo", "Entre numa sala de grupo (aba Senhas) para enviar aos amigos", 3000); return; }
        ShowToast(ThumbOf(bmp), "Enviando print ao grupo…", (jpg.Length / 1024) + " KB", 8000);
        Sync.Upload("shot", SyncName, jpg, (ok, err) =>
            ShowToast(ThumbOf(bmp), ok ? "Print enviado ao grupo" : "Falha ao enviar o print", ok ? "" : err, 3000));
    }

    void AddShot(Shot s)
    {
        ShotList.Add(s);
        while (ShotList.Count > 20) { ShotList[0].Image.Dispose(); ShotList.RemoveAt(0); }
        if (shotViewer != null && !shotViewer.IsDisposed) shotViewer.RefreshList();
    }

    static Bitmap ThumbOf(Bitmap b) { return b == null ? null : Shots.Thumb(b, 40); }

    public void ViewShots()
    {
        if (shotViewer == null || shotViewer.IsDisposed) shotViewer = new ShotViewer(() => ShotList);
        shotViewer.Open(CrossScreen());
    }

    void OnFileOffered(FileOffer f)
    {
        var age = DateTime.UtcNow - f.SentAt;
        if (f.Kind == "shot")
        {
            if (f.Mine || age.TotalMinutes > 10) return;   // prints antigos do histórico: ignora
            string author = f.Fields.Length > 0 ? f.Fields[0] : "?";
            Sync.Download(f.Url, data =>
            {
                var bmp = data == null ? null : Shots.Decode(data);
                if (bmp == null) return;
                Shots.SaveFile(data, author, DateTime.Now);
                AddShot(new Shot { Author = author, Time = DateTime.Now, Image = bmp });
                ShowToast(ThumbOf(bmp), "Print de " + author, Hotkeys.ToText(Settings.ShotViewKey) + " para ver", 5000);
                if (Settings.AutoSound) Beeper.Play(1, Settings.AutoVolume);
            });
        }
        else if (f.Kind == "upd" && f.Fields.Length >= 3)
        {
            long b;
            if (!long.TryParse(f.Fields[0], out b)) return;
            updSeen[b] = f.SentAt;
            if (!Settings.AutoUpdate || b <= BuildInfo.Build || b <= Updater.StagedBuild || age.TotalHours > 3) return;
            string sha = f.Fields[1], sig = f.Fields[2];
            UpdateStatus = "Baixando versão nova…";
            Sync.Download(f.Url, exe =>
            {
                if (exe == null) { UpdateStatus = "Falha ao baixar a versão nova"; RefreshSettings(); return; }
                if (b <= Updater.StagedBuild) return;
                if (!Updater.Stage(exe, b, sha, sig)) { UpdateStatus = "Versão recebida com assinatura inválida — ignorada"; RefreshSettings(); return; }
                UpdateStatus = "Versão nova baixada — instala ao fechar o app ou em \"Atualizar agora\"";
                RefreshSettings();
                ShowToast(AppIcon.ToBitmap(), "Atualização do Crosshair Overlay baixada", "Instala quando você fechar o app (ou: bandeja → Atualizar agora)", 6000);
            });
        }
    }

    // Alguém entrou na sala com versão mais velha: repassa a minha (se ninguém mandou nas últimas horas).
    void OnPeerVersion(long build, DateTime sentAt)
    {
        if ((DateTime.UtcNow - sentAt).TotalSeconds > 60 || build >= BuildInfo.Build || shareScheduled) return;
        DateTime seen;
        if (updSeen.TryGetValue(BuildInfo.Build, out seen) && (DateTime.UtcNow - seen).TotalHours < 2.5) return;
        if (!Updater.CanShare) return;
        shareScheduled = true;
        var t = new System.Windows.Forms.Timer { Interval = new Random().Next(1000, 8000) };
        t.Tick += (s, e) =>
        {
            t.Stop(); t.Dispose(); shareScheduled = false;
            DateTime seen2;
            if (updSeen.TryGetValue(BuildInfo.Build, out seen2) && (DateTime.UtcNow - seen2).TotalHours < 2.5) return;
            ShareUpdate(false);
        };
        t.Start();
    }

    public void ShareUpdate(bool manual)
    {
        if (Sync == null) { if (manual) MessageBox.Show("Entre numa sala de grupo primeiro (aba Senhas).", "Atualização"); return; }
        byte[] exe; string sha, sig;
        if (!Updater.GetPackage(out exe, out sha, out sig))
        {
            if (manual) MessageBox.Show("Esta cópia do app não tem assinatura válida para repassar.", "Atualização");
            return;
        }
        updSeen[BuildInfo.Build] = DateTime.UtcNow;
        UpdateStatus = "Enviando esta versão ao grupo…"; RefreshSettings();
        Sync.Upload("upd", BuildInfo.Build + "\n" + sha + "\n" + sig, exe, (ok, err) =>
        {
            UpdateStatus = ok ? "Versão " + Updater.VersionText + " enviada ao grupo" : "Falha ao enviar a versão: " + err;
            if (!ok) updSeen.Remove(BuildInfo.Build);
            RefreshSettings();
        });
    }

    System.Windows.Forms.Timer ghTimer;
    bool ghChecking;

    public void CheckGitHubUpdate(bool manual)
    {
        if ((!Settings.AutoUpdate && !manual) || ghChecking) return;
        ghChecking = true;
        if (manual) { UpdateStatus = "Procurando versão nova no GitHub…"; RefreshSettings(); }
        ThreadPool.QueueUserWorkItem(_ =>
        {
            long b = 0; string err = null;
            try { b = Updater.CheckGitHub(); } catch (Exception ex) { err = ex.Message; }
            try
            {
                overlay.BeginInvoke((Action)(() =>
                {
                    ghChecking = false;
                    if (b > 0)
                    {
                        UpdateStatus = "Versão nova baixada do GitHub — instala ao fechar o app ou em \"Atualizar agora\"";
                        ShowToast(AppIcon.ToBitmap(), "Atualização do Crosshair Overlay baixada", "Instala quando você fechar o app (ou: bandeja → Atualizar agora)", 6000);
                    }
                    else if (manual)
                        UpdateStatus = err != null ? "Não consegui falar com o GitHub: " + err
                            : Updater.StagedBuild > BuildInfo.Build ? "Versão nova já baixada — instala ao fechar o app" : "Você já está na versão mais nova";
                    RefreshSettings();
                }));
            }
            catch { ghChecking = false; }
        });
    }

    public void UpdateNow()
    {
        if (Updater.StagedBuild <= BuildInfo.Build) return;
        updateRestart = true;
        Exit();
    }

    void RefreshSettings() { if (settingsForm != null && !settingsForm.IsDisposed && settingsForm.Visible) settingsForm.RefreshControls(); }

    // Todas as senhas na ordem dos números do overlay (suas, depois as do grupo).
    public List<string> AllNotesForList()
    {
        var l = new List<string>(NoteLines);
        foreach (var g in GroupNotes) l.Add(g.Text + "   · " + g.Author + "  [grupo]");
        return l;
    }
    bool notesShown;
    public string CurrentGame = "", CurrentGameTitle = "";
    public Bitmap CurrentGameIcon;
    readonly OverlayForm toast = new OverlayForm();
    long toastUntil;   // último programa em foco (sem contar este app)
    public Icon AppIcon;

    readonly OverlayForm overlay;
    readonly OverlayForm hud = new OverlayForm();
    bool hudShown;
    SettingsForm settingsForm;
    readonly NotifyIcon tray;
    readonly HotkeyWindow hk;
    public AutoPresser Auto;
    readonly System.Windows.Forms.Timer poll, saveTimer;
    bool userVisible = true, rmbHidden, shownState;
    volatile bool gameFocused;
    uint fgPid; string fgExe = "", manualGame = "";
    public bool InGameOk { get { return !Settings.OnlyInGame || gameFocused; } }
    int tick;

    const int HK_MENU = 1, HK_TOGGLE = 2, HK_NEXT = 3, HK_AUTO = 4, HK_NOTE_ADD = 5, HK_NOTE_TOGGLE = 6, HK_NOTE_DEL = 7, HK_SHOT = 8, HK_SHOT_VIEW = 9, HK_UP = 10, HK_DOWN = 11, HK_LEFT = 12, HK_RIGHT = 13, HK_CENTER = 14;

    public CrosshairApp()
    {
        AppIcon = LoadIcon();
        Settings = Storage.LoadSettings();
        Profiles = Storage.ListProfiles();
        if (Profiles.Count == 0)
        {
            Storage.SaveProfile("Padrão", new Config());
            Profiles = Storage.ListProfiles();
        }
        if (!Profiles.Contains(Settings.ActiveProfile)) Settings.ActiveProfile = Profiles[0];
        Cfg = Storage.LoadProfile(Settings.ActiveProfile);

        overlay = new OverlayForm();
        overlay.Show();
        shownState = true;
        hud.Show();
        NoteLines = Notes.Load();
        notesOverlay.Show();
        UpdateNotes();
        toast.Show();
        Native.ShowWindow(toast.Handle, Native.SW_HIDE);
        Native.ShowWindow(hud.Handle, Native.SW_HIDE);
        UpdateOverlay();
        ApplyVisibility();

        Auto = new AutoPresser(Settings);
        AutoPresser.AllowSend = () => InGameOk;
        RestartSync();
        // GitHub: confere 20 s depois de abrir e depois a cada 3 horas
        ghTimer = new System.Windows.Forms.Timer { Interval = 20000 };
        ghTimer.Tick += (s, e) => { ghTimer.Interval = 3 * 3600 * 1000; CheckGitHubUpdate(false); };
        ghTimer.Start();
        hk = new HotkeyWindow();
        hk.HotkeyPressed += OnHotkey;
        hk.RightButton += down =>
        {
            bool hide = down && Cfg.HideOnRightClick;
            if (hide != rmbHidden) { rmbHidden = hide; ApplyVisibility(); }
        };
        RegisterHotkeys();
        SyncMouseListening();

        // Bandeja
        var menu = new ContextMenuStrip();
        var miSettings = new ToolStripMenuItem("Configurações", null, (s, e) => ToggleSettings(true));
        miSettings.Font = new Font(miSettings.Font, FontStyle.Bold);
        var miToggle = new ToolStripMenuItem("Mostrar mira", null, (s, e) => ToggleVisible());
        var miProfiles = new ToolStripMenuItem("Perfis");
        var miAuto = new ToolStripMenuItem("Auto-tecla ligado", null, (s, e) => ToggleAuto());
        var miOnly = new ToolStripMenuItem("Só mostrar com o jogo em foco", null, (s, e) => SetOnlyInGame(!Settings.OnlyInGame));
        var miShots = new ToolStripMenuItem("Ver prints", null, (s, e) => ViewShots());
        var miUpdate = new ToolStripMenuItem("Atualizar agora", null, (s, e) => UpdateNow());
        miUpdate.Font = new Font(miUpdate.Font, FontStyle.Bold);
        menu.Opening += (s, e) =>
        {
            miProfiles.DropDownItems.Clear();
            foreach (var p in Profiles)
            {
                string name = p;
                var it = new ToolStripMenuItem(name, null, (s2, e2) => SwitchProfile(name));
                it.Checked = name == Settings.ActiveProfile;
                miProfiles.DropDownItems.Add(it);
            }
            miToggle.Checked = userVisible;
            miAuto.Checked = Auto.Running;
            miOnly.Checked = Settings.OnlyInGame;
            miUpdate.Visible = Updater.StagedBuild > BuildInfo.Build;
        };
        menu.Items.AddRange(new ToolStripItem[] {
            miSettings, miUpdate, miToggle, miOnly, miProfiles, miAuto, miShots, new ToolStripSeparator(),
            new ToolStripMenuItem("Sair", null, (s, e) => Exit()) });
        tray = new NotifyIcon { Icon = AppIcon, Text = "Crosshair Overlay — by " + AppInfo.Author, Visible = true, ContextMenuStrip = menu };
        tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ToggleSettings(true); };

        poll = new System.Windows.Forms.Timer { Interval = 15 };
        poll.Tick += OnPoll;
        poll.Start();

        saveTimer = new System.Windows.Forms.Timer { Interval = 500 };
        saveTimer.Tick += (s, e) => { saveTimer.Stop(); SaveNow(); };

        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;

        if (Settings.FirstRun)
        {
            Settings.FirstRun = false;
            SaveSettings();
            ToggleSettings(true);
        }
        else
        {
            tray.ShowBalloonTip(2500, "Crosshair Overlay ativo — by " + AppInfo.Author,
                "Menu: " + Hotkeys.ToText(Settings.MenuKey) + "   •   Mostrar/ocultar: " + Hotkeys.ToText(Settings.ToggleKey),
                ToolTipIcon.None);
        }
    }

    static Icon LoadIcon()
    {
        try
        {
            using (var big = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                if (big != null) return new Icon(big, SystemInformation.SmallIconSize);
        }
        catch { }
        return MakeIcon();
    }

    static Icon MakeIcon()
    {
        using (var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                foreach (var pen in new[] { new Pen(Color.Black, 6), new Pen(Color.FromArgb(0, 230, 140), 3) })
                {
                    g.DrawEllipse(pen, 7, 7, 18, 18);
                    g.DrawLine(pen, 16, 1, 16, 10); g.DrawLine(pen, 16, 22, 16, 31);
                    g.DrawLine(pen, 1, 16, 10, 16); g.DrawLine(pen, 22, 16, 31, 16);
                    pen.Dispose();
                }
            }
            return Icon.FromHandle(bmp.GetHicon());
        }
    }

    public Color CurrentColor()
    {
        if (Cfg.Rainbow)
            return Util.FromHue(Environment.TickCount / 1000.0 * Cfg.RainbowSpeed * 36.0);
        var c = Color.FromArgb(Cfg.MainColor);
        return Color.FromArgb(255, c.R, c.G, c.B);
    }

    public void UpdateOverlay()
    {
        var screens = Screen.AllScreens;
        var s = screens[Util.Clamp(Cfg.Monitor, 0, screens.Length - 1)];
        using (var bmp = Renderer.Render(Cfg, CurrentColor()))
        {
            int cx = s.Bounds.X + s.Bounds.Width / 2 + Cfg.OffsetX;
            int cy = s.Bounds.Y + s.Bounds.Height / 2 + Cfg.OffsetY;
            overlay.SetBitmap(bmp, cx - bmp.Width / 2, cy - bmp.Height / 2);
        }
        if (settingsForm != null && !settingsForm.IsDisposed && settingsForm.Visible) settingsForm.RefreshPreview();
        SyncMouseListening();
    }

    void SyncMouseListening()
    {
        if (hk == null) return;
        hk.SetMouseListening(Cfg.HideOnRightClick);
        if (!Cfg.HideOnRightClick && rmbHidden) { rmbHidden = false; ApplyVisibility(); }
    }

    public void ConfigChanged()
    {
        UpdateOverlay();
        saveTimer.Stop();
        saveTimer.Start();
    }

    void SaveNow()
    {
        try { Storage.SaveProfile(Settings.ActiveProfile, Cfg); } catch { }
    }

    public void SaveSettings() { Storage.SaveSettings(Settings); }

    void OnDisplayChanged(object sender, EventArgs e) { UpdateOverlay(); UpdateNotes(); }

    // ---------- senhas ----------

    Rectangle CrossScreen()
    {
        var screens = Screen.AllScreens;
        return screens[Util.Clamp(Cfg.Monitor, 0, screens.Length - 1)].Bounds;
    }

    public int notesHeight, notesWidth;
    public Rectangle NotesScreen() { return CrossScreen(); }

    public void UpdateNotes()
    {
        if (!Settings.NotesShow || !InGameOk)
        {
            if (notesShown) { Native.ShowWindow(notesOverlay.Handle, Native.SW_HIDE); notesShown = false; }
            return;
        }
        var sc = CrossScreen();
        string gTitle = Sync == null ? null : "GRUPO" + (Sync.Connected ? "" : "  (sem conexão)");
        var gLines = GroupNotes.Select(n => n.Text + "   · " + n.Author).ToList();
        using (var bmp = Notes.Render(NoteLines, gLines, gTitle, Settings.NotesFontSize, Hotkeys.ToText(Settings.NotesAddKey)))
        {
            notesWidth = bmp.Width; notesHeight = bmp.Height;
            // mantém o bloco inteiro dentro da tela
            int x = Util.Clamp(Settings.NotesX, 0, Math.Max(0, sc.Width - bmp.Width));
            int y = Util.Clamp(Settings.NotesY, 0, Math.Max(0, sc.Height - bmp.Height));
            notesOverlay.SetBitmap(bmp, sc.X + x, sc.Y + y);
        }
        if (!notesShown) { Native.ShowWindow(notesOverlay.Handle, Native.SW_SHOWNA); notesOverlay.EnsureTopMost(); notesShown = true; }
    }

    public void SetNotes(List<string> lines)
    {
        NoteLines = lines;
        Notes.Save(NoteLines);
        UpdateNotes();
    }

    public void DeleteNote(int idx)
    {
        if (idx >= NoteLines.Count)
        {
            int gi = idx - NoteLines.Count;
            if (Sync == null || gi >= Sync.Notes.Count) return;
            lastGroupDeleted = Sync.Delete(Sync.Notes[gi].Id);
            lastDeleted = null;
            return;
        }
        if (idx < 0) return;
        lastGroupDeleted = null;
        lastDeleted = NoteLines[idx]; lastDeletedAt = idx;
        var l = new List<string>(NoteLines); l.RemoveAt(idx);
        SetNotes(l);
        if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.RefreshControls();
    }

    public bool UndoDeleteNote()
    {
        if (lastGroupDeleted != null)
        {
            if (Sync == null) return false;
            Sync.Add(lastGroupDeleted.Text, lastGroupDeleted.Author);
            lastGroupDeleted = null;
            return true;
        }
        if (lastDeleted == null) return false;
        var l = new List<string>(NoteLines);
        l.Insert(Util.Clamp(lastDeletedAt, 0, l.Count), lastDeleted);
        lastDeleted = null;
        SetNotes(l);
        if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.RefreshControls();
        return true;
    }

    public void OpenDeleteNotes()
    {
        if (deleteNotes == null || deleteNotes.IsDisposed)
            deleteNotes = new DeleteNotesForm(AllNotesForList, DeleteNote, UndoDeleteNote);
        var sc = CrossScreen();
        int nx = sc.X + Util.Clamp(Settings.NotesX, 0, Math.Max(0, sc.Width - 480));
        int ny = sc.Y + Util.Clamp(Settings.NotesY, 0, Math.Max(0, sc.Height - notesHeight));
        deleteNotes.Open(new Point(nx, ny + (notesShown ? notesHeight + 8 : 0)), sc);
    }

    public void ToggleNotes()
    {
        Settings.NotesShow = !Settings.NotesShow;
        SaveSettings();
        UpdateNotes();
        if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.RefreshControls();
    }

    public void QuickAddNote()
    {
        if (quickNote == null || quickNote.IsDisposed)
        {
            quickNote = new QuickNoteForm();
            quickNote.Saved += (text, toGroup) =>
            {
                int n;
                if (text.StartsWith("-") && int.TryParse(text.Substring(1).Trim(), out n))
                {
                    DeleteNote(n - 1);
                    return;
                }
                if (toGroup && Sync != null) Sync.Add(text, SyncName);
                else { NoteLines.Add(text); Notes.Save(NoteLines); }
                if (!Settings.NotesShow) { Settings.NotesShow = true; SaveSettings(); }
                UpdateNotes();
                if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.RefreshControls();
            };
        }
        var sc = CrossScreen();
        int nx = sc.X + Util.Clamp(Settings.NotesX, 0, Math.Max(0, sc.Width - 440));
        int ny = sc.Y + Util.Clamp(Settings.NotesY, 0, Math.Max(0, sc.Height - notesHeight));
        int qy = ny + (notesShown ? notesHeight + 8 : 0);
        if (qy + 80 > sc.Bottom) qy = ny - 82;          // sem espaço embaixo: abre acima do bloco
        quickNote.Open(new Point(nx, Math.Max(sc.Y, qy)), Sync != null, Settings.NotesDefaultGroup);
    }

    void OnPoll(object sender, EventArgs e)
    {
        tick++;
        if (Cfg.Rainbow && tick % 2 == 0) UpdateOverlay();
        if (tick % 66 == 0 && shownState) overlay.EnsureTopMost();
        if (tick % 2 == 0) UpdateHud();
        if (tick % 10 == 0) CheckGame();
        if (tick % 66 == 33 && notesShown) notesOverlay.EnsureTopMost();
        if (toastUntil != 0 && AutoPresser.Now > toastUntil) { toastUntil = 0; Native.ShowWindow(toast.Handle, Native.SW_HIDE); }
    }

    // ---------- detecção do jogo ----------

    static readonly HashSet<string> IgnoredApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        "explorer", "shellexperiencehost", "startmenuexperiencehost", "searchhost", "searchapp",
        "textinputhost", "lockapp", "applicationframehost", "taskmgr", "systemsettings",
        // programas comuns que não são jogos
        "notepad", "notepad++", "chrome", "msedge", "firefox", "opera", "brave", "discord", "steam", "steamwebhelper",
        "epicgameslauncher", "battle.net", "claude", "code", "devenv", "spotify", "obs64", "obs32", "whatsapp",
        "telegram", "winword", "excel", "powerpnt", "outlook", "teams", "ms-teams", "slack", "cmd", "powershell",
        "windowsterminal", "conhost", "vlc", "mspaint", "photos", "calculatorapp", "nvcontainer", "nvidia share" };
    static readonly int MyPid = Process.GetCurrentProcess().Id;

    public Dictionary<string, string> GameMap()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in (Settings.GameProfiles ?? "").Split('|'))
        {
            int i = part.IndexOf('=');
            if (i > 0) d[part.Substring(0, i)] = part.Substring(i + 1);
        }
        return d;
    }

    public void SetGameMap(Dictionary<string, string> d)
    {
        Settings.GameProfiles = string.Join("|", d.Select(kv => kv.Key + "=" + kv.Value));
        SaveSettings();
    }

    public string LinkedProfile(string game)
    {
        string p;
        return game.Length > 0 && GameMap().TryGetValue(game, out p) ? p : null;
    }

    static void DebugLog(string s)
    {
        string f = Environment.GetEnvironmentVariable("CROSSHAIR_DEBUG");
        if (string.IsNullOrEmpty(f)) return;
        try { System.IO.File.AppendAllText(f, DateTime.Now.ToString("HH:mm:ss.fff") + " " + s + "\r\n"); } catch { }
    }

    // Roda a cada ~150 ms: descobre se o jogo está em foco e mostra/esconde o overlay.
    void CheckGame()
    {
        IntPtr h = Native.GetForegroundWindow();
        uint pid = 0;
        if (h != IntPtr.Zero) Native.GetWindowThreadProcessId(h, out pid);
        if (pid != 0 && pid == MyPid) return;   // nosso menu / caixinha de senha: mantém como está
        string exe = "";
        if (pid != 0)
        {
            if (pid == fgPid) exe = fgExe;
            else
            {
                try { using (var p = Process.GetProcessById((int)pid)) exe = p.ProcessName; } catch { }
                fgPid = pid; fgExe = exe;
                DebugLog("foreground pid " + pid + " " + exe);
            }
        }
        // É jogo: não está na lista de programas comuns, não está minimizado e
        // (está vinculado a um perfil, foi escolhido à mão ou ocupa a tela inteira).
        bool isGame = exe.Length > 0 && !IgnoredApps.Contains(exe) && !Native.IsIconic(h)
            && (LinkedProfile(exe) != null || string.Equals(exe, manualGame, StringComparison.OrdinalIgnoreCase) || IsFullscreen(h));
        if (isGame && !string.Equals(exe, CurrentGame, StringComparison.OrdinalIgnoreCase)) SetGame(h, pid, exe);
        SetGameFocused(isGame);
    }

    static bool IsFullscreen(IntPtr h)
    {
        Native.RECT r;
        if (!Native.GetWindowRect(h, out r)) return false;
        var b = Screen.FromHandle(h).Bounds;
        return r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom;
    }

    void SetGameFocused(bool v)
    {
        if (v == gameFocused) return;
        gameFocused = v;
        DebugLog("jogo em foco: " + v);
        RefreshInGameVisibility();
    }

    void RefreshInGameVisibility()
    {
        ApplyVisibility();
        UpdateNotes();
        UpdateHud();
        if (!InGameOk) { toastUntil = 0; Native.ShowWindow(toast.Handle, Native.SW_HIDE); }
    }

    public void SetOnlyInGame(bool v)
    {
        Settings.OnlyInGame = v;
        SaveSettings();
        RefreshInGameVisibility();
        if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.RefreshControls();
    }

    // Programas com janela aberta agora (para escolher o jogo manualmente).
    public List<Process> RunningApps()
    {
        var l = new List<Process>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == MyPid || p.MainWindowHandle == IntPtr.Zero || string.IsNullOrEmpty(p.MainWindowTitle) || IgnoredApps.Contains(p.ProcessName))
                { p.Dispose(); continue; }
                l.Add(p);
            }
            catch { p.Dispose(); }
        }
        return l.OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void SelectGameManually(Process p)
    {
        try { manualGame = p.ProcessName; SetGame(p.MainWindowHandle, (uint)p.Id, p.ProcessName); } catch { }
    }

    void SetGame(IntPtr h, uint pid, string exe)
    {
        var sb = new System.Text.StringBuilder(256);
        Native.GetWindowText(h, sb, sb.Capacity);
        CurrentGame = exe;
        CurrentGameTitle = sb.ToString();
        var oldIcon = CurrentGameIcon;
        CurrentGameIcon = ExeIcon(pid);

        string prof = LinkedProfile(exe);
        if (Settings.AutoSwitchProfiles && prof != null && Profiles.Contains(prof) && prof != Settings.ActiveProfile)
        {
            SwitchProfile(prof);
            tray.ShowBalloonTip(1200, "Crosshair Overlay", "Jogo: " + exe + "  →  perfil " + prof, ToolTipIcon.None);
        }
        if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.RefreshControls();
        if (oldIcon != null) oldIcon.Dispose();
        ShowGameToast();
    }

    static Bitmap ExeIcon(uint pid)
    {
        IntPtr hp = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hp == IntPtr.Zero) return null;
        try
        {
            var sb = new System.Text.StringBuilder(1024);
            int size = sb.Capacity;
            if (!Native.QueryFullProcessImageName(hp, 0, sb, ref size)) return null;
            using (var ic = Icon.ExtractAssociatedIcon(sb.ToString()))
                return ic == null ? null : ic.ToBitmap();
        }
        catch { return null; }
        finally { Native.CloseHandle(hp); }
    }

    // Aviso rápido no topo da tela: ícone + nome do jogo + perfil.
    public void ShowToast(Bitmap icon, string line1, string line2, int ms)
    {
        var sc = CrossScreen();
        using (var bmp = GameToast.Render(icon, line1, line2 ?? ""))
            toast.SetBitmap(bmp, sc.X + (sc.Width - bmp.Width) / 2, sc.Y + 40);
        if (icon != null) icon.Dispose();
        Native.ShowWindow(toast.Handle, Native.SW_SHOWNA);
        toast.EnsureTopMost();
        toastUntil = AutoPresser.Now + ms;
    }

    void ShowGameToast()
    {
        string linked = LinkedProfile(CurrentGame);
        string line2 = "Perfil: " + Settings.ActiveProfile + (linked != null ? "  (vinculado)" : "");
        var screens = Screen.AllScreens;
        var sc = screens[Util.Clamp(Cfg.Monitor, 0, screens.Length - 1)].Bounds;
        using (var bmp = GameToast.Render(CurrentGameIcon, "Jogo detectado: " + CurrentGame, line2))
            toast.SetBitmap(bmp, sc.X + (sc.Width - bmp.Width) / 2, sc.Y + 40);
        Native.ShowWindow(toast.Handle, Native.SW_SHOWNA);
        toast.EnsureTopMost();
        toastUntil = AutoPresser.Now + 3000;
    }

    // Contador do Auto-tecla no overlay
    void UpdateHud()
    {
        bool want = Auto.Running && Settings.AutoHud && InGameOk;
        var st = want ? Auto.Snapshot() : null;
        if (want && st.Length == 0) want = false;
        if (!want)
        {
            if (hudShown) { Native.ShowWindow(hud.Handle, Native.SW_HIDE); hudShown = false; }
            return;
        }
        var screens = Screen.AllScreens;
        var sc = screens[Util.Clamp(Cfg.Monitor, 0, screens.Length - 1)].Bounds;
        var cross = new Point(sc.X + sc.Width / 2 + Cfg.OffsetX, sc.Y + sc.Height / 2 + Cfg.OffsetY);
        using (var bmp = AutoHud.Render(st, AutoPresser.Now, Settings.AutoHudScale))
        {
            var pt = AutoHud.Place(Settings.AutoHudPos, sc, cross, bmp.Size);
            hud.SetBitmap(bmp, pt.X, pt.Y);
        }
        if (!hudShown) { Native.ShowWindow(hud.Handle, Native.SW_SHOWNA); hud.EnsureTopMost(); hudShown = true; }
        else if (tick % 66 == 0) hud.EnsureTopMost();
    }

    void ApplyVisibility()
    {
        bool want = userVisible && !rmbHidden && InGameOk;
        if (want == shownState) return;
        Native.ShowWindow(overlay.Handle, want ? Native.SW_SHOWNA : Native.SW_HIDE);
        if (want) overlay.EnsureTopMost();
        shownState = want;
    }

    public void ToggleVisible()
    {
        userVisible = !userVisible;
        ApplyVisibility();
    }

    public void SuspendHotkeys() { hk.UnregisterAll(); }

    public void RegisterHotkeys()
    {
        hk.UnregisterAll();
        var fails = new List<string>();
        if (!hk.Register(HK_MENU, Settings.MenuKey, false)) fails.Add(Hotkeys.ToText(Settings.MenuKey));
        if (!hk.Register(HK_TOGGLE, Settings.ToggleKey, false)) fails.Add(Hotkeys.ToText(Settings.ToggleKey));
        if (!hk.Register(HK_NEXT, Settings.NextProfileKey, false)) fails.Add(Hotkeys.ToText(Settings.NextProfileKey));
        if (!hk.Register(HK_AUTO, Settings.AutoToggleKey, false)) fails.Add(Hotkeys.ToText(Settings.AutoToggleKey));
        if (!hk.Register(HK_NOTE_ADD, Settings.NotesAddKey, false)) fails.Add(Hotkeys.ToText(Settings.NotesAddKey));
        if (!hk.Register(HK_NOTE_TOGGLE, Settings.NotesToggleKey, false)) fails.Add(Hotkeys.ToText(Settings.NotesToggleKey));
        if (!hk.Register(HK_NOTE_DEL, Settings.NotesDelKey, false)) fails.Add(Hotkeys.ToText(Settings.NotesDelKey));
        if (!hk.Register(HK_SHOT, Settings.ShotKey, false)) fails.Add(Hotkeys.ToText(Settings.ShotKey));
        if (!hk.Register(HK_SHOT_VIEW, Settings.ShotViewKey, false)) fails.Add(Hotkeys.ToText(Settings.ShotViewKey));
        if (Settings.NudgeKeys)
        {
            Keys m = Keys.Control | Keys.Alt | Keys.Shift;
            hk.Register(HK_UP, (int)(m | Keys.Up), true);
            hk.Register(HK_DOWN, (int)(m | Keys.Down), true);
            hk.Register(HK_LEFT, (int)(m | Keys.Left), true);
            hk.Register(HK_RIGHT, (int)(m | Keys.Right), true);
            hk.Register(HK_CENTER, (int)(m | Keys.Home), false);
        }
        HotkeyStatus = fails.Count == 0
            ? "✔ Atalhos ativos."
            : "⚠ Não foi possível registrar: " + string.Join(", ", fails) + " (já usado por outro programa).";
    }

    void OnHotkey(int id)
    {
        switch (id)
        {
            case HK_MENU: ToggleSettings(false); return;
            case HK_TOGGLE: ToggleVisible(); return;
            case HK_NEXT: NextProfile(); return;
            case HK_AUTO: ToggleAuto(); return;
            case HK_NOTE_ADD: QuickAddNote(); return;
            case HK_NOTE_TOGGLE: ToggleNotes(); return;
            case HK_NOTE_DEL: OpenDeleteNotes(); return;
            case HK_SHOT: TakeShot(); return;
            case HK_SHOT_VIEW: ViewShots(); return;
            case HK_UP: Cfg.OffsetY--; break;
            case HK_DOWN: Cfg.OffsetY++; break;
            case HK_LEFT: Cfg.OffsetX--; break;
            case HK_RIGHT: Cfg.OffsetX++; break;
            case HK_CENTER: Cfg.OffsetX = 0; Cfg.OffsetY = 0; break;
            default: return;
        }
        ConfigChanged();
        if (settingsForm != null && !settingsForm.IsDisposed && settingsForm.Visible) settingsForm.RefreshControls();
    }

    public void ToggleSettings(bool forceShow)
    {
        if (settingsForm == null || settingsForm.IsDisposed) settingsForm = new SettingsForm(this);
        if (settingsForm.Visible && !forceShow) { settingsForm.Hide(); return; }
        settingsForm.ReloadAll();
        settingsForm.Show();
        if (settingsForm.WindowState == FormWindowState.Minimized) settingsForm.WindowState = FormWindowState.Normal;
        settingsForm.Activate();
    }

    bool ProfileExists(string name)
    {
        return Profiles.Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
    }

    void ReloadForm()
    {
        if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.ReloadAll();
    }

    public void SwitchProfile(string name)
    {
        if (name == Settings.ActiveProfile) return;
        saveTimer.Stop();
        SaveNow();
        Cfg = Storage.LoadProfile(name);
        Settings.ActiveProfile = name;
        SaveSettings();
        UpdateOverlay();
        ReloadForm();
    }

    public bool CreateProfile(string name, Config c)
    {
        name = Storage.Sanitize(name);
        if (name.Length == 0 || ProfileExists(name)) return false;
        Storage.SaveProfile(name, c);
        Profiles = Storage.ListProfiles();
        SwitchProfile(name);
        return true;
    }

    public bool RenameProfile(string newName)
    {
        newName = Storage.Sanitize(newName);
        if (newName.Length == 0 || ProfileExists(newName)) return false;
        saveTimer.Stop();
        SaveNow();
        try { Storage.RenameProfile(Settings.ActiveProfile, newName); } catch { return false; }
        var gm = GameMap();
        foreach (var k in gm.Keys.ToList()) if (gm[k] == Settings.ActiveProfile) gm[k] = newName;
        SetGameMap(gm);
        Settings.ActiveProfile = newName;
        SaveSettings();
        Profiles = Storage.ListProfiles();
        ReloadForm();
        return true;
    }

    public bool DeleteActiveProfile()
    {
        if (Profiles.Count <= 1) return false;
        saveTimer.Stop();
        Storage.DeleteProfile(Settings.ActiveProfile);
        var gm = GameMap();
        foreach (var k in gm.Keys.ToList()) if (gm[k] == Settings.ActiveProfile) gm.Remove(k);
        SetGameMap(gm);
        Profiles = Storage.ListProfiles();
        Settings.ActiveProfile = Profiles[0];
        Cfg = Storage.LoadProfile(Settings.ActiveProfile);
        SaveSettings();
        UpdateOverlay();
        ReloadForm();
        return true;
    }

    public void NextProfile()
    {
        if (Profiles.Count < 2) return;
        int i = Profiles.IndexOf(Settings.ActiveProfile);
        SwitchProfile(Profiles[(i + 1) % Profiles.Count]);
        tray.ShowBalloonTip(1200, "Crosshair Overlay", "Perfil: " + Settings.ActiveProfile, ToolTipIcon.None);
    }

    public void ToggleAuto()
    {
        if (Auto.Running) Auto.Stop();
        else if (Auto.ActiveKeys().Count == 0)
        {
            tray.ShowBalloonTip(1500, "Auto-tecla", "Nenhuma tecla configurada (aba Auto-tecla).", ToolTipIcon.Warning);
            return;
        }
        else Auto.Start();
        tray.ShowBalloonTip(1000, "Auto-tecla", Auto.Running ? "LIGADO" : "DESLIGADO", ToolTipIcon.None);
        if (settingsForm != null && !settingsForm.IsDisposed && settingsForm.Visible) settingsForm.RefreshControls();
    }

    public void Exit()
    {
        if (Sync != null) Sync.Dispose();
        if (ghTimer != null) ghTimer.Stop();
        Auto.Dispose();
        saveTimer.Stop();
        SaveNow();
        SaveSettings();
        poll.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        hk.Dispose();
        tray.Visible = false;
        tray.Dispose();
        if (settingsForm != null && !settingsForm.IsDisposed) { settingsForm.AllowClose = true; settingsForm.Close(); }
        hud.Close();
        notesOverlay.Close();
        if (quickNote != null) quickNote.Close();
        if (deleteNotes != null) deleteNotes.Close();
        if (shotViewer != null) shotViewer.Close();
        if (Updater.StagedBuild > BuildInfo.Build) Updater.Apply(updateRestart);
        toast.Close();
        overlay.Close();
        ExitThread();
    }
}
