using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

// Um perfil de mira. Todos os campos públicos são salvos automaticamente.
public class Config
{
    // Componentes
    public bool ShowCross = true;
    public bool ShowSolidCross = false;   // cruz contínua, sem espaço no meio
    public bool ShowDot = false;
    public bool DotRound = false;
    public bool ShowCircle = false;
    public bool ShowSquare = false;
    public bool ShowChevron = false;
    public bool ArmTop = true, ArmBottom = true, ArmLeft = true, ArmRight = true;

    // Dimensões
    public int Length = 8;
    public int Thickness = 2;
    public int Gap = 4;
    public int DotSize = 3;
    public int Radius = 12;
    public int Rotation = 0;
    public bool AntiAlias = false;

    // Imagem personalizada
    public bool UseImage = false;
    public string ImagePath = "";
    public int ImageScale = 100;

    // Cores
    public int MainColor = unchecked((int)0xFF00FF00);
    public int Opacity = 100;
    public bool Outline = true;
    public int OutlineThickness = 1;
    public int OutlineColor = unchecked((int)0xFF000000);
    public bool Rainbow = false;
    public int RainbowSpeed = 5;

    // Posição
    public int Monitor = 0;
    public int OffsetX = 0;
    public int OffsetY = 0;

    // Comportamento
    public bool HideOnRightClick = false;

    public Config Clone() { return (Config)MemberwiseClone(); }
}

public class AppSettings
{
    public string ActiveProfile = "Padrão";
    public int MenuKey = (int)(Keys.Control | Keys.Alt | Keys.C);
    public int ToggleKey = (int)(Keys.Control | Keys.Alt | Keys.H);
    public int NextProfileKey = (int)(Keys.Control | Keys.Alt | Keys.P);
    public bool NudgeKeys = true;
    public bool FirstRun = true;

    // Auto-tecla (global, não depende do perfil)
    public int AutoToggleKey = (int)(Keys.Control | Keys.Alt | Keys.K);
    public int AutoKey1 = (int)Keys.F;
    public int AutoKey2 = 0;
    public int AutoKey3 = 0;
    public bool AutoTogether = false;   // true = todas juntas (combinação); false = em sequência
    public bool AutoRandom = false;     // false = intervalo fixo; true = aleatório entre mín e máx
    public int AutoIntervalMs = 1000;
    public int AutoMinMs = 800;
    public int AutoMaxMs = 2000;
    public bool AutoHoldRandom = false; // duração fixa ou aleatória entre mín e máx
    public int AutoHoldMs = 50;         // quanto tempo cada tecla fica pressionada
    public int AutoHoldMinMs = 40;
    public int AutoHoldMaxMs = 120;
    // Jogos: perfil vinculado a cada executável, no formato "exe=Perfil|exe2=Perfil2"
    public string GameProfiles = "";
    public bool AutoSwitchProfiles = true;
    public bool OnlyInGame = true;      // overlay só aparece com o jogo em foco (some ao minimizar/sair)
    public int MenuOpacity = 92;
    public int MenuPos = 0;             // 0 esquerda, 1 direita, 2 em cima, 3 embaixo, 4 janela (como antes)
    public string MenuTheme = "Windows 11";   // instalação nova: tema do Windows 11 e menu à esquerda (MenuPos = 0)
    public int ThemeBg, ThemeNav, ThemeInput, ThemeBorder, ThemeText, ThemeDim, ThemeAccent;   // tema "Personalizado"        // transparência do menu (%)

    // Senhas (anotações no overlay)
    public bool NotesShow = true;
    public int NotesAddKey = (int)(Keys.Control | Keys.Alt | Keys.N);
    public int NotesToggleKey = (int)(Keys.Control | Keys.Alt | Keys.M);
    public int NotesDelKey = (int)(Keys.Control | Keys.Alt | Keys.D);
    // Senhas do grupo (sincronizadas pelo ntfy.sh)
    public bool SyncEnabled = false;
    public string SyncRoom = "";
    public string SyncName = "";
    public bool NotesDefaultGroup = true;
    public int ShotKey = (int)(Keys.Control | Keys.Alt | Keys.S);       // tirar print e mandar ao grupo
    public int ShotViewKey = (int)(Keys.Control | Keys.Alt | Keys.O);   // ver prints recebidos
    public bool AutoUpdate = true;          // baixar versões novas que chegam pelo grupo   // caixinha de anotar começa em "grupo"
    public int NotesFontSize = 15;
    public int NotesX = 16, NotesY = 16;   // posição do bloco (canto superior esquerdo), relativa ao monitor da mira

