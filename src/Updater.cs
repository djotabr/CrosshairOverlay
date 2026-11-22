using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Forms;

// Atualização automática pelo grupo.
// Quem compila (o autor) tem a chave privada em %APPDATA%\CrosshairOverlay\update-private.xml (criada pelo build.ps1).
// Cada exe leva a chave pública (BuildInfo.UpdateKey) e só aceita versões assinadas com a chave privada do autor —
// então ninguém consegue empurrar um programa falso para o grupo, mesmo tendo o código da sala.
static class Updater
{
    static string ExePath { get { return Application.ExecutablePath; } }
    static string Dir { get { return Path.GetDirectoryName(ExePath); } }
    static string BaseName { get { return Path.GetFileNameWithoutExtension(ExePath); } }
    public static string NewPath { get { return Path.Combine(Dir, BaseName + ".new.exe"); } }
    static string NewSigPath { get { return NewPath + ".sig"; } }
    static string OldPath { get { return Path.Combine(Dir, BaseName + ".old.exe"); } }
    static string SigPath { get { return Path.Combine(Dir, BaseName + ".sig"); } }   // assinatura do exe atual (para repassar)

    public static string PrivateKeyPath
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CrosshairOverlay", "update-private.xml"); }
    }
    public static bool IsAuthor { get { return File.Exists(PrivateKeyPath); } }

    public static string VersionText
    {
        get
        {
            string b = BuildInfo.Build.ToString();
            return b.Length == 12 ? b.Substring(6, 2) + "/" + b.Substring(4, 2) + "/" + b.Substring(0, 4) + " " + b.Substring(8, 2) + ":" + b.Substring(10, 2) + " UTC" : b;
        }
    }

    static RSACryptoServiceProvider Rsa(string xml)
    {
        var rsa = new RSACryptoServiceProvider(new CspParameters(24));   // provedor com suporte a SHA-256
        rsa.FromXmlString(xml);
        return rsa;
    }

    public static string Hex(byte[] b) { return BitConverter.ToString(b).Replace("-", "").ToLowerInvariant(); }
    static byte[] Sha(byte[] d) { using (var s = SHA256.Create()) return s.ComputeHash(d); }

    public static bool Verify(byte[] exe, string shaHex, string sigB64)
    {
        try
        {
            var sha = Sha(exe);
            if (Hex(sha) != shaHex) return false;
            using (var rsa = Rsa(BuildInfo.UpdateKey))
                return rsa.VerifyHash(sha, CryptoConfig.MapNameToOID("SHA256"), Convert.FromBase64String(sigB64));
        }
        catch { return false; }
    }

    // Pacote para mandar ao grupo: o exe que está rodando + assinatura.
    public static bool GetPackage(out byte[] exe, out string shaHex, out string sig)
    {
        exe = null; shaHex = sig = null;
        try
        {
            exe = File.ReadAllBytes(ExePath);
            var sha = Sha(exe);
            shaHex = Hex(sha);
            if (IsAuthor)
                using (var rsa = Rsa(File.ReadAllText(PrivateKeyPath)))
                    sig = Convert.ToBase64String(rsa.SignHash(sha, CryptoConfig.MapNameToOID("SHA256")));
            else if (File.Exists(SigPath))
            {
                var l = File.ReadAllLines(SigPath);
                if (l.Length >= 2 && l[0] == shaHex) sig = l[1];
            }
            return sig != null && Verify(exe, shaHex, sig);
        }
        catch { return false; }
    }

    public static bool CanShare { get { byte[] e; string h, s; return GetPackage(out e, out h, out s); } }

    // Versão já baixada esperando para instalar (0 = nenhuma).
    public static long StagedBuild
    {
        get
        {
            try
            {
                if (!File.Exists(NewPath) || !File.Exists(NewSigPath)) return 0;
                var l = File.ReadAllLines(NewSigPath);
                long b; return l.Length >= 3 && long.TryParse(l[0], out b) ? b : 0;
            }
            catch { return 0; }
        }
    }

    public static bool Stage(byte[] exe, long build, string shaHex, string sig)
    {
        if (!Verify(exe, shaHex, sig)) return false;
        try
        {
            File.WriteAllBytes(NewPath, exe);
            File.WriteAllLines(NewSigPath, new[] { build.ToString(), shaHex, sig });
            return true;
        }
        catch { return false; }
    }

    // Troca o exe pelo baixado (o Windows deixa renomear um exe em uso). Confere a assinatura de novo antes.
    public static bool Apply(bool restart)
    {
        try
        {
            if (StagedBuild <= BuildInfo.Build) { Discard(); return false; }
            var l = File.ReadAllLines(NewSigPath);
            var exe = File.ReadAllBytes(NewPath);
            if (!Verify(exe, l[1], l[2])) { Discard(); return false; }
            if (File.Exists(OldPath)) File.Delete(OldPath);
            File.Move(ExePath, OldPath);
            File.Move(NewPath, ExePath);
            File.WriteAllLines(SigPath, new[] { l[1], l[2] });
            File.Delete(NewSigPath);
            if (restart) Process.Start(ExePath, "--after-update " + Process.GetCurrentProcess().Id);
            return true;
        }
        catch { return false; }
    }

    // Versão anunciada (GitHub ou grupo).
    public class Release { public long Build; public string Sha, Sig, Version = ""; }

    static string GitHubBase { get { return "https://github.com/" + AppInfo.Repo + "/releases/latest/download/"; } }

    // Consulta o último release do GitHub sem baixar o exe.
    // CrosshairOverlay.sig = "build\nsha256\nassinatura\nversão". null = nenhum release.
    public static Release LatestGitHub()
    {
        using (var wc = new System.Net.WebClient())
        {
            wc.Headers["User-Agent"] = "CrosshairOverlay-updater";
            string[] l;
            try { l = wc.DownloadString(GitHubBase + "CrosshairOverlay.sig").Replace("\r", "").Trim().Split('\n'); }
            catch (System.Net.WebException ex)
            {
                var r = ex.Response as System.Net.HttpWebResponse;
                if (r != null && r.StatusCode == System.Net.HttpStatusCode.NotFound) return null;   // ainda sem releases
                throw;
            }
            long b;
            if (l.Length < 3 || !long.TryParse(l[0], out b)) return null;
            return new Release { Build = b, Sha = l[1], Sig = l[2], Version = l.Length > 3 ? l[3].Trim() : "" };
        }
    }

    public static byte[] DownloadGitHubExe()
    {
        using (var wc = new System.Net.WebClient())
        {
            wc.Headers["User-Agent"] = "CrosshairOverlay-updater";
            return wc.DownloadData(GitHubBase + "CrosshairOverlay.exe");
        }
    }

    public static string BuildDate(long build)
    {
        DateTime d;
        return DateTime.TryParseExact(build.ToString(), "yyyyMMddHHmm", null, System.Globalization.DateTimeStyles.AssumeUniversal, out d)
            ? d.ToLocalTime().ToString("dd/MM/yyyy HH:mm") : build.ToString();
    }

    public static void Discard()
    {
        try { File.Delete(NewPath); } catch { }
        try { File.Delete(NewSigPath); } catch { }
    }

    public static void CleanupOld()
    {
        try { if (File.Exists(OldPath)) File.Delete(OldPath); } catch { }
        if (StagedBuild != 0 && StagedBuild <= BuildInfo.Build) Discard();
    }
}
