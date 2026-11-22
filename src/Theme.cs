using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Microsoft.Win32;

// Tema de cores do menu.
class MenuTheme
{
    public string Name;
    public Color Bg, Nav, Input, Border, Text, Dim, Accent;
    // Extras opcionais (usados pelo tema Windows XP): faixa colorida no topo, logo, botão fechar e fontes.
    public Color HeaderTop = Color.Empty, HeaderBottom = Color.Empty, Logo = Color.Empty, Close = Color.Empty;
    public string FontName, TitleFontName;

    public MenuTheme(string name, Color bg, Color nav, Color input, Color border, Color text, Color dim, Color accent)
    {
        Name = name; Bg = bg; Nav = nav; Input = input; Border = border; Text = text; Dim = dim; Accent = accent;
    }

    public MenuTheme Copy(string name)
    {
        return new MenuTheme(name, Bg, Nav, Input, Border, Text, Dim, Accent)
        { HeaderTop = HeaderTop, HeaderBottom = HeaderBottom, Logo = Logo, Close = Close, FontName = FontName, TitleFontName = TitleFontName };
    }

    static Color Mix(Color a, Color b, double t)
    {
        return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    public Color AccentDark { get { return Mix(Accent, Color.Black, 0.35); } }   // aba selecionada
    public Color Hover { get { return Mix(Input, Text, 0.12); } }               // botão com o mouse em cima
    public Color Line { get { return Mix(Nav, Text, 0.14); } }                  // linha do cabeçalho
    public Color Capture { get { return Mix(Input, Accent, 0.35); } }           // campo esperando uma tecla
}

static class Themes
{
    public const string Custom = "Personalizado";
    public const string Windows = "Windows 11";

    // Tema que acompanha o Windows: claro/escuro e cor de destaque do sistema.
    public static MenuTheme FromWindows()
    {
        bool light = false;
        Color accent = Color.FromArgb(0, 120, 212);   // azul padrão do Windows
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                if (k != null) light = Convert.ToInt32(k.GetValue("AppsUseLightTheme", 0)) == 1;
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
            {
                object v = k == null ? null : k.GetValue("AccentColor");   // formato AABBGGRR
                if (v != null)
                {
                    uint abgr = unchecked((uint)Convert.ToInt32(v));
                    accent = Color.FromArgb((int)(abgr & 0xFF), (int)((abgr >> 8) & 0xFF), (int)((abgr >> 16) & 0xFF));
                }
            }
        }
        catch { }
        // no escuro o Windows usa uma versão mais clara do destaque; no claro, mais escura
        double lum = (0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B) / 255;
        if (!light && lum < 0.45) accent = Mix(accent, Color.White, 0.35);
        if (light && lum > 0.6) accent = Mix(accent, Color.Black, 0.3);
        return light
            ? new MenuTheme(Windows, C(243, 243, 243), C(235, 235, 235), C(253, 253, 253), C(214, 214, 214), C(27, 27, 27), C(96, 96, 96), accent)
            : new MenuTheme(Windows, C(32, 32, 32), C(26, 26, 26), C(50, 50, 50), C(64, 64, 64), C(255, 255, 255), C(162, 162, 162), accent);
    }

