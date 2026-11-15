using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace BigPicture
{
    enum Cmd { Up, Down, Left, Right, Accept, Back, Options, Menu, Start, PageUp, PageDown, TabPrev, TabNext }

    public class MenuEntry
    {
        public string Label { get; set; }
        public string Hint { get; set; }
        public Action Run;
    }

    class MenuPage
    {
        public string Title;
        public List<MenuEntry> Items = new List<MenuEntry>();
        public int Index;
        public Action OnBack;       // null = chiude la pagina
    }

    class KbState
    {
        public string Title;
        public StringBuilder Text = new StringBuilder();
        public int Index = 10;
        public bool Shift;
        public Action<string> Done;
    }

    // Parti: MainWindow.cs (libreria, input, griglia), MainWindow.Menus.cs (menu),
    // MainWindow.System.cs (gioco in esecuzione, modalità desktop, audio, schermo), MainWindow.Test.cs (autotest).
    partial class MainWindow : Window
    {
        const Pad Dirs = Pad.Up | Pad.Down | Pad.Left | Pad.Right;
        const string KbKeys = "1234567890qwertyuiopasdfghjkl'zxcvbnm-:.";      // 4 righe da 10
        static readonly string[] KbSpecial = { "Maiusc", "Spazio", "Canc", "Annulla", "OK" };

        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint from, uint to, bool attach);
        [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("powrprof.dll", SetLastError = true)] static extern bool SetSuspendState(bool hibernate, bool force, bool noWake);

        readonly Options opt;
        readonly string configFile, coversDir;
        readonly Config cfg;
        readonly ObservableCollection<Game> games = new ObservableCollection<Game>();   // sezione mostrata
        List<Game> allItems = new List<Game>();                                         // giochi + applicazioni
        int tab;                                                                        // 0 = giochi, 1 = applicazioni
        readonly string[] tabSel = new string[2];
        readonly Stack<MenuPage> menus = new Stack<MenuPage>();
        KbState kb;

        Grid root, overlay, kbOverlay, standbyOverlay;
        ListBox gameList, menuList, kbList;
        TextBlock countText, statusText, clockText, selName, selSource, emptyText, menuTitle, toastText, kbTitle, kbText;
        TextBlock tabGames, tabApps, sysText, runText, standbyTitle, standbySub;
        Border toast;

        ScrollViewer scroll;
        double scrollTarget = -1;
        DispatcherTimer timer;
        Pad prev;
        bool swallow;               // ignora i tasti finché non vengono rilasciati
        DateTime nextRepeat, volRepeat, nextSys;
        DateTime homeSince;         // da quando è tenuto premuto Back+Start
        bool homeFired;
        string lastClock;
        int loadGen, reloadGen;
        volatile bool busy;
        Point lastMouse;
        bool haveMouse, realMove;
        bool forceInput;            // il controller comanda il launcher anche se Windows non gli ha ridato il focus
        int recallStage;            // con quale stadio è riuscito l'ultimo richiamo (per i test)

        public MainWindow(Options o)
        {
            opt = o;
            configFile = Path.Combine(opt.DataDir, "config.json");
            coversDir = Path.Combine(opt.DataDir, "covers");
            try { Directory.CreateDirectory(coversDir); } catch { }

            cfg = Config.Load(configFile);
            bool changed = false;
            foreach (string f in opt.Folders)
            {
                string path = f;
                if (!cfg.Folders.Any(x => Same(x, path))) { cfg.Folders.Add(path); changed = true; }
            }
            if (changed) Save();

            Title = "Big Picture Launcher";
            Background = Brushes.Black;
            WindowStyle = WindowStyle.None;
            if (opt.SelfTest != null)
            {
                ResizeMode = ResizeMode.NoResize;
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = -30000; Top = 0; Width = 1600; Height = 900;
                ShowActivated = false;
                ShowInTaskbar = false;
            }
            else if (opt.Windowed)
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
                Width = 1280; Height = 720;
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            else
            {
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
            }

            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml"))
                root = (Grid)XamlReader.Load(s);
            Content = root;

            overlay = (Grid)root.FindName("Overlay");
            kbOverlay = (Grid)root.FindName("KbOverlay");
            standbyOverlay = (Grid)root.FindName("StandbyOverlay");
            gameList = (ListBox)root.FindName("GameList");
            menuList = (ListBox)root.FindName("MenuList");
            kbList = (ListBox)root.FindName("KbList");
            countText = (TextBlock)root.FindName("CountText");
            statusText = (TextBlock)root.FindName("StatusText");
            clockText = (TextBlock)root.FindName("ClockText");
            selName = (TextBlock)root.FindName("SelName");
            selSource = (TextBlock)root.FindName("SelSource");
            emptyText = (TextBlock)root.FindName("EmptyText");
            menuTitle = (TextBlock)root.FindName("MenuTitle");
            toastText = (TextBlock)root.FindName("ToastText");
            kbTitle = (TextBlock)root.FindName("KbTitle");
            kbText = (TextBlock)root.FindName("KbText");
            tabGames = (TextBlock)root.FindName("TabGames");
            tabApps = (TextBlock)root.FindName("TabApps");
            sysText = (TextBlock)root.FindName("SysText");
            runText = (TextBlock)root.FindName("RunText");
            standbyTitle = (TextBlock)root.FindName("StandbyTitle");
            standbySub = (TextBlock)root.FindName("StandbySub");
            toast = (Border)root.FindName("Toast");

            gameList.ItemsSource = games;
            gameList.SelectionChanged += delegate { OnSelection(); };
            gameList.MouseDoubleClick += delegate { if (menus.Count == 0 && kb == null) Launch(Selected); };
            menuList.PreviewMouseLeftButtonUp += OnMenuClick;
            menuList.MouseMove += OnMenuHover;
            kbList.PreviewMouseLeftButtonUp += OnKbClick;

            // Mouse: i comandi a schermo sono pulsanti; clic destro = opzioni / indietro.
            Hook("BtnLaunch", delegate { Launch(Selected); });
            Hook("BtnOptions", delegate { GridCmd(Cmd.Options); });
            Hook("BtnMenu", delegate { GridCmd(Cmd.Menu); });
            Hook("BtnExit", ConfirmExit);
            Hook("BtnClose", delegate { Close(); });
            Hook("BtnMenuOk", delegate { if (menus.Count > 0) MenuCmd(Cmd.Accept); });
            Hook("BtnMenuBack", delegate { if (menus.Count > 0) MenuCmd(Cmd.Back); });
            Hook("TabGames", delegate { SwitchTab(0); });
            Hook("TabApps", delegate { SwitchTab(1); });
            Hook("StandbyOverlay", Recall);
            Hook("RunText", delegate { if (menus.Count == 0 && kb == null) RunningMenu(null); });
            overlay.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.OriginalSource == overlay && kb == null) { e.Handled = true; CloseMenus(); }   // clic fuori dal riquadro
            };
            MouseRightButtonUp += OnRightClick;
            PreviewMouseMove += OnMouseMoved;
            PreviewMouseDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ChangedButton == MouseButton.XButton1) { e.Handled = true; Handle(Cmd.Back); }
            };
            PreviewKeyDown += OnKey;
            PreviewTextInput += OnText;
            SizeChanged += delegate { ApplyScale(); };
            Activated += delegate { swallow = true; };
            Closed += delegate
            {
                // Le finestre di servizio della modalità desktop non devono restare aperte.
                try { if (leftDown) Input.MouseButton(true, false); if (rightDown) Input.MouseButton(false, false); } catch { }
                try { if (hud != null) hud.Close(); if (osk != null) osk.Close(); } catch { }
            };
            Loaded += delegate
            {
                ApplyScale();
                PaintTabs();
                Reload();
                timer = new DispatcherTimer(DispatcherPriority.Input);
                timer.Interval = TimeSpan.FromMilliseconds(16);
                timer.Tick += Tick;
                timer.Start();
                if (opt.SelfTest != null) RunSelfTest();
                if (opt.RecallTest != null) RunRecallTest();
                if (opt.SessionTest != null) RunSessionTest();
                StartDriverChecks();
                UpdateSys();
            };
        }

        Game Selected { get { return gameList.SelectedItem as Game; } }

        IntPtr Hwnd { get { return new WindowInteropHelper(this).Handle; } }

        void Save() { cfg.Save(configFile); }

        static bool Same(string a, string b)
        {
            return string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>L'interfaccia è disegnata per 1080 righe e viene scalata sull'altezza reale.</summary>
        void ApplyScale()
        {
            double h = ActualHeight;
            if (WindowStyle != WindowStyle.None) h = ActualHeight - SystemParameters.WindowCaptionHeight - 16;
            if (h <= 0) return;
            double s = h / 1080.0;
            root.LayoutTransform = new ScaleTransform(s, s);
            QueueScroll();
        }

        // ---------------------------------------------------------------- libreria

        async void Reload()
        {
            int gen = ++reloadGen;
            if (Selected != null) tabSel[tab] = Selected.Key;
            statusText.Text = "Scansione in corso";
            List<Game> list;
            try { list = await Task.Run(() => Library.Scan(cfg)); }
            catch { list = new List<Game>(); }
            if (gen != reloadGen) return;

            allItems = list;
            statusText.Text = "";
            ShowTab();
            LoadImages(list, true, false);
        }

        /// <summary>Riempie la griglia con la sezione corrente (giochi o applicazioni).</summary>
        void ShowTab()
        {
            bool apps = tab == 1;
            List<Game> list = allItems.Where(g => g.IsApp == apps).ToList();
            games.Clear();
            foreach (Game g in list) games.Add(g);

            if (apps) countText.Text = list.Count == 1 ? "1 applicazione" : list.Count + " applicazioni";
            else countText.Text = list.Count == 1 ? "1 gioco" : list.Count + " giochi";
            emptyText.Text = (apps ? "Nessuna applicazione" : "Nessun gioco") + "\nMenu > Aggiungi alla libreria";
            emptyText.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            int idx = 0;
            string keep = tabSel[tab];
            if (keep != null)
                for (int i = 0; i < list.Count; i++)
                    if (list[i].Key == keep) idx = i;
            if (list.Count > 0) gameList.SelectedIndex = idx;
            else { selName.Text = ""; selSource.Text = ""; }
            PaintTabs();
        }

        void SwitchTab(int n)
        {
            if (n == tab || menus.Count > 0 || kb != null) return;
            if (Selected != null) tabSel[tab] = Selected.Key;
            tab = n;
            ShowTab();
        }

        void PaintTabs()
        {
            tabGames.Opacity = tab == 0 ? 1 : 0.38;
            tabApps.Opacity = tab == 1 ? 1 : 0.38;
            tabGames.FontWeight = tab == 0 ? FontWeights.SemiBold : FontWeights.Light;
            tabApps.FontWeight = tab == 1 ? FontWeights.SemiBold : FontWeights.Light;
        }

        /// <summary>Carica copertine e icone in un thread a parte; scarica quelle mancanti.</summary>
        void LoadImages(List<Game> list, bool cancelPrevious, bool forceOnline)
        {
            int gen = cancelPrevious ? ++loadGen : loadGen;
            bool online = cfg.DownloadCovers || forceOnline;
            string sgdb = cfg.SteamGridDbKey;
            Thread t = new Thread(delegate()
            {
                busy = true;
                try
                {
                    List<Game> missing = new List<Game>();
                    foreach (Game g in list)
                    {
                        if (gen != loadGen) return;
                        string local = Covers.FindLocal(g, coversDir);
                        BitmapSource img = local != null ? Images.LoadFile(local) : null;
                        if (img != null) { SetImage(g, img); continue; }

                        string iconFrom = g.IconPath;
                        if (iconFrom == null && g.InstallDir != null) iconFrom = Library.MainExe(g.InstallDir);
                        if (iconFrom == null && g.Source.Length == 0) iconFrom = g.Exe;
                        BitmapSource ico = iconFrom != null ? Images.ShellIcon(iconFrom) : null;
                        if (ico != null) SetThumb(g, ico);
                        // Le applicazioni restano con la loro icona: una ricerca per nome darebbe copertine sbagliate.
                        if (!g.IsApp || forceOnline) missing.Add(g);
                    }
                    if (!online) return;

                    int done = 0;
                    foreach (Game g in missing)
                    {
                        if (gen != loadGen) return;
                        done++;
                        if (Covers.KnownMissing(g, coversDir)) continue;
                                SetStatus("Download copertine " + done + "/" + missing.Count, gen);
                        string p = Covers.Download(g, coversDir, sgdb);
                        if (p != null)
                        {
                            BitmapSource dl = Images.LoadFile(p);
                            if (dl != null) SetImage(g, dl);
                        }
                        Thread.Sleep(120);
                    }
                    SetStatus("", gen);
                }
                finally { busy = false; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
        }

        void SetImage(Game g, BitmapSource img)
        {
            bool wide = img.PixelWidth > img.PixelHeight * 1.15;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (wide) { g.Cover = null; g.Thumb = img; }
                else g.Cover = img;
            }));
        }

        void SetThumb(Game g, BitmapSource img)
        {
            Dispatcher.BeginInvoke(new Action(delegate { g.Thumb = img; }));
        }

        void SetStatus(string text, int gen)
        {
            Dispatcher.BeginInvoke(new Action(delegate { if (gen == loadGen) statusText.Text = text; }));
        }

        void RetryCovers()
        {
            try
            {
                foreach (string f in Directory.GetFiles(coversDir, "*.none")) File.Delete(f);
            }
            catch { }
            LoadImages(allItems.ToList(), true, true);
        }

        void Launch(Game g) { Launch(g, false); }

        /// <summary>Avvia un gioco e mette il launcher in standby finché il gioco resta aperto.</summary>
        void Launch(Game g, bool evenIfRunning)
        {
            if (g == null || opt.SelfTest != null) return;
            if (session != null && !evenIfRunning)
            {
                // C'è già un gioco aperto: non se ne fa partire un altro per sbaglio.
                if (session.Game.Key == g.Key) ReturnToGame();
                else RunningMenu(g);
                return;
            }
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(g.Exe);
                psi.UseShellExecute = true;
                string dir = g.WorkDir;
                if (string.IsNullOrEmpty(dir) && File.Exists(g.Exe)) dir = Path.GetDirectoryName(g.Exe);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) psi.WorkingDirectory = dir;
                ReleaseForce();     // il gioco deve potersi mettere davanti
                Process p = Process.Start(psi);
                swallow = true;
                g.LastPlayed = DateTime.UtcNow.Ticks;
                cfg.Get(g.Key).LastPlayed = g.LastPlayed;
                Save();
                StartSession(g, p);
            }
            catch (Exception ex)
            {
                Toast("Impossibile avviare: " + ex.Message);
            }
        }

        // ---------------------------------------------------------------- input

        void Tick(object sender, EventArgs e)
        {
            string clock = DateTime.Now.ToString("HH:mm");
            if (clock != lastClock) { lastClock = clock; clockText.Text = clock; }

            // Scorrimento morbido verso la riga selezionata.
            if (scroll != null && scrollTarget >= 0)
            {
                double off = scroll.VerticalOffset;
                double d = scrollTarget - off;
                if (Math.Abs(d) < 1) { scroll.ScrollToVerticalOffset(scrollTarget); scrollTarget = -1; }
                else scroll.ScrollToVerticalOffset(off + d * 0.3);
            }

            PadState st;
            Pad now = XInput.Read(out st);
            DateTime t = DateTime.UtcNow;
            if (t >= nextSys) { nextSys = t.AddSeconds(5); UpdateSys(); }

            // Richiamo del launcher: Guide, oppure Back+Start tenuti mezzo secondo. Vale in ogni stato.
            bool home = HomeHeld((st.Buttons & Pad.Guide) != 0,
                (st.Buttons & (Pad.Back | Pad.Start)) == (Pad.Back | Pad.Start), t);

            if (desktop)
            {
                if (home) ExitDesktop(); else DesktopTick(st, t);
                return;
            }

            // Voci di cui non si può seguire il processo (es. collegamenti .url): lo standby finisce
            // quando il launcher perde il primo piano e poi lo riprende, o dopo 20 secondi.
            if (session != null && !session.Trackable)
            {
                if (!IsActive) session.LostFocus = true;
                else if (session.LostFocus || session.Seconds > 20) EndSession();
            }

            // Gioco avviato: il controller è del gioco, anche se la sua finestra non è ancora comparsa.
            if (standby)
            {
                if (home) Recall();
                swallow = true;
                prev = Pad.None;
                return;
            }

            if (IsActive) ReleaseForce();       // il focus è tornato davvero: basta forzature
            if (!IsActive && !forceInput)
            {
                if (home) Recall();
                swallow = true;
                prev = Pad.None;
                return;
            }

            now &= ~(Pad.Guide | Pad.LS | Pad.RS);
            bool lt = st.LT > 100, rt = st.RT > 100;
            if (swallow)
            {
                if (now == Pad.None && !lt && !rt) swallow = false;
                prev = now;
                return;
            }

            Pad pressed = now & ~prev;
            prev = now;
            // Usando il controller il puntatore del mouse sparisce; riappare al primo movimento.
            if (pressed != Pad.None && opt.SelfTest == null && Mouse.OverrideCursor != Cursors.None)
                Mouse.OverrideCursor = Cursors.None;
            if ((pressed & Dirs) != 0)
            {
                nextRepeat = t.AddMilliseconds(380);
                FireDir(pressed & Dirs);
            }
            else if ((now & Dirs) != 0 && t >= nextRepeat)
            {
                nextRepeat = t.AddMilliseconds(110);
                FireDir(now & Dirs);
            }

            if ((pressed & Pad.A) != 0) Handle(Cmd.Accept);
            else if ((pressed & Pad.B) != 0) Handle(Cmd.Back);
            else if ((pressed & Pad.X) != 0) Handle(Cmd.Options);
            else if ((pressed & Pad.Y) != 0) Handle(Cmd.Menu);
            else if ((pressed & Pad.Start) != 0) Handle(Cmd.Start);
            else if ((pressed & Pad.LB) != 0) Handle(Cmd.TabPrev);
            else if ((pressed & Pad.RB) != 0) Handle(Cmd.TabNext);

            // Grilletti: volume, sempre disponibile nel launcher.
            if ((lt || rt) && kb == null && t >= volRepeat)
            {
                volRepeat = t.AddMilliseconds(150);
                StepVolume(rt ? 0.04f : -0.04f);
            }
        }

        bool HomeHeld(bool guide, bool combo, DateTime t)
        {
            if (!guide && !combo)
            {
                homeSince = DateTime.MinValue;
                homeFired = false;
                return false;
            }
            if (homeSince == DateTime.MinValue) homeSince = t;
            if (homeFired) return false;
            if (guide || (t - homeSince).TotalMilliseconds >= 500)
            {
                homeFired = true;
                return true;
            }
            return false;
        }

        void FireDir(Pad d)
        {
            if ((d & Pad.Up) != 0) Handle(Cmd.Up);
            else if ((d & Pad.Down) != 0) Handle(Cmd.Down);
            else if ((d & Pad.Left) != 0) Handle(Cmd.Left);
            else if ((d & Pad.Right) != 0) Handle(Cmd.Right);
        }

        /// <summary>
        /// Riporta il launcher in primo piano quando un altro programma ha preso il focus.
        /// Windows ostacola chi "ruba" il primo piano, quindi si prova in tre stadi:
        /// 1) richiesta diretta, 2) riduci a icona e ripristina, 3) finestra sempre in primo piano
        /// con il controller che la comanda anche senza focus (forceInput).
        /// </summary>
        void BringToFront()
        {
            if (opt.SelfTest != null) return;
            recallStage = 0;
            try
            {
                IntPtr h = Hwnd;
                if (IsIconic(h)) ShowWindow(h, 9);              // SW_RESTORE
                TryForeground(h);
                recallStage = 1;
                if (GetForegroundWindow() != h)
                {
                    ShowWindow(h, 6);                           // SW_MINIMIZE
                    ShowWindow(h, 9);
                    TryForeground(h);
                    recallStage = 2;
                }
                if (GetForegroundWindow() != h)
                {
                    Topmost = true;
                    forceInput = true;
                    recallStage = 3;
                }
                else Activate();
            }
            catch { }
            swallow = true;
        }

        /// <summary>Mette in primo piano una finestra qualsiasi (la nostra o quella di un gioco).</summary>
        void TryForeground(IntPtr h)
        {
            IntPtr fg = GetForegroundWindow();
            uint pid;
            uint other = fg != IntPtr.Zero ? GetWindowThreadProcessId(fg, out pid) : 0;
            uint me = GetCurrentThreadId();
            // Agganciarsi alla coda di input della finestra attiva e simulare un tocco di Alt
            // sono i due modi noti per farsi concedere il primo piano.
            bool attached = other != 0 && other != me && AttachThreadInput(me, other, true);
            keybd_event(0x12, 0, 0, UIntPtr.Zero);
            keybd_event(0x12, 0, 2, UIntPtr.Zero);
            BringWindowToTop(h);
            SetForegroundWindow(h);
            if (attached) AttachThreadInput(me, other, false);
        }

        /// <summary>Esce dalla modalità "sempre in primo piano" usata come ultima risorsa.</summary>
        void ReleaseForce()
        {
            if (!forceInput) return;
            forceInput = false;
            Topmost = false;
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (standby)
            {
                if (e.Key == Key.Escape) Recall();
                e.Handled = true;
                return;
            }
            if (kb != null)
            {
                // Tastiera fisica: lettere e spazio arrivano da OnText.
                switch (e.Key)
                {
                    case Key.Up: KbCmd(Cmd.Up); break;
                    case Key.Down: KbCmd(Cmd.Down); break;
                    case Key.Left: KbCmd(Cmd.Left); break;
                    case Key.Right: KbCmd(Cmd.Right); break;
                    case Key.Enter: KbDone(); break;
                    case Key.Escape: KbCmd(Cmd.Back); break;
                    case Key.Back: KbBackspace(); break;
                    case Key.V:
                        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
                        try { KbAppend(Clipboard.GetText()); } catch { }
                        break;
                    default: return;
                }
                e.Handled = true;
                return;
            }

            Cmd c;
            switch (e.Key)
            {
                case Key.Up: c = Cmd.Up; break;
                case Key.Down: c = Cmd.Down; break;
                case Key.Left: c = Cmd.Left; break;
                case Key.Right: c = Cmd.Right; break;
                case Key.Enter:
                case Key.Space: c = Cmd.Accept; break;
                case Key.Escape:
                case Key.Back: c = Cmd.Back; break;
                case Key.Tab:
                case Key.F1: c = Cmd.Menu; break;
                case Key.O:
                case Key.Apps: c = Cmd.Options; break;
                case Key.PageUp: c = Cmd.PageUp; break;
                case Key.PageDown: c = Cmd.PageDown; break;
                case Key.Q: c = Cmd.TabPrev; break;
                case Key.E: c = Cmd.TabNext; break;
                default: return;
            }
            e.Handled = true;
            Handle(c);
        }

        void OnText(object sender, TextCompositionEventArgs e)
        {
            if (kb == null) return;
            KbAppend(e.Text);
            e.Handled = true;
        }

        void Handle(Cmd c)
        {
            // LB / RB cambiano sezione sulla griglia; nei menu e nella tastiera fanno da pagina su / giù.
            if (kb != null || menus.Count > 0)
            {
                if (c == Cmd.TabPrev) c = Cmd.PageUp;
                else if (c == Cmd.TabNext) c = Cmd.PageDown;
            }
            if (kb != null) KbCmd(c);
            else if (menus.Count > 0) MenuCmd(c);
            else GridCmd(c);
        }

        void GridCmd(Cmd c)
        {
            switch (c)
            {
                case Cmd.Menu:
                case Cmd.Start: MainMenu(); return;
                case Cmd.Options: if (Selected != null) GameMenu(Selected); return;
                case Cmd.Accept: Launch(Selected); return;
                case Cmd.Back: ConfirmExit(); return;
                case Cmd.TabPrev:
                case Cmd.TabNext: SwitchTab(1 - tab); return;
            }

            int n = games.Count;
            if (n == 0) return;
            int i = gameList.SelectedIndex;
            if (i < 0) { gameList.SelectedIndex = 0; return; }

            int cols = Columns();
            int j = i;
            switch (c)
            {
                case Cmd.Left: j = i - 1; break;
                case Cmd.Right: j = i + 1; break;
                case Cmd.Up: j = i - cols; break;
                case Cmd.Down:
                    j = i + cols;
                    if (j >= n && i / cols < (n - 1) / cols) j = n - 1;
                    break;
                case Cmd.PageUp:
                    j = i - cols * 3;
                    if (j < 0) j = i % cols;
                    break;
                case Cmd.PageDown:
                    j = i + cols * 3;
                    if (j >= n) j = n - 1;
                    break;
            }
            if (j < 0 || j >= n) return;
            gameList.SelectedIndex = j;
        }

        // ---------------------------------------------------------------- griglia

        void OnSelection()
        {
            Game g = Selected;
            selName.Text = g != null ? g.Name : "";
            selSource.Text = g != null ? g.Source : "";
            QueueScroll();
        }

        void QueueScroll()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateScrollTarget));
        }

        FrameworkElement Container(int index)
        {
            return gameList.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement;
        }

        void UpdateScrollTarget()
        {
            if (scroll == null) scroll = FindChild<ScrollViewer>(gameList);
            FrameworkElement c = Container(gameList.SelectedIndex);
            if (scroll == null || c == null) return;
            try
            {
                Point p = c.TransformToAncestor(scroll).Transform(new Point(0, 0));
                double center = p.Y + scroll.VerticalOffset + c.ActualHeight / 2;
                double target = center - scroll.ViewportHeight / 2;
                scrollTarget = Math.Max(0, Math.Min(scroll.ScrollableHeight, target));
            }
            catch (InvalidOperationException) { }
        }

        /// <summary>Numero di tessere nella prima riga.</summary>
        int Columns()
        {
            FrameworkElement first = Container(0);
            if (first == null) return 1;
            try
            {
                double y0 = first.TransformToAncestor(gameList).Transform(new Point(0, 0)).Y;
                int cols = 1;
                for (int i = 1; i < games.Count; i++)
                {
                    FrameworkElement c = Container(i);
                    if (c == null) break;
                    double y = c.TransformToAncestor(gameList).Transform(new Point(0, 0)).Y;
                    if (Math.Abs(y - y0) > 1) break;
                    cols++;
                }
                return cols;
            }
            catch (InvalidOperationException) { return 1; }
        }

        static T FindChild<T>(DependencyObject d) where T : DependencyObject
        {
            int n = VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++)
            {
                DependencyObject c = VisualTreeHelper.GetChild(d, i);
                T t = c as T;
                if (t != null) return t;
                t = FindChild<T>(c);
                if (t != null) return t;
            }
            return null;
        }

        void Toast(string msg)
        {
            toastText.Text = msg;
            DoubleAnimationUsingKeyFrames a = new DoubleAnimationUsingKeyFrames();
            a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))));
            a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.8))));
            a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.4))));
            toast.BeginAnimation(OpacityProperty, a);
        }

        // ---------------------------------------------------------------- mouse

        void Hook(string name, Action run)
        {
            FrameworkElement f = (FrameworkElement)root.FindName(name);
            f.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                run();
            };
        }

        /// <summary>Distingue i movimenti veri da quelli simulati da WPF quando cambia il layout sotto il puntatore.</summary>
        void OnMouseMoved(object sender, MouseEventArgs e)
        {
            Point p = e.GetPosition(this);
            realMove = haveMouse && (Math.Abs(p.X - lastMouse.X) > 2 || Math.Abs(p.Y - lastMouse.Y) > 2);
            if (!haveMouse || realMove) lastMouse = p;
            haveMouse = true;
            if (realMove && Mouse.OverrideCursor != null) Mouse.OverrideCursor = null;      // il puntatore riappare
        }

        /// <summary>Passando sopra una voce di menu la si seleziona.</summary>
        void OnMenuHover(object sender, MouseEventArgs e)
        {
            if (!realMove || menus.Count == 0 || kb != null) return;
            ListBoxItem it = ItemsControl.ContainerFromElement(menuList, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (it == null) return;
            int idx = menuList.ItemContainerGenerator.IndexFromContainer(it);
            MenuPage p = menus.Peek();
            if (idx < 0 || idx == p.Index) return;
            p.Index = idx;
            menuList.SelectedIndex = idx;
        }

        /// <summary>Clic destro: su un gioco apre le opzioni, altrove il menu; dentro un menu torna indietro.</summary>
        void OnRightClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (standby) { Recall(); return; }
            if (kb != null) { KbCmd(Cmd.Back); return; }
            if (menus.Count > 0) { MenuCmd(Cmd.Back); return; }
            ListBoxItem it = ItemsControl.ContainerFromElement(gameList, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (it != null)
            {
                gameList.SelectedItem = it.DataContext;
                if (Selected != null) GameMenu(Selected);
            }
            else MainMenu();
        }

        // ---------------------------------------------------------------- menu: meccanica

        static MenuEntry E(string label, string hint, Action run)
        {
            MenuEntry m = new MenuEntry();
            m.Label = label;
            m.Hint = hint;
            m.Run = run;
            return m;
        }

        void Push(MenuPage p) { menus.Push(p); RenderMenu(); }
        void PopMenu() { if (menus.Count > 0) menus.Pop(); RenderMenu(); }
        void CloseMenus() { menus.Clear(); RenderMenu(); }

        void RenderMenu()
        {
            if (menus.Count == 0)
            {
                overlay.Visibility = Visibility.Collapsed;
                menuList.ItemsSource = null;
                return;
            }
            MenuPage p = menus.Peek();
            if (p.Index >= p.Items.Count) p.Index = p.Items.Count - 1;
            if (p.Index < 0) p.Index = 0;
            overlay.Visibility = Visibility.Visible;
            menuTitle.Text = p.Title;
            menuList.ItemsSource = p.Items;
            menuList.SelectedIndex = p.Index;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
            {
                if (menuList.SelectedItem != null) menuList.ScrollIntoView(menuList.SelectedItem);
            }));
        }

        void MenuCmd(Cmd c)
        {
            MenuPage p = menus.Peek();
            int n = p.Items.Count;
            switch (c)
            {
                case Cmd.Up: if (n > 0) { p.Index = (p.Index - 1 + n) % n; SyncMenu(p); } break;
                case Cmd.Down: if (n > 0) { p.Index = (p.Index + 1) % n; SyncMenu(p); } break;
                case Cmd.PageUp: if (n > 0) { p.Index = Math.Max(0, p.Index - 8); SyncMenu(p); } break;
                case Cmd.PageDown: if (n > 0) { p.Index = Math.Min(n - 1, p.Index + 8); SyncMenu(p); } break;
                case Cmd.Accept:
                    if (n > 0 && p.Items[p.Index].Run != null) p.Items[p.Index].Run();
                    break;
                case Cmd.Back:
                    if (p.OnBack != null) p.OnBack(); else PopMenu();
                    break;
                case Cmd.Menu:
                case Cmd.Start: CloseMenus(); break;
            }
        }

        void SyncMenu(MenuPage p)
        {
            menuList.SelectedIndex = p.Index;
            if (menuList.SelectedItem != null) menuList.ScrollIntoView(menuList.SelectedItem);
        }

        void OnMenuClick(object sender, MouseButtonEventArgs e)
        {
            if (menus.Count == 0 || kb != null) return;
            ListBoxItem it = ItemsControl.ContainerFromElement(menuList, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (it == null) return;
            int idx = menuList.ItemContainerGenerator.IndexFromContainer(it);
            if (idx < 0) return;
            menus.Peek().Index = idx;
            e.Handled = true;
            Dispatcher.BeginInvoke(new Action(delegate { if (menus.Count > 0) MenuCmd(Cmd.Accept); }));
        }

        /// <summary>Pagina di conferma: «Annulla» è la voce predefinita.</summary>
        void Confirm(string title, string yesLabel, string yesHint, Action yes)
        {
            MenuPage p = new MenuPage();
            p.Title = title;
            p.Items.Add(E("Annulla", "", PopMenu));
            p.Items.Add(E(yesLabel, yesHint, yes));
            Push(p);
        }

        // ---------------------------------------------------------------- tastiera a schermo (interna al launcher)

        void OpenKeyboard(string title, string initial, Action<string> done)
        {
            kb = new KbState();
            kb.Title = title;
            kb.Text.Append(initial ?? "");
            kb.Done = done;
            RenderKb(true);
        }

        void RenderKb(bool rebuild)
        {
            if (kb == null) { kbOverlay.Visibility = Visibility.Collapsed; return; }
            kbOverlay.Visibility = Visibility.Visible;
            kbTitle.Text = kb.Title;
            string t = kb.Text.ToString();
            kbText.Text = (t.Length > 58 ? "…" + t.Substring(t.Length - 58) : t) + "_";
            if (rebuild)
            {
                List<string> keys = new List<string>();
                foreach (char c in KbKeys) keys.Add((kb.Shift ? char.ToUpperInvariant(c) : c).ToString());
                keys.AddRange(KbSpecial);
                kbList.ItemsSource = keys;
            }
            kbList.SelectedIndex = kb.Index;
        }

        void KbAppend(string s)
        {
            if (kb == null || s == null) return;
            foreach (char c in s)
                if (!char.IsControl(c) && kb.Text.Length < 120) kb.Text.Append(c);
            RenderKb(false);
        }

        void KbBackspace()
        {
            if (kb.Text.Length > 0) kb.Text.Length--;
            RenderKb(false);
        }

        void KbDone()
        {
            KbState k = kb;
            kb = null;
            RenderKb(false);
            swallow = true;
            if (k.Done != null) k.Done(k.Text.ToString().Trim());
        }

        void KbPress(int i)
        {
            if (i < KbKeys.Length)
            {
                char c = KbKeys[i];
                kb.Text.Append(kb.Shift ? char.ToUpperInvariant(c) : c);
                bool was = kb.Shift;
                kb.Shift = false;       // maiuscola valida per una sola lettera
                RenderKb(was);
                return;
            }
            switch (i - KbKeys.Length)
            {
                case 0: kb.Shift = !kb.Shift; RenderKb(true); break;
                case 1: KbAppend(" "); break;
                case 2: KbBackspace(); break;
                case 3: kb = null; RenderKb(false); break;
                case 4: KbDone(); break;
            }
        }

        void KbCmd(Cmd c)
        {
            int row = kb.Index / 10, col = kb.Index % 10;   // righe 0-3 da 10 tasti, riga 4 da 5 tasti
            switch (c)
            {
                case Cmd.Left: col = row < 4 ? (col + 9) % 10 : (col + 4) % 5; break;
                case Cmd.Right: col = row < 4 ? (col + 1) % 10 : (col + 1) % 5; break;
                case Cmd.Up:
                    if (row == 0) { row = 4; col = col / 2; }
                    else if (row == 4) { row = 3; col = col * 2; }
                    else row--;
                    break;
                case Cmd.Down:
                    if (row == 3) { row = 4; col = col / 2; }
                    else if (row == 4) { row = 0; col = col * 2; }
                    else row++;
                    break;
                case Cmd.Accept: KbPress(kb.Index); return;
                case Cmd.Options: KbBackspace(); return;
                case Cmd.Menu: KbAppend(" "); return;
                case Cmd.Start: KbDone(); return;
                case Cmd.Back: kb = null; RenderKb(false); return;
                case Cmd.PageUp:
                case Cmd.PageDown: kb.Shift = !kb.Shift; RenderKb(true); return;
            }
            kb.Index = row * 10 + col;
            RenderKb(false);
        }

        void OnKbClick(object sender, MouseButtonEventArgs e)
        {
            if (kb == null) return;
            ListBoxItem it = ItemsControl.ContainerFromElement(kbList, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (it == null) return;
            int idx = kbList.ItemContainerGenerator.IndexFromContainer(it);
            if (idx < 0) return;
            kb.Index = idx;
            e.Handled = true;
            Dispatcher.BeginInvoke(new Action(delegate { if (kb != null) KbPress(idx); }));
        }
    }
}
