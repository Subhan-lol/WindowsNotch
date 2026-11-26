using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace NotchIsland
{
    enum ToastKind { None, Volume, Battery, Track, Info }

    /// <summary>
    /// The notch itself. One fixed-size transparent, click-through window pinned to the top of the primary screen;
    /// only the black shape is visible/clickable. Nothing renders while idle, so it costs ~0% CPU.
    /// </summary>
    public partial class NotchWindow : Window
    {
        const double WinW = 780, WinH = 290;
        const double ExpW = 680, ExpH = 232, ExpR = 30;
        const int HotkeyId = 0x4E1;

        IntPtr hwnd;
        readonly Grid root;
        readonly MediaService media;
        readonly AudioService audio;
        readonly ShelfStore shelf;
        Color accentColor = MediaService.DefaultAccent;

        // named elements
        Border hoverZone; NotchBody body; Grid compact, expandedGrid;
        ScaleTransform expScale; TranslateTransform expShift;
        Mascot mini, big;
        TextBlock moodText;

        bool expanded, hovering, dragOver, draggingOut, pinnedOpen, scrubbing, hiddenFullscreen, userHidden, quitting;
        string tab = "home";
        DateTime lastInteraction = DateTime.Now, startedAt = DateTime.Now, lastTrim = DateTime.MinValue;
        ToastKind toast = ToastKind.None;
        string toastTitle = "", toastSub = "", toastGlyph = "";
        readonly DispatcherTimer hoverTimer, collapseTimer, toastTimer, fsTimer, idleTimer, trimTimer;

        public NotchWindow()
        {
            Title = "Notch Island";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Width = WinW; Height = WinH;
            Left = -20000; Top = 0;
            AllowDrop = true;

            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Notch.xaml"))
                root = (Grid)XamlReader.Load(s);
            // Lives on the notch (not app-wide) so it stays animatable for the album-colour fade.
            root.Resources["Accent"] = new SolidColorBrush(accentColor);
            Content = root;

            hoverZone = E<Border>("HoverZone");
            body = E<NotchBody>("Body");
            compact = E<Grid>("Compact");
            expandedGrid = E<Grid>("Expanded");
            expScale = E<ScaleTransform>("ExpScale");
            expShift = E<TranslateTransform>("ExpShift");
            mini = E<Mascot>("MiniMascot");
            big = E<Mascot>("BigMascot");
            big.Lively = true;
            moodText = E<TextBlock>("MoodText");

            media = new MediaService(Dispatcher);
            audio = new AudioService(Dispatcher);
            shelf = new ShelfStore(Dispatcher);

            hoverTimer = Timer(170, (s, e) => { hoverTimer.Stop(); if (hovering && !expanded) Expand(null); });
            collapseTimer = Timer(380, (s, e) => { collapseTimer.Stop(); if (CanCollapse()) Collapse(); else if (expanded && !CursorOverNotch() && !pinnedOpen) collapseTimer.Start(); });
            toastTimer = Timer(1600, (s, e) => { toastTimer.Stop(); toast = ToastKind.None; ApplyCompact(); });
            fsTimer = Timer(1500, (s, e) => CheckFullscreen());
            idleTimer = Timer(15000, (s, e) => UpdateMood());
            trimTimer = Timer(20000, (s, e) => { trimTimer.Stop(); TrimMemory(); });

            WireNotch();
            WireMedia();
            WireShelf();
            WireTools();
            WireBuddy();
            WirePhone();

            Settings.Changed += OnSettingChanged;
            Deactivated += (s, e) => { if (pinnedOpen) { pinnedOpen = false; SetNoActivate(true); ScheduleCollapse(250); } };
            KeyDown += (s, e) => { if (e.Key == Key.Escape) { pinnedOpen = false; Collapse(); } };
        }

        T E<T>(string name) where T : class
        {
            var o = root.FindName(name) as T;
            if (o == null) throw new InvalidOperationException("Missing element " + name);
            return o;
        }

        DispatcherTimer Timer(int ms, EventHandler tick)
        {
            var t = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += tick;
            return t;
        }

        // ================= window plumbing =================
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            hwnd = new WindowInteropHelper(this).Handle;
            long ex = Native.GetExStyle(hwnd);
            ex |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
            ex &= ~(long)Native.WS_EX_APPWINDOW;
            Native.SetExStyle(hwnd, ex);
            HwndSource.FromHwnd(hwnd).AddHook(WndProc);
            Native.RegisterHotKey(hwnd, HotkeyId, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, 0x20);
            Native.AddClipboardFormatListener(hwnd);
            PlaceWindow();
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            if (SnapshotDir != null) { RunSnapshots(); return; }
            media.Start();
            audio.Start();
            shelf.Load();
            RefreshBattery(false);
            UpdateVolumeUI();
            UpdateShelfUI();
            ApplyCompact();
            UpdateMood();
            mini.Active = true;
            RestoreBuddy();
            fsTimer.Start();
            idleTimer.Start();
            trimTimer.Interval = TimeSpan.FromSeconds(8); trimTimer.Start();
            // say hi on launch
            Dispatcher.BeginInvoke(new Action(() => { if (buddyOut) return; mini.Jump(); mini.Wave(); mini.Flash(Mood.Happy, 1500); }), DispatcherPriority.ApplicationIdle);
        }

        protected override void OnClosed(EventArgs e)
        {
            try { Native.UnregisterHotKey(hwnd, HotkeyId); Native.RemoveClipboardFormatListener(hwnd); } catch { }
            audio.Stop();
            SaveNotesNow();
            base.OnClosed(e);
        }

        IntPtr WndProc(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            switch (msg)
            {
                case Native.WM_HOTKEY:
                    if (w.ToInt32() == HotkeyId) ToggleFromHotkey();
                    break;
                case Native.WM_CLIPBOARDUPDATE:
                    OnClipboardUpdate();
                    break;
                case Native.WM_POWERBROADCAST:
                    if (w.ToInt32() == 0x0A) RefreshBattery(true);
                    break;
                case Native.WM_DISPLAYCHANGE:
                case Native.WM_DPICHANGED:
                case Native.WM_SETTINGCHANGE:
                    Dispatcher.BeginInvoke(new Action(PlaceWindow), DispatcherPriority.Background);
                    break;
            }
            return IntPtr.Zero;
        }

        void PlaceWindow()
        {
            if (hwnd == IntPtr.Zero) return;
            var mon = Native.PrimaryMonitor();
            double scale = 1;
            try { scale = Native.GetDpiForWindow(hwnd) / 96.0; } catch { }
            if (scale <= 0) scale = 1;
            int wpx = (int)Math.Round(WinW * scale);
            int x = mon.Left + ((mon.Right - mon.Left) - wpx) / 2;
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, mon.Top, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        void AssertTopmost()
        {
            if (hwnd != IntPtr.Zero)
                Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        void SetNoActivate(bool on)
        {
            if (hwnd == IntPtr.Zero) return;
            long ex = Native.GetExStyle(hwnd);
            ex = on ? (ex | Native.WS_EX_NOACTIVATE) : (ex & ~(long)Native.WS_EX_NOACTIVATE);
            Native.SetExStyle(hwnd, ex);
        }

        /// <summary>Lets the notch take keyboard focus (notes, hotkey, Esc).</summary>
        void TakeFocus()
        {
            pinnedOpen = true;
            SetNoActivate(false);
            Activate();
        }

        bool CursorOverNotch()
        {
            try
            {
                POINT p;
                if (!Native.GetCursorPos(out p)) return false;
                var pt = hoverZone.PointFromScreen(new Point(p.X, p.Y));
                return pt.X >= 0 && pt.Y >= -2 && pt.X <= hoverZone.ActualWidth && pt.Y <= hoverZone.ActualHeight;
            }
            catch { return false; }
        }

        bool CanCollapse()
        {
            return expanded && !dragOver && !draggingOut && !pinnedOpen && !scrubbing && !CursorOverNotch();
        }

        void ScheduleCollapse(int ms)
        {
            collapseTimer.Stop();
            collapseTimer.Interval = TimeSpan.FromMilliseconds(ms);
            collapseTimer.Start();
        }

        void Touch() { lastInteraction = DateTime.Now; if (mini.CurrentMood == Mood.Sleepy) UpdateMood(); }

        // ================= hover / click on the notch =================
        void WireNotch()
        {
            hoverZone.MouseEnter += (s, e) =>
            {
                hovering = true;
                collapseTimer.Stop();
                Touch();
                if (!expanded)
                {
                    if (!buddyOut) mini.Hello();
                    if (Settings.Get("ExpandOnHover")) hoverTimer.Start();
                    else Peek(true);
                }
            };
            hoverZone.MouseLeave += (s, e) =>
            {
                hovering = false;
                hoverTimer.Stop();
                if (!expanded) Peek(false);
                else ScheduleCollapse(380);
            };
            hoverZone.MouseLeftButtonUp += (s, e) =>
            {
                if (!expanded) { hoverTimer.Stop(); Expand(null); e.Handled = true; }
            };
            hoverZone.MouseDown += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Middle) { media.PlayPause(); React(m => m.Nod()); e.Handled = true; }
            };
            hoverZone.MouseWheel += (s, e) =>
            {
                if (e.Handled || !Settings.Get("ScrollVolume") || !audio.Available) return;
                audio.SetVolume(audio.Volume + (e.Delta > 0 ? 0.02f : -0.02f));
                e.Handled = true;
            };
            hoverZone.MouseMove += (s, e) =>
            {
                var m = expanded ? big : mini;
                var p = e.GetPosition(m);
                double cx = m.ActualWidth / 2, cy = m.ActualHeight / 2;
                m.LookAt((p.X - cx) / 160.0, (p.Y - cy) / 90.0);
            };
        }
        int pets;

        void Peek(bool on)
        {
            if (expanded) return;
            double w, h, r;
            CompactSize(out w, out h, out r);
            if (on) { w += 18; h += 4; }
            SizeTo(w, h, r, Anim.OutBack, 320);
        }

        void ToggleFromHotkey()
        {
            if (expanded) { pinnedOpen = false; Collapse(); return; }
            if (!IsVisible) Show();
            Expand(null);
            TakeFocus();
        }

        public void OpenFromTray()
        {
            if (userHidden) SetUserHidden(false);
            if (!IsVisible) Show();
            Expand(null);
            TakeFocus();
        }

        public void SetUserHidden(bool hide)
        {
            userHidden = hide;
            UpdateVisibility();
        }

        public void Quit()
        {
            quitting = true;
            SaveBuddy();
            if (buddy != null) buddy.Close();
            if (phone != null) phone.Stop();
            Close();
            Application.Current.Shutdown();
        }

        // ================= expand / collapse =================
        void SizeTo(double w, double h, double r, IEasingFunction ease, double ms)
        {
            Anim.To(body, FrameworkElement.WidthProperty, w, ms, ease);
            Anim.To(body, FrameworkElement.HeightProperty, h, ms, ease);
            Anim.To(body, NotchBody.RadiusProperty, r, ms, Anim.OutCubic);
        }

        void Expand(string tabName)
        {
            Dbg("expand " + tabName);
            if (tabName != null) SelectTab(tabName);
            if (expanded) return;
            PropsForTab(tab);
            expanded = true;
            Touch();
            AssertTopmost();
            toastTimer.Stop(); toast = ToastKind.None;

            expandedGrid.Visibility = Visibility.Visible;
            compact.IsHitTestVisible = false;
            Anim.To(compact, UIElement.OpacityProperty, 0, 90);
            SizeTo(ExpW, ExpH, ExpR, Anim.OutBack, 430);
            Anim.To(expandedGrid, UIElement.OpacityProperty, 1, 220, Anim.OutCubic, 90);
            expShift.Y = -10; expScale.ScaleX = expScale.ScaleY = 0.96;
            Anim.To(expShift, TranslateTransform.YProperty, 0, 380, Anim.OutBack, 60);
            Anim.To(expScale, ScaleTransform.ScaleXProperty, 1, 380, Anim.OutBack, 60);
            Anim.To(expScale, ScaleTransform.ScaleYProperty, 1, 380, Anim.OutBack, 60);

            mini.Active = false;
            big.Particles = true;
            big.Active = !buddyOut;
            if (!buddyOut) big.Hello();
            OnExpandedChanged();
        }

        void Collapse()
        {
            if (!expanded) return;
            Dbg("collapse");
            expanded = false;
            pinnedOpen = false;
            collapseTimer.Stop();
            SetNoActivate(true);
            Keyboard.ClearFocus();

            Anim.To(expandedGrid, UIElement.OpacityProperty, 0, 110, null, 0, 0, (s, e) =>
            {
                if (!expanded) expandedGrid.Visibility = Visibility.Collapsed;
            });
            compact.IsHitTestVisible = true;
            Anim.To(compact, UIElement.OpacityProperty, 1, 200, Anim.OutCubic, 130);
            big.Active = false;
            mini.Active = IsVisible && !buddyOut;
            ApplyCompact(Anim.OutBackSoft, 380);
            OnExpandedChanged();
            ScheduleTrim();
        }

        void OnExpandedChanged()
        {
            UpdateMediaUI();
            UpdateViz();
            UpdateClock();
            UpdateMood();
            if (expanded) { RefreshBattery(false); RefreshClipAges(); }
        }

        void SelectTab(string name)
        {
            tab = name;
            foreach (var c in E<StackPanel>("Tabs").Children)
            {
                var rb = c as RadioButton;
                if (rb != null && (string)rb.Tag == name && rb.IsChecked != true) rb.IsChecked = true;
            }
            string[] panels = { "home", "PanelHome", "shelf", "PanelShelf", "clip", "PanelClip", "timer", "PanelTimer", "notes", "PanelNotes", "settings", "PanelSettings" };
            for (int i = 0; i < panels.Length; i += 2)
                E<Grid>(panels[i + 1]).Visibility = panels[i] == name ? Visibility.Visible : Visibility.Collapsed;
            if (name == "clip") RefreshClipAges();
            PropsForTab(name);
            if (expanded && !buddyOut) big.Nod();
            UpdateMediaUI();
            UpdateViz();
        }

        // ================= compact live activities =================
        void CompactSize(out double w, out double h, out double r)
        {
            h = 32; r = 14;
            switch (toast)
            {
                case ToastKind.Volume: w = 318; return;
                case ToastKind.Battery: w = 300; return;
                case ToastKind.Track: w = 380; h = 38; r = 16; return;
                case ToastKind.Info: w = 340; h = 38; r = 16; return;
            }
            if (timerRunning) { w = 268; return; }
            if (media.IsPlaying && media.HasTrack) { w = 296; return; }
            w = buddyOut ? 150 : 176;
        }

        void ShowToast(ToastKind k, int ms, string title, string sub, string glyph)
        {
            if (expanded || !IsVisible) return;
            toast = k; toastTitle = title ?? ""; toastSub = sub ?? ""; toastGlyph = glyph ?? "";
            toastTimer.Stop();
            toastTimer.Interval = TimeSpan.FromMilliseconds(ms);
            toastTimer.Start();
            ApplyCompact();
        }

        void ApplyCompact() { ApplyCompact(Anim.OutBack, 420); }

        void ApplyCompact(IEasingFunction ease, double ms)
        {
            var cArt = E<Border>("CArt"); var cIcon = E<TextBlock>("CIcon"); var cRing = E<Grid>("CRing");
            var cText = E<StackPanel>("CText"); var cEq = E<StackPanel>("CEq"); var cBar = E<Grid>("CBar");
            var cRightText = E<TextBlock>("CRightText");
            Visibility V = Visibility.Visible, C = Visibility.Collapsed;
            cArt.Visibility = cIcon.Visibility = cRing.Visibility = cText.Visibility = cEq.Visibility = cBar.Visibility = cRightText.Visibility = C;
            mini.Visibility = buddyOut ? C : V;
            cRightText.Margin = new Thickness(0);
            cIcon.Foreground = Brushes.White;
            bool playing = media.IsPlaying && media.HasTrack;

            switch (toast)
            {
                case ToastKind.Volume:
                    cIcon.Text = VolumeGlyph(); cIcon.Visibility = V;
                    cBar.Visibility = V;
                    E<Border>("CBarFill").Width = Math.Max(0, 96 * (audio.Muted ? 0 : audio.Volume));
                    cRightText.Text = audio.Muted ? "0" : ((int)Math.Round(audio.Volume * 100)).ToString();
                    cRightText.Margin = new Thickness(9, 0, 0, 0); cRightText.Visibility = V;
                    break;
                case ToastKind.Battery:
                    cIcon.Text = toastGlyph; cIcon.Visibility = V;
                    cIcon.Foreground = toastTitle == "Charging" ? Brush(0x7D, 0xF0, 0x9A) : (batPct >= 0 && batPct <= 20) ? Brush(0xFF, 0x6B, 0x6B) : Brushes.White;
                    cRightText.Text = toastSub; cRightText.Visibility = V;
                    break;
                case ToastKind.Track:
                    cArt.Visibility = media.Art != null ? V : C;
                    E<ImageBrush>("CArtBrush").ImageSource = media.Art;
                    if (media.Art == null) { cIcon.Text = ""; cIcon.Visibility = V; }
                    mini.Visibility = C;
                    E<TextBlock>("CTitle").Text = toastTitle; E<TextBlock>("CSub").Text = toastSub; cText.Visibility = V;
                    cEq.Visibility = playing ? V : C;
                    break;
                case ToastKind.Info:
                    cIcon.Text = toastGlyph; cIcon.Visibility = V;
                    mini.Visibility = C;
                    E<TextBlock>("CTitle").Text = toastTitle; E<TextBlock>("CSub").Text = toastSub; cText.Visibility = V;
                    break;
                default:
                    if (timerRunning)
                    {
                        cRing.Visibility = V;
                        cRightText.Text = FormatTime(timerLeft); cRightText.Visibility = V;
                    }
                    else if (playing)
                    {
                        if (media.Art != null) { cArt.Visibility = V; E<ImageBrush>("CArtBrush").ImageSource = media.Art; }
                        else { cIcon.Text = ""; cIcon.Visibility = V; }
                        cEq.Visibility = V;
                    }
                    break;
            }
            compact.Height = (toast == ToastKind.Track || toast == ToastKind.Info) ? 38 : 32;
            if (!expanded)
            {
                double w, h, r;
                CompactSize(out w, out h, out r);
                if (hovering && !Settings.Get("ExpandOnHover")) { w += 18; h += 4; }
                SizeTo(w, h, r, ease, ms);
            }
            UpdateViz();
        }

        static SolidColorBrush Brush(byte r, byte g, byte b) { return new SolidColorBrush(Color.FromRgb(r, g, b)); }

        // ================= mood =================
        void UpdateMood()
        {
            bool sleepy = (DateTime.Now - lastInteraction).TotalSeconds > 75;
            Mood m;
            string caption;
            if (dragOver) { m = Mood.Hungry; caption = "ooh, gimme gimme!"; }
            else if (timerRunning) { m = Mood.Focus; caption = "focus mode · " + FormatTime(timerLeft) + " left"; }
            else if (media.IsPlaying && media.HasTrack) { m = Mood.Vibing; caption = "vibing to " + (media.Artist.Length > 0 ? media.Artist : media.Title); }
            else if (hasBattery && !charging && batPct >= 0 && batPct <= 15) { m = Mood.Worried; caption = "battery's getting low…"; }
            else if (sleepy && !expanded && !hovering) { m = Mood.Sleepy; caption = "zzz…"; }
            else
            {
                m = Mood.Idle;
                int n = shelf.Items.Count;
                caption = n > 0 ? "holding " + n + (n == 1 ? " file" : " files") + " for you" : "hi! I'm Mochi";
            }
            mini.SetMood(m);
            big.SetMood(m);
            if (buddy != null) buddy.SetContext(ContextMood());
            if (!(big.IsMouseOver && moodText.Text.StartsWith("hehe")) && !moodText.Text.StartsWith("ok ok")) moodText.Text = caption;
        }

        // ================= fullscreen / visibility / memory =================
        static readonly bool debug = Environment.GetEnvironmentVariable("NOTCH_DEBUG") == "1";
        public static void Dbg(string msg)
        {
            if (!debug) return;
            try { System.IO.File.AppendAllText(Paths.File("debug.log"), DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + Environment.NewLine); } catch { }
        }

        void CheckFullscreen()
        {
            bool fs = Settings.Get("HideInFullscreen") && Native.ForegroundIsFullscreen(hwnd);
            if (fs == hiddenFullscreen) return;
            if (debug)
            {
                var fg = Native.GetForegroundWindow();
                var sb = new System.Text.StringBuilder(128); Native.GetClassName(fg, sb, 128);
                Dbg("fullscreen=" + fs + " fg=" + fg + " class=" + sb + " visible=" + IsVisible);
            }
            hiddenFullscreen = fs;
            UpdateVisibility();
        }

        void UpdateVisibility()
        {
            bool show = !hiddenFullscreen && !userHidden;
            if (show && !IsVisible)
            {
                Show();
                PlaceWindow();
                mini.Active = !expanded && !buddyOut;
                ApplyCompact();
            }
            else if (!show && IsVisible)
            {
                if (expanded) { pinnedOpen = false; Collapse(); }
                mini.Active = false; big.Active = false;
                Hide();
            }
            if (buddy != null) buddy.Suppress(!show);
            UpdateViz();
        }

        void ScheduleTrim() { trimTimer.Stop(); trimTimer.Interval = TimeSpan.FromSeconds(25); trimTimer.Start(); }

        /// <summary>Hand unused pages back to Windows when idle so we stay tiny in Task Manager.</summary>
        void TrimMemory()
        {
            if (expanded || (DateTime.Now - lastTrim).TotalMinutes < 2) return;
            lastTrim = DateTime.Now;
            try
            {
                GC.Collect(2, GCCollectionMode.Forced, true);
                GC.WaitForPendingFinalizers();
                Native.EmptyWorkingSet(Process.GetCurrentProcess().Handle);
            }
            catch { }
        }

        void OnSettingChanged(string key)
        {
            if (key == "Visualizer") UpdateViz();
            else if (key == "AlbumColors") UpdateAccent();
            else if (key == "HideInFullscreen") CheckFullscreen();
            else if (key == "ClipboardHistory" && !Settings.Get(key)) ClearClips();
            else if (key == "StartWithWindows") App.SetStartup(Settings.Get(key));
            else if (key == "PhoneDrop") { if (Settings.Get(key)) StartPhone(); else StopPhone(); SyncSettingSwitch(key); }
        }

        static string FormatTime(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            int total = (int)Math.Round(t.TotalSeconds);
            if (total >= 3600) return (total / 3600) + ":" + ((total / 60) % 60).ToString("00") + ":" + (total % 60).ToString("00");
            return (total / 60) + ":" + (total % 60).ToString("00");
        }
    }
}
