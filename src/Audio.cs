using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BigPicture
{
    class AudioDevice
    {
        public string Id;
        public string Name;
        public bool IsDefault;
    }

    /// <summary>Volume, muto e scelta dell'uscita audio tramite le Core Audio API di Windows.</summary>
    static class Audio
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        class MMDeviceEnumerator { }

        [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
        class PolicyConfigClient { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int Item(int index, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
            [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetState(out int state);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PropertyKey
        {
            public Guid fmtid;
            public int pid;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PropVariant
        {
            public short vt, r1, r2, r3;
            public IntPtr p1, p2;
        }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetAt(int index, out PropertyKey key);
            [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int GetChannelCount(out int count);
            [PreserveSig] int SetMasterVolumeLevel(float db, ref Guid ctx);
            [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
            [PreserveSig] int GetMasterVolumeLevel(out float db);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
            [PreserveSig] int SetChannelVolumeLevel(uint channel, float db, ref Guid ctx);
            [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid ctx);
            [PreserveSig] int GetChannelVolumeLevel(uint channel, out float db);
            [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        // Interfaccia non documentata, la stessa usata dal pannello Audio di Windows per cambiare l'uscita predefinita.
        [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPolicyConfig
        {
            [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr format);
            [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string dev, [MarshalAs(UnmanagedType.Bool)] bool def, IntPtr format);
            [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string dev);
            [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr endpointFormat, IntPtr mixFormat);
            [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string dev, [MarshalAs(UnmanagedType.Bool)] bool def, IntPtr defPeriod, IntPtr minPeriod);
            [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr period);
            [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr mode);
            [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string dev, IntPtr mode);
            [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string dev, [MarshalAs(UnmanagedType.Bool)] bool fx, IntPtr key, IntPtr value);
            [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string dev, [MarshalAs(UnmanagedType.Bool)] bool fx, IntPtr key, IntPtr value);
            [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string dev, int role);
            [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string dev, [MarshalAs(UnmanagedType.Bool)] bool visible);
        }

        [DllImport("ole32.dll")]
        static extern int PropVariantClear(ref PropVariant pv);

        static Guid noContext = Guid.Empty;

        static void Release(object o)
        {
            if (o != null) try { Marshal.ReleaseComObject(o); } catch { }
        }

        /// <summary>Controllo del volume dell'uscita predefinita, o null.</summary>
        static IAudioEndpointVolume Endpoint()
        {
            IMMDeviceEnumerator en = null;
            IMMDevice dev = null;
            try
            {
                en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                if (en.GetDefaultAudioEndpoint(0, 0, out dev) != 0 || dev == null) return null;
                Guid iid = typeof(IAudioEndpointVolume).GUID;
                object o;
                if (dev.Activate(ref iid, 23, IntPtr.Zero, out o) != 0) return null;
                return o as IAudioEndpointVolume;
            }
            catch { return null; }
            finally { Release(dev); Release(en); }
        }

        /// <summary>Volume da 0 a 1, oppure -1 se non disponibile.</summary>
        public static float GetVolume()
        {
            IAudioEndpointVolume v = Endpoint();
            if (v == null) return -1;
            try
            {
                float f;
                return v.GetMasterVolumeLevelScalar(out f) == 0 ? f : -1;
            }
            catch { return -1; }
            finally { Release(v); }
        }

        public static bool SetVolume(float level)
        {
            IAudioEndpointVolume v = Endpoint();
            if (v == null) return false;
            try { return v.SetMasterVolumeLevelScalar(Math.Max(0f, Math.Min(1f, level)), ref noContext) == 0; }
            catch { return false; }
            finally { Release(v); }
        }

        public static bool GetMute()
        {
            IAudioEndpointVolume v = Endpoint();
            if (v == null) return false;
            try
            {
                bool m;
                return v.GetMute(out m) == 0 && m;
            }
            catch { return false; }
            finally { Release(v); }
        }

        public static bool SetMute(bool mute)
        {
            IAudioEndpointVolume v = Endpoint();
            if (v == null) return false;
            try { return v.SetMute(mute, ref noContext) == 0; }
            catch { return false; }
            finally { Release(v); }
        }

        static string Name(IMMDevice dev)
        {
            IPropertyStore store = null;
            try
            {
                if (dev.OpenPropertyStore(0, out store) != 0 || store == null) return null;
                PropertyKey key = new PropertyKey();
                key.fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0");   // PKEY_Device_FriendlyName
                key.pid = 14;
                PropVariant pv;
                if (store.GetValue(ref key, out pv) != 0) return null;
                try { return pv.vt == 31 ? Marshal.PtrToStringUni(pv.p1) : null; }   // VT_LPWSTR
                finally { PropVariantClear(ref pv); }
            }
            catch { return null; }
            finally { Release(store); }
        }

        /// <summary>Uscite audio attive (casse, TV via HDMI, cuffie…).</summary>
        public static List<AudioDevice> Devices()
        {
            List<AudioDevice> list = new List<AudioDevice>();
            IMMDeviceEnumerator en = null;
            IMMDeviceCollection coll = null;
            IMMDevice def = null;
            try
            {
                en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                string defId = null;
                if (en.GetDefaultAudioEndpoint(0, 0, out def) == 0 && def != null) def.GetId(out defId);
                if (en.EnumAudioEndpoints(0, 1, out coll) != 0 || coll == null) return list;    // riproduzione, solo attive
                int n;
                coll.GetCount(out n);
                for (int i = 0; i < n; i++)
                {
                    IMMDevice d = null;
                    try
                    {
                        if (coll.Item(i, out d) != 0 || d == null) continue;
                        AudioDevice a = new AudioDevice();
                        d.GetId(out a.Id);
                        a.Name = Name(d) ?? "Uscita " + (i + 1);
                        a.IsDefault = a.Id == defId;
                        list.Add(a);
                    }
                    finally { Release(d); }
                }
            }
            catch { }
            finally { Release(def); Release(coll); Release(en); }
            return list;
        }

        /// <summary>Rende predefinita un'uscita, per tutti gli usi (console, multimedia, comunicazioni).</summary>
        public static bool SetDefault(string id)
        {
            IPolicyConfig pc = null;
            try
            {
                pc = (IPolicyConfig)new PolicyConfigClient();
                bool ok = true;
                for (int role = 0; role < 3; role++)
                    if (pc.SetDefaultEndpoint(id, role) != 0) ok = false;
                return ok;
            }
            catch { return false; }
            finally { Release(pc); }
        }
    }
}
