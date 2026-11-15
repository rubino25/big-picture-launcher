using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace BigPicture
{
    /// <summary>Prove automatiche: catture della finestra (--selftest), richiamo (--recalltest), gioco avviato (--sessiontest).</summary>
    partial class MainWindow
    {
        string kbTestResult;

        static void Click(UIElement target, MouseButton button)
        {
            if (target == null) return;
            MouseButtonEventArgs e = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, button);
            // MouseUp risale l'albero e WPF lo traduce in MouseLeft/RightButtonUp su ogni elemento, come un clic vero.
            e.RoutedEvent = Mouse.MouseUpEvent;
            target.RaiseEvent(e);
        }

        static void SavePng(Visual v, int w, int h, string path)
        {
            RenderTargetBitmap rtb = new RenderTargetBitmap(Math.Max(1, w), Math.Max(1, h), 96, 96, PixelFormats.Pbgra32);
            rtb.Render(v);
            PngBitmapEncoder enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (FileStream fs = File.Create(path)) enc.Save(fs);
        }

        /// <summary>Disegna il contenuto di una finestra di servizio senza mostrarla sullo schermo.</summary>
        static void RenderOffscreen(Window w, string path)
        {
            FrameworkElement c = (FrameworkElement)w.Content;
            w.Content = null;
            Border bg = new Border();
            bg.Background = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30));
            bg.Padding = new Thickness(20);
            bg.Child = c;
            bg.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            bg.Arrange(new Rect(bg.DesiredSize));
            bg.UpdateLayout();
            SavePng(bg, (int)Math.Ceiling(bg.ActualWidth), (int)Math.Ceiling(bg.ActualHeight), path);
        }

        void RunSelfTest()
        {
            DateTime start = DateTime.UtcNow;
            int step = 0;
            string note = "";
            DispatcherTimer t = new DispatcherTimer();
            t.Interval = TimeSpan.FromMilliseconds(500);
            t.Tick += delegate
            {
                double sec = (DateTime.UtcNow - start).TotalSeconds;
                if (sec < 3 || (busy && sec < 60)) return;
                step++;
                string v = opt.SelfTestView;
                if (step == 1)
                {
                    try { SelfTestView(v, ref note); }
                    catch (Exception ex) { note += " ERRORE=" + ex.GetType().Name + ":" + ex.Message; }
                    return;
                }
                if (step < 5) return;       // lascia il tempo a ricerche e ricaricamenti
                if (v == "search" && menus.Count > 0) note += " risultati=" + menus.Peek().Items.Count;
                if (menus.Count > 0) note += " vociMenu=" + menus.Peek().Items.Count;
                t.Stop();
                try
                {
                    SavePng((Visual)VisualTreeHelper.GetChild(this, 0), (int)ActualWidth, (int)ActualHeight, opt.SelfTest);
                    if (v == "kb" && kb != null) { KbCmd(Cmd.Start); note += " testo=" + kbTestResult; }   // conferma la digitazione
                    File.WriteAllText(opt.SelfTest + ".txt",
                        "giochi=" + games.Count + " colonne=" + Columns() + " selezione=" + gameList.SelectedIndex +
                        " copertine=" + games.Count(g => g.Cover != null) + " pad=" + XInput.AnyConnected +
                        " sezione=" + tab + " standby=" + standby + " sessione=" + (session != null) +
                        " store=" + string.Join(",", games.Where(g => g.Source.Length > 0).Select(g => g.Source + ":" + g.Name).ToArray()) +
                        " stato=[" + sysText.Text + "]" + note);
                }
                catch (Exception ex)
                {
                    try { File.WriteAllText(opt.SelfTest + ".err", ex.ToString()); } catch { }
                }
                if (session != null) { session = null; sessionTimer.Stop(); }
                Close();
            };
            t.Start();
        }

        void SelfTestView(string v, ref string note)
        {
            switch (v)
            {
                case "menu": MainMenu(); break;
                case "add": MainMenu(); AddMenu(); break;
                case "startapps": StartMenuApps(); break;
                case "settings": MainMenu(); SettingsMenu(0, false); break;
                case "power": MainMenu(); PowerMenu(); break;
                case "game": if (Selected != null) GameMenu(Selected); break;
                case "browse": Browse(null, 0, false); break;
                case "end": for (int i = 0; i < 6; i++) GridCmd(Cmd.Down); GridCmd(Cmd.Right); break;
                case "exit":
                    GridCmd(Cmd.Back);
                    note += " conferma=" + (menus.Count > 0 ? menus.Peek().Title + "/" + menus.Peek().Items[menus.Peek().Index].Label : "-");
                    break;
                case "windows": WindowsMenu(); break;
                case "audio": AudioMenu(0, false); break;
                case "display": DisplayMenu(0, false); break;
                case "resolution": ResolutionMenu(); break;
                case "drivers":
                    // Controllo reale verso Nvidia (con BIGPICTURE_GPU si simula una scheda su PC che non ce l'hanno).
                    gpu = Drivers.Detect();
                    DriverMenu(0, false);
                    CheckDrivers(false);
                    break;
                case "apps":
                {
                    string np = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
                    if (File.Exists(np) && !cfg.Apps.Contains(np)) cfg.Apps.Add(np);
                    GridCmd(Cmd.TabNext);
                    Reload();
                    break;
                }
                case "standby":
                    if (Selected != null) StartSession(Selected, null);
                    break;
                case "running":
                    if (Selected != null)
                    {
                        StartSession(Selected, null);
                        standby = false;
                        ShowStandby();
                        RunningMenu(games.Count > 1 ? games[1] : null);
                    }
                    break;
                case "desktop":
                {
                    DesktopHud h = new DesktopHud();
                    RenderOffscreen(h, opt.SelfTest + "_hud.png");
                    h.Close();
                    OskWindow k = new OskWindow();
                    k.Move(Cmd.Down); k.Move(Cmd.Right);
                    RenderOffscreen(k, opt.SelfTest + "_osk.png");
                    k.Close();
                    note += " hud+osk=ok";
                    break;
                }
                case "kb":
                    // Simula la digitazione dal controller: Maiusc, "h", poi "i", spazio e una cancellazione.
                    OpenKeyboard("Nuovo nome", "", delegate(string s) { kbTestResult = s; });
                    KbCmd(Cmd.PageUp);
                    for (int i = 0; i < 5; i++) KbCmd(Cmd.Right);
                    KbCmd(Cmd.Down); KbCmd(Cmd.Accept);
                    KbCmd(Cmd.Up); KbCmd(Cmd.Right); KbCmd(Cmd.Right); KbCmd(Cmd.Accept);
                    KbCmd(Cmd.Menu); KbCmd(Cmd.Accept); KbCmd(Cmd.Options);
                    break;
                case "search": if (Selected != null) SearchCover(Selected, "portal"); break;
                case "rename":
                    if (Selected != null) { cfg.Get(Selected.Key).Name = "Nome Di Prova"; Reload(); }
                    break;
                case "mouse":
                    Click((UIElement)root.FindName("BtnMenu"), MouseButton.Left);
                    note += " menu=" + (menus.Count > 0 ? menus.Peek().Title : "-");
                    Click(overlay, MouseButton.Right);
                    note += " dopoDestro=" + menus.Count;
                    if (games.Count > 1) Click(Container(1), MouseButton.Right);
                    note += " opzioni=" + (menus.Count > 0 ? menus.Peek().Title : "-") + " sel=" + gameList.SelectedIndex;
                    Click(overlay, MouseButton.Left);
                    note += " dopoFuori=" + menus.Count;
                    Click((UIElement)root.FindName("BtnOptions"), MouseButton.Left);
                    Click((UIElement)root.FindName("BtnMenuBack"), MouseButton.Left);
                    note += " dopoIndietro=" + menus.Count;
                    break;
            }
        }

        /// <summary>
        /// Prova reale del richiamo: apre una finestra che ruba il focus, richiama il launcher
        /// e scrive su file se è tornato in primo piano e con quale stadio.
        /// </summary>
        void RunRecallTest()
        {
            DateTime start = DateTime.UtcNow;
            int step = 0;
            bool before = false;
            Process thief = null;
            DispatcherTimer t = new DispatcherTimer();
            t.Interval = TimeSpan.FromMilliseconds(250);
            t.Tick += delegate
            {
                double sec = (DateTime.UtcNow - start).TotalSeconds;
                if (step == 0 && sec >= 1.5)
                {
                    step = 1;
                    try { thief = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 8 127.0.0.1 >nul")); } catch { }
                }
                else if (step == 1 && sec >= 4)
                {
                    step = 2;
                    before = IsActive;
                    BringToFront();
                }
                else if (step == 2 && sec >= 5.5)
                {
                    t.Stop();
                    try
                    {
                        File.WriteAllText(opt.RecallTest,
                            "attivoPrima=" + before + " stadio=" + recallStage + " attivoDopo=" + IsActive +
                            " primoPiano=" + (GetForegroundWindow() == Hwnd) + " forzato=" + forceInput);
                    }
                    catch { }
                    try { if (thief != null && !thief.HasExited) thief.Kill(); } catch { }
                    Close();
                }
            };
            t.Start();
        }

        /// <summary>
        /// Prova reale del gioco avviato, con un finto gioco (una copia di cmd.exe nella cartella "Hades"):
        /// 1) parte, il launcher va in standby; il "gioco" si chiude da solo dopo ~14 s → fine sessione e launcher davanti;
        /// 2) riparte e viene terminato a forza.
        /// </summary>
        void RunSessionTest()
        {
            DateTime start = DateTime.UtcNow;
            int step = 0;
            string log = "";
            Process p1 = null, p2 = null;
            DateTime phase = DateTime.MinValue;
            DispatcherTimer t = new DispatcherTimer();
            t.Interval = TimeSpan.FromMilliseconds(250);
            t.Tick += delegate
            {
                double sec = (DateTime.UtcNow - start).TotalSeconds;
                double ph = (DateTime.UtcNow - phase).TotalSeconds;
                try
                {
                    if (step == 0 && sec >= 2.5)
                    {
                        Game g = allItems.FirstOrDefault(x => x.Name == "Hades" && File.Exists(x.Exe));
                        if (g == null) { log += " giocoDiProva=assente"; step = 99; return; }
                        p1 = Process.Start(new ProcessStartInfo(g.Exe, "/c ping -n 15 127.0.0.1 >nul") { WorkingDirectory = Path.GetDirectoryName(g.Exe) });
                        StartSession(g, p1);
                        phase = DateTime.UtcNow;
                        step = 1;
                    }
                    else if (step == 1 && ph >= 3)
                    {
                        log += " [fase1 t=3s] standby=" + standby + " vivo=" + (session != null && session.Alive) +
                               " processi=" + (session != null ? session.Pids.Count : -1);
                        // Prova anche che il controller resti del gioco: un comando alla griglia non deve avviare nulla.
                        step = 2;
                    }
                    else if (step == 2 && session == null)
                    {
                        log += " [fine] dopo=" + ph.ToString("0.0") + "s attivo=" + IsActive + " primoPiano=" + (GetForegroundWindow() == Hwnd) +
                               " standby=" + standby;
                        step = 3;
                        phase = DateTime.UtcNow;
                    }
                    else if (step == 2 && ph > 40) { log += " [fine] NON rilevata"; step = 3; phase = DateTime.UtcNow; }
                    else if (step == 3 && ph >= 1.5)
                    {
                        Game g = allItems.First(x => x.Name == "Hades");
                        p2 = Process.Start(new ProcessStartInfo(g.Exe, "/c ping -n 60 127.0.0.1 >nul") { WorkingDirectory = Path.GetDirectoryName(g.Exe) });
                        StartSession(g, p2);
                        phase = DateTime.UtcNow;
                        step = 4;
                    }
                    else if (step == 4 && ph >= 3)
                    {
                        // Stessa azione di «Termina forzatamente», senza la pagina di conferma.
                        int failed = session != null ? session.Kill() : -1;
                        if (failed == 0) EndSession(false);
                        log += " [termina] falliti=" + failed;
                        phase = DateTime.UtcNow;
                        step = 5;
                    }
                    else if (step == 5 && ph >= 1.5)
                    {
                        bool exited = false;
                        try { exited = p2.HasExited; } catch { }
                        log += " processoTerminato=" + exited + " sessione=" + (session != null) + " standby=" + standby;
                        step = 99;
                    }
                }
                catch (Exception ex) { log += " ERRORE=" + ex.Message; step = 99; }

                if (step == 99)
                {
                    t.Stop();
                    try { File.WriteAllText(opt.SessionTest, log.Trim()); } catch { }
                    try { if (p2 != null && !p2.HasExited) p2.Kill(); } catch { }
                    Close();
                }
            };
            t.Start();
        }
    }
}
