using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace BigPicture
{
    /// <summary>Menu del launcher: principale, impostazioni, libreria, opzioni del gioco, sfoglia cartelle.</summary>
    partial class MainWindow
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "BigPictureLauncher";

        /// <summary>Chiusura dal controller (B sulla griglia): chiede conferma per evitare uscite accidentali.</summary>
        void ConfirmExit()
        {
            string t = session != null ? "Chiudere il launcher? " + session.Game.Name + " rimarrà in esecuzione." : "Chiudere il launcher?";
            Confirm(t, "Chiudi", "", delegate { Close(); });
        }

        void MainMenu()
        {
            MenuPage p = new MenuPage();
            p.Title = "Menu";
            if (session != null)
                p.Items.Add(E(session.Game.Name, "In esecuzione", delegate { RunningMenu(null); }));
            p.Items.Add(E("Programmi aperti", "", WindowsMenu));
            p.Items.Add(E("Modalità desktop", "", EnterDesktop));
            p.Items.Add(E("Audio", VolumeHint(), delegate { AudioMenu(0, false); }));
            p.Items.Add(E("Schermo", "", delegate { DisplayMenu(0, false); }));
            if (gpu != null)
                p.Items.Add(E("Driver video", DriverUpdateAvailable ? "Aggiornamento disponibile" : (gpu.Version ?? ""),
                    delegate { DriverMenu(0, false); }));
            p.Items.Add(E("Aggiungi alla libreria", "", AddMenu));
            p.Items.Add(E("Aggiorna libreria", "", delegate { CloseMenus(); Reload(); }));
            p.Items.Add(E("Ordina per", cfg.Sort == "recent" ? "Recenti" : "Nome", delegate
            {
                cfg.Sort = cfg.Sort == "recent" ? "name" : "recent";
                Save(); CloseMenus(); Reload();
            }));
            p.Items.Add(E("Impostazioni", "", delegate { SettingsMenu(0, false); }));
            p.Items.Add(E("Spegnimento", "", PowerMenu));
            p.Items.Add(E("Esci", "", delegate { Close(); }));
            Push(p);
        }

        void AddMenu()
        {
            MenuPage p = new MenuPage();
            p.Title = "Aggiungi alla libreria";
            p.Items.Add(E("Cartella giochi", "", delegate { Browse(null, 0, false); }));
            p.Items.Add(E("Gioco singolo", "", delegate { Browse(null, 1, false); }));
            p.Items.Add(E("Applicazione dal menu Start", "", StartMenuApps));
            p.Items.Add(E("Sito web", "", AddWebsite));
            p.Items.Add(E("Applicazione da file", "", delegate { Browse(null, 2, false); }));
            Push(p);
        }

        // ---------------------------------------------------------------- impostazioni

        void SettingsMenu(int index, bool replace)
        {
            MenuPage p = new MenuPage();
            p.Title = "Impostazioni";
            p.Index = index;
            p.Items.Add(Toggle("Libreria Steam", cfg.ImportSteam, delegate { cfg.ImportSteam = !cfg.ImportSteam; }, true));
            p.Items.Add(Toggle("Libreria Epic Games", cfg.ImportEpic, delegate { cfg.ImportEpic = !cfg.ImportEpic; }, true));
            p.Items.Add(Toggle("Libreria GOG", cfg.ImportGog, delegate { cfg.ImportGog = !cfg.ImportGog; }, true));
            p.Items.Add(Toggle("Copertine online", cfg.DownloadCovers, delegate
            {
                cfg.DownloadCovers = !cfg.DownloadCovers;
                if (cfg.DownloadCovers) RetryCovers();
            }, false));
            p.Items.Add(E("Chiave SteamGridDB", cfg.SteamGridDbKey.Length > 0 ? "Impostata" : "Non impostata", delegate
            {
                int i = menus.Peek().Index;
                OpenKeyboard("Chiave API SteamGridDB", cfg.SteamGridDbKey, delegate(string k)
                {
                    cfg.SteamGridDbKey = k;
                    Save();
                    SettingsMenu(i, true);
                    if (k.Length > 0) RetryCovers();
                });
            }));
            p.Items.Add(E("Cerca copertine mancanti", "", delegate { CloseMenus(); RetryCovers(); }));
            p.Items.Add(E("Avvio con Windows", AutoStart ? "Sì" : "No", delegate
            {
                int i = menus.Peek().Index;
                SetAutoStart(!AutoStart);
                SettingsMenu(i, true);
            }));
            p.Items.Add(E("Cartelle e voci", (cfg.Folders.Count + cfg.Manual.Count + cfg.Apps.Count).ToString(), FoldersMenu));
            int hidden = cfg.Games.Count(g => g.Hidden);
            if (hidden > 0) p.Items.Add(E("Giochi nascosti", hidden.ToString(), HiddenMenu));
            if (replace && menus.Count > 0) menus.Pop();
            Push(p);
        }

        MenuEntry Toggle(string label, bool value, Action flip, bool reload)
        {
            return E(label, value ? "Sì" : "No", delegate
            {
                int i = menus.Peek().Index;
                flip();
                Save();
                SettingsMenu(i, true);
                if (reload) Reload();
            });
        }

        bool AutoStart
        {
            get
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                        return k != null && k.GetValue(RunName) != null;
                }
                catch { return false; }
            }
        }

        void SetAutoStart(bool on)
        {
            if (opt.SelfTest != null) return;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue(RunName, "\"" + Assembly.GetExecutingAssembly().Location + "\"");
                    else k.DeleteValue(RunName, false);
                }
            }
            catch (Exception ex) { Toast("Impossibile modificare l'avvio automatico: " + ex.Message); }
        }

        void PowerMenu()
        {
            MenuPage p = new MenuPage();
            p.Title = "Spegnimento";
            p.Items.Add(E("Sospendi", "", delegate
            {
                Confirm("Sospendere il PC?", "Sospendi", "", delegate
                {
                    CloseMenus();
                    if (opt.SelfTest == null) SetSuspendState(false, false, false);
                });
            }));
            p.Items.Add(E("Riavvia", "", delegate
            {
                Confirm("Riavviare il PC?", "Riavvia", "", delegate { CloseMenus(); Shutdown("/r /t 0"); });
            }));
            p.Items.Add(E("Spegni", "", delegate
            {
                Confirm("Spegnere il PC?", "Spegni", "", delegate { CloseMenus(); Shutdown("/s /t 0"); });
            }));
            Push(p);
        }

        void Shutdown(string args)
        {
            if (opt.SelfTest != null) return;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("shutdown.exe", args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process.Start(psi);
            }
            catch (Exception ex) { Toast("Operazione non riuscita: " + ex.Message); }
        }

        void FoldersMenu()
        {
            MenuPage p = new MenuPage();
            p.Title = "Cartelle e voci";
            foreach (string f in cfg.Folders)
            {
                string path = f;
                p.Items.Add(E(path, Directory.Exists(path) ? "Cartella" : "Non trovata", delegate { ConfirmRemove(path, 0); }));
            }
            foreach (string f in cfg.Manual)
            {
                string path = f;
                p.Items.Add(E(path, File.Exists(path) ? "Gioco" : "Non trovato", delegate { ConfirmRemove(path, 1); }));
            }
            foreach (string f in cfg.Apps)
            {
                string path = f;
                p.Items.Add(E(path, File.Exists(path) ? "Applicazione" : "Non trovata", delegate { ConfirmRemove(path, 2); }));
            }
            if (p.Items.Count == 0) p.Items.Add(E("Nessuna voce", "", PopMenu));
            Push(p);
        }

        void ConfirmRemove(string path, int kind)
        {
            Confirm(path, "Rimuovi dalla libreria", "", delegate
            {
                if (kind == 0) cfg.Folders.RemoveAll(x => Same(x, path));
                else if (kind == 1) cfg.Manual.RemoveAll(x => Same(x, path));
                else cfg.Apps.RemoveAll(x => Same(x, path));
                Save(); CloseMenus(); Reload();
            });
        }

        void HiddenMenu()
        {
            MenuPage p = new MenuPage();
            p.Title = "Giochi nascosti";
            foreach (GameOverride o in cfg.Games.Where(x => x.Hidden))
            {
                GameOverride ov = o;
                string label = !string.IsNullOrEmpty(ov.Label) ? ov.Label : ov.Key;
                p.Items.Add(E(label, "Mostra", delegate
                {
                    ov.Hidden = false;
                    Save(); CloseMenus(); Reload();
                }));
            }
            Push(p);
        }

        // ---------------------------------------------------------------- opzioni del gioco

        void GameMenu(Game g)
        {
            MenuPage p = new MenuPage();
            p.Title = g.Name;
            bool running = session != null && session.Game.Key == g.Key;
            if (running) p.Items.Add(E("In esecuzione", "", delegate { RunningMenu(null); }));
            else p.Items.Add(E("Avvia", g.Source, delegate { CloseMenus(); Launch(g); }));
            if (g.Candidates.Count > 1)
                p.Items.Add(E("Cambia eseguibile", Path.GetFileName(g.Exe), delegate { ExeMenu(g); }));
            p.Items.Add(E("Rinomina", "", delegate
            {
                OpenKeyboard("Nuovo nome", g.Name, delegate(string name)
                {
                    if (name.Length == 0) return;
                    // La cache delle copertine è per nome: la copertina attuale segue il gioco.
                    try
                    {
                        string from = Covers.CachePath(g, coversDir);
                        string to = Path.Combine(coversDir, Covers.Safe(name) + ".jpg");
                        if (File.Exists(from) && !File.Exists(to)) File.Copy(from, to);
                    }
                    catch { }
                    cfg.Get(g.Key).Name = name;
                    Save(); CloseMenus(); Reload();
                });
            }));
            p.Items.Add(E("Cerca copertina", "", delegate
            {
                OpenKeyboard("Titolo da cercare", g.Name, delegate(string term)
                {
                    if (term.Length > 0) SearchCover(g, term);
                });
            }));
            p.Items.Add(E("Riscarica copertina", "", delegate
            {
                CloseMenus();
                Covers.Forget(g, coversDir);
                g.Cover = null;
                List<Game> one = new List<Game>();
                one.Add(g);
                LoadImages(one, false, true);
            }));
            p.Items.Add(E(g.IsApp ? "Sposta in Giochi" : "Sposta in Applicazioni", "", delegate
            {
                cfg.Get(g.Key).Category = g.IsApp ? "game" : "app";
                Save(); CloseMenus();
                Toast(g.Name + (g.IsApp ? " spostato in Giochi" : " spostato in Applicazioni"));
                Reload();
            }));
            p.Items.Add(E("Nascondi", "", delegate
            {
                GameOverride o = cfg.Get(g.Key);
                o.Hidden = true;
                o.Label = g.Name;
                Save(); CloseMenus(); Reload();
            }));
            Push(p);
        }

        void ExeMenu(Game g)
        {
            MenuPage p = new MenuPage();
            p.Title = "Eseguibile di " + g.Name;
            int i = 0;
            foreach (string c in g.Candidates)
            {
                string exe = c;
                string label = exe.StartsWith(g.Key, StringComparison.OrdinalIgnoreCase)
                    ? exe.Substring(g.Key.Length).TrimStart('\\') : exe;
                string hint = "";
                try { hint = (new FileInfo(exe).Length / 1048576.0).ToString("0.#") + " MB"; } catch { }
                if (Same(exe, g.Exe)) p.Index = i;
                p.Items.Add(E(label, hint, delegate
                {
                    g.Exe = exe;
                    cfg.Get(g.Key).Exe = exe;
                    Save(); CloseMenus();
                    Toast("Eseguibile: " + Path.GetFileName(exe));
                }));
                i++;
            }
            Push(p);
        }

        async void SearchCover(Game g, string term)
        {
            string key = cfg.SteamGridDbKey;
            Toast("Ricerca in corso");
            List<CoverHit> hits = await Task.Run(() => Covers.SearchAll(term, key));
            if (hits.Count == 0) { Toast("Nessun risultato"); return; }

            MenuPage p = new MenuPage();
            p.Title = "Risultati: " + term;
            foreach (CoverHit h in hits)
            {
                CoverHit hit = h;
                p.Items.Add(E(hit.Name, hit.Source, delegate { ApplyCover(g, hit); }));
            }
            Push(p);
        }

        async void ApplyCover(Game g, CoverHit hit)
        {
            CloseMenus();
            Toast("Download copertina");
            string key = cfg.SteamGridDbKey;
            string dest = Covers.CachePath(g, coversDir);
            BitmapSource img = await Task.Run(() => Covers.Fetch(hit, dest, key) ? Images.LoadFile(dest) : null);
            if (img == null) { Toast("Copertina non disponibile"); return; }
            try { File.Delete(Path.Combine(coversDir, Covers.Safe(g.Name) + ".none")); } catch { }
            g.Cover = null;
            SetImage(g, img);
            Toast("Copertina aggiornata");
        }

        // ---------------------------------------------------------------- applicazioni

        static readonly string[] AppJunk =
        {
            "uninstall", "disinstall", "readme", "leggimi", "help", "guida", "manual", "documentation",
            "website", "web site", "release notes", "license", "licenza", "support"
        };

        static void CollectLinks(string dir, int depth, List<string> found)
        {
            foreach (string f in Library.SafeFiles(dir))
                if (f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
                    found.Add(f);
            if (depth < 3)
                foreach (string d in Library.SafeDirs(dir)) CollectLinks(d, depth + 1, found);
        }

        /// <summary>Elenco dei collegamenti del menu Start, per aggiungere un'applicazione con un clic.</summary>
        void StartMenuApps()
        {
            List<string> links = new List<string>();
            foreach (Environment.SpecialFolder sf in new[] { Environment.SpecialFolder.CommonStartMenu, Environment.SpecialFolder.StartMenu })
            {
                string dir = Path.Combine(Environment.GetFolderPath(sf), "Programs");
                if (Directory.Exists(dir)) CollectLinks(dir, 0, links);
            }

            Dictionary<string, string> byName = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (string f in links)
            {
                string name = Path.GetFileNameWithoutExtension(f);
                string low = name.ToLowerInvariant();
                if (AppJunk.Any(j => low.Contains(j))) continue;
                if (!byName.ContainsKey(name)) byName[name] = f;
            }

            MenuPage p = new MenuPage();
            p.Title = "Menu Start";
            foreach (KeyValuePair<string, string> kv in byName.OrderBy(x => x.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                string file = kv.Value;
                string parent = Path.GetFileName(Path.GetDirectoryName(file));
                bool added = cfg.Apps.Any(x => Same(x, file));
                p.Items.Add(E(kv.Key, added ? "Già aggiunta" : (parent == "Programs" ? "" : parent), delegate { AddApp(file); }));
            }
            if (p.Items.Count == 0) p.Items.Add(E("Nessuna applicazione trovata", "", PopMenu));
            Push(p);
        }

        /// <summary>Sito web come applicazione: crea un collegamento .url che si apre nel browser predefinito.</summary>
        void AddWebsite()
        {
            OpenKeyboard("Indirizzo web", "https://", delegate(string url)
            {
                url = url.Trim();
                if (url.Length == 0 || url == "https://") return;
                if (url.IndexOf("://", StringComparison.Ordinal) < 0) url = "https://" + url;
                Uri uri;
                if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
                {
                    Toast("Indirizzo non valido");
                    return;
                }
                string guess = uri.Host.StartsWith("www.") ? uri.Host.Substring(4) : uri.Host;
                int dot = guess.IndexOf('.');
                if (dot > 0) guess = guess.Substring(0, dot);
                if (guess.Length > 0) guess = char.ToUpperInvariant(guess[0]) + guess.Substring(1);

                OpenKeyboard("Nome", guess, delegate(string name)
                {
                    if (name.Length == 0) name = guess;
                    try
                    {
                        string dir = Path.Combine(opt.DataDir, "apps");
                        Directory.CreateDirectory(dir);
                        string file = Path.Combine(dir, Covers.Safe(name) + ".url");
                        File.WriteAllText(file, "[InternetShortcut]\r\nURL=" + uri.AbsoluteUri + "\r\n");
                        AddApp(file);
                    }
                    catch (Exception ex) { Toast("Impossibile creare il collegamento: " + ex.Message); }
                });
            });
        }

        void AddApp(string file)
        {
            if (!cfg.Apps.Any(x => Same(x, file))) { cfg.Apps.Add(file); Save(); }
            CloseMenus();
            Toast("Applicazione aggiunta: " + Path.GetFileNameWithoutExtension(file));
            // Passa alla sezione Applicazioni con la nuova voce selezionata.
            if (Selected != null) tabSel[tab] = Selected.Key;
            games.Clear();
            tab = 1;
            tabSel[1] = file;
            Reload();
        }

        // ---------------------------------------------------------------- sfoglia cartelle (dal controller)

        /// <summary>mode: 0 = cartella di giochi, 1 = singolo gioco, 2 = applicazione.</summary>
        void Browse(string path, int mode, bool replace)
        {
            MenuPage p = new MenuPage();
            if (path == null)
            {
                p.Title = mode == 0 ? "Seleziona cartella giochi"
                    : mode == 1 ? "Seleziona gioco" : "Seleziona applicazione";
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    bool ready = false;
                    try { ready = d.IsReady; } catch { }
                    if (!ready) continue;
                    string drive = d.Name;
                    string label = "";
                    try { label = d.VolumeLabel; } catch { }
                    p.Items.Add(E(drive + (label.Length > 0 ? "   " + label : ""), "Unità", delegate { Browse(drive, mode, true); }));
                }
                AddShortcut(p, "Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), mode);
                AddShortcut(p, "Documenti", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), mode);
            }
            else
            {
                p.Title = path;
                string parent = null;
                try { parent = Path.GetDirectoryName(path.TrimEnd('\\') + "\\x"); parent = Path.GetDirectoryName(parent); } catch { }
                Action up = delegate { Browse(parent, mode, true); };
                p.OnBack = up;
                if (mode == 0) p.Items.Add(E("Seleziona questa cartella", "", delegate { AddFolder(path); }));
                p.Items.Add(E("Cartella superiore", "", up));

                foreach (string d in Library.SafeDirs(path).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
                {
                    string dir = d;
                    string name = Path.GetFileName(dir);
                    if (name.Length == 0 || name[0] == '$') continue;
                    try
                    {
                        FileAttributes a = File.GetAttributes(dir);
                        if ((a & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    }
                    catch { continue; }
                    p.Items.Add(E(name, "Cartella", delegate { Browse(dir, mode, true); }));
                }
                if (mode != 0)
                {
                    foreach (string f in Library.SafeFiles(path).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
                    {
                        string file = f;
                        string ext = Path.GetExtension(file).ToLowerInvariant();
                        if (ext != ".exe" && ext != ".lnk" && ext != ".url") continue;
                        p.Items.Add(E(Path.GetFileName(file), mode == 1 ? "Gioco" : "Applicazione", delegate
                        {
                            if (mode == 1) AddManual(file); else AddApp(file);
                        }));
                    }
                }
            }
            if (replace && menus.Count > 0) menus.Pop();
            Push(p);
        }

        void AddShortcut(MenuPage p, string label, string dir, int mode)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            p.Items.Add(E(label, dir, delegate { Browse(dir, mode, true); }));
        }

        void AddFolder(string path)
        {
            if (!cfg.Folders.Any(x => Same(x, path))) { cfg.Folders.Add(path); Save(); }
            CloseMenus();
            Toast("Cartella aggiunta: " + path);
            Reload();
        }

        void AddManual(string file)
        {
            if (!cfg.Manual.Any(x => Same(x, file))) { cfg.Manual.Add(file); Save(); }
            CloseMenus();
            Toast("Gioco aggiunto: " + Path.GetFileName(file));
            Reload();
        }
    }
}
