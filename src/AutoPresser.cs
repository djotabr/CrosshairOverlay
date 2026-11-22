using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

// Estado de uma tecla para o contador no overlay (tempos em ms do relógio AutoPresser.Now).
public struct KeyStatus
{
    public Keys Key;
    public long CountFrom;     // quando a contagem atual começou (para a barra de progresso)
    public long NextDownAt;    // quando a tecla será apertada
    public long PressedUntil;  // até quando mostrar a tecla "acesa"
}

// Pressiona de 1 a 3 teclas automaticamente, em intervalo fixo ou aleatório.
// Os tempos são sorteados com antecedência (uma agenda), então o contador de cada tecla é exato
// mesmo no modo aleatório. Mudanças nas configurações valem a partir do próximo sorteio.
class AutoPresser : IDisposable
{
    readonly AppSettings s;
    ManualResetEvent stopEvent;   // um por execução, para Start/Stop rápidos não se misturarem
    static readonly uint MyPid = (uint)Process.GetCurrentProcess().Id;
    static readonly Stopwatch clock = Stopwatch.StartNew();
    public static long Now { get { return clock.ElapsedMilliseconds; } }

    readonly object lk = new object();
    KeyStatus[] status = new KeyStatus[0];

    public bool Running { get { return stopEvent != null; } }

    public AutoPresser(AppSettings settings) { s = settings; }

    public void Start()
    {
        if (Running || ActiveKeys().Count == 0) return;
        var ev = new ManualResetEvent(false);
        stopEvent = ev;
        new Thread(() => Loop(ev)) { IsBackground = true, Name = "AutoPresser" }.Start();
    }

    public void Stop()
    {
        var ev = stopEvent;
        stopEvent = null;
        if (ev != null) ev.Set();
        lock (lk) status = new KeyStatus[0];
    }

    public void Dispose() { Stop(); }

    public KeyStatus[] Snapshot() { lock (lk) return (KeyStatus[])status.Clone(); }

    public List<Keys> ActiveKeys()
    {
        var l = new List<Keys>();
        foreach (int k in new[] { s.AutoKey1, s.AutoKey2, s.AutoKey3 })
            if (k != 0) l.Add((Keys)k & Keys.KeyCode);
        return l;
    }

    static int Pick(Random rnd, bool random, int fixedMs, int minMs, int maxMs, int floor)
    {
        if (!random) return Math.Max(floor, fixedMs);
        int a = Math.Max(floor, minMs), b = Math.Max(floor, maxMs);
        if (a > b) { int t = a; a = b; b = t; }
        return rnd.Next(a, b + 1);
    }

    int NextDelay(Random rnd) { return Pick(rnd, s.AutoRandom, s.AutoIntervalMs, s.AutoMinMs, s.AutoMaxMs, 10); }
    int NextHold(Random rnd) { return Pick(rnd, s.AutoHoldRandom, s.AutoHoldMs, s.AutoHoldMinMs, s.AutoHoldMaxMs, 1); }

    // Não envia teclas enquanto a própria janela de configurações estiver em foco.
    public static Func<bool> AllowSend;   // false = jogo fora de foco: não manda teclas para outros programas