    public bool AutoSound = true;       // bipe a cada aperto
    public int AutoVolume = 40;
    public bool AutoHud = true;         // contador regressivo no overlay
    public int AutoHudPos = 0;          // ver AutoHud.PositionNames
    public int AutoHudScale = 100;
}

public static class Presets
{
    public static readonly string[] Names = {
        "Clássica", "Cruz sem cortes", "Cruz + ponto", "Ponto",
        "Cruz em T", "X diagonal", "X sem cortes", "Círculo + ponto",
        "Círculo com cruz", "Chevron", "Quadrado + ponto", "Sniper",
        "Mínima 1px", "Cruz grossa", "Mira de rifle", "+ pequeno"
    };

    public static void Apply(Config c, int i)
    {
        c.ShowCross = c.ShowSolidCross = c.ShowDot = c.ShowCircle = c.ShowSquare = c.ShowChevron = c.UseImage = false;
        c.ArmTop = c.ArmBottom = c.ArmLeft = c.ArmRight = true;
        c.Rotation = 0; c.DotRound = false;
        switch (Names[i])
        {
            case "Clássica": c.ShowCross = true; c.Length = 8; c.Thickness = 2; c.Gap = 4; break;
            case "Cruz sem cortes": c.ShowSolidCross = true; c.Length = 10; c.Thickness = 2; break;
            case "Cruz + ponto": c.ShowCross = true; c.ShowDot = true; c.Length = 7; c.Thickness = 2; c.Gap = 5; c.DotSize = 2; break;
            case "Ponto": c.ShowDot = true; c.DotSize = 5; c.DotRound = true; break;
            case "Cruz em T": c.ShowCross = true; c.ArmTop = false; c.Length = 8; c.Thickness = 2; c.Gap = 3; break;
            case "X diagonal": c.ShowCross = true; c.Rotation = 45; c.Length = 8; c.Thickness = 2; c.Gap = 4; break;
            case "X sem cortes": c.ShowSolidCross = true; c.Rotation = 45; c.Length = 10; c.Thickness = 2; break;
            case "Círculo + ponto": c.ShowCircle = true; c.ShowDot = true; c.Radius = 12; c.Thickness = 2; c.DotSize = 2; break;
            case "Círculo com cruz": c.ShowCircle = true; c.ShowCross = true; c.Radius = 14; c.Thickness = 1; c.Gap = 6; c.Length = 14; break;
            case "Chevron": c.ShowChevron = true; c.Length = 8; c.Thickness = 2; c.Gap = 2; break;
            case "Quadrado + ponto": c.ShowSquare = true; c.ShowDot = true; c.Radius = 8; c.Thickness = 1; c.DotSize = 2; break;
            case "Sniper": c.ShowCross = true; c.ShowDot = true; c.Length = 80; c.Thickness = 1; c.Gap = 12; c.DotSize = 2; break;
            case "Mínima 1px": c.ShowCross = true; c.Length = 4; c.Thickness = 1; c.Gap = 2; break;
            case "Cruz grossa": c.ShowCross = true; c.Length = 12; c.Thickness = 4; c.Gap = 6; break;
            case "Mira de rifle": c.ShowSolidCross = true; c.ShowCircle = true; c.Length = 22; c.Radius = 16; c.Thickness = 1; break;
            case "+ pequeno": c.ShowSolidCross = true; c.Length = 5; c.Thickness = 2; break;
        }
    }
}

