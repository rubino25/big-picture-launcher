using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BigPicture
{
    /// <summary>Un risultato di ricerca copertina.</summary>
    public class CoverHit
    {
        public string Name;
        public string Source;       // "Steam" oppure "SteamGridDB"
        public int Id;
        public string Tiny;         // miniatura (solo Steam)
    }

    /// <summary>Copertine: file locali, cache, store di Steam (senza chiave) e SteamGridDB (con chiave).</summary>
    static class Covers
    {
        const string SgdbApi = "https://www.steamgriddb.com/api/v2/";
        static readonly string[] LocalNames = { "cover", "poster", "folder", "boxart", "box" };
        static readonly string[] ImageExt = { ".jpg", ".jpeg", ".png", ".bmp" };

        public static string Safe(string name)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in name)
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            return sb.ToString().Trim();
        }

        public static string CachePath(Game g, string coversDir)
        {
            return Path.Combine(coversDir, Safe(g.Name) + ".jpg");
        }

        /// <summary>Copertina già presente sul disco, altrimenti null.</summary>
        public static string FindLocal(Game g, string coversDir)
        {
            try
            {
                // 1) Immagine accanto al gioco (scelta dall'utente).
                if (g.IsFile)
                {
                    string b = Path.Combine(Path.GetDirectoryName(g.Key), Path.GetFileNameWithoutExtension(g.Key));
                    foreach (string ext in ImageExt)
                        if (File.Exists(b + ext)) return b + ext;
                }
                else if (g.LocalDir != null)
                {
                    foreach (string n in LocalNames)
                        foreach (string ext in ImageExt)
                        {
                            string p = Path.Combine(g.LocalDir, n + ext);
                            if (File.Exists(p)) return p;
                        }
                }
                // 2) Cache: data\covers\<Nome gioco>.png/.jpg (scaricata o messa a mano).
                string safe = Safe(g.Name);
                foreach (string ext in new[] { ".png", ".jpg", ".jpeg", ".bmp" })
                {
                    string c = Path.Combine(coversDir, safe + ext);
                    if (File.Exists(c)) return c;
                }
                // 3) Copertina già scaricata dal client Steam.
                if (g.SteamAppId > 0) return SteamCache(g.SteamAppId);
            }
            catch { }
            return null;
        }

        static string SteamCache(int id)
        {
            string steam = Stores.SteamPath();
            if (steam == null) return null;
            string cache = Path.Combine(steam, @"appcache\librarycache");
            string flat = Path.Combine(cache, id + "_library_600x900.jpg");
            if (File.Exists(flat)) return flat;
            string dir = Path.Combine(cache, id.ToString());
            if (Directory.Exists(dir))
                foreach (string f in Directory.GetFiles(dir, "library_600x900*.jpg", SearchOption.AllDirectories))
                    return f;
            return null;
        }

        public static bool KnownMissing(Game g, string coversDir)
        {
            return File.Exists(Path.Combine(coversDir, Safe(g.Name) + ".none"));
        }

        public static void Forget(Game g, string coversDir)
        {
            string safe = Safe(g.Name);
            try { File.Delete(Path.Combine(coversDir, safe + ".none")); } catch { }
            try { File.Delete(Path.Combine(coversDir, safe + ".jpg")); } catch { }
        }

        static WebClient Client(string bearer)
        {
            WebClient wc = new WebClient();
            wc.Encoding = Encoding.UTF8;
            wc.Headers[HttpRequestHeader.UserAgent] = "GameLauncher/1.0";
            if (!string.IsNullOrEmpty(bearer)) wc.Headers[HttpRequestHeader.Authorization] = "Bearer " + bearer;
            return wc;
        }

        static Dictionary<string, object> Json(string url, string bearer)
        {
            using (WebClient wc = Client(bearer))
                return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(wc.DownloadString(url));
        }

        static IEnumerable List(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v is string) return new object[0];
            IEnumerable e = v as IEnumerable;
            return e != null ? e : new object[0];
        }

        /// <summary>Cerca su Steam per nome. Lancia eccezione se la rete non risponde.</summary>
        public static List<CoverHit> SearchSteam(string term)
        {
            List<CoverHit> hits = new List<CoverHit>();
            Dictionary<string, object> root = Json(
                "https://store.steampowered.com/api/storesearch/?l=english&cc=US&term=" + Uri.EscapeDataString(term), null);
            foreach (object o in List(root, "items"))
            {
                Dictionary<string, object> it = o as Dictionary<string, object>;
                if (it == null || !it.ContainsKey("id") || !it.ContainsKey("name")) continue;
                CoverHit h = new CoverHit();
                h.Source = "Steam";
                h.Id = Convert.ToInt32(it["id"]);
                h.Name = Convert.ToString(it["name"]);
                h.Tiny = it.ContainsKey("tiny_image") ? Convert.ToString(it["tiny_image"]) : null;
                hits.Add(h);
            }
            return hits;
        }

        /// <summary>Cerca su SteamGridDB (serve la chiave API personale).</summary>
        public static List<CoverHit> SearchSgdb(string term, string key)
        {
            List<CoverHit> hits = new List<CoverHit>();
            Dictionary<string, object> root = Json(SgdbApi + "search/autocomplete/" + Uri.EscapeDataString(term), key);
            foreach (object o in List(root, "data"))
            {
                Dictionary<string, object> it = o as Dictionary<string, object>;
                if (it == null || !it.ContainsKey("id") || !it.ContainsKey("name")) continue;
                CoverHit h = new CoverHit();
                h.Source = "SteamGridDB";
                h.Id = Convert.ToInt32(it["id"]);
                h.Name = Convert.ToString(it["name"]);
                hits.Add(h);
            }
            return hits;
        }

        /// <summary>Ricerca manuale: tutti i risultati di tutte le fonti disponibili.</summary>
        public static List<CoverHit> SearchAll(string term, string sgdbKey)
        {
            List<CoverHit> hits = new List<CoverHit>();
            try { hits.AddRange(SearchSteam(term)); } catch { }
            if (!string.IsNullOrEmpty(sgdbKey))
                try { hits.AddRange(SearchSgdb(term, sgdbKey)); } catch { }
            return hits;
        }

        /// <summary>Il risultato il cui nome combacia meglio; null se nessuno combacia (meglio niente che sbagliato).</summary>
        static CoverHit Best(List<CoverHit> hits, string query)
        {
            CoverHit best = null;
            int bestRank = 99;
            foreach (CoverHit h in hits)
            {
                string n = Library.Norm(h.Name);
                int rank;
                if (n == query) rank = 0;
                else if (query.Length >= 4 && n.StartsWith(query)) rank = 1;
                else if (n.Length >= 4 && query.StartsWith(n)) rank = 2;
                else if (query.Length >= 5 && n.Contains(query)) rank = 3;
                else continue;
                if (rank < bestRank) { bestRank = rank; best = h; }
            }
            return best;
        }

        /// <summary>Ricerca automatica e download. Ritorna il percorso o null.</summary>
        public static string Download(Game g, string coversDir, string sgdbKey)
        {
            string none = Path.Combine(coversDir, Safe(g.Name) + ".none");
            if (File.Exists(none)) return null;
            string query = Library.Norm(g.Name);
            string dest = CachePath(g, coversDir);

            try
            {
                Directory.CreateDirectory(coversDir);

                CoverHit hit = null;
                if (g.SteamAppId > 0)
                {
                    hit = new CoverHit();
                    hit.Source = "Steam";
                    hit.Id = g.SteamAppId;
                }
                else if (query.Length >= 2) hit = Best(SearchSteam(g.Name), query);
                if (hit != null && Fetch(hit, dest, sgdbKey)) return dest;

                if (!string.IsNullOrEmpty(sgdbKey) && query.Length >= 2)
                {
                    try
                    {
                        hit = Best(SearchSgdb(g.Name, sgdbKey), query);
                        if (hit != null && Fetch(hit, dest, sgdbKey)) return dest;
                    }
                    catch { }
                }
                File.WriteAllText(none, "");
            }
            catch { }   // offline o errore temporaneo: si riprova al prossimo avvio
            return null;
        }

        /// <summary>Scarica la copertina di un risultato preciso.</summary>
        public static bool Fetch(CoverHit hit, string dest, string sgdbKey)
        {
            try
            {
                if (hit.Source == "Steam")
                {
                    string b = "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/" + hit.Id + "/";
                    if (TryGet(b + "library_600x900.jpg", dest)) return true;
                    if (TryGet(b + "header.jpg", dest)) return true;
                    string header = SteamHeader(hit.Id);
                    if (header != null && TryGet(header, dest)) return true;
                    return !string.IsNullOrEmpty(hit.Tiny) && TryGet(hit.Tiny, dest);
                }

                Dictionary<string, object> root = Json(
                    SgdbApi + "grids/game/" + hit.Id + "?dimensions=600x900,342x482,660x930&types=static&mimes=image/png,image/jpeg",
                    sgdbKey);
                foreach (object o in List(root, "data"))
                {
                    Dictionary<string, object> it = o as Dictionary<string, object>;
                    if (it == null || !it.ContainsKey("url")) continue;
                    if (TryGet(Convert.ToString(it["url"]), dest)) return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>Indirizzo dell'immagine di testata dai dettagli dello store (per i giochi recenti).</summary>
        static string SteamHeader(int id)
        {
            try
            {
                Dictionary<string, object> root = Json(
                    "https://store.steampowered.com/api/appdetails?filters=basic&appids=" + id, null);
                object o;
                if (root == null || !root.TryGetValue(id.ToString(), out o)) return null;
                Dictionary<string, object> app = o as Dictionary<string, object>;
                if (app == null || !app.TryGetValue("data", out o)) return null;
                Dictionary<string, object> data = o as Dictionary<string, object>;
                if (data == null || !data.TryGetValue("header_image", out o)) return null;
                return Convert.ToString(o);
            }
            catch { return null; }
        }

        static bool TryGet(string url, string dest)
        {
            string tmp = dest + ".tmp";
            try
            {
                using (WebClient wc = Client(null)) wc.DownloadFile(url, tmp);
                if (new FileInfo(tmp).Length < 1000) { File.Delete(tmp); return false; }
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(tmp, dest);
                return true;
            }
            catch
            {
                try { File.Delete(tmp); } catch { }
                return false;
            }
        }
    }

    static class Images
    {
        public static BitmapSource LoadFile(string path)
        {
            try
            {
                BitmapImage bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.IgnoreImageCache;
                bi.DecodePixelWidth = 480;
                bi.UriSource = new Uri(path);
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { return null; }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SIZE
        {
            public int cx, cy;
        }

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(SIZE size, int flags, out IntPtr phbm);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(
            string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr h);

        /// <summary>Icona grande (256px) del file, con trasparenza.</summary>
        public static BitmapSource ShellIcon(string path)
        {
            IntPtr hbm = IntPtr.Zero;
            try
            {
                IShellItemImageFactory f;
                SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out f);
                SIZE sz = new SIZE();
                sz.cx = 256;
                sz.cy = 256;
                int hr = f.GetImage(sz, 0x4 /* ICONONLY */, out hbm);
                Marshal.ReleaseComObject(f);
                if (hr != 0 || hbm == IntPtr.Zero) return null;

                using (Bitmap bmp = System.Drawing.Image.FromHbitmap(hbm))
                {
                    int w = bmp.Width, h = bmp.Height;
                    BitmapData d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, bmp.PixelFormat);
                    try
                    {
                        // Lo stride può essere negativo (bitmap dal basso verso l'alto): copia riga per riga.
                        int stride = Math.Abs(d.Stride);
                        int len = stride * h;
                        byte[] px = new byte[len];
                        for (int y = 0; y < h; y++)
                            Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), px, y * stride, stride);
                        bool alpha = false;
                        for (int i = 3; i < len; i += 4)
                            if (px[i] != 0) { alpha = true; break; }
                        BitmapSource src = BitmapSource.Create(w, h, 96, 96,
                            alpha ? PixelFormats.Pbgra32 : PixelFormats.Bgr32, null, px, stride);
                        src.Freeze();
                        return src;
                    }
                    finally { bmp.UnlockBits(d); }
                }
            }
            catch (Exception ex)
            {
                string dbg = Environment.GetEnvironmentVariable("BIGPICTURE_DEBUG");
                if (!string.IsNullOrEmpty(dbg)) try { File.AppendAllText(dbg, path + "\r\n" + ex + "\r\n"); } catch { }
                return null;
            }
            finally
            {
                if (hbm != IntPtr.Zero) DeleteObject(hbm);
            }
        }
    }
}