    static bool OurWindowFocused()
    {
        uint pid;
        Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid);
        return pid == MyPid;
    }

    class Entry { public int Idx; public long DownAt; public int Hold; }   // Idx = -1: todas juntas

    void Loop(ManualResetEvent ev)
    {
        var rnd = new Random();
        var down = new List<Keys>();
        var plan = new List<Entry>();
        List<Keys> keys = null;
        string sig = null;
        try
        {
            while (true)
            {
                // (Re)monta a agenda quando as teclas ou o modo mudam.
                var cur = ActiveKeys();
                string curSig = string.Join(",", cur) + "|" + s.AutoTogether;
                if (curSig != sig)
                {
                    sig = curSig; keys = cur; plan.Clear();
                    long t = Now;
                    var st = new KeyStatus[keys.Count];
                    if (keys.Count > 0)
                    {
                        int n = s.AutoTogether ? 1 : keys.Count;
                        for (int i = 0; i < n; i++)
                        {
                            var e = new Entry { Idx = s.AutoTogether ? -1 : i, DownAt = t + NextDelay(rnd), Hold = NextHold(rnd) };
                            plan.Add(e);
                            t = e.DownAt + e.Hold;
                        }
                        for (int i = 0; i < keys.Count; i++)
                            st[i] = new KeyStatus { Key = keys[i], CountFrom = Now, NextDownAt = plan[s.AutoTogether ? 0 : i].DownAt };
                    }
                    lock (lk) status = st;
                }
                if (plan.Count == 0) { if (ev.WaitOne(200)) break; continue; }

                // Espera até o próximo pressionamento (em pedaços, para notar mudanças de configuração).
                var next = plan[0];
                long wait = next.DownAt - Now;
                if (wait > 0) { if (ev.WaitOne((int)Math.Min(wait, 100))) break; continue; }

                down.Clear();
                if (next.Idx < 0) down.AddRange(keys); else down.Add(keys[next.Idx]);
                bool send = !OurWindowFocused() && (AllowSend == null || AllowSend());
                long pressedAt = Now;
                if (send)
                {
                    lock (lk)
                        for (int i = 0; i < status.Length; i++)
                            if (next.Idx < 0 || next.Idx == i) status[i].PressedUntil = pressedAt + Math.Max(next.Hold, 180);
                    if (s.AutoSound) Beeper.Play(Math.Max(0, next.Idx), s.AutoVolume);
                    foreach (var k in down) Send(k, false);
                    ev.WaitOne(next.Hold);
                    for (int i = down.Count - 1; i >= 0; i--) Send(down[i], true);
                }
                down.Clear();

                // Agenda o próximo pressionamento desta mesma tecla no fim da fila.
                plan.RemoveAt(0);
                var last = plan.Count > 0 ? plan[plan.Count - 1] : next;
                var ne = new Entry { Idx = next.Idx, DownAt = Math.Max(Now, last.DownAt + last.Hold) + NextDelay(rnd), Hold = NextHold(rnd) };
                plan.Add(ne);
                lock (lk)
                    for (int i = 0; i < status.Length; i++)
                        if (next.Idx < 0 || next.Idx == i) { status[i].CountFrom = pressedAt; status[i].NextDownAt = ne.DownAt; }

                if (ev.WaitOne(0)) break;
            }
        }
        finally
        {
            foreach (var k in down) Send(k, true);   // nunca deixa tecla "presa"
            ev.Dispose();
        }
    }

    static bool IsExtended(Keys k)
    {
        switch (k)
        {
            case Keys.Insert: case Keys.Delete: case Keys.Home: case Keys.End:
            case Keys.PageUp: case Keys.PageDown: case Keys.Up: case Keys.Down:
            case Keys.Left: case Keys.Right: case Keys.RControlKey: case Keys.RMenu:
            case Keys.LWin: case Keys.RWin: case Keys.Apps: case Keys.Divide:
            case Keys.NumLock: case Keys.PrintScreen:
                return true;
        }
        return false;
    }

    // Envia por scancode (aceito pela maioria dos jogos); cai para código virtual se não houver scancode.
    static void Send(Keys k, bool up)
    {
        uint vk = (uint)k;
        ushort scan = (ushort)Native.MapVirtualKey(vk, 0);
        var ki = new Native.KEYBDINPUT();
        if (scan != 0)
        {
            ki.wScan = scan;
            ki.dwFlags = Native.KEYEVENTF_SCANCODE;
            if (IsExtended(k)) ki.dwFlags |= Native.KEYEVENTF_EXTENDEDKEY;
        }
        else ki.wVk = (ushort)vk;
        if (up) ki.dwFlags |= Native.KEYEVENTF_KEYUP;
        var input = new Native.INPUT { type = Native.INPUT_KEYBOARD };
        input.u.ki = ki;
        Native.SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Native.INPUT)));
    }
}

// Bipe curto gerado na memória — um tom diferente para cada tecla (1 = grave, 3 = agudo).
static class Beeper
{
    static readonly int[] Freqs = { 880, 1175, 1568 };
    static readonly Dictionary<long, System.Media.SoundPlayer> cache = new Dictionary<long, System.Media.SoundPlayer>();

    public static void Play(int idx, int volume)
    {
        volume = Util.Clamp(volume, 0, 100);
        if (volume == 0) return;
        try
        {
            System.Media.SoundPlayer sp;
            long key = idx * 1000L + volume;
            lock (cache)
                if (!cache.TryGetValue(key, out sp))
                {
                    sp = new System.Media.SoundPlayer(new System.IO.MemoryStream(MakeWav(Freqs[Util.Clamp(idx, 0, 2)], 70, volume)));
                    sp.Load();
                    cache[key] = sp;
                }
            sp.Play();
        }
        catch { }
    }

    static byte[] MakeWav(int freq, int ms, int volume)
    {
        const int rate = 44100;
        int n = rate * ms / 1000;
        var ms2 = new System.IO.MemoryStream();
        var w = new System.IO.BinaryWriter(ms2);
        w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' }); w.Write(36 + n * 2);
        w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
        w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' }); w.Write(n * 2);
        double amp = 30000 * (volume / 100.0);
        int fade = rate * 8 / 1000;
        for (int i = 0; i < n; i++)
        {
            double env = Math.Min(1.0, Math.Min(i / (double)fade, (n - i) / (double)fade));
            w.Write((short)(amp * env * Math.Sin(2 * Math.PI * freq * i / rate)));
        }
        w.Flush();
        return ms2.ToArray();
    }
}
