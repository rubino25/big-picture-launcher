using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Media;

namespace BigPicture
{
    /// <summary>Impostazioni salvate per singolo gioco (chiave = cartella o file del gioco).</summary>
    public class GameOverride
    {
        public string Key { get; set; }
        public string Exe { get; set; }
        public bool Hidden { get; set; }
        public long LastPlayed { get; set; }
        public string Name { get; set; }        // nome scelto dall'utente (rinomina)
        public string Label { get; set; }       // nome mostrato nell'elenco dei nascosti
        public string Category { get; set; }    // "app" / "game": sezione scelta dall'utente
    }

    public class Config
    {
        public List<string> Folders { get; set; }
        public List<string> Manual { get; set; }
        public List<string> Apps { get; set; }          // applicazioni (non giochi) aggiunte a mano
        public List<GameOverride> Games { get; set; }
        public string Sort { get; set; }
        public bool DownloadCovers { get; set; }
        public bool ImportSteam { get; set; }
        public bool ImportEpic { get; set; }
        public bool ImportGog { get; set; }
        public string SteamGridDbKey { get; set; }
        public bool CheckDrivers { get; set; }          // controllo automatico dei driver Nvidia
        public string DriverNotified { get; set; }      // ultima versione già notificata

        public Config()
        {
            CheckDrivers = true;
            DriverNotified = "";
            Folders = new List<string>();
            Manual = new List<string>();
            Apps = new List<string>();
            Games = new List<GameOverride>();
            Sort = "name";
            DownloadCovers = true;
            ImportSteam = true;
            ImportEpic = true;
            ImportGog = true;
            SteamGridDbKey = "";
        }

        public static Config Load(string file)
        {
            Config c = null;
            try
            {
                if (File.Exists(file))
                    c = new JavaScriptSerializer().Deserialize<Config>(File.ReadAllText(file, Encoding.UTF8));
            }
            catch { }
            if (c == null) c = new Config();
            if (c.Folders == null) c.Folders = new List<string>();
            if (c.Manual == null) c.Manual = new List<string>();
            if (c.Apps == null) c.Apps = new List<string>();
            if (c.Games == null) c.Games = new List<GameOverride>();
            if (c.Sort != "recent") c.Sort = "name";
            if (c.SteamGridDbKey == null) c.SteamGridDbKey = "";
            if (c.DriverNotified == null) c.DriverNotified = "";
            return c;
        }

