using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace BigPicture
{
    class ProcInfo
    {
        public int Pid, Parent;
        public string Exe;
    }

    /// <summary>Elenco veloce dei processi (pid, padre, nome) e percorso dell'eseguibile.</summary>
    static class Procs
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct PROCESSENTRY32
        {
            public uint dwSize, cntUsage, th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID, cntThreads, th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW")] static extern bool Process32First(IntPtr snap, ref PROCESSENTRY32 e);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW")] static extern bool Process32Next(IntPtr snap, ref PROCESSENTRY32 e);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
        static extern bool QueryFullProcessImageName(IntPtr h, uint flags, StringBuilder name, ref int size);

        public static List<ProcInfo> Snapshot()
        {
            List<ProcInfo> list = new List<ProcInfo>();
            IntPtr snap = CreateToolhelp32Snapshot(2, 0);       // TH32CS_SNAPPROCESS
            if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return list;
            try
            {
                PROCESSENTRY32 e = new PROCESSENTRY32();
                e.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));
                if (!Process32First(snap, ref e)) return list;
                do
                {
                    ProcInfo p = new ProcInfo();
                    p.Pid = (int)e.th32ProcessID;
                    p.Parent = (int)e.th32ParentProcessID;
                    p.Exe = e.szExeFile;
                    list.Add(p);
                }
                while (Process32Next(snap, ref e));
            }
            finally { CloseHandle(snap); }
            return list;
        }

        /// <summary>Percorso completo dell'eseguibile, o null se il processo è protetto.</summary>
        public static string PathOf(int pid)
        {
            IntPtr h = OpenProcess(0x1000, false, (uint)pid);    // PROCESS_QUERY_LIMITED_INFORMATION
            if (h == IntPtr.Zero) return null;
            try
            {
                StringBuilder sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { CloseHandle(h); }
        }
    }

    /// <summary>
    /// Un gioco avviato dal launcher. Si capisce se è ancora in esecuzione da:
    /// il processo avviato e i suoi discendenti, più qualsiasi processo il cui exe sta nella cartella del gioco
    /// (serve per i giochi che partono da Steam/Epic o da un avviatore che si chiude subito).
    /// </summary>
    class GameSession
    {
        public readonly Game Game;
        public readonly DateTime Started = DateTime.UtcNow;
        public readonly int RootPid;
        public readonly string Dir;         // cartella del gioco (con \ finale) oppure null
        public readonly string ExePath;     // exe preciso, per le voci a file singolo

        readonly HashSet<int> known = new HashSet<int>();
        readonly Dictionary<int, string> paths = new Dictionary<int, string>();
        readonly Dictionary<int, string> names = new Dictionary<int, string>();

        public volatile bool Seen;          // almeno una volta è stato trovato un processo del gioco
        public volatile int Misses;         // controlli consecutivi senza processi
        public bool LostFocus;              // il launcher ha perso il primo piano dopo l'avvio
        List<int> pids = new List<int>();

        public GameSession(Game g, int rootPid)
        {
            Game = g;
            RootPid = rootPid;
            if (rootPid > 0) known.Add(rootPid);

            string dir = g.InstallDir;
            if (dir == null && !g.IsFile && g.Source.Length == 0) dir = g.Key;
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) Dir = dir.TrimEnd('\\') + "\\";
            if (g.Exe != null && g.Exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(g.Exe))
                ExePath = g.Exe;
        }

        /// <summary>false = non c'è modo di sapere se è in esecuzione (es. collegamento .url).</summary>
        public bool Trackable { get { return RootPid > 0 || Dir != null || ExePath != null; } }

        public bool Alive { get { lock (this) return pids.Count > 0; } }

        public List<int> Pids { get { lock (this) return new List<int>(pids); } }

        public double Seconds { get { return (DateTime.UtcNow - Started).TotalSeconds; } }

        /// <summary>Aggiorna l'elenco dei processi del gioco. Va chiamato fuori dal thread dell'interfaccia.</summary>
        public void Refresh()
        {
            List<ProcInfo> snap = Procs.Snapshot();
            int me = Process.GetCurrentProcess().Id;

            // Discendenti del processo avviato.
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (ProcInfo p in snap)
                    if (p.Pid != me && known.Contains(p.Parent) && known.Add(p.Pid)) grew = true;
            }

            List<int> found = new List<int>();
            foreach (ProcInfo p in snap)
            {
                if (p.Pid == me || p.Pid <= 4) continue;
                bool mine = known.Contains(p.Pid);
                if (!mine && (Dir != null || ExePath != null))
                {
                    string path;
                    string oldName;
                    if (!paths.TryGetValue(p.Pid, out path) || !names.TryGetValue(p.Pid, out oldName) || oldName != p.Exe)
                    {
                        path = Procs.PathOf(p.Pid);
                        paths[p.Pid] = path;
                        names[p.Pid] = p.Exe;
                    }
                    if (path != null)
                    {
                        if (Dir != null && path.StartsWith(Dir, StringComparison.OrdinalIgnoreCase)) mine = true;
                        else if (ExePath != null && string.Equals(path, ExePath, StringComparison.OrdinalIgnoreCase)) mine = true;
                    }
                }
                if (mine)
                {
                    known.Add(p.Pid);
                    found.Add(p.Pid);
                }
            }

            // I pid "noti" che non esistono più vanno dimenticati (Windows li riutilizza).
            HashSet<int> alive = new HashSet<int>();
            foreach (ProcInfo p in snap) alive.Add(p.Pid);
            known.RemoveWhere(delegate(int pid) { return !alive.Contains(pid); });

            lock (this) pids = found;
            if (found.Count > 0) { Seen = true; Misses = 0; }
            else Misses++;
        }

        /// <summary>Termina a forza tutti i processi del gioco. Ritorna quanti non si sono lasciati chiudere.</summary>
        public int Kill()
        {
            int failed = 0;
            foreach (int pid in Pids)
            {
                try { Process.GetProcessById(pid).Kill(); }
                catch (ArgumentException) { }       // già terminato
                catch { failed++; }
            }
            return failed;
        }
    }
}
