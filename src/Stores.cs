using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace BigPicture
{
    /// <summary>Giochi installati tramite Steam, Epic Games e GOG (sola lettura di file e registro).</summary>
    static class Stores
    {
        static string steamPath;
        static bool steamChecked;

        public static string SteamPath()
        {
            if (steamChecked) return steamPath;
            steamChecked = true;
            try
            {
                string p = Environment.GetEnvironmentVariable("BIGPICTURE_STEAM");    // solo per i test
                if (string.IsNullOrEmpty(p))
                {
                    object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null);
                    if (v != null) p = v.ToString().Replace('/', '\\');
                }
                if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) steamPath = p;
            }
            catch { }
            return steamPath;
        }

        static string Val(string text, string key)
        {
            Match m = Regex.Match(text, "\"" + key + "\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null;
        }

        static bool IsSteamTool(string id, string name)
        {
            if (id == "228980") return true;   // Steamworks Common Redistributables
            string n = name.ToLowerInvariant();
            return n.StartsWith("proton ") || n.Contains("steam linux runtime") || n.Contains("steamworks common")
                || n.Contains("redistributable") || n == "steamvr";
        }

        public static List<Game> Steam()
        {
            List<Game> list = new List<Game>();
            try
            {
                string steam = SteamPath();
                if (steam == null) return list;

                List<string> libs = new List<string>();
                libs.Add(steam);
                string vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
                if (File.Exists(vdf))
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                        libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));

                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string lib in libs)
                {
                    string apps = Path.Combine(lib, "steamapps");
                    if (!seen.Add(apps.TrimEnd('\\')) || !Directory.Exists(apps)) continue;
                    foreach (string acf in Directory.GetFiles(apps, "appmanifest_*.acf"))
                    {
                        try
                        {
                            string t = File.ReadAllText(acf, Encoding.UTF8);
                            string id = Val(t, "appid"), name = Val(t, "name"), dir = Val(t, "installdir");
                            int appId;
                            if (id == null || name == null || dir == null || !int.TryParse(id, out appId)) continue;
                            if (IsSteamTool(id, name)) continue;
                            string inst = Path.Combine(Path.Combine(apps, "common"), dir);
                            if (!Directory.Exists(inst)) continue;

                            Game g = new Game();
                            g.Source = "Steam";
                            g.Key = "steam:" + id;
                            g.Name = name;
                            g.Exe = "steam://rungameid/" + id;
                            g.SteamAppId = appId;
                            g.InstallDir = inst;
                            g.Candidates.Add(g.Exe);
                            list.Add(g);
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return list;
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) && v != null ? v.ToString() : "";
        }

        public static List<Game> Epic()
        {
            List<Game> list = new List<Game>();
            try
            {
                string dir = Environment.GetEnvironmentVariable("BIGPICTURE_EPIC");     // solo per i test
                if (string.IsNullOrEmpty(dir))
                    dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        @"Epic\EpicGamesLauncher\Data\Manifests");
                if (!Directory.Exists(dir)) return list;

                foreach (string file in Directory.GetFiles(dir, "*.item"))
                {
                    try
                    {
                        Dictionary<string, object> d = new JavaScriptSerializer()
                            .Deserialize<Dictionary<string, object>>(File.ReadAllText(file, Encoding.UTF8));
                        if (d == null) continue;
                        string name = Str(d, "DisplayName"), loc = Str(d, "InstallLocation"), app = Str(d, "AppName");
                        if (name.Length == 0 || app.Length == 0 || loc.Length == 0 || !Directory.Exists(loc)) continue;
                        if (Str(d, "bIsIncompleteInstall").ToLowerInvariant() == "true") continue;
                        string main = Str(d, "MainGameAppName");
                        if (main.Length > 0 && main != app) continue;           // DLC / componenti aggiuntivi

                        object cats;
                        if (d.TryGetValue("AppCategories", out cats) && cats is IEnumerable && !(cats is string))
                        {
                            bool isGame = false, any = false;
                            foreach (object c in (IEnumerable)cats)
                            {
                                any = true;
                                if (Convert.ToString(c) == "games") isGame = true;
                            }
                            if (any && !isGame) continue;
                        }

                        string ns = Str(d, "CatalogNamespace"), item = Str(d, "CatalogItemId");
                        string id = ns.Length > 0 && item.Length > 0 ? ns + "%3A" + item + "%3A" + app : app;

                        Game g = new Game();
                        g.Source = "Epic";
                        g.Key = "epic:" + app;
                        g.Name = name;
                        g.Exe = "com.epicgames.launcher://apps/" + id + "?action=launch&silent=true";
                        g.InstallDir = loc;
                        string exe = Str(d, "LaunchExecutable");
                        if (exe.Length > 0)
                        {
                            string full = Path.Combine(loc, exe.Replace('/', '\\'));
                            if (File.Exists(full)) g.IconPath = full;
                        }
                        g.Candidates.Add(g.Exe);
                        list.Add(g);
                    }
                    catch { }
                }
            }
            catch { }
            return list;
        }

        public static List<Game> Gog()
        {
            List<Game> list = new List<Game>();
            foreach (string path in new[] { @"SOFTWARE\WOW6432Node\GOG.com\Games", @"SOFTWARE\GOG.com\Games" })
            {
                try
                {
                    using (RegistryKey root = Registry.LocalMachine.OpenSubKey(path))
                    {
                        if (root == null) continue;
                        foreach (string id in root.GetSubKeyNames())
                        {
                            using (RegistryKey k = root.OpenSubKey(id))
                            {
                                if (k == null) continue;
                                string name = Convert.ToString(k.GetValue("gameName"));
                                string exe = Convert.ToString(k.GetValue("exe"));
                                string depends = Convert.ToString(k.GetValue("dependsOn"));
                                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(exe) || !File.Exists(exe)) continue;
                                if (!string.IsNullOrEmpty(depends)) continue;      // DLC

                                Game g = new Game();
                                g.Source = "GOG";
                                g.Key = "gog:" + id;
                                g.Name = name;
                                g.Exe = exe;
                                g.IconPath = exe;
                                g.WorkDir = Convert.ToString(k.GetValue("workingDir"));
                                string inst = Convert.ToString(k.GetValue("path"));
                                g.InstallDir = !string.IsNullOrEmpty(inst) && Directory.Exists(inst) ? inst : Path.GetDirectoryName(exe);
                                g.Candidates.Add(exe);
                                list.Add(g);
                            }
                        }
                    }
                }
                catch { }
                if (list.Count > 0) break;
            }
            return list;
        }
    }
}
