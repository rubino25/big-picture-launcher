using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace BigPicture
{
    class GpuInfo
    {
        public string Name;             // es. "NVIDIA GeForce RTX 3070"
        public string DeviceId;         // id PCI in esadecimale, es. "2484"
        public string Version;          // versione commerciale, es. "560.94"
    }

    class DriverRelease
    {
        public string Version;          // es. "621.24"
        public string Name;             // es. "GeForce Game Ready Driver"
        public DateTime Date;
        public string DetailsUrl;
        public string DownloadUrl;
    }

    /// <summary>Driver Nvidia: versione installata (registro) e ultima versione pubblicata (servizio Nvidia).</summary>
    static class Drivers
    {
        const string DisplayClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

        // Servizio interrogato da GeForce Experience / NVIDIA App: cerca il driver per id del dispositivo.
        // Non è documentato: se Nvidia lo cambia, il controllo smette di funzionare ma il resto del launcher no.
        const string Service = "https://gfwsl.geforce.com/nvidia_web_services/controller.gfeclientcontent.NG.php/com.nvidia.services.GFEClientContent_NG.getDispDrvrByDevid/";

        [StructLayout(LayoutKind.Sequential)]
        struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
            public int BatteryLifeTime, BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll")]
        static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);

        /// <summary>"32.0.15.6094" → "560.94" (le ultime cinque cifre delle due parti finali).</summary>
        public static string NvidiaVersion(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            string[] p = raw.Split('.');
            if (p.Length < 4) return null;
            string s = p[2] + p[3].PadLeft(4, '0');
            if (s.Length < 5) return null;
            s = s.Substring(s.Length - 5);
            return s.Substring(0, 3).TrimStart('0') + "." + s.Substring(3);
        }

        /// <summary>Prima scheda Nvidia installata, o null.</summary>
        public static GpuInfo Detect()
        {
            // Solo per i test su PC senza scheda Nvidia: "id|nome|versione grezza".
            string fake = Environment.GetEnvironmentVariable("BIGPICTURE_GPU");
            if (!string.IsNullOrEmpty(fake))
            {
                string[] f = fake.Split('|');
                if (f.Length == 3)
                {
                    GpuInfo t = new GpuInfo();
                    t.DeviceId = f[0];
                    t.Name = f[1];
                    t.Version = NvidiaVersion(f[2]);
                    return t;
                }
            }

            try
            {
                using (RegistryKey cls = Registry.LocalMachine.OpenSubKey(DisplayClass))
                {
                    if (cls == null) return null;
                    foreach (string sub in cls.GetSubKeyNames())
                    {
                        if (sub.Length != 4) continue;              // 0000, 0001… ("Properties" non è accessibile)
                        try
                        {
                            using (RegistryKey k = cls.OpenSubKey(sub))
                            {
                                if (k == null) continue;
                                string match = Convert.ToString(k.GetValue("MatchingDeviceId")).ToLowerInvariant();
                                if (match.IndexOf("ven_10de", StringComparison.Ordinal) < 0) continue;
                                int i = match.IndexOf("dev_", StringComparison.Ordinal);
                                if (i < 0 || match.Length < i + 8) continue;
                                GpuInfo g = new GpuInfo();
                                g.DeviceId = match.Substring(i + 4, 4).ToUpperInvariant();
                                g.Name = Convert.ToString(k.GetValue("DriverDesc"));
                                g.Version = NvidiaVersion(Convert.ToString(k.GetValue("DriverVersion")));
                                return g;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return null;
        }

        static string WindowsBuild()
        {
            try
            {
                object v = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber", null);
                if (v != null) return v.ToString();
            }
            catch { }
            return "19045";
        }

        static bool IsLaptop()
        {
            SYSTEM_POWER_STATUS s;
            return GetSystemPowerStatus(out s) && s.BatteryFlag != 128 && s.BatteryFlag != 255;   // 128 = nessuna batteria
        }

        /// <summary>Ultimo driver Game Ready per la scheda. Lancia eccezione se il servizio non risponde.</summary>
        public static DriverRelease Latest(GpuInfo g)
        {
            string query = "{\"dIDa\":[\"" + g.DeviceId + "_10DE\"],\"osC\":\"10.0\",\"osB\":\"" + WindowsBuild() +
                "\",\"is6\":\"1\",\"lg\":\"1033\",\"iLp\":\"" + (IsLaptop() ? "1" : "0") +
                "\",\"prvMd\":\"0\",\"gcV\":\"3.28.0.412\",\"gIsB\":\"0\",\"dch\":\"1\",\"upCRD\":\"0\",\"isCRD\":\"0\"}";
            string json;
            using (WebClient wc = new WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0";
                json = wc.DownloadString(Service + Uri.EscapeDataString(query));
            }

            Dictionary<string, object> root = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }
                .Deserialize<Dictionary<string, object>>(json);
            object o;
            if (root == null || !root.TryGetValue("DriverAttributes", out o)) return null;
            Dictionary<string, object> d = o as Dictionary<string, object>;
            if (d == null) return null;

            DriverRelease r = new DriverRelease();
            r.Version = Str(d, "Version");
            r.Name = Str(d, "Name");
            r.DetailsUrl = Str(d, "DetailsURL");
            r.DownloadUrl = Str(d, "DownloadURLAdmin");
            DateTime dt;
            if (DateTime.TryParse(Str(d, "ReleaseDateTime"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out dt)) r.Date = dt;
            return string.IsNullOrEmpty(r.Version) ? null : r;
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) && v != null ? v.ToString() : "";
        }

        /// <summary>true se a è più recente di b ("621.24" &gt; "560.94").</summary>
        public static bool Newer(string a, string b)
        {
            decimal x, y;
            if (!decimal.TryParse(a, NumberStyles.Number, CultureInfo.InvariantCulture, out x)) return false;
            if (!decimal.TryParse(b, NumberStyles.Number, CultureInfo.InvariantCulture, out y)) return true;
            return x > y;
        }

        /// <summary>Collegamento a NVIDIA App (o GeForce Experience) nel menu Start, o null.</summary>
        public static string NvidiaApp()
        {
            string[] names = { "NVIDIA app", "GeForce Experience" };
            foreach (Environment.SpecialFolder sf in new[] { Environment.SpecialFolder.CommonStartMenu, Environment.SpecialFolder.StartMenu })
            {
                string dir = Path.Combine(Environment.GetFolderPath(sf), "Programs");
                foreach (string n in names)
                {
                    string hit = FindLink(dir, n, 0);
                    if (hit != null) return hit;
                }
            }
            return null;
        }

        static string FindLink(string dir, string name, int depth)
        {
            foreach (string f in Library.SafeFiles(dir))
                if (f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(Path.GetFileNameWithoutExtension(f), name, StringComparison.OrdinalIgnoreCase))
                    return f;
            if (depth >= 2) return null;
            foreach (string d in Library.SafeDirs(dir))
            {
                string hit = FindLink(d, name, depth + 1);
                if (hit != null) return hit;
            }
            return null;
        }
    }
}