        public void Save(string file)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, new JavaScriptSerializer().Serialize(this), Encoding.UTF8);
            }
            catch { }
        }

        public GameOverride Find(string key)
        {
            foreach (GameOverride o in Games)
                if (string.Equals(o.Key, key, StringComparison.OrdinalIgnoreCase)) return o;
            return null;
        }

        public GameOverride Get(string key)
        {
            GameOverride o = Find(key);
            if (o == null)
            {
                o = new GameOverride();
                o.Key = key;
                Games.Add(o);
            }
            return o;
        }
    }

    public class Game : INotifyPropertyChanged
    {
        public string Key;                  // cartella del gioco, file (.exe/.lnk/.url) oppure "steam:id", "epic:id", "gog:id"
        public string Exe;                  // cosa viene avviato (file o indirizzo steam:// / com.epicgames.launcher://)
        public bool IsFile;                 // true = voce a file singolo
        public bool IsApp;                  // true = sta nella sezione Applicazioni
        public List<string> Candidates = new List<string>();
        public long LastPlayed;
        public string Source = "";          // "", "Steam", "Epic", "GOG"
        public string InstallDir;           // cartella di installazione (giochi degli store)
        public string IconPath;             // exe da cui prendere l'icona
        public string WorkDir;              // cartella di lavoro all'avvio
        public int SteamAppId;

        /// <summary>Cartella in cui cercare una copertina messa a mano.</summary>
        public string LocalDir
        {
            get { return InstallDir != null ? InstallDir : (IsFile ? Path.GetDirectoryName(Key) : Key); }
        }

        string name;
        ImageSource cover, thumb;

        public string Name { get { return name; } set { name = value; Notify("Name"); } }
        /// <summary>Copertina verticale, riempie la tessera.</summary>
        public ImageSource Cover { get { return cover; } set { cover = value; Notify("Cover"); } }
        /// <summary>Icona o banner orizzontale, usati quando manca la copertina.</summary>
        public ImageSource Thumb { get { return thumb; } set { thumb = value; Notify("Thumb"); } }

        public event PropertyChangedEventHandler PropertyChanged;
        void Notify(string p)
        {
            PropertyChangedEventHandler h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(p));
        }
    }

    static class Library
    {
        const int MaxDepth = 3;

        // Eseguibili che non sono il gioco.
        static readonly string[] BadTokens =
        {
            "unins", "setup", "install", "redist", "vcredist", "vc_redist", "dxsetup", "dxwebsetup", "directx",
            "crash", "report", "helper", "updater", "update", "dotnet", "ndp4", "oalinst", "physx", "prereq",
            "cefsharp", "webengine", "easyanticheat", "beservice", "battleye", "activation", "touchup",
            "cleanup", "uploader", "diagnostic", "subprocess", "7z", "unrar", "python", "ffmpeg"
        };

        static readonly string[] SkipDirs =
        {
            "_commonredist", "commonredist", "redist", "redistributables", "redistributable", "directx",
            "__installer", "_installer", "installer", "prerequisites", "easyanticheat", "battleye",
            "support", "node_modules", "extras", "thirdparty", "dotnet", "$recycle.bin", "system volume information"
        };

        static readonly string[] GenericNames = { "game", "launcher", "start", "play", "run", "main", "app", "client" };

        class ExeInfo
        {
            public string Path;
            public int Depth;
            public long Size;
        }

        public static List<Game> Scan(Config cfg)
        {
            List<Game> list = new List<Game>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Giochi degli store: vanno per primi, così le loro cartelle non compaiono due volte.
            HashSet<string> storeDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<Game> store = new List<Game>();
            if (cfg.ImportSteam) store.AddRange(Stores.Steam());
            if (cfg.ImportEpic) store.AddRange(Stores.Epic());
            if (cfg.ImportGog) store.AddRange(Stores.Gog());
            foreach (Game g in store)
            {
                if (g.InstallDir != null) storeDirs.Add(g.InstallDir.TrimEnd('\\'));
                Add(list, seen, cfg, g);
            }

            foreach (string folder in cfg.Folders.ToArray())
            {
                if (!Directory.Exists(folder)) continue;

                // File avviabili direttamente nella cartella (exe o collegamenti).
                foreach (string f in SafeFiles(folder))
                {
                    if (!IsLaunchable(f)) continue;
                    Add(list, seen, cfg, FileGame(f));
                }

                // Ogni sottocartella = un gioco.
                foreach (string sub in SafeDirs(folder))
                {
                    if (SkipDir(sub) || storeDirs.Contains(sub.TrimEnd('\\'))) continue;
                    Add(list, seen, cfg, FolderGame(sub));
                }
            }

            foreach (string m in cfg.Manual.ToArray())
                if (File.Exists(m)) Add(list, seen, cfg, FileGame(m));

            foreach (string a in cfg.Apps.ToArray())
            {
                if (!File.Exists(a)) continue;
                Game g = FileGame(a);
                g.IsApp = true;
                Add(list, seen, cfg, g);
            }

            if (cfg.Sort == "recent")
                return list.OrderByDescending(g => g.LastPlayed)
                           .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            return list.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        static void Add(List<Game> list, HashSet<string> seen, Config cfg, Game g)
        {
            if (g == null || !seen.Add(g.Key)) return;
            GameOverride o = cfg.Find(g.Key);
            if (o != null)
            {
                if (o.Hidden) return;
                if (!string.IsNullOrEmpty(o.Exe) && File.Exists(o.Exe)) g.Exe = o.Exe;
                if (!string.IsNullOrEmpty(o.Name)) g.Name = o.Name;
                if (o.Category == "app") g.IsApp = true;
                else if (o.Category == "game") g.IsApp = false;
                g.LastPlayed = o.LastPlayed;
            }
            list.Add(g);
        }

        static bool IsLaunchable(string file)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext == ".lnk" || ext == ".url") return true;
            return ext == ".exe" && !IsBad(Path.GetFileNameWithoutExtension(file));
        }

        static Game FileGame(string file)
        {
            Game g = new Game();
            g.Key = file;
            g.Exe = file;
            g.IsFile = true;
            g.Candidates.Add(file);
            string n = Path.GetFileNameWithoutExtension(file);
            if (Array.IndexOf(GenericNames, n.ToLowerInvariant()) >= 0)
            {
                string parent = Path.GetFileName(Path.GetDirectoryName(file));
                if (!string.IsNullOrEmpty(parent)) n = parent;
            }
            g.Name = CleanName(n);
            return g;
        }

        /// <summary>Eseguibile principale dentro una cartella, o null.</summary>
        public static string MainExe(string dir)
        {
            try
            {
                Game g = FolderGame(dir);
                return g != null && File.Exists(g.Exe) ? g.Exe : null;
            }
            catch { return null; }
        }

        static Game FolderGame(string dir)
        {
            List<ExeInfo> found = new List<ExeInfo>();
            Collect(dir, 0, found);

            Game g = new Game();
            g.Key = dir;
            g.Name = CleanName(Path.GetFileName(dir));

            if (found.Count == 0)
            {
                // Nessun exe: accetta collegamenti dentro la cartella.
                List<string> links = SafeFiles(dir).Where(f =>
                {
                    string e = Path.GetExtension(f).ToLowerInvariant();
                    return e == ".lnk" || e == ".url";
                }).ToList();
                if (links.Count == 0) return null;
                g.Exe = links[0];
                g.Candidates = links;
                return g;
            }

            string key = Norm(Path.GetFileName(dir));
            int minDepth = found.Min(e => e.Depth);
            List<ExeInfo> pool = found.Where(e => e.Depth == minDepth).ToList();

            ExeInfo best = pool.FirstOrDefault(e => Norm(Path.GetFileNameWithoutExtension(e.Path)) == key);
            if (best == null && key.Length >= 3)
            {
                best = pool.Where(e =>
                {
                    string n = Norm(Path.GetFileNameWithoutExtension(e.Path));
                    return n.Length >= 3 && (key.Contains(n) || n.Contains(key));
                }).OrderByDescending(e => e.Size).FirstOrDefault();
            }
            if (best == null) best = pool.OrderByDescending(e => e.Size).First();

            g.Exe = best.Path;
            g.Candidates = found.OrderBy(e => e.Depth).ThenByDescending(e => e.Size).Select(e => e.Path).ToList();
            return g;
        }

        static void Collect(string dir, int depth, List<ExeInfo> list)
        {
            if (list.Count >= 200) return;
            foreach (string f in SafeFiles(dir))
            {
                if (!f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (IsBad(Path.GetFileNameWithoutExtension(f))) continue;
                ExeInfo e = new ExeInfo();
                e.Path = f;
                e.Depth = depth;
                try { e.Size = new FileInfo(f).Length; } catch { }
                list.Add(e);
            }
            if (depth >= MaxDepth) return;
            foreach (string sub in SafeDirs(dir))
                if (!SkipDir(sub)) Collect(sub, depth + 1, list);
        }

        static bool IsBad(string name)
        {
            string n = name.ToLowerInvariant();
            foreach (string t in BadTokens) if (n.Contains(t)) return true;
            return false;
        }

        static bool SkipDir(string dir)
        {
            string n = Path.GetFileName(dir).ToLowerInvariant();
            if (n.Length == 0 || n[0] == '.' || n[0] == '$') return true;
            if (Array.IndexOf(SkipDirs, n) >= 0) return true;
            try
            {
                FileAttributes a = File.GetAttributes(dir);
                if ((a & FileAttributes.System) != 0 || (a & FileAttributes.Hidden) != 0) return true;
            }
            catch { return true; }
            return false;
        }

        public static string[] SafeFiles(string dir)
        {
            try { return Directory.GetFiles(dir); } catch { return new string[0]; }
        }

        public static string[] SafeDirs(string dir)
        {
            try { return Directory.GetDirectories(dir); } catch { return new string[0]; }
        }

        /// <summary>Solo lettere e cifre minuscole: per confrontare nomi.</summary>
        public static string Norm(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in s) if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        /// <summary>Ripulisce il nome cartella: toglie [tag], (anno), versioni finali, underscore.</summary>
        public static string CleanName(string raw)
        {
            string s = Regex.Replace(raw, @"[\[\(\{][^\]\)\}]*[\]\)\}]", " ");
            s = Regex.Replace(s, @"[\s._-]+v?\d+(\.\d+)+\s*$", "", RegexOptions.IgnoreCase);
            s = s.Replace('_', ' ');
            if (s.IndexOf(' ') < 0) s = s.Replace('.', ' ');
            s = Regex.Replace(s, @"\s+", " ").Trim(' ', '-', '.');
            return s.Length == 0 ? raw : s;
        }
    }
}
