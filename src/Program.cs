using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading;
using System.Windows;

[assembly: AssemblyTitle("Big Picture Launcher")]
[assembly: AssemblyProduct("Big Picture Launcher")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace BigPicture
{
    class Options
    {
        public bool Windowed;
        public string DataDir;
        public string SelfTest;                 // percorso PNG: disegna la finestra, salva e chiude
        public string SelfTestView = "";        // "", "menu", "game", "browse"
        public string RecallTest;               // percorso file: prova reale del ritorno in primo piano
        public string SessionTest;              // percorso file: prova reale di standby e chiusura del gioco
        public List<string> Folders = new List<string>();
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Options opt = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--windowed") opt.Windowed = true;
                else if (a == "--data" && i + 1 < args.Length) opt.DataDir = args[++i];
                else if (a == "--selftest" && i + 1 < args.Length) opt.SelfTest = args[++i];
                else if (a == "--view" && i + 1 < args.Length) opt.SelfTestView = args[++i];
                else if (a == "--recalltest" && i + 1 < args.Length) { opt.RecallTest = args[++i]; opt.Windowed = true; }
                else if (a == "--sessiontest" && i + 1 < args.Length) { opt.SessionTest = args[++i]; opt.Windowed = true; }
                else if (Directory.Exists(a)) opt.Folders.Add(Path.GetFullPath(a));
            }
            if (opt.DataDir == null)
                opt.DataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");

            bool created = true;
            Mutex mutex = null;
            if (opt.SelfTest == null && opt.RecallTest == null && opt.SessionTest == null)
            {
                mutex = new Mutex(true, "BigPictureLauncher.SingleInstance", out created);
                if (!created) return;   // già in esecuzione
            }

            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }

            Application app = new Application();
            // Chiudendo il launcher si chiudono anche le finestre di servizio (indicatore e tastiera della modalità desktop).
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            string log = Path.Combine(opt.DataDir, "error.log");
            app.DispatcherUnhandledException += delegate(object s, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
            {
                try
                {
                    Directory.CreateDirectory(opt.DataDir);
                    File.AppendAllText(log, DateTime.Now + "\r\n" + e.Exception + "\r\n\r\n");
                }
                catch { }
                e.Handled = true;
            };
            app.Run(new MainWindow(opt));
            GC.KeepAlive(mutex);
        }
    }
}