    static Color Mix(Color a, Color b, double t)
    {
        return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    // Todos os temas mostrados no menu (o do Windows primeiro).
    public static IEnumerable<MenuTheme> All { get { yield return FromWindows(); foreach (var t in Presets) yield return t; } }

    static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

    public static readonly MenuTheme[] Presets =
    {
        // Windows XP (Luna): faixa azul no topo, fundo bege, painel lateral azul-claro, botão fechar vermelho,
        // logo verde como o botão Iniciar, fontes Tahoma / Trebuchet.
        new MenuTheme("Windows XP",       C(236, 233, 216), C(214, 223, 247), C(255, 255, 255), C(127, 157, 185), C(0, 0, 0), C(80, 80, 80), C(49, 106, 197))
        {
            HeaderTop = C(61, 149, 255), HeaderBottom = C(0, 78, 216), Logo = C(58, 158, 58), Close = C(218, 74, 40),
            FontName = "Tahoma", TitleFontName = "Trebuchet MS"
        },
        new MenuTheme("Verde",            C(22, 23, 28), C(17, 18, 22), C(46, 49, 58), C(72, 76, 88),    C(232, 234, 240), C(150, 155, 170), C(0, 210, 130)),
        new MenuTheme("Azul gelo",        C(18, 22, 30), C(13, 16, 23), C(36, 44, 58), C(60, 74, 96),    C(230, 238, 248), C(140, 156, 180), C(70, 170, 255)),
        new MenuTheme("Roxo neon",        C(22, 18, 32), C(16, 12, 24), C(44, 36, 62), C(76, 62, 104),   C(238, 232, 250), C(160, 150, 185), C(180, 100, 255)),
        new MenuTheme("Vermelho tático",  C(24, 18, 18), C(17, 12, 12), C(52, 38, 38), C(90, 62, 62),    C(245, 232, 232), C(175, 150, 150), C(235, 70, 60)),
        new MenuTheme("Laranja",          C(24, 20, 16), C(17, 14, 11), C(52, 44, 36), C(92, 76, 60),    C(245, 238, 228), C(180, 165, 145), C(255, 150, 40)),
        new MenuTheme("Militar",          C(26, 28, 22), C(19, 21, 16), C(50, 54, 42), C(84, 90, 70),    C(232, 236, 222), C(160, 166, 145), C(170, 190, 90)),
        new MenuTheme("Rosa",             C(26, 18, 24), C(19, 12, 17), C(56, 38, 50), C(96, 64, 86),    C(248, 232, 242), C(180, 150, 170), C(255, 90, 170)),
        new MenuTheme("Ciano",            C(16, 24, 26), C(11, 17, 19), C(34, 50, 54), C(56, 84, 90),    C(228, 244, 246), C(140, 170, 176), C(0, 220, 220)),
        new MenuTheme("Preto e dourado",  C(8, 8, 8),    C(0, 0, 0),    C(30, 30, 30), C(64, 64, 64),    C(240, 240, 240), C(140, 140, 140), C(230, 190, 60)),
        new MenuTheme("Claro",            C(242, 243, 246), C(226, 228, 234), C(255, 255, 255), C(190, 194, 204), C(30, 32, 38), C(100, 106, 118), C(0, 150, 95)),
    };

    // Padrão de instalação nova: acompanha o Windows 11.
    public static MenuTheme Default { get { return FromWindows(); } }

    public static MenuTheme Find(string name)
    {
        if (name == "Verde (padrão)") name = "Verde";   // nome antigo
        return Presets.FirstOrDefault(t => t.Name == name);
    }

    // Tema escolhido nas configurações (ou o personalizado).
    public static MenuTheme Current(AppSettings s)
    {
        if (s.MenuTheme == Custom) return FromCustom(s);
        if (s.MenuTheme == Windows) return FromWindows();
        return Find(s.MenuTheme) ?? Default;
    }

    static MenuTheme FromCustom(AppSettings s)
    {
        if (s.ThemeAccent == 0) return Default.Copy(Custom);   // nunca personalizado ainda
        Func<int, Color> c = v => Color.FromArgb(255, Color.FromArgb(v));
        return new MenuTheme(Custom, c(s.ThemeBg), c(s.ThemeNav), c(s.ThemeInput), c(s.ThemeBorder), c(s.ThemeText), c(s.ThemeDim), c(s.ThemeAccent));
    }

    public static void SaveCustom(AppSettings s, MenuTheme t)
    {
        s.MenuTheme = Custom;
        s.ThemeBg = t.Bg.ToArgb(); s.ThemeNav = t.Nav.ToArgb(); s.ThemeInput = t.Input.ToArgb(); s.ThemeBorder = t.Border.ToArgb();
        s.ThemeText = t.Text.ToArgb(); s.ThemeDim = t.Dim.ToArgb(); s.ThemeAccent = t.Accent.ToArgb();
    }
}