public static class Storage
{
    public static readonly string Dir = GetDir();
    static string ProfDir { get { return Path.Combine(Dir, "profiles"); } }
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "CrosshairOverlay";
    const string SharePrefix = "XH1-";

    static string GetDir()
    {
        string over = Environment.GetEnvironmentVariable("CROSSHAIR_DATA");
        if (!string.IsNullOrEmpty(over)) return over;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CrosshairOverlay");
    }

    public static string ToText(object o)
    {
        var sb = new StringBuilder();
        foreach (var f in o.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            string s = Convert.ToString(f.GetValue(o), CultureInfo.InvariantCulture) ?? "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            sb.Append(f.Name).Append('=').Append(s).Append('\n');
        }
        return sb.ToString();
    }

    public static void FromText(object o, string text)
    {
        var t = o.GetType();
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            int i = line.IndexOf('=');
            if (i <= 0) continue;
            var f = t.GetField(line.Substring(0, i).Trim(), BindingFlags.Public | BindingFlags.Instance);
            if (f == null) continue;
            string v = line.Substring(i + 1);
            try
            {
                if (f.FieldType == typeof(int)) f.SetValue(o, int.Parse(v.Trim(), CultureInfo.InvariantCulture));
                else if (f.FieldType == typeof(bool)) f.SetValue(o, bool.Parse(v.Trim()));
                else if (f.FieldType == typeof(string)) f.SetValue(o, v);
            }
            catch { }
        }
    }

    public static string Sanitize(string name)
    {
        if (name == null) return "";
        foreach (char ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch.ToString(), "");
        name = name.Trim();
        if (name.Length > 40) name = name.Substring(0, 40).Trim();
        return name;
    }

    public static List<string> ListProfiles()
    {
        if (!Directory.Exists(ProfDir)) return new List<string>();
        var l = Directory.GetFiles(ProfDir, "*.txt").Select(p => Path.GetFileNameWithoutExtension(p)).ToList();
        l.Sort(StringComparer.OrdinalIgnoreCase);
        return l;
    }

    static string ProfilePath(string name) { return Path.Combine(ProfDir, name + ".txt"); }

    public static Config LoadProfile(string name)
    {
        var c = new Config();
        try { string p = ProfilePath(name); if (File.Exists(p)) FromText(c, File.ReadAllText(p, Encoding.UTF8)); }
        catch { }
        return c;
    }

    public static void SaveProfile(string name, Config c)
    {
        Directory.CreateDirectory(ProfDir);
        File.WriteAllText(ProfilePath(name), ToText(c), Encoding.UTF8);
    }

    public static void DeleteProfile(string name)
    {
        try { File.Delete(ProfilePath(name)); } catch { }
    }

    public static void RenameProfile(string oldName, string newName)
    {
        File.Move(ProfilePath(oldName), ProfilePath(newName));
    }

    public static AppSettings LoadSettings()
    {
        var s = new AppSettings();
        try { string p = Path.Combine(Dir, "settings.txt"); if (File.Exists(p)) FromText(s, File.ReadAllText(p, Encoding.UTF8)); }
        catch { }
        return s;
    }

    public static void SaveSettings(AppSettings s)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(Path.Combine(Dir, "settings.txt"), ToText(s), Encoding.UTF8);
        }
        catch { }
    }

    public static string ToShareCode(Config c)
    {
        return SharePrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(ToText(c)));
    }

    public static Config FromShareCode(string code)
    {
        try
        {
            code = (code ?? "").Trim();
            if (!code.StartsWith(SharePrefix)) return null;
            string text = Encoding.UTF8.GetString(Convert.FromBase64String(code.Substring(SharePrefix.Length)));
            if (!text.Contains("=")) return null;
            var c = new Config();
            FromText(c, text);
            return c;
        }
        catch { return null; }
    }

    public static bool GetAutoStart()
    {
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                return k != null && k.GetValue(RunName) != null;
        }
        catch { return false; }
    }

    public static void SetAutoStart(bool on)
    {
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (k == null) return;
                if (on) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
                else k.DeleteValue(RunName, false);
            }
        }
        catch { }
    }
}
