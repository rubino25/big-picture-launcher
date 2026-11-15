using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace BigPicture
{
    /// <summary>
    /// Finestra sempre in primo piano che non prende mai il focus: il programma sotto resta attivo
    /// e riceve mouse e tastiera simulati. Base per l'indicatore e la tastiera della modalità desktop.
    /// </summary>
    class OverlayWindow : Window
    {
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetLong32(IntPtr h, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] static extern int SetLong32(IntPtr h, int index, int value);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetLong64(IntPtr h, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetLong64(IntPtr h, int index, IntPtr value);

        protected readonly double Scale;

        public OverlayWindow(bool clickThrough)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowActivated = false;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            Focusable = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Scale = Math.Max(0.6, SystemParameters.PrimaryScreenHeight / 1080.0);

            SourceInitialized += delegate
            {
                IntPtr h = new WindowInteropHelper(this).Handle;
                long ex = 0x08000000L | 0x80L;              // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
                if (clickThrough) ex |= 0x20L;               // WS_EX_TRANSPARENT: i clic passano sotto
                if (IntPtr.Size == 8) SetLong64(h, -20, new IntPtr(GetLong64(h, -20).ToInt64() | ex));
                else SetLong32(h, -20, GetLong32(h, -20) | (int)ex);
            };
        }

        protected static TextBlock Text(string s, double size, Brush color, FontWeight weight)
        {
            TextBlock t = new TextBlock();
            t.Text = s;
            t.FontSize = size;
            t.Foreground = color;
            t.FontWeight = weight;
            t.FontFamily = new FontFamily("Segoe UI");
            return t;
        }

        protected static Brush Rgb(byte a, byte r, byte g, byte b)
        {
            SolidColorBrush br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            br.Freeze();
            return br;
        }
    }

    /// <summary>Striscia in alto che ricorda che il controller sta facendo da mouse e tastiera.</summary>
    class DesktopHud : OverlayWindow
    {
        public DesktopHud() : base(true)
        {
            StackPanel sp = new StackPanel();
            sp.Margin = new Thickness(22, 10, 22, 12);
            TextBlock title = Text("MODALITÀ DESKTOP", 17, Brushes.White, FontWeights.Bold);
            title.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(title);
            TextBlock help = Text(
                "Levetta destra  Puntatore   ·   RT / A  Clic   ·   LT  Clic destro   ·   Levetta sinistra  Scorrimento   ·   Croce  Frecce\n" +
                "Y  Tastiera   ·   X  Invio   ·   B  Esc   ·   RB  Tab   ·   LB  Alt+Tab   ·   Start  Windows   ·   Back + Start  Esci",
                14, Rgb(255, 0xC9, 0xD6, 0xF2), FontWeights.Normal);
            help.TextAlignment = TextAlignment.Center;
            help.Margin = new Thickness(0, 5, 0, 0);
            sp.Children.Add(help);

            Border b = new Border();
            b.CornerRadius = new CornerRadius(0, 0, 14, 14);
            b.Background = Rgb(0xE0, 0x10, 0x16, 0x2B);
            b.BorderBrush = Rgb(0x66, 0xFF, 0xFF, 0xFF);
            b.BorderThickness = new Thickness(1.5, 0, 1.5, 1.5);
            b.Child = sp;
            b.LayoutTransform = new ScaleTransform(Scale, Scale);
            Content = b;

            Loaded += delegate
            {
                Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
                Top = 0;
            };
        }
    }

    /// <summary>
    /// Tastiera a schermo della modalità desktop: scrive nel programma in primo piano.
    /// Si usa con la croce (o cliccando i tasti col puntatore).
    /// </summary>
    class OskWindow : OverlayWindow
    {
        static readonly string[] Rows = { "1234567890", "qwertyuiop", "asdfghjkl'", "zxcvbnm,.-", "@_/:;!?#&+" };
        static readonly string[] Special = { "Maiusc", "Spazio", "Canc", "Invio", "Tab", "Chiudi" };

        readonly List<Border> keys = new List<Border>();
        readonly List<TextBlock> labels = new List<TextBlock>();
        readonly Brush normal = Rgb(255, 0x22, 0x2C, 0x4F), selected = Rgb(255, 0x2F, 0x6F, 0xEB), shiftOn = Rgb(255, 0x1F, 0x9D, 0x55);
        int index = 10;
        bool shift;

        /// <summary>Richiesta di chiusura (tasto «Chiudi»).</summary>
        public event Action CloseRequested;

        public int CharCount { get { return Rows.Length * 10; } }

        public OskWindow() : base(false)
        {
            StackPanel sp = new StackPanel();
            sp.Margin = new Thickness(14);

            UniformGrid grid = new UniformGrid();
            grid.Columns = 10;
            foreach (string row in Rows)
                foreach (char c in row) grid.Children.Add(MakeKey(c.ToString(), 84));
            sp.Children.Add(grid);

            UniformGrid bottom = new UniformGrid();
            bottom.Columns = Special.Length;
            foreach (string s in Special) bottom.Children.Add(MakeKey(s, 140));
            sp.Children.Add(bottom);

            TextBlock help = Text("A  Scrivi   ·   X  Cancella   ·   Y  Spazio   ·   LB  Maiuscole   ·   Start  Invio   ·   B  Chiudi",
                14, Rgb(255, 0xC9, 0xD6, 0xF2), FontWeights.Normal);
            help.HorizontalAlignment = HorizontalAlignment.Center;
            help.Margin = new Thickness(0, 8, 0, 0);
            sp.Children.Add(help);

            Border b = new Border();
            b.CornerRadius = new CornerRadius(16);
            b.Background = Rgb(0xF2, 0x10, 0x16, 0x2B);
            b.BorderBrush = Rgb(0x66, 0xFF, 0xFF, 0xFF);
            b.BorderThickness = new Thickness(1.5);
            b.Child = sp;
            b.LayoutTransform = new ScaleTransform(Scale, Scale);
            Content = b;

            Loaded += delegate
            {
                Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
                Top = SystemParameters.PrimaryScreenHeight - ActualHeight - 60 * Scale;
            };
            Paint();
        }

        Border MakeKey(string label, double width)
        {
            int my = keys.Count;
            TextBlock t = Text(label, 22, Brushes.White, FontWeights.Normal);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
            Border k = new Border();
            k.Width = width;
            k.Height = 56;
            k.Margin = new Thickness(3);
            k.CornerRadius = new CornerRadius(8);
            k.Background = normal;
            k.Child = t;
            k.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                index = my;
                Paint();
                Press();
            };
            keys.Add(k);
            labels.Add(t);
            return k;
        }

        void Paint()
        {
            int n = CharCount;
            for (int i = 0; i < keys.Count; i++)
            {
                if (i < n)
                {
                    char c = Rows[i / 10][i % 10];
                    labels[i].Text = (shift ? char.ToUpperInvariant(c) : c).ToString();
                }
                keys[i].Background = i == index ? selected : (i == n && shift ? shiftOn : normal);
            }
        }

        public void ToggleShift()
        {
            shift = !shift;
            Paint();
        }

        public void Move(Cmd c)
        {
            int n = CharCount, rows = Rows.Length, sp = Special.Length;
            int row = index < n ? index / 10 : rows;
            int col = index < n ? index % 10 : index - n;
            switch (c)
            {
                case Cmd.Left: col = row < rows ? (col + 9) % 10 : (col + sp - 1) % sp; break;
                case Cmd.Right: col = row < rows ? (col + 1) % 10 : (col + 1) % sp; break;
                case Cmd.Up:
                    if (row == 0) { row = rows; col = col * sp / 10; }
                    else if (row == rows) { row = rows - 1; col = col * 10 / sp; }
                    else row--;
                    break;
                case Cmd.Down:
                    if (row == rows - 1) { row = rows; col = col * sp / 10; }
                    else if (row == rows) { row = 0; col = col * 10 / sp; }
                    else row++;
                    break;
            }
            index = row < rows ? row * 10 + col : n + col;
            Paint();
        }

        /// <summary>Preme il tasto selezionato.</summary>
        public void Press()
        {
            int n = CharCount;
            if (index < n)
            {
                char c = Rows[index / 10][index % 10];
                Input.Char(shift ? char.ToUpperInvariant(c) : c);
                if (shift) { shift = false; Paint(); }      // maiuscola valida per una lettera
                return;
            }
            switch (index - n)
            {
                case 0: ToggleShift(); break;
                case 1: Input.Tap(Input.VkSpace); break;
                case 2: Input.Tap(Input.VkBack); break;
                case 3: Input.Tap(Input.VkEnter); break;
                case 4: Input.Tap(Input.VkTab); break;
                case 5:
                    Action h = CloseRequested;
                    if (h != null) h();
                    break;
            }
        }
    }
}
