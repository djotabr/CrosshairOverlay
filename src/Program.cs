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
        try { L.Set(Storage.LoadSettings().Language); } catch { }
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
                MessageBox.Show(L.T("O Crosshair Overlay já está em execução.\nProcure o ícone na bandeja do sistema (perto do relógio)."),
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
        if (v == 0) return L.T("Nenhum");
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
            Text = L.T("Clique onde você quer a mira   •   Esc para cancelar"),
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
            var cancel = new Button { Text = L.T("Cancelar"), DialogResult = DialogResult.Cancel, Location = new Point(353, 80), Width = 75 };
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
        catch (Exception ex) { ShowToast(null, L.T("Não consegui tirar o print"), ex.Message, 3000); return; }
        var now = DateTime.Now;
        Shots.SaveFile(jpg, "eu", now);
        var bmp = Shots.Decode(jpg);
        if (bmp != null) AddShot(new Shot { Author = L.T("você"), Time = now, Image = bmp });
        if (Sync == null) { ShowToast(ThumbOf(bmp), L.T("Print salvo"), L.T("Entre numa sala de grupo (aba Senhas) para enviar aos amigos"), 3000); return; }
        ShowToast(ThumbOf(bmp), L.T("Enviando print ao grupo…"), (jpg.Length / 1024) + " KB", 8000);
        Sync.Upload("shot", SyncName, jpg, (ok, err) =>
            ShowToast(ThumbOf(bmp), ok ? L.T("Print enviado ao grupo") : L.T("Falha ao enviar o print"), ok ? "" : err, 3000));
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
                ShowToast(ThumbOf(bmp), L.T("Print de ") + author, Hotkeys.ToText(Settings.ShotViewKey) + L.T(" para ver"), 5000);
                if (Settings.AutoSound) Beeper.Play(1, Settings.AutoVolume);
            });
        }
        else if (f.Kind == "upd" && f.Fields.Length >= 3)
        {
            long b;
            if (!long.TryParse(f.Fields[0], out b)) return;
            updSeen[b] = f.SentAt;
            if (!Settings.AutoUpdate || age.TotalHours > 3) return;
            string url = f.Url;
            var sync = Sync;
            OfferUpdate(new Updater.Release { Build = b, Sha = f.Fields[1], Sig = f.Fields[2], Version = f.Fields.Length > 3 ? f.Fields[3] : "" },
                done => sync.Download(url, done), false);
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
        if (Sync == null) { if (manual) MessageBox.Show(L.T("Entre numa sala de grupo primeiro (aba Senhas)."), L.T("Atualização")); return; }
        byte[] exe; string sha, sig;
        if (!Updater.GetPackage(out exe, out sha, out sig))
        {
            if (manual) MessageBox.Show(L.T("Esta cópia do app não tem assinatura válida para repassar."), L.T("Atualização"));
            return;
        }
        updSeen[BuildInfo.Build] = DateTime.UtcNow;
        UpdateStatus = L.T("Enviando esta versão ao grupo…"); RefreshSettings();
        Sync.Upload("upd", BuildInfo.Build + "\n" + sha + "\n" + sig + "\n" + AppInfo.Version, exe, (ok, err) =>
        {
            UpdateStatus = ok ? L.T("Versão ") + Updater.VersionText + L.T(" enviada ao grupo") : L.T("Falha ao enviar a versão: ") + err;
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
        if (manual) { UpdateStatus = L.T("Procurando versão nova no GitHub…"); RefreshSettings(); }
        ThreadPool.QueueUserWorkItem(_ =>
        {
            Updater.Release r = null; string err = null;
            try { r = Updater.LatestGitHub(); } catch (Exception ex) { err = ex.Message; }
            try
            {
                overlay.BeginInvoke((Action)(() =>
                {
                    ghChecking = false;
                    if (r != null && r.Build > BuildInfo.Build && r.Build > Updater.StagedBuild)
                        OfferUpdate(r, done => ThreadPool.QueueUserWorkItem(__ =>
                        {
                            byte[] exe = null;
                            try { exe = Updater.DownloadGitHubExe(); } catch { }
                            try { overlay.BeginInvoke((Action)(() => done(exe))); } catch { }
                        }), manual);
                    else if (manual)
                    {
                        UpdateStatus = err != null ? L.T("Não consegui falar com o GitHub: ") + err
                            : Updater.StagedBuild > BuildInfo.Build ? L.T("Atualização já baixada — reinicie o app para instalar") : L.T("Você já está na versão mais nova");
                        RefreshSettings();
                    }
                }));
            }
            catch { ghChecking = false; }
        });
    }

    // ---------- atualização: pergunta antes de baixar e oferece reiniciar ----------

    Updater.Release offer;                        // versão nova encontrada (ainda não baixada)
    Action<Action<byte[]>> offerDownload;
    long declinedBuild;                           // "Não" para esta versão: não pergunta de novo sozinho
    bool askingUpdate, offerToastShown, restartAskPending;

    public bool UpdateAvailable { get { return offer != null; } }

    static string VersionLabel(Updater.Release r)
    {
        return (r.Version.Length > 0 ? L.T("versão ") + r.Version : L.T("versão nova")) + " (" + Updater.BuildDate(r.Build) + ")";
    }

    void OfferUpdate(Updater.Release r, Action<Action<byte[]>> download, bool manual)
    {
        if (r.Build <= BuildInfo.Build || r.Build <= Updater.StagedBuild) return;
        if (offer != null && offer.Build > r.Build) return;
        offer = r; offerDownload = download;
        if (manual) declinedBuild = 0;
        if (r.Build != declinedBuild) offerToastShown = false;
        UpdateStatus = L.T("Atualização disponível: ") + VersionLabel(r);
        RefreshSettings();
        TryAskUpdate(manual);
    }

    // Também roda pelo timer: com o jogo em foco, espera você sair dele para perguntar.
    void TryAskUpdate(bool now = false)
    {
        if (askingUpdate) return;
        if (restartAskPending)
        {
            if (now || !gameFocused) AskRestart();
            return;
        }
        if (offer == null || offer.Build == declinedBuild) return;
        if (gameFocused && !now)
        {
            if (!offerToastShown)
            {
                offerToastShown = true;
                ShowToast(AppIcon.ToBitmap(), L.T("Atualização disponível"), L.T("Vou perguntar quando você sair do jogo"), 4000);
            }
            return;
        }
        var r = offer;
        askingUpdate = true;
        bool yes = Ask(L.T("Saiu a ") + VersionLabel(r) + L.T(" do Crosshair Overlay.\n\nQuer baixar agora?"), L.T("Atualização disponível"));
        askingUpdate = false;
        if (!yes)
        {
            declinedBuild = r.Build;
            UpdateStatus = L.T("Atualização disponível: ") + VersionLabel(r) + L.T(" — use \"Baixar atualização\" quando quiser");
            RefreshSettings();
            return;
        }
        DownloadOffer();
    }

    public void DownloadOffer()
    {
        if (offer == null || offerDownload == null) return;
        var r = offer;
        var download = offerDownload;
        UpdateStatus = L.T("Baixando a ") + VersionLabel(r) + "…";
        RefreshSettings();
        download(exe =>
        {
            if (exe == null) { UpdateStatus = L.T("Falha ao baixar a atualização — tente de novo em \"Baixar atualização\""); RefreshSettings(); return; }
            if (!Updater.Stage(exe, r.Build, r.Sha, r.Sig))
            {
                UpdateStatus = L.T("A versão recebida não tem a assinatura do autor — ignorada");
                if (offer == r) offer = null;
                RefreshSettings();
                return;
            }
            if (offer == r) offer = null;
            UpdateStatus = L.T("Atualização baixada — reinicie o app para instalar");
            RefreshSettings();
            restartAskPending = true;
            if (gameFocused) ShowToast(AppIcon.ToBitmap(), L.T("Atualização baixada"), L.T("Vou perguntar sobre reiniciar quando você sair do jogo"), 4000);
            TryAskUpdate();
        });
    }

    void AskRestart()
    {
        restartAskPending = false;
        if (Updater.StagedBuild <= BuildInfo.Build) return;
        askingUpdate = true;
        bool yes = Ask(L.T("A atualização foi baixada.\n\nReiniciar o Crosshair Overlay agora para atualizar?\n\n(Se escolher Não, ela é instalada quando você fechar o app.)"), L.T("Atualização pronta"));
        askingUpdate = false;
        if (yes) UpdateNow();
    }

    // Pergunta Sim/Não por cima de tudo (inclusive do menu e do jogo).
    bool Ask(string text, string title)
    {
        var sc = CrossScreen();
        using (var owner = new Form
        {
            TopMost = true, ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None, Opacity = 0,
            StartPosition = FormStartPosition.Manual, Size = new Size(1, 1),
            Location = new Point(sc.X + sc.Width / 2, sc.Y + sc.Height / 2)
        })
        {
            owner.Show();
            Native.ForceForeground(owner.Handle);
            return MessageBox.Show(owner, text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }
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
        foreach (var g in GroupNotes) l.Add(g.Text + "   · " + g.Author + L.T("  [grupo]"));
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
    public bool InGameOk { get { return !Settings.OnlyInGame || gameFocused || TestMode; } }

    // ---------- modo de testes (PC do desenvolvedor) ----------
    // O PC do desenvolvedor é reconhecido pela chave de assinatura das atualizações (%APPDATA%\CrosshairOverlay\update-private.xml),
    // que só existe onde o app é compilado. Em outros PCs o modo de testes não aparece nem liga.
    public readonly bool IsAuthorPc = Updater.IsAuthor;
    public bool TestMode { get { return IsAuthorPc && Settings.TestMode; } }
    readonly OverlayForm testBadge = new OverlayForm();
    bool badgeShown;
    public string ForegroundExe { get { return fgExe; } }
    public bool GameFocused { get { return gameFocused; } }

    public void SetTestMode(bool on)
    {
        if (!IsAuthorPc) return;
        Settings.TestMode = on;
        SaveSettings();
        debugToFile = on;
        RefreshInGameVisibility();
        UpdateTestBadge();
        ShowToast(AppIcon.ToBitmap(), on ? "Modo de testes ligado" : "Modo de testes desligado",
            on ? "O overlay aparece mesmo com o jogo fechado" : "O overlay volta a aparecer só com o jogo em foco", 3000);
        RefreshSettings();
    }

    // Selo "MODO DE TESTES" embaixo da tela, para não esquecer ligado.
    void UpdateTestBadge()
    {
        if (!TestMode)
        {
            if (badgeShown) { Native.ShowWindow(testBadge.Handle, Native.SW_HIDE); badgeShown = false; }
            return;
        }
        var sc = CrossScreen();
        var th = Themes.A;
        using (var f = new Font(th.Font, 13f, FontStyle.Bold, GraphicsUnit.Pixel))
        {
            string text = "●  MODO DE TESTES  •  jogo não necessário";
            Size sz;
            using (var tmp = new Bitmap(1, 1)) using (var g0 = Graphics.FromImage(tmp)) sz = Size.Ceiling(g0.MeasureString(text, f));
            using (var bmp = new Bitmap(sz.Width + 28, sz.Height + 14, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    using (var path = Ui.Round(new RectangleF(1, 1, bmp.Width - 3, bmp.Height - 3), (bmp.Height - 3) / 2f))
                    {
                        using (var b = new SolidBrush(Color.FromArgb(225, 200, 120, 0))) g.FillPath(b, path);
                        using (var p = new Pen(Color.FromArgb(240, 255, 210, 90), 1.5f)) g.DrawPath(p, path);
                    }
                    g.DrawString(text, f, Brushes.White, 14, 7);
                }
                testBadge.SetBitmap(bmp, sc.X + (sc.Width - bmp.Width) / 2, sc.Bottom - bmp.Height - 54);
            }
        }
        if (!badgeShown) { Native.ShowWindow(testBadge.Handle, Native.SW_SHOWNA); badgeShown = true; }
        testBadge.EnsureTopMost();
    }

    // ---- simulações da aba Testes ----
    public void TestGameToast()
    {
        ShowToast(AppIcon.ToBitmap(), L.T("Jogo detectado: ") + "jogo-de-teste", L.T("Perfil: ") + Settings.ActiveProfile, 3000);
    }

    public void TestReceivedShot()
    {
        byte[] jpg;
        try { jpg = Shots.Capture(CrossScreen()); } catch { return; }
        var bmp = Shots.Decode(jpg);
        if (bmp == null) return;
        AddShot(new Shot { Author = "Teste", Time = DateTime.Now, Image = bmp });
        ShowToast(ThumbOf(bmp), L.T("Print de ") + "Teste", Hotkeys.ToText(Settings.ShotViewKey) + L.T(" para ver"), 5000);
        if (Settings.AutoSound) Beeper.Play(1, Settings.AutoVolume);
    }

    // Mostra as duas perguntas da atualização sem baixar nem instalar nada.
    public void TestUpdateDialogs()
    {
        var fake = new Updater.Release { Build = BuildInfo.Build + 1, Version = "9.9.9" };
        bool a = Ask(L.T("Saiu a ") + VersionLabel(fake) + L.T(" do Crosshair Overlay.\n\nQuer baixar agora?"), L.T("Atualização disponível") + "  [teste]");
        bool b = Ask(L.T("A atualização foi baixada.\n\nReiniciar o Crosshair Overlay agora para atualizar?\n\n(Se escolher Não, ela é instalada quando você fechar o app.)"), L.T("Atualização pronta") + "  [teste]");
        ShowToast(AppIcon.ToBitmap(), "Teste das perguntas (nada foi baixado)", "Baixar: " + (a ? "Sim" : "Não") + "   •   Reiniciar: " + (b ? "Sim" : "Não"), 4000);
    }

    public string DebugLogPath { get { return System.IO.Path.Combine(Storage.Dir, "debug.log"); } }
    int tick;

    const int HK_MENU = 1, HK_TOGGLE = 2, HK_NEXT = 3, HK_AUTO = 4, HK_NOTE_ADD = 5, HK_NOTE_TOGGLE = 6, HK_NOTE_DEL = 7, HK_SHOT = 8, HK_SHOT_VIEW = 9, HK_UP = 10, HK_DOWN = 11, HK_LEFT = 12, HK_RIGHT = 13, HK_CENTER = 14;

    public CrosshairApp()
    {
        AppIcon = LoadIcon();
        Settings = Storage.LoadSettings();
        L.Set(Settings.Language);
        Themes.Active = Themes.Current(Settings);
        // versões antigas usavam Ctrl+Alt+P para o print, que é o mesmo atalho de "próximo perfil"
        if (Settings.ShotKey == Settings.NextProfileKey) Settings.ShotKey = (int)(Keys.Control | Keys.Alt | Keys.S);
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
        testBadge.Show();
        Native.ShowWindow(testBadge.Handle, Native.SW_HIDE);
        debugToFile = TestMode;
        UpdateTestBadge();
        toast.Show();
        Native.ShowWindow(toast.Handle, Native.SW_HIDE);
        Native.ShowWindow(hud.Handle, Native.SW_HIDE);
        UpdateOverlay();
        ApplyVisibility();

        Auto = new AutoPresser(Settings);
        AutoPresser.AllowSend = () => InGameOk;
        RestartSync();
        SystemEvents.UserPreferenceChanged += OnWindowsThemeChanged;
        // GitHub: confere 20 s depois de abrir e depois a cada 3 horas
        ghTimer = new System.Windows.Forms.Timer { Interval = 20000 };
        ghTimer.Tick += (s, e) => { ghTimer.Interval = 3 * 3600 * 1000; CheckGitHubUpdate(false); };
        ghTimer.Start();
        // teste: CROSSHAIR_OPENMENU=1 abre o menu logo ao iniciar
        int testPage; if (int.TryParse(Environment.GetEnvironmentVariable("CROSSHAIR_OPENMENU"), out testPage))
        {
            var t = new System.Windows.Forms.Timer { Interval = 800 };
            t.Tick += (s, e) => { t.Stop(); ToggleSettings(true); settingsForm.ShowPage(testPage - 1); };
            t.Start();
        }
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
        var miTest = new ToolStripMenuItem("Modo de testes", null, (s, e) => SetTestMode(!Settings.TestMode)) { Visible = IsAuthorPc };
        var miUpdate = new ToolStripMenuItem("Reiniciar e atualizar", null, (s, e) => UpdateNow());
        var miDownload = new ToolStripMenuItem("Baixar atualização", null, (s, e) => DownloadOffer());
        miUpdate.Font = new Font(miUpdate.Font, FontStyle.Bold);
        menu.Opening += (s, e) =>
        {
            foreach (ToolStripItem it in menu.Items) { var key = it.Tag as string; if (key != null) it.Text = L.T(key); }
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
            miTest.Checked = TestMode;
            miTest.Visible = IsAuthorPc;
            miUpdate.Visible = Updater.StagedBuild > BuildInfo.Build;
            miDownload.Visible = UpdateAvailable && !miUpdate.Visible;
        };
        menu.Items.AddRange(new ToolStripItem[] {
            miSettings, miUpdate, miDownload, miToggle, miOnly, miProfiles, miAuto, miShots, miTest, new ToolStripSeparator(),
            new ToolStripMenuItem("Sair", null, (s, e) => Exit()) });
        foreach (ToolStripItem it in menu.Items) if (it is ToolStripMenuItem) it.Tag = it.Text;   // texto em português = chave
        tray = new NotifyIcon { Icon = AppIcon, Text = L.T("Crosshair Overlay — by ") + AppInfo.Author, Visible = true, ContextMenuStrip = menu };
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
            tray.ShowBalloonTip(2500, L.T("Crosshair Overlay ativo — by ") + AppInfo.Author,
                L.T("Menu: ") + Hotkeys.ToText(Settings.MenuKey) + L.T("   •   Mostrar/ocultar: ") + Hotkeys.ToText(Settings.ToggleKey),
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
        string gTitle = Sync == null ? null : L.T("GRUPO") + (Sync.Connected ? "" : L.T("  (sem conexão)"));
        var gLines = GroupNotes.Select(n => n.Text + "   · " + n.Author).ToList();
        using (var bmp = Notes.Render(NoteLines, gLines, gTitle, Settings.NotesFontSize, Hotkeys.ToText(Settings.NotesAddKey), Settings.NotesStyle, Settings.NotesBgOpacity))
        {
            notesWidth = bmp.Width; notesHeight = bmp.Height;
            int a = Settings.NotesAnchor;
            if (a >= 0 && a <= 8 && a != 4)
            {
                // posição pronta: recalcula com o tamanho atual do bloco (cresce para o lado certo)
                const int m = 16;
                int col = a % 3, row = a / 3;
                Settings.NotesX = col == 0 ? m : col == 1 ? (sc.Width - bmp.Width) / 2 : sc.Width - bmp.Width - m;
                Settings.NotesY = row == 0 ? m : row == 1 ? (sc.Height - bmp.Height) / 2 : sc.Height - bmp.Height - m;
            }
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
        if (badgeShown && tick % 66 == 20) testBadge.EnsureTopMost();
        tick++;
        if (Cfg.Rainbow && tick % 2 == 0) UpdateOverlay();
        if (tick % 66 == 0 && shownState) overlay.EnsureTopMost();
        if (tick % 2 == 0) UpdateHud();
        if (tick % 10 == 0) CheckGame();
        if (tick % 66 == 33 && notesShown) notesOverlay.EnsureTopMost();
        if (tick % 66 == 50) TryAskUpdate();
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

    static bool debugToFile;   // modo de testes: grava o log em debug.log na pasta dos dados

    static void DebugLog(string s)
    {
        string f = Environment.GetEnvironmentVariable("CROSSHAIR_DEBUG");
        if (string.IsNullOrEmpty(f) && debugToFile)
        {
            f = System.IO.Path.Combine(Storage.Dir, "debug.log");
            try { var fi = new System.IO.FileInfo(f); if (fi.Exists && fi.Length > 1024 * 1024) fi.Delete(); } catch { }
        }
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
            tray.ShowBalloonTip(1200, "Crosshair Overlay", L.T("Jogo: ") + exe + L.T("  →  perfil ") + prof, ToolTipIcon.None);
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
        string line2 = L.T("Perfil: ") + Settings.ActiveProfile + (linked != null ? L.T("  (vinculado)") : "");
        var screens = Screen.AllScreens;
        var sc = screens[Util.Clamp(Cfg.Monitor, 0, screens.Length - 1)].Bounds;
        using (var bmp = GameToast.Render(CurrentGameIcon, L.T("Jogo detectado: ") + CurrentGame, line2))
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
            ? L.T("✔ Atalhos ativos.")
            : L.T("⚠ Não foi possível registrar: ") + string.Join(", ", fails) + L.T(" (já usado por outro programa).");
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
        if (settingsForm.IsOpen && !forceShow) { settingsForm.SlideOut(); return; }
        settingsForm.ReloadAll();
        settingsForm.SlideIn(CrossScreen());
    }

    // Tema "Windows 11": quando o Windows troca claro/escuro ou a cor de destaque, o menu acompanha.
    void OnWindowsThemeChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (Settings.MenuTheme == Themes.Windows && (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color || e.Category == UserPreferenceCategory.VisualStyle))
            RebuildSettings();
    }

    // Recria o menu (tema ou posição mudou), mantendo a aba e deixando aberto se estava.
    // Troca o idioma ("" = automático) e redesenha tudo que tem texto.
    public void SetLanguage(string code)
    {
        Settings.Language = code;
        SaveSettings();
        L.Set(code);
        if (quickNote != null) { quickNote.Close(); quickNote = null; }
        if (deleteNotes != null) { deleteNotes.Close(); deleteNotes = null; }
        if (shotViewer != null) { shotViewer.Close(); shotViewer = null; }
        tray.Text = L.T("Crosshair Overlay — by ") + AppInfo.Author;
        RegisterHotkeys();
        UpdateNotes();
        RebuildSettings();
    }

    public void RebuildSettings()
    {
        // tema pode ter mudado: painel de senhas, avisos e caixinhas acompanham
        Themes.Active = Themes.Current(Settings);
        UpdateTestBadge();
        if (quickNote != null && !quickNote.Visible) { quickNote.Close(); quickNote = null; }
        if (deleteNotes != null && !deleteNotes.Visible) { deleteNotes.Close(); deleteNotes = null; }
        if (shotViewer != null && !shotViewer.Visible) { shotViewer.Close(); shotViewer = null; }
        UpdateNotes();
        if (settingsForm == null || settingsForm.IsDisposed) return;
        var old = settingsForm;
        int page = old.CurrentPage;
        bool open = old.Visible;
        IntPtr prev = old.PreviousWindow;
        overlay.BeginInvoke((Action)(() =>
        {
            settingsForm = new SettingsForm(this);
            settingsForm.ReloadAll();
            settingsForm.ShowPage(page);
            if (open) settingsForm.OpenInstant(CrossScreen(), prev);
            old.AllowClose = true;
            old.Close();
            old.Dispose();
        }));
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
        tray.ShowBalloonTip(1200, "Crosshair Overlay", L.T("Perfil: ") + Settings.ActiveProfile, ToolTipIcon.None);
    }

    public void ToggleAuto()
    {
        if (Auto.Running) Auto.Stop();
        else if (Auto.ActiveKeys().Count == 0)
        {
            tray.ShowBalloonTip(1500, L.T("Auto-tecla"), L.T("Nenhuma tecla configurada (aba Auto-tecla)."), ToolTipIcon.Warning);
            return;
        }
        else Auto.Start();
        tray.ShowBalloonTip(1000, L.T("Auto-tecla"), Auto.Running ? L.T("LIGADO") : L.T("DESLIGADO"), ToolTipIcon.None);
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
        SystemEvents.UserPreferenceChanged -= OnWindowsThemeChanged;
        hk.Dispose();
        tray.Visible = false;
        tray.Dispose();
        if (settingsForm != null && !settingsForm.IsDisposed) { settingsForm.AllowClose = true; settingsForm.Close(); }
        hud.Close();
        notesOverlay.Close();
        testBadge.Close();
        if (quickNote != null) quickNote.Close();
        if (deleteNotes != null) deleteNotes.Close();
        if (shotViewer != null) shotViewer.Close();
        if (Updater.StagedBuild > BuildInfo.Build) Updater.Apply(updateRestart);
        toast.Close();
        overlay.Close();
        ExitThread();
    }
}
