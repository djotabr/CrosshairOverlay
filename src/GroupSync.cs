using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

// Senha compartilhada com o grupo.
class GroupNote { public string Id, Author, Text; }

// Arquivo anunciado na sala (print ou atualização). Fields = campos depois do remetente.
class FileOffer { public string Kind; public bool Mine; public string[] Fields; public DateTime SentAt; public string Url; }

// Sincroniza as senhas do grupo pelo ntfy.sh (gratuito, sem cadastro).
// Todos que usam o mesmo código de sala leem e escrevem no mesmo "tópico".
// As mensagens vão criptografadas (AES-256 + HMAC) com uma chave tirada do código da sala,
// então o servidor só vê texto embaralhado.
//
// Mensagens (antes de criptografar, campos separados por \n):
//   add  <remetente> <id> <autor> <texto>
//   del  <remetente> <id>
//   req  <remetente>                         -> quem entrou pede a lista completa
//   full <remetente> + linhas "a\tid\tautor\ttexto" / "x\tid"
//   ver  <remetente> <build>                 -> versão do app de quem entrou
//   shot <remetente> <autor>                 + anexo: JPG criptografado
//   upd  <remetente> <build> <sha256> <assinatura> + anexo: exe criptografado
class GroupSync : IDisposable
{
    public const string Server = "https://ntfy.sh/";

    readonly string topic, cacheFile;
    readonly byte[] encKey = new byte[32], macKey = new byte[32];
    readonly string me = Guid.NewGuid().ToString("N").Substring(0, 10);
    readonly Action<Action> ui;                 // executa no thread da interface

    // Só mexer nestes na thread da interface.
    public readonly List<GroupNote> Notes = new List<GroupNote>();   // ordenadas por Id (= ordem de criação)
    readonly HashSet<string> deleted = new HashSet<string>();
    readonly List<string> deletedOrder = new List<string>();
    DateTime reqAt = DateTime.MinValue, fullSeenAt = DateTime.MinValue;

    public event Action Changed;                 // disparado na thread da interface
    public event Action<FileOffer> FileOffered;  // thread da interface
    public event Action<long, DateTime> PeerVersion;
    readonly long myBuild;
    public volatile string Status = "conectando…";
    public volatile bool Connected;

    volatile bool stop;
    readonly ManualResetEvent stopEv = new ManualResetEvent(false);
    readonly object sendLock = new object();
    HttpWebRequest current;
    string lastId;
    readonly Thread th;

