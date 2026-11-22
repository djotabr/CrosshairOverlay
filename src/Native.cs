using System;
using System.Runtime.InteropServices;

static class Native
{
    public const int WS_EX_TRANSPARENT = 0x20;
    public const int WS_EX_TOOLWINDOW = 0x80;
    public const int WS_EX_TOPMOST = 0x8;
    public const int WS_EX_LAYERED = 0x80000;
    public const int WS_EX_NOACTIVATE = 0x8000000;

    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
    public const int SW_HIDE = 0, SW_SHOWNA = 8;
    public const int WM_HOTKEY = 0x312;
    public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_NOREPEAT = 0x4000;
    public const int ULW_ALPHA = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE { public int CX, CY; public SIZE(int cx, int cy) { CX = cx; CY = cy; } }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    // Raw Input (só mouse) — usado para "ocultar ao segurar o botão direito"
    public const int WM_INPUT = 0x00FF;
    public const uint RIDEV_REMOVE = 0x1, RIDEV_INPUTSINK = 0x100;
    public const uint RID_INPUT = 0x10000003;
    public const int RIM_TYPEMOUSE = 0;
    public const ushort RI_MOUSE_RIGHT_DOWN = 0x4, RI_MOUSE_RIGHT_UP = 0x8;

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICE { public ushort UsagePage; public ushort Usage; public uint Flags; public IntPtr Target; }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);
    [DllImport("user32.dll")]
    public static extern uint GetRawInputData(IntPtr hRawInput, uint command, IntPtr data, ref uint size, uint headerSize);

    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hDC);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObject);

    // SendInput — usado pelo Auto-tecla
    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_SCANCODE = 0x8;

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT { public uint type; public InputUnion u; }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] public static extern int SetWindowTheme(IntPtr hWnd, string app, string idList);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool ReleaseCapture();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();

    // Traz uma janela para frente com foco de teclado mesmo quando outro programa (o jogo) está ativo.
    public static void ForceForeground(IntPtr h)
    {
        uint pid;
        uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out pid);
        uint me = GetCurrentThreadId();
        bool attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
        try { BringWindowToTop(h); SetForegroundWindow(h); }
        finally { if (attached) AttachThreadInput(me, fgThread, false); }
    }
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern bool QueryFullProcessImageName(IntPtr h, int flags, System.Text.StringBuilder sb, ref int size);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp);
}
