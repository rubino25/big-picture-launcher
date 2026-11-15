using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace BigPicture
{
    /// <summary>Gestione del PC: gioco in esecuzione (standby), programmi aperti, modalità desktop, audio, schermo.</summary>
    partial class MainWindow
    {
        GameSession session;
        bool standby;                   // gioco avviato: il controller non comanda il launcher
        DispatcherTimer sessionTimer;
        bool checking;

        bool desktop, desktopFresh;     // modalità desktop: il controller fa da mouse e tastiera
        DesktopHud hud;
        OskWindow osk;
        bool leftDown, rightDown, startSolo;
        double accX, accY, accWheelV, accWheelH;
        Pad dPrev;
        DateTime dRepeat;
        WindowState savedState = WindowState.Maximized;

        static readonly string[] Untouchable =
        {
            "explorer", "dwm", "csrss", "winlogon", "applicationframehost", "shellexperiencehost",
            "startmenuexperiencehost", "searchhost", "textinputhost", "lockapp", "systemsettings"
        };

        // ================================================================ gioco in esecuzione

        void StartSession(Game g, Process p)
        {
            int pid = 0;
            if (p != null) try { pid = p.Id; } catch { }
            session = new GameSession(g, pid);
            standby = true;
            ShowStandby();
            if (sessionTimer == null)
            {
                sessionTimer = new DispatcherTimer();
                sessionTimer.Interval = TimeSpan.FromSeconds(1);
                sessionTimer.Tick += delegate { CheckSession(); };
            }
            sessionTimer.Start();
        }

        void ShowStandby()
        {
            bool on = session != null && standby;
            standbyOverlay.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (on)
            {
                if (menus.Count > 0) CloseMenus();
                if (kb != null) { kb = null; RenderKb(false); }
                standbyTitle.Text = session.Game.Name;
                standbySub.Text = session.Alive || !session.Trackable ? "In esecuzione" : "Avvio in corso";
            }
            runText.Text = session != null && !standby ? "▶  " + session.Game.Name + " in esecuzione" : "";
        }

        /// <summary>Ogni secondo: il gioco è ancora aperto? (lettura dei processi in un thread a parte)</summary>
        async void CheckSession()
        {
            GameSession s = session;
            if (s == null) { sessionTimer.Stop(); return; }
            if (checking || !s.Trackable) return;
            checking = true;
            try { await Task.Run(() => s.Refresh()); }
            catch { }
            finally { checking = false; }
            if (s != session) return;

            if (s.Alive) { if (standby) ShowStandby(); return; }
            // Chiuso. Se è durato pochissimo era un avviatore che ha passato la mano a un programma
            // già aperto (es. il browser): in quel caso il launcher non si rimette davanti.
            if (s.Seen && s.Misses >= 3) EndSession(s.Seconds > 12);
            else if (!s.Seen && s.Seconds > 90)
            {
                EndSession(false);
                Toast("Avvio di " + s.Game.Name + " non rilevato");
            }
        }

        void EndSession(bool closed = false)
        {
            GameSession s = session;
            session = null;
            if (sessionTimer != null) sessionTimer.Stop();
            standby = false;
            ShowStandby();
            if (s == null || !closed || desktop) return;
            BringToFront();
            Toast(s.Game.Name + " chiuso");
        }

        /// <summary>Back+Start / Guide: riporta il launcher davanti; se c'è un gioco aperto, propone cosa farne.</summary>
        void Recall()
        {
            if (desktop) { ExitDesktop(); return; }
            standby = false;
            if (session != null && !session.Trackable) session = null;     // non si può sapere se è ancora aperto
            ShowStandby();
            BringToFront();
            if (session != null) RunningMenu(null);
        }

        void RunningMenu(Game other)
        {
            GameSession s = session;
            if (s == null)
            {
                if (other != null) Launch(other);
                return;
            }
            MenuPage p = new MenuPage();
            p.Title = s.Game.Name + (s.Alive ? " (in esecuzione)" : " (avvio in corso)");
            p.Items.Add(E("Riprendi", "", ReturnToGame));
            if (other != null)
                p.Items.Add(E("Avvia " + other.Name, "", delegate
                {
                    CloseMenus();
                    Launch(other, true);
                }));
            p.Items.Add(E("Chiudi", "", CloseGameGently));
            p.Items.Add(E("Forza chiusura", "", KillGame));
            p.Items.Add(E("Modalità desktop", "", EnterDesktop));
            p.Items.Add(E("Torna alla libreria", "", CloseMenus));
            Push(p);
        }

        List<WinInfo> SessionWindows(GameSession s)
        {
            HashSet<int> pids = new HashSet<int>(s.Pids);
            return WinList.Get(Hwnd).Where(w => pids.Contains((int)w.Pid)).ToList();
        }

        void ReturnToGame()
        {
            GameSession s = session;
            if (s == null) return;
            CloseMenus();
            List<WinInfo> wins = SessionWindows(s);
            if (wins.Count == 0)
            {
                Toast("Finestra non trovata");
                return;
            }
            IntPtr h = wins[0].Handle;
            if (IsIconic(h)) ShowWindow(h, 9);
            TryForeground(h);
            standby = true;
            ShowStandby();
        }

        void CloseGameGently()
        {
            GameSession s = session;
            if (s == null) return;
            CloseMenus();
            List<WinInfo> wins = SessionWindows(s);
            if (wins.Count == 0)
            {
                Toast("Nessuna finestra trovata. Usare Forza chiusura.");
                return;
            }
            foreach (WinInfo w in wins) WinList.Close(w.Handle);
            Toast("Chiusura in corso");
            DispatcherTimer t = new DispatcherTimer();
            t.Interval = TimeSpan.FromSeconds(8);
            t.Tick += delegate
            {
                t.Stop();
                if (session == s && s.Alive)
                    Toast(s.Game.Name + " non risponde. Usare Forza chiusura.");
            };
            t.Start();
        }

        void KillGame()
        {
            GameSession s = session;
            if (s == null) return;
            Confirm("Forzare la chiusura di " + s.Game.Name + "? I dati non salvati andranno persi.", "Forza chiusura", "", delegate
            {
                CloseMenus();
                int failed = s.Kill();
                if (failed > 0)
                {
                    Toast("Chiusura non riuscita: permessi insufficienti");
                    return;
                }
                Toast(s.Game.Name + " chiuso");
                if (session == s) EndSession(false);
            });
        }

        // ================================================================ programmi aperti

        void WindowsMenu() { WindowsMenu(false); }

        void WindowsMenu(bool replace)
        {
            MenuPage p = new MenuPage();
            p.Title = "Programmi aperti";
            foreach (WinInfo w in WinList.Get(Hwnd))
            {
                WinInfo win = w;
                p.Items.Add(E(win.Title, win.Process, delegate { WindowActions(win); }));
            }
            if (p.Items.Count == 0) p.Items.Add(E("Nessun programma aperto", "", PopMenu));
            if (replace && menus.Count > 0) menus.Pop();
            Push(p);
        }

        void WindowActions(WinInfo w)
        {
            MenuPage p = new MenuPage();
            p.Title = w.Title;
            p.Items.Add(E("Mostra", "", delegate { FocusWindow(w, false); }));
            p.Items.Add(E("Mostra in modalità desktop", "", delegate { FocusWindow(w, true); }));
            p.Items.Add(E("Chiudi", "", delegate
            {
                WinList.Close(w.Handle);
                PopMenu();
                RefreshWindowsLater();
            }));
            if (Array.IndexOf(Untouchable, (w.Process ?? "").ToLowerInvariant()) < 0)
                p.Items.Add(E("Forza chiusura", "", delegate
                {
                    Confirm("Forzare la chiusura di " + w.Process + "? I dati non salvati andranno persi.", "Forza chiusura", "", delegate
                    {
                        try
                        {
                            Process.GetProcessById((int)w.Pid).Kill();
                            Toast(w.Process + " chiuso");
                        }
                        catch (Exception ex) { Toast("Operazione non riuscita: " + ex.Message); }
                        PopMenu();
                        PopMenu();
                        RefreshWindowsLater();
                    });
                }));
            Push(p);
        }

        void RefreshWindowsLater()
        {
            DispatcherTimer t = new DispatcherTimer();
            t.Interval = TimeSpan.FromMilliseconds(800);
            t.Tick += delegate
            {
                t.Stop();
                if (menus.Count > 0 && menus.Peek().Title == "Programmi aperti") WindowsMenu(true);
            };
            t.Start();
        }

        void FocusWindow(WinInfo w, bool withMouse)
        {
            CloseMenus();
            if (opt.SelfTest != null) return;
            if (IsIconic(w.Handle)) ShowWindow(w.Handle, 9);
            TryForeground(w.Handle);
            swallow = true;
            if (withMouse) EnterDesktop();
            else if (session != null && session.Pids.Contains((int)w.Pid)) { standby = true; ShowStandby(); }
        }

        // ================================================================ modalità desktop

        void EnterDesktop()
        {
            if (desktop || opt.SelfTest != null) return;
            CloseMenus();
            if (kb != null) { kb = null; RenderKb(false); }
            desktop = true;
            desktopFresh = true;
            standby = false;
            ShowStandby();
            ReleaseForce();
            hud = new DesktopHud();
            hud.Show();
            if (WindowState != WindowState.Minimized) savedState = WindowState;
            WindowState = WindowState.Minimized;
            Mouse.OverrideCursor = null;
        }

        void ExitDesktop()
        {
            if (!desktop) return;
            desktop = false;
            if (leftDown) { Input.MouseButton(true, false); leftDown = false; }
            if (rightDown) { Input.MouseButton(false, false); rightDown = false; }
            CloseOsk();
            if (hud != null) { try { hud.Close(); } catch { } hud = null; }
            WindowState = savedState;
            BringToFront();
            swallow = true;
        }

        void OpenOsk()
        {
            if (osk != null) return;
            osk = new OskWindow();
            osk.CloseRequested += CloseOsk;
            osk.Show();
        }

        void CloseOsk()
        {
            if (osk == null) return;
            try { osk.Close(); } catch { }
            osk = null;
        }

        static double Axis(short v)
        {
            const double dz = 7849;
            double a = Math.Abs((double)v);
            if (a < dz) return 0;
            return Math.Sign(v) * Math.Min(1.0, (a - dz) / (32767 - dz));
        }

        static Cmd DirCmd(Pad d)
        {
            if ((d & Pad.Up) != 0) return Cmd.Up;
            if ((d & Pad.Down) != 0) return Cmd.Down;
            if ((d & Pad.Left) != 0) return Cmd.Left;
            return Cmd.Right;
        }

        static ushort DirKey(Pad d)
        {
            if ((d & Pad.Up) != 0) return Input.VkUp;
            if ((d & Pad.Down) != 0) return Input.VkDown;
            if ((d & Pad.Left) != 0) return Input.VkLeft;
            return Input.VkRight;
        }

        void DeskDir(Pad d)
        {
            if (osk != null) osk.Move(DirCmd(d));
            else Input.Tap(DirKey(d));
        }

        /// <summary>Un fotogramma (16 ms) della modalità desktop: levette, grilletti e tasti diventano mouse e tastiera.</summary>
        void DesktopTick(PadState st, DateTime t)
        {
            Pad b = st.Buttons & ~Pad.Guide;
            if (desktopFresh)
            {
                // Tasti ancora premuti dal menu: non devono scattare subito.
                desktopFresh = false;
                dPrev = b;
                return;
            }
            Pad pressed = b & ~dPrev, released = dPrev & ~b;
            dPrev = b;
            bool oskOpen = osk != null;

            // Levetta destra: puntatore, con curva per avere precisione a piccoli spostamenti.
            double rx = Axis(st.RX), ry = Axis(st.RY);
            double mag = Math.Sqrt(rx * rx + ry * ry);
            if (mag > 0)
            {
                double speed = (1 + 26 * Math.Pow(Math.Min(mag, 1), 2.2)) * Math.Max(1, SystemParameters.PrimaryScreenHeight / 1080);
                accX += rx / mag * speed;
                accY -= ry / mag * speed;
                int ix = (int)accX, iy = (int)accY;
                accX -= ix;
                accY -= iy;
                if (ix != 0 || iy != 0) Input.MouseMove(ix, iy);
            }

            // Levetta sinistra: scorrimento verticale e orizzontale.
            accWheelV += Axis(st.LY) * 14;
            accWheelH += Axis(st.LX) * 14;
            if (Math.Abs(accWheelV) >= 20) { int d = (int)accWheelV; accWheelV -= d; Input.Wheel(d, false); }
            if (Math.Abs(accWheelH) >= 20) { int d = (int)accWheelH; accWheelH -= d; Input.Wheel(d, true); }
            if (Axis(st.LY) == 0) accWheelV = 0;
            if (Axis(st.LX) == 0) accWheelH = 0;

            // RT (o A senza tastiera aperta) = tasto sinistro tenuto, quindi si può anche trascinare. LT = tasto destro.
            bool l = st.RT > 100 || (!oskOpen && (b & Pad.A) != 0);
            if (l != leftDown) { Input.MouseButton(true, l); leftDown = l; }
            bool r = st.LT > 100;
            if (r != rightDown) { Input.MouseButton(false, r); rightDown = r; }

            // Croce: frecce, o spostamento sulla tastiera a schermo; con ripetizione se tenuta.
            Pad dirs = b & Dirs;
            if ((pressed & Dirs) != 0) { dRepeat = t.AddMilliseconds(380); DeskDir(pressed & Dirs); }
            else if (dirs != 0 && t >= dRepeat) { dRepeat = t.AddMilliseconds(90); DeskDir(dirs); }

            if (oskOpen)
            {
                if ((pressed & Pad.A) != 0) osk.Press();
                if ((pressed & Pad.X) != 0) Input.Tap(Input.VkBack);
                if ((pressed & Pad.Y) != 0) Input.Tap(Input.VkSpace);
                if ((pressed & Pad.LB) != 0) osk.ToggleShift();
                if ((pressed & Pad.RB) != 0) Input.Tap(Input.VkTab);
                if ((pressed & Pad.B) != 0) CloseOsk();
            }
            else
            {
                if ((pressed & Pad.X) != 0) Input.Tap(Input.VkEnter);
                if ((pressed & Pad.B) != 0) Input.Tap(Input.VkEsc);
                if ((pressed & Pad.Y) != 0) OpenOsk();
                if ((pressed & Pad.RB) != 0) Input.Tap(Input.VkTab);
                if ((pressed & Pad.LB) != 0) Input.AltTab();
            }

            // Start da solo agisce al rilascio, così non scatta quando si usa la combinazione Back+Start.
            if ((pressed & Pad.Start) != 0) startSolo = (b & Pad.Back) == 0;
            if ((b & Pad.Back) != 0) startSolo = false;
            if ((released & Pad.Start) != 0 && startSolo)
            {
                startSolo = false;
                Input.Tap(osk != null ? Input.VkEnter : Input.VkWin);
            }
        }

        // ================================================================ audio

        string VolumeHint()
        {
            float v = Audio.GetVolume();
            if (v < 0) return "";
            return Audio.GetMute() ? "Muto" : (int)Math.Round(v * 100) + "%";
        }

        void StepVolume(float d)
        {
            if (opt.SelfTest != null) return;
            float v = Audio.GetVolume();
            if (v < 0) { Toast("Volume non disponibile"); return; }
            if (d > 0 && Audio.GetMute()) Audio.SetMute(false);
            Audio.SetVolume(v + d);
            Toast("Volume " + VolumeHint());
            UpdateSys();
        }

        void AudioMenu(int index, bool replace)
        {
            MenuPage p = new MenuPage();
            p.Title = "Audio";
            p.Index = index;
            p.Items.Add(E("Alza volume", VolumeHint(), delegate { int i = menus.Peek().Index; StepVolume(0.05f); AudioMenu(i, true); }));
            p.Items.Add(E("Abbassa volume", VolumeHint(), delegate { int i = menus.Peek().Index; StepVolume(-0.05f); AudioMenu(i, true); }));
            p.Items.Add(E("Muto", Audio.GetMute() ? "Sì" : "No", delegate
            {
                int i = menus.Peek().Index;
                if (opt.SelfTest == null) Audio.SetMute(!Audio.GetMute());
                UpdateSys();
                AudioMenu(i, true);
            }));
            foreach (AudioDevice d in Audio.Devices())
            {
                AudioDevice dev = d;
                p.Items.Add(E("Uscita: " + dev.Name, dev.IsDefault ? "In uso" : "", delegate
                {
                    int i = menus.Peek().Index;
                    if (opt.SelfTest == null)
                    {
                        if (Audio.SetDefault(dev.Id)) Toast("Uscita audio: " + dev.Name);
                        else Toast("Impossibile cambiare l'uscita audio");
                    }
                    UpdateSys();
                    AudioMenu(i, true);
                }));
            }
            if (replace && menus.Count > 0) menus.Pop();
            Push(p);
        }

        // ================================================================ schermo

        static readonly string[] TopoLabels = { "Solo schermo principale", "Duplica schermi", "Estendi schermi", "Solo secondo schermo" };
        static readonly int[] Topos = { Display.Internal, Display.Clone, Display.Extend, Display.External };

        void DisplayMenu(int index, bool replace)
        {
            MenuPage p = new MenuPage();
            p.Title = "Schermo";
            p.Index = index;
            int cur = Display.Topology();
            for (int i = 0; i < Topos.Length; i++)
            {
                int topo = Topos[i];
                string label = TopoLabels[i];
                p.Items.Add(E(label, topo == cur ? "In uso" : "", delegate { ChangeTopology(topo, label); }));
            }
            DisplayMode m = Display.Current();
            p.Items.Add(E("Risoluzione", m != null ? m.ToString() : "", ResolutionMenu));
            if (replace && menus.Count > 0) menus.Pop();
            Push(p);
        }

        void ChangeTopology(int topo, string label)
        {
            if (opt.SelfTest != null) return;
            int old = Display.Topology();
            if (old == 0) { Toast("Configurazione schermi non disponibile"); return; }
            if (old == topo) { Toast("Già in uso"); return; }
            if (!Display.SetTopology(topo)) { Toast("Modifica non riuscita. Verificare il collegamento del secondo schermo."); return; }
            ConfirmDisplay(label, delegate { Display.SetTopology(old); });
        }

        void ResolutionMenu()
        {
            MenuPage p = new MenuPage();
            p.Title = "Risoluzione";
            DisplayMode cur = Display.Current();
            int i = 0;
            foreach (DisplayMode m in Display.Modes())
            {
                DisplayMode mode = m;
                bool inUse = cur != null && m.Width == cur.Width && m.Height == cur.Height;
                if (inUse) p.Index = i;
                string label = m.Width + " × " + m.Height;
                p.Items.Add(E(label, inUse ? "In uso" : m.Hz + " Hz", delegate
                {
                    if (opt.SelfTest != null || cur == null) return;
                    if (mode.Width == cur.Width && mode.Height == cur.Height) { Toast("Già in uso"); return; }
                    if (!Display.SetMode(mode)) { Toast("Risoluzione non supportata"); return; }
                    ConfirmDisplay(label, delegate { Display.SetMode(cur); });
                }));
                i++;
            }
            if (p.Items.Count == 0) p.Items.Add(E("Nessuna risoluzione disponibile", "", PopMenu));
            Push(p);
        }

        /// <summary>
        /// Dopo una modifica dello schermo: se non si conferma entro 15 secondi si torna come prima.
        /// Fondamentale a distanza: se l'immagine sparisce dalla TV basta aspettare.
        /// </summary>
        void ConfirmDisplay(string what, Action revert)
        {
            DispatcherTimer t = new DispatcherTimer();
            t.Interval = TimeSpan.FromSeconds(1);
            bool pending = true;
            int left = 15;
            MenuPage p = new MenuPage();
            Action restore = delegate
            {
                if (!pending) return;
                pending = false;
                t.Stop();
                revert();
                CloseMenus();
                Toast("Impostazioni schermo ripristinate");
            };
            p.Title = "Mantenere le modifiche? Ripristino tra " + left + " s";
            p.Items.Add(E("Mantieni", "", delegate
            {
                if (!pending) return;
                pending = false;
                t.Stop();
                CloseMenus();
                Toast("Impostazioni schermo salvate");
            }));
            p.Items.Add(E("Ripristina", "", restore));
            p.OnBack = restore;
            t.Tick += delegate
            {
                if (!pending) { t.Stop(); return; }
                left--;
                if (left <= 0) { restore(); return; }
                p.Title = "Mantenere le modifiche? Ripristino tra " + left + " s";
                if (menus.Count > 0 && menus.Peek() == p) menuTitle.Text = p.Title;
            };
            t.Start();
            Push(p);
        }

        // ================================================================ driver video (Nvidia)

        GpuInfo gpu;
        DriverRelease latestDriver;
        bool driverChecking, driverError;
        DispatcherTimer driverTimer;

        bool DriverUpdateAvailable
        {
            get { return gpu != null && latestDriver != null && Drivers.Newer(latestDriver.Version, gpu.Version); }
        }

        /// <summary>Primo controllo 15 secondi dopo l'avvio, poi ogni 6 ore.</summary>
        void StartDriverChecks()
        {
            gpu = Drivers.Detect();
            if (gpu == null || opt.SelfTest != null) return;
            driverTimer = new DispatcherTimer();
            driverTimer.Interval = TimeSpan.FromSeconds(15);
            driverTimer.Tick += delegate
            {
                driverTimer.Interval = TimeSpan.FromHours(6);
                if (cfg.CheckDrivers) CheckDrivers(false);
            };
            driverTimer.Start();
        }

        async void CheckDrivers(bool manual)
        {
            if (driverChecking) return;
            gpu = Drivers.Detect();         // la versione installata cambia dopo un aggiornamento
            if (gpu == null) return;
            driverChecking = true;
            GpuInfo g = gpu;
            DriverRelease r = null;
            bool err = false;
            try { r = await Task.Run(() => Drivers.Latest(g)); }
            catch { err = true; }
            driverChecking = false;
            if (r != null) latestDriver = r;
            driverError = err || r == null;

            if (manual)
            {
                if (driverError) Toast("Verifica non riuscita");
                else if (!DriverUpdateAvailable) Toast("Driver aggiornato");
            }
            if (DriverUpdateAvailable && cfg.DriverNotified != latestDriver.Version && opt.SelfTest == null)
            {
                Toast("Driver Nvidia " + latestDriver.Version + " disponibile");
                cfg.DriverNotified = latestDriver.Version;
                Save();
            }
            UpdateSys();
            if (menus.Count > 0 && menus.Peek().Title == "Driver video") DriverMenu(menus.Peek().Index, true);
        }

        void DriverMenu(int index, bool replace)
        {
            MenuPage p = new MenuPage();
            p.Title = "Driver video";
            if (gpu == null) p.Items.Add(E("Nessuna scheda Nvidia rilevata", "", PopMenu));
            else
            {
                string latest = driverChecking ? "Verifica in corso"
                    : latestDriver != null ? latestDriver.Version + "   " + latestDriver.Date.ToString("dd/MM/yyyy")
                    : driverError ? "Non disponibile" : "Non verificata";
                p.Items.Add(E(gpu.Name, "", null));
                p.Items.Add(E("Versione installata", gpu.Version ?? "Non rilevata", null));
                p.Items.Add(E("Ultima versione", latest, null));
                int first = p.Items.Count;
                if (DriverUpdateAvailable)
                {
                    string app = Drivers.NvidiaApp();
                    if (app != null)
                        p.Items.Add(E("Aggiorna con " + Path.GetFileNameWithoutExtension(app), "", delegate { OpenForUpdate(app); }));
                    string page = latestDriver.DetailsUrl;
                    if (!string.IsNullOrEmpty(page))
                        p.Items.Add(E("Apri pagina del driver", "", delegate { OpenForUpdate(page); }));
                }
                p.Items.Add(E("Controlla ora", "", delegate
                {
                    int i = menus.Peek().Index;
                    CheckDrivers(true);
                    DriverMenu(i, true);
                }));
                p.Items.Add(E("Controllo automatico", cfg.CheckDrivers ? "Sì" : "No", delegate
                {
                    int i = menus.Peek().Index;
                    cfg.CheckDrivers = !cfg.CheckDrivers;
                    Save();
                    DriverMenu(i, true);
                }));
                p.Index = replace ? index : first;      // all'apertura: prima voce utilizzabile
            }
            if (replace && menus.Count > 0) menus.Pop();
            Push(p);
        }

        /// <summary>Apre NVIDIA App (o la pagina del driver) e passa in modalità desktop per comandarla.</summary>
        void OpenForUpdate(string target)
        {
            CloseMenus();
            if (opt.SelfTest != null) return;
            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                EnterDesktop();
            }
            catch (Exception ex) { Toast("Apertura non riuscita: " + ex.Message); }
        }

        // ================================================================ stato in alto

        void UpdateSys()
        {
            List<string> parts = new List<string>();
            if (DriverUpdateAvailable) parts.Add("Driver " + latestDriver.Version + " disponibile");
            string bat = XInput.Battery();
            if (bat != null) parts.Add(bat == "via cavo" ? "Controller via cavo" : "Batteria controller: " + bat);
            bool net = false;
            try { net = NetworkInterface.GetIsNetworkAvailable(); } catch { }
            parts.Add(net ? "Rete connessa" : "Rete non disponibile");
            string vol = VolumeHint();
            if (vol.Length > 0) parts.Add(vol == "Muto" ? "Muto" : "Volume " + vol);
            sysText.Text = string.Join("   ·   ", parts.ToArray());
            ShowStandby();
        }
    }
}
