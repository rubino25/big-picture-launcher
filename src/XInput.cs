using System;
using System.Runtime.InteropServices;

namespace BigPicture
{
    [Flags]
    enum Pad
    {
        None = 0,
        Up = 0x1, Down = 0x2, Left = 0x4, Right = 0x8,
        Start = 0x10, Back = 0x20,
        LS = 0x40, RS = 0x80,           // pressione delle levette
        LB = 0x100, RB = 0x200,
        Guide = 0x400,
        A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000
    }

    /// <summary>Stato completo: tasti, grilletti e levette (del primo controller collegato).</summary>
    struct PadState
    {
        public Pad Buttons;             // tasti premuti, senza le levette
        public byte LT, RT;
        public short LX, LY, RX, RY;
        public bool Connected;
    }

    /// <summary>Lettura dei controller XInput (Xbox e compatibili). Unisce i 4 slot.</summary>
    static class XInput
    {
        [StructLayout(LayoutKind.Sequential)]
        struct Gamepad
        {
            public ushort Buttons;
            public byte LT, RT;
            public short LX, LY, RX, RY;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct State
        {
            public uint Packet;
            public Gamepad Pad;
            public uint Reserved;       // spazio in più richiesto dalla variante "Ex"
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BatteryInfo
        {
            public byte Type, Level;
        }

        // Funzione non documentata (ordinale 100): come XInputGetState ma riporta anche il tasto Guide.
        [DllImport("xinput1_4.dll", EntryPoint = "#100")]
        static extern uint GetEx(uint index, out State state);

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        static extern uint Get14(uint index, out State state);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        static extern uint Get910(uint index, out State state);

        [DllImport("xinput1_4.dll")]
        static extern uint XInputGetBatteryInformation(uint index, byte devType, out BatteryInfo info);

        const int Mask = 0x1 | 0x2 | 0x4 | 0x8 | 0x10 | 0x20 | 0x40 | 0x80 | 0x100 | 0x200 | 0x400 | 0x1000 | 0x2000 | 0x4000 | 0x8000;
        const int DeadZone = 16000;

        static int api;                                  // 0 = Ex, 1 = xinput1_4, 2 = xinput9_1_0, 3 = non disponibile
        static readonly int[] retryAt = new int[4];      // slot scollegati: ricontrolla ogni secondo
        static int firstSlot = -1;
        public static bool AnyConnected;

        static uint Get(uint i, out State s)
        {
            s = new State();
            if (api == 0)
            {
                try { return GetEx(i, out s); }
                catch (DllNotFoundException) { api = 2; }
                catch (EntryPointNotFoundException) { api = 1; }
            }
            if (api == 1)
            {
                try { return Get14(i, out s); }
                catch (DllNotFoundException) { api = 2; }
                catch (EntryPointNotFoundException) { api = 2; }
            }
            if (api == 2)
            {
                try { return Get910(i, out s); }
                catch (DllNotFoundException) { api = 3; }
                catch (EntryPointNotFoundException) { api = 3; }
            }
            return 1167; // ERROR_DEVICE_NOT_CONNECTED
        }

        /// <summary>Tasti per la navigazione: come <see cref="PadState.Buttons"/> più la levetta sinistra letta come croce.</summary>
        public static Pad Read(out PadState full)
        {
            full = new PadState();
            int now = Environment.TickCount;
            int buttons = 0, nav = 0;
            bool any = false;
            int first = -1;
            for (uint i = 0; i < 4; i++)
            {
                if (retryAt[i] != 0 && unchecked(now - retryAt[i]) < 0) continue;
                State s;
                if (Get(i, out s) != 0)
                {
                    retryAt[i] = unchecked(now + 1000);
                    if (retryAt[i] == 0) retryAt[i] = 1;
                    continue;
                }
                retryAt[i] = 0;
                buttons |= s.Pad.Buttons & Mask;
                if (s.Pad.LX < -DeadZone) nav |= (int)Pad.Left;
                if (s.Pad.LX > DeadZone) nav |= (int)Pad.Right;
                if (s.Pad.LY > DeadZone) nav |= (int)Pad.Up;
                if (s.Pad.LY < -DeadZone) nav |= (int)Pad.Down;
                if (!any)
                {
                    first = (int)i;
                    full.LT = s.Pad.LT; full.RT = s.Pad.RT;
                    full.LX = s.Pad.LX; full.LY = s.Pad.LY;
                    full.RX = s.Pad.RX; full.RY = s.Pad.RY;
                }
                any = true;
            }
            AnyConnected = any;
            firstSlot = first;
            full.Connected = any;
            full.Buttons = (Pad)buttons;
            return (Pad)(buttons | nav);
        }

        public static Pad Read()
        {
            PadState s;
            return Read(out s);
        }

        /// <summary>Carica del primo controller: "piena", "media", "bassa", "scarica", "via cavo" oppure null.</summary>
        public static string Battery()
        {
            int slot = firstSlot;
            if (slot < 0) return null;
            try
            {
                BatteryInfo b;
                if (XInputGetBatteryInformation((uint)slot, 0, out b) != 0) return null;
                if (b.Type == 0 || b.Type == 0xFF) return null;
                if (b.Type == 1) return "via cavo";
                switch (b.Level)
                {
                    case 0: return "scarica";
                    case 1: return "bassa";
                    case 2: return "media";
                    default: return "piena";
                }
            }
            catch { return null; }
        }
    }
}