    static GroupSync()
    {
        try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }   // TLS 1.2
        ServicePointManager.DefaultConnectionLimit = Math.Max(ServicePointManager.DefaultConnectionLimit, 8);
        ServicePointManager.Expect100Continue = false;
    }

    public GroupSync(string room, string cacheDir, Action<Action> ui, long myBuild = 0)
    {
        this.ui = ui; this.myBuild = myBuild;
        room = Normalize(room);
        using (var sha = SHA256.Create())
            topic = "xho_" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes("topic|" + room))).Replace("-", "").Substring(0, 28).ToLowerInvariant();
        using (var kdf = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(room), Encoding.UTF8.GetBytes("CrosshairOverlay.sync.v1"), 20000))
        {
            var k = kdf.GetBytes(64);
            Buffer.BlockCopy(k, 0, encKey, 0, 32);
            Buffer.BlockCopy(k, 32, macKey, 0, 32);
        }
        cacheFile = Path.Combine(cacheDir, "senhas-grupo.txt");
        LoadCache();
        th = new Thread(Loop) { IsBackground = true, Name = "GroupSync" };
        th.Start();
    }

    // ---------- código da sala ----------

    const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";   // sem 0/O, 1/I

    public static string NewRoomCode()
    {
        var b = new byte[20];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
        var sb = new StringBuilder();
        for (int i = 0; i < 20; i++)
        {
            if (i > 0 && i % 5 == 0) sb.Append('-');
            sb.Append(Alphabet[b[i] % Alphabet.Length]);
        }
        return sb.ToString();
    }

    public static string Normalize(string room)
    {
        return new string((room ?? "").ToUpperInvariant().Where(c => !char.IsWhiteSpace(c)).ToArray());
    }

    public static bool ValidRoom(string room) { return Normalize(room).Length >= 8; }

    // ---------- API (thread da interface) ----------

    public void Add(string text, string author)
    {
        text = Clean(text); author = Clean(author);
        if (text.Length == 0) return;
        string id = DateTime.UtcNow.Ticks.ToString("x16") + Guid.NewGuid().ToString("N").Substring(0, 6);
        AddLocal(id, author, text);
        SaveCache(); Fire();
        Post("add\n" + me + "\n" + id + "\n" + author + "\n" + text);
    }

    public GroupNote Delete(string id)
    {
        var n = Notes.FirstOrDefault(x => x.Id == id);
        DelLocal(id);
        SaveCache(); Fire();
        Post("del\n" + me + "\n" + id);
        return n;
    }

    public void DeleteAll()
    {
        var ids = Notes.Select(n => n.Id).ToList();
        foreach (var id in ids) DelLocal(id);
        SaveCache(); Fire();
        // manda em blocos para não estourar o limite de mensagens do ntfy
        for (int i = 0; i < ids.Count; i += 40)
            Post("full\n" + me + "\n" + string.Join("\n", ids.Skip(i).Take(40).Select(id => "x\t" + id)));
    }

    // ---------- estado local ----------

    static string Clean(string s) { return (s ?? "").Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim(); }

    void AddLocal(string id, string author, string text)
    {
        if (deleted.Contains(id) || Notes.Any(n => n.Id == id)) return;
        var note = new GroupNote { Id = id, Author = author, Text = text };
        int i = Notes.FindIndex(n => string.CompareOrdinal(n.Id, id) > 0);
        if (i < 0) Notes.Add(note); else Notes.Insert(i, note);
    }

    void DelLocal(string id)
    {
        Notes.RemoveAll(n => n.Id == id);
        if (deleted.Add(id))
        {
            deletedOrder.Add(id);
            if (deletedOrder.Count > 1000) { deleted.Remove(deletedOrder[0]); deletedOrder.RemoveAt(0); }
        }
    }

    void Fire() { var c = Changed; if (c != null) c(); }

    void LoadCache()
    {
        try
        {
            if (!File.Exists(cacheFile)) return;
            var lines = File.ReadAllLines(cacheFile, Encoding.UTF8);
            if (lines.Length == 0 || lines[0] != "sala " + topic) return;   // cache de outra sala
            foreach (var l in lines.Skip(1)) ApplyLine(l);
        }
        catch { }
    }

    void SaveCache()
    {
        try
        {
            var l = new List<string> { "sala " + topic };
            l.AddRange(deletedOrder.Select(id => "x\t" + id));
            l.AddRange(Notes.Select(n => "a\t" + n.Id + "\t" + n.Author + "\t" + n.Text));
            Directory.CreateDirectory(Path.GetDirectoryName(cacheFile));
            File.WriteAllLines(cacheFile, l, Encoding.UTF8);
        }
        catch { }
    }

    void ApplyLine(string l)
    {
        var f = l.Split('\t');
        if (f[0] == "a" && f.Length >= 4) AddLocal(f[1], f[2], string.Join(" ", f.Skip(3)));
        else if (f[0] == "x" && f.Length >= 2) DelLocal(f[1]);
    }

    // Mensagem recebida (thread da interface). sentAt = horário do servidor.
    void Apply(string plain, DateTime sentAt, string url)
    {
        var p = plain.Split('\n');
        if (p.Length < 2) return;
        string from = p[1];
        switch (p[0])
        {
            case "add":
                if (p.Length >= 5) AddLocal(p[2], p[3], string.Join(" ", p.Skip(4)));
                break;
            case "del":
                if (p.Length >= 3) DelLocal(p[2]);
                break;
            case "req":
                // só responde pedidos recentes (o histórico do servidor também traz pedidos antigos)
                if (from != me && (DateTime.UtcNow - sentAt).TotalSeconds < 30) { reqAt = DateTime.UtcNow; ScheduleFull(); }
                return;
            case "ver":
                long b;
                if (from != me && p.Length >= 3 && long.TryParse(p[2], out b) && PeerVersion != null) PeerVersion(b, sentAt);
                return;
            case "shot":
            case "upd":
                if (url != null && FileOffered != null)
                    FileOffered(new FileOffer { Kind = p[0], Mine = from == me, Fields = p.Skip(2).ToArray(), SentAt = sentAt, Url = url });
                return;
            case "full":
                if (from != me) fullSeenAt = DateTime.UtcNow;
                foreach (var l in p.Skip(2)) ApplyLine(l);
                break;
            default: return;
        }
        SaveCache(); Fire();
    }

    // Alguém entrou e pediu a lista: espera um pouco; se ninguém respondeu ainda, manda.
    void ScheduleFull()
    {
        if (Notes.Count == 0 && deletedOrder.Count == 0) return;
        int wait = new Random().Next(400, 3000);
        ThreadPool.QueueUserWorkItem(_ =>
        {
            if (stopEv.WaitOne(wait)) return;
            ui(() =>
            {
                if (fullSeenAt > reqAt) return;   // outro membro já respondeu
                fullSeenAt = DateTime.UtcNow;
                var lines = deletedOrder.Skip(Math.Max(0, deletedOrder.Count - 200)).Select(id => "x\t" + id)
                    .Concat(Notes.Select(n => "a\t" + n.Id + "\t" + n.Author + "\t" + n.Text)).ToList();
                var chunk = new StringBuilder();
                foreach (var l in lines)
                {
                    if (chunk.Length + l.Length > 2200) { Post("full\n" + me + "\n" + chunk); chunk.Clear(); }
                    if (chunk.Length > 0) chunk.Append('\n');
                    chunk.Append(l);
                }
                if (chunk.Length > 0) Post("full\n" + me + "\n" + chunk);
            });
        });
    }

    // ---------- rede ----------

    void SetStatus(string s, bool connected)
    {
        if (s == Status && connected == Connected) return;
        Status = s; Connected = connected;
        ui(Fire);
    }

    void Post(string plain)
    {
        string body = Encrypt(plain);
        ThreadPool.QueueUserWorkItem(_ =>
        {
            lock (sendLock)
                for (int tries = 0; tries < 4 && !stop; tries++)
                {
                    try
                    {
                        var r = (HttpWebRequest)WebRequest.Create(Server + topic);
                        r.Method = "POST";
                        r.ContentType = "text/plain";
                        r.Timeout = 10000;
                        var b = Encoding.ASCII.GetBytes(body);
                        r.ContentLength = b.Length;
                        using (var s = r.GetRequestStream()) s.Write(b, 0, b.Length);
                        using (r.GetResponse()) { }
                        return;
                    }
                    catch (Exception ex)
                    {
                        SetStatus("erro ao enviar: " + ex.Message, Connected);
                        if (stopEv.WaitOne(1500 * (tries + 1))) return;
                    }
                }
        });
    }

    // Envia um arquivo (print / atualização) criptografado. done roda na thread da interface.
    public void Upload(string kind, string fields, byte[] data, Action<bool, string> done)
    {
        string meta = Encrypt(kind + "\n" + me + "\n" + fields);
        byte[] body = EncryptBytes(data);
        ThreadPool.QueueUserWorkItem(_ =>
        {
            string err = null;
            lock (sendLock)
                for (int tries = 0; tries < 3 && !stop; tries++)
                {
                    try
                    {
                        var r = (HttpWebRequest)WebRequest.Create(Server + topic);
                        r.Method = "PUT";
                        r.Headers["Filename"] = kind + ".bin";
                        r.Headers["Message"] = meta;
                        r.ContentType = "application/octet-stream";
                        r.Timeout = 30000; r.ReadWriteTimeout = 60000;
                        r.ContentLength = body.Length;
                        using (var s = r.GetRequestStream()) s.Write(body, 0, body.Length);
                        using (r.GetResponse()) { }
                        err = null;
                        break;
                    }
                    catch (WebException ex)
                    {
                        err = ex.Message;
                        try { using (var rd = new StreamReader(ex.Response.GetResponseStream())) err += " " + rd.ReadToEnd(); } catch { }
                        if (stopEv.WaitOne(1500 * (tries + 1))) break;
                    }
                    catch (Exception ex) { err = ex.Message; break; }
                }
            if (done != null) ui(() => done(err == null, err));
        });
    }

    // Baixa e decifra um anexo. done(null) se falhar.
    public void Download(string url, Action<byte[]> done)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            byte[] plain = null;
            try
            {
                if (!url.StartsWith(Server, StringComparison.OrdinalIgnoreCase)) throw new Exception("url estranha");
                using (var wc = new WebClient()) plain = DecryptBytes(wc.DownloadData(url));
            }
            catch { }
            ui(() => done(plain));
        });
    }

    void Loop()
    {
        bool asked = false;
        while (!stop)
        {
            try
            {
                var r = (HttpWebRequest)WebRequest.Create(Server + topic + "/json?since=" + (lastId ?? "12h"));
                r.Timeout = 15000;
                r.ReadWriteTimeout = 120000;     // o servidor manda "keepalive" a cada ~45 s
                current = r;
                using (var resp = r.GetResponse())
                using (var rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    SetStatus("conectado", true);
                    if (!asked) { asked = true; Post("req\n" + me); Post("ver\n" + me + "\n" + myBuild); }
                    string line;
                    while (!stop && (line = rd.ReadLine()) != null)
                    {
                        if (Field(line, "event") != "message") continue;
                        string id = Field(line, "id");
                        if (id != null) lastId = id;
                        string msg = Field(line, "message");
                        if (msg == null) continue;
                        string plain = Decrypt(msg.Replace("\\/", "/"));
                        if (plain == null) continue;
                        long t; var tm = Regex.Match(line, "\"time\":(\\d+)");
                        var sentAt = tm.Success && long.TryParse(tm.Groups[1].Value, out t)
                            ? new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(t) : DateTime.UtcNow;
                        string url = Field(line, "url");
                        if (url != null) url = url.Replace("\\/", "/");
                        ui(() => Apply(plain, sentAt, url));
                    }
                }
            }
            catch (Exception ex)
            {
                if (stop) break;
                SetStatus("sem conexão — tentando de novo (" + ex.Message + ")", false);
            }
            if (stop) break;
            if (Connected) SetStatus("reconectando…", false);
            if (stopEv.WaitOne(3000)) break;
        }
    }

    static string Field(string json, string name)
    {
        var m = Regex.Match(json, "\"" + name + "\":\"((?:[^\"\\\\]|\\\\.)*)\"");
        return m.Success ? m.Groups[1].Value : null;
    }

    // ---------- criptografia ----------

    string Encrypt(string plain) { return Convert.ToBase64String(EncryptBytes(Encoding.UTF8.GetBytes(plain))); }

    byte[] EncryptBytes(byte[] data)
    {
        using (var aes = new AesCryptoServiceProvider { Key = encKey, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 })
        {
            aes.GenerateIV();
            byte[] ct;
            using (var enc = aes.CreateEncryptor()) ct = enc.TransformFinalBlock(data, 0, data.Length);
            var buf = new byte[16 + ct.Length + 32];
            Buffer.BlockCopy(aes.IV, 0, buf, 0, 16);
            Buffer.BlockCopy(ct, 0, buf, 16, ct.Length);
            using (var h = new HMACSHA256(macKey)) Buffer.BlockCopy(h.ComputeHash(buf, 0, 16 + ct.Length), 0, buf, 16 + ct.Length, 32);
            return buf;
        }
    }

    string Decrypt(string b64)
    {
        try
        {
            var d = DecryptBytes(Convert.FromBase64String(b64));
            return d == null ? null : Encoding.UTF8.GetString(d);
        }
        catch { return null; }
    }

    byte[] DecryptBytes(byte[] buf)
    {
        try
        {
            if (buf.Length < 16 + 16 + 32) return null;
            int ctLen = buf.Length - 48;
            byte[] mac;
            using (var h = new HMACSHA256(macKey)) mac = h.ComputeHash(buf, 0, 16 + ctLen);
            int diff = 0;
            for (int i = 0; i < 32; i++) diff |= mac[i] ^ buf[16 + ctLen + i];
            if (diff != 0) return null;                  // outra sala / mensagem alterada
            var iv = new byte[16];
            Buffer.BlockCopy(buf, 0, iv, 0, 16);
            using (var aes = new AesCryptoServiceProvider { Key = encKey, IV = iv, Mode = CipherMode.CBC, Padding = PaddingMode.PKCS7 })
            using (var dec = aes.CreateDecryptor())
                return dec.TransformFinalBlock(buf, 16, ctLen);
        }
        catch { return null; }
    }

    public void Dispose()
    {
        stop = true;
        stopEv.Set();
        try { var r = current; if (r != null) r.Abort(); } catch { }
    }
}
