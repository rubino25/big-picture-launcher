using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BigPicture
{
    /// <summary>Mouse e tastiera simulati (SendInput): servono alla modalità desktop.</summary>
    static class Input
    {
        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk, wScan;
            public uint dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct Union
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public uint type;       // 0 = mouse, 1 = tastiera
            public Union u;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint count, ref INPUT input, int size);

        public const ushort VkBack = 0x08, VkTab = 0x09, VkEnter = 0x0D, VkShift = 0x10, VkAlt = 0x12, VkEsc = 0x1B,
            VkSpace = 0x20, VkLeft = 0x25, VkUp = 0x26, VkRight = 0x27, VkDown = 0x28, VkWin = 0x5B,
            VkVolMute = 0xAD, VkVolDown = 0xAE, VkVolUp = 0xAF;

        /// <summary>Quanti eventi sono stati accettati dall'ultima chiamata (0 = rifiutato, es. finestra di amministratore).</summary>
        public static uint LastSent;

        static void Send(ref INPUT i)
        {
            LastSent = SendInput(1, ref i, Marshal.SizeOf(typeof(INPUT)));
        }

        static void Mouse(int dx, int dy, uint data, uint flags)
        {
            INPUT i = new INPUT();
            i.type = 0;
            i.u.mi.dx = dx;
            i.u.mi.dy = dy;
            i.u.mi.mouseData = data;
            i.u.mi.dwFlags = flags;
            Send(ref i);
        }

        public static void MouseMove(int dx, int dy) { Mouse(dx, dy, 0, 0x0001); }

        public static void MouseButton(bool left, bool down)
        {
            Mouse(0, 0, 0, left ? (down ? 0x0002u : 0x0004u) : (down ? 0x0008u : 0x0010u));
        }

        public static void Wheel(int delta, bool horizontal)
        {
            Mouse(0, 0, unchecked((uint)delta), horizontal ? 0x1000u : 0x0800u);
        }

        public static void Key(ushort vk, bool down)
        {
            INPUT i = new INPUT();
            i.type = 1;
            i.u.ki.wVk = vk;
            uint flags = down ? 0u : 0x0002u;
            if ((vk >= VkLeft && vk <= VkDown) || vk == VkWin) flags |= 0x0001;     // tasto esteso
            i.u.ki.dwFlags = flags;
            Send(ref i);
        }

        public static void Tap(ushort vk)
        {
            Key(vk, true);
            Key(vk, false);
        }

        /// <summary>Scrive un carattere qualsiasi nel programma in primo piano.</summary>
        public static void Char(char c)
        {
            INPUT i = new INPUT();
            i.type = 1;
            i.u.ki.wScan = c;
            i.u.ki.dwFlags = 0x0004;            // KEYEVENTF_UNICODE
            Send(ref i);
            i.u.ki.dwFlags = 0x0004 | 0x0002;
            Send(ref i);
        }

        public static void AltTab()
        {
            Key(VkAlt, true);
            Tap(VkTab);
            Key(VkAlt, false);
        }
    }

    class WinInfo
    {
        public IntPtr Handle;
        public string Title;
        public string Process;
        public uint Pid;
    }

    /// <summary>Elenco delle finestre aperte (quelle che comparirebbero in Alt+Tab).</summary>
    static class WinList
    {
        delegate bool EnumProc(IntPtr h, IntPtr l);

        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);

        public static List<WinInfo> Get(IntPtr self)
        {
            List<WinInfo> list = new List<WinInfo>();
            uint me = (uint)Process.GetCurrentProcess().Id;
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                try
                {
                    if (h == self || !IsWindowVisible(h)) return true;
                    if (GetWindow(h, 4) != IntPtr.Zero) return true;                 // ha un proprietario: è una finestra secondaria
                    if ((GetWindowLong(h, -20) & 0x80) != 0) return true;            // WS_EX_TOOLWINDOW
                    int cloaked;
                    if (DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return true;  // app sospese/nascoste
                    StringBuilder sb = new StringBuilder(260);
                    GetWindowText(h, sb, 260);
                    string title = sb.ToString().Trim();
                    if (title.Length == 0 || title == "Program Manager") return true;

                    WinInfo w = new WinInfo();
                    w.Handle = h;
                    w.Title = title;
                    GetWindowThreadProcessId(h, out w.Pid);
                    if (w.Pid == me) return true;
                    try { w.Process = Process.GetProcessById((int)w.Pid).ProcessName; } catch { w.Process = ""; }
                    if (w.Process == "TextInputHost") return true;
                    list.Add(w);
                }
                catch { }
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static void Close(IntPtr h)
        {
            PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero);       // WM_CLOSE: come cliccare la X
        }
    }

    class DisplayMode
    {
        public int Width, Height, Hz;
        public Display.DEVMODE Raw;
        public override string ToString() { return Width + " × " + Height + "  " + Hz + " Hz"; }
    }

    /// <summary>Schermi: modalità di proiezione (come Win+P) e risoluzione dello schermo principale.</summary>
    static class Display
    {
        public const int Internal = 1, Clone = 2, Extend = 4, External = 8;

        [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
        [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint numPaths, IntPtr paths, ref uint numModes, IntPtr modes, out uint topology);
        [DllImport("user32.dll")] static extern int SetDisplayConfig(uint numPaths, IntPtr paths, uint numModes, IntPtr modes, uint flags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields;
            public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string device, int mode, ref DEVMODE dm);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettings(ref DEVMODE dm, int flags);

        /// <summary>Modalità di proiezione attuale (Internal/Clone/Extend/External) o 0 se non si riesce a leggerla.</summary>
        public static int Topology()
        {
            IntPtr p = IntPtr.Zero, m = IntPtr.Zero;
            try
            {
                uint np, nm, topo;
                if (GetDisplayConfigBufferSizes(4, out np, out nm) != 0) return 0;     // QDC_DATABASE_CURRENT
                p = Marshal.AllocHGlobal((int)Math.Max(1, np) * 72);                    // DISPLAYCONFIG_PATH_INFO
                m = Marshal.AllocHGlobal((int)Math.Max(1, nm) * 64);                    // DISPLAYCONFIG_MODE_INFO
                if (QueryDisplayConfig(4, ref np, p, ref nm, m, out topo) != 0) return 0;
                return (int)topo;
            }
            catch { return 0; }
            finally
            {
                if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
                if (m != IntPtr.Zero) Marshal.FreeHGlobal(m);
            }
        }

        public static bool SetTopology(int topology)
        {
            try { return SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, 0x80 | (uint)topology) == 0; }   // SDC_APPLY
            catch { return false; }
        }

        static DEVMODE New()
        {
            DEVMODE d = new DEVMODE();
            d.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            return d;
        }

        public static DisplayMode Current()
        {
            try
            {
                DEVMODE d = New();
                if (!EnumDisplaySettings(null, -1, ref d)) return null;
                return Wrap(d);
            }
            catch { return null; }
        }

        static DisplayMode Wrap(DEVMODE d)
        {
            DisplayMode m = new DisplayMode();
            m.Width = d.dmPelsWidth;
            m.Height = d.dmPelsHeight;
            m.Hz = d.dmDisplayFrequency;
            m.Raw = d;
            return m;
        }

        /// <summary>Risoluzioni disponibili sullo schermo principale: per ciascuna la frequenza più alta.</summary>
        public static List<DisplayMode> Modes()
        {
            Dictionary<long, DisplayMode> best = new Dictionary<long, DisplayMode>();
            try
            {
                for (int i = 0; i < 2000; i++)
                {
                    DEVMODE d = New();
                    if (!EnumDisplaySettings(null, i, ref d)) break;
                    if (d.dmBitsPerPel != 32 || d.dmPelsWidth < 1024 || d.dmPelsHeight < 720) continue;
                    long key = (long)d.dmPelsWidth * 100000L + d.dmPelsHeight;
                    DisplayMode old;
                    if (!best.TryGetValue(key, out old) || d.dmDisplayFrequency > old.Hz) best[key] = Wrap(d);
                }
            }
            catch { }
            List<DisplayMode> list = new List<DisplayMode>(best.Values);
            list.Sort(delegate(DisplayMode a, DisplayMode b)
            {
                int c = (b.Width * b.Height).CompareTo(a.Width * a.Height);
                return c != 0 ? c : b.Width.CompareTo(a.Width);
            });
            return list;
        }

        public static bool SetMode(DisplayMode m)
        {
            try
            {
                DEVMODE d = m.Raw;
                return ChangeDisplaySettings(ref d, 1) == 0;        // CDS_UPDATEREGISTRY
            }
            catch { return false; }
        }
    }
}
