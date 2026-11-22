using System.Reflection;
using System.Runtime.InteropServices;

// Informações exibidas em Propriedades > Detalhes do .exe (ajudam o antivírus a confiar no programa)
[assembly: AssemblyTitle("Crosshair Overlay")]
[assembly: AssemblyDescription("Mira sobreposta (overlay) personalizável para jogos")]
[assembly: AssemblyCompany("DJotaBR")]
[assembly: AssemblyProduct("Crosshair Overlay")]
[assembly: AssemblyCopyright("© 2026 DJotaBR")]
[assembly: AssemblyTrademark("DJotaBR")]
[assembly: AssemblyVersion("2.2.0.0")]
[assembly: AssemblyFileVersion("2.2.0.0")]
[assembly: AssemblyInformationalVersion("2.2.0")]
[assembly: ComVisible(false)]
[assembly: Guid("3c1f6a52-8d2e-4b7a-9f10-6a1e2d4c7b91")]

static class AppInfo
{
    public const string Name = "Crosshair Overlay";
    public const string Author = "DJotaBR";
    public const string Version = "2.2.0";
    public const string Repo = "djotabr/CrosshairOverlay";   // GitHub: de onde vêm as versões novas
    public const string RepoUrl = "https://github.com/" + Repo;
    public const string AuthorUrl = "https://github.com/djotabr";
    public const string PixKey = "5ae47365-057d-4fa9-ab86-91d59b9709aa";   // chave PIX para doações (vazio = botão desativado)
}
