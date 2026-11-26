using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace NotchIsland
{
    /// <summary>
    /// Mochi out of the notch: a tiny click-through window that lives on top of the taskbar.
    /// Drag to carry, fling to throw, drop or throw it at the notch to send it home.
    /// Timers only run while it's moving; sitting still costs nothing but the odd blink.
    /// </summary>
    public partial class BuddyWindow : Window
    {
        enum State { Hidden, Carried, Flying, Grounded, Walking, GoingHome }
        struct Sample { public double T, X, Y; }

        const double W = 300, H = 190, Size = 84;
        public readonly Mascot Mascot;
        readonly Grid rootGrid;
        readonly ScaleTransform homeScale = new ScaleTransform(1, 1);
        readonly DispatcherTimer tick, ai, eyes;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly List<Sample> trail = new List<Sample>();
        readonly Random rnd = new Random();

        IntPtr hwnd;
        State state = State.Hidden;
        double scale = 1, x, y, vx, vy, lastT, walkTarget, walkSpeed;
        double grabX, grabY, carryStartT, carryDist, notchImmuneUntil;
        bool carriedByClick, suppressed, swinging;
        Mood context = Mood.Idle;
        double lastLookX = 9, lastLookY = 9;
        DateTime lastHello = DateTime.MinValue;
        Point homeFrom;

        /// <summary>Screen rect (physical px) of the notch: drop/throw Mochi here to send it home.</summary>
        public Func<Rect> NotchZone;
        public Func<bool> MusicPlaying;
        public event Action WentHome;
        public event Action<string> Said;

        public bool IsOut { get { return state != State.Hidden; } }
        public string DebugState { get { return string.Format("state={0} x={1:0} y={2:0} ground={3:0} scale={4}", state, x, y, Ground(), scale); } }
        public FrameworkElement Visual { get { return rootGrid; } }

        public BuddyWindow()
        {
            Title = "Mochi";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Width = W; Height = H;
            Left = -20000; Top = -20000;

            Mascot = new Mascot { Width = Size, Height = Size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Cursor = Cursors.Hand };
            Mascot.Particles = true;
            var mt = new TransformGroup(); mt.Children.Add(homeScale); mt.Children.Add(sink);
            Mascot.RenderTransform = mt;
            Mascot.RenderTransformOrigin = new Point(0.5, 0.9);
            Mascot.ToolTip = "Drag me around · right-click for toys · throw me at the notch to go home";
            Mascot.Lively = true;
            toyBack = new Canvas { Width = W, Height = H };
            toyFront = new Canvas { Width = W, Height = H };
            rootGrid = new Grid();
            rootGrid.Children.Add(toyBack);
            rootGrid.Children.Add(Mascot);
            rootGrid.Children.Add(toyFront);
            Content = rootGrid;
            InitPlay();
            Mascot.ContextMenu = BuildMenu();

            tick = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            tick.Tick += OnTick;
            ai = new DispatcherTimer(DispatcherPriority.Background);
            ai.Tick += (s, e) => { ai.Stop(); NextActivity(); };
            eyes = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(120) };
            eyes.Tick += (s, e) => TrackCursor();

            Mascot.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2) { GoHome(); e.Handled = true; return; }
                if (state == State.GoingHome) return;
                Mascot.CaptureMouse();
                POINT p; Native.GetCursorPos(out p);
                BeginCarry(p.X - x, p.Y - y, true);
                e.Handled = true;
            };
            Mascot.MouseLeftButtonUp += (s, e) => { if (state == State.Carried) Release(); };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            hwnd = new WindowInteropHelper(this).Handle;
            long ex = Native.GetExStyle(hwnd) | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
            Native.SetExStyle(hwnd, ex);
            if (Environment.GetEnvironmentVariable("NOTCH_SW") == "1")
            {
                var src = HwndSource.FromHwnd(hwnd);
                if (src != null && src.CompositionTarget != null) src.CompositionTarget.RenderMode = RenderMode.SoftwareOnly;
            }
            UpdateScale();
        }

        void UpdateScale()
        {
            try { if (hwnd != IntPtr.Zero) scale = Native.GetDpiForWindow(hwnd) / 96.0; } catch { }
            if (scale <= 0) scale = 1;
        }

        // ---------------- geometry (physical px) ----------------
        static RECT Work() { return WorkArea(); }
        static RECT WorkArea()
        {
            var mon = Native.MonitorFromPoint(new POINT(), 1);
            var mi = new MONITORINFO(); mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            Native.GetMonitorInfo(mon, ref mi);
            return mi.rcWork;
        }
        double Ground() { return Work().Bottom - (H - Size * 0.06) * scale; }
        double CenterX() { return x + W / 2 * scale; }
        double CenterY() { return y + (H - Size * 0.5) * scale; }
        void Move() { Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, (int)x, (int)y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE); UpdateToys(); }

        // ---------------- lifecycle ----------------
        /// <summary>Pulled out of the notch: appears under the cursor, already being carried.</summary>
        public void LaunchCarried()
        {
            EnsureShown();
            homeScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            homeScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            homeScale.ScaleX = homeScale.ScaleY = 1;
            BeginCarry(W / 2 * scale, (H - Size * 0.55) * scale, false);
            notchImmuneUntil = clock.Elapsed.TotalSeconds + 0.5;
            OnTick(null, null);
            Say("wheee!");
        }

        /// <summary>Hop out of the notch on its own (tray menu / button) and fall onto the taskbar.</summary>
        public void DropFromNotch()
        {
            EnsureShown();
            homeScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            homeScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            homeScale.ScaleX = homeScale.ScaleY = 1;
            var t = HomeTarget();
            x = t.X; y = t.Y + 30 * scale;
            vx = (rnd.NextDouble() < 0.5 ? -1 : 1) * (500 + rnd.Next(700)) * scale;
            vy = -700 * scale;
            Move();
            Mascot.SetMood(Mood.Excited);
            notchImmuneUntil = clock.Elapsed.TotalSeconds + 0.8;
            state = State.Flying;
            lastT = clock.Elapsed.TotalSeconds;
            tick.Interval = TimeSpan.FromMilliseconds(16);
            tick.Start();
            Say("wheee!");
        }

        /// <summary>Restore on the taskbar at a remembered spot (app restart).</summary>
        public void SpawnGrounded(double fracX)
        {
            EnsureShown();
            var w = Work();
            x = w.Left + Math.Max(0, Math.Min(1, fracX)) * (w.Right - w.Left - W * scale);
            y = Ground();
            Move();
            Land(0);
        }

        void EnsureShown()
        {
            if (!IsVisible) Show();
            UpdateScale();
            Mascot.Active = !suppressed;
            eyes.Start();
        }

        public double SavedFraction
        {
            get { var w = Work(); double span = Math.Max(1, w.Right - w.Left - W * scale); return (x - w.Left) / span; }
        }

        public void Suppress(bool hide)
        {
            if (suppressed == hide) return;
            suppressed = hide;
            if (!IsOut) return;
            if (hide) { AbortPlay(); Hide(); Mascot.Active = false; tick.Stop(); ai.Stop(); eyes.Stop(); }
            else { Show(); Move(); Mascot.Active = true; eyes.Start(); if (state == State.Grounded) Schedule(800); else tick.Start(); }
        }

        /// <summary>Notch context (music, focus timer, low battery) the buddy should go along with.</summary>
        public void SetContext(Mood m)
        {
            if (context == m) return;
            context = m;
            if (!IsOut || playing) return;
            if (state == State.Grounded || state == State.Walking)
            {
                StopWalking();
                Mascot.SetMood(m == Mood.Idle ? Mood.Idle : m);
                state = State.Grounded;
                Schedule(600);
            }
        }

        public void React(Action<Mascot> a)
        {
            if (!IsOut || suppressed) return;
            if (Mascot.CurrentMood == Mood.Sleepy) Mascot.SetMood(context);
            a(Mascot);
        }

        void Say(string s) { var h = Said; if (h != null) h(s); }

        // ---------------- carry / throw ----------------
        void BeginCarry(double offX, double offY, bool byClick)
        {
            ai.Stop();
            AbortPlay();
            StopWalking();
            if (swinging) { Mascot.SwingLegs(false); swinging = false; }
            state = State.Carried;
            grabX = offX; grabY = offY;
            carriedByClick = byClick;
            carryStartT = clock.Elapsed.TotalSeconds;
            carryDist = 0;
            trail.Clear();
            Mascot.SetMood(Mood.Excited);
            Mascot.SwingLegs(true);
            lastT = clock.Elapsed.TotalSeconds;
            tick.Interval = TimeSpan.FromMilliseconds(16);
            tick.Start();
        }

        static bool LeftButtonDown()
        {
            int vk = GetSystemMetrics(23) != 0 ? 0x02 : 0x01; // respect swapped buttons
            return (GetAsyncKeyState(vk) & 0x8000) != 0;
        }
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);

        void Release()
        {
            if (state != State.Carried) return;
            Mascot.ReleaseMouseCapture();
            Mascot.SwingLegs(false);
            double now = clock.Elapsed.TotalSeconds;
            bool click = carriedByClick && carryDist < 6 * scale && now - carryStartT < 0.35;
            POINT p; Native.GetCursorPos(out p);
            bool justLeft = clock.Elapsed.TotalSeconds < notchImmuneUntil;
            if (!justLeft && (InNotch(p.X, p.Y) || InNotch(CenterX(), CenterY()))) { GoHome(); return; }
            if (click)
            {
                state = State.Grounded;
                Mascot.SetMood(context);
                Mascot.Pet();
                Say("hehe ♥");
                tick.Stop();
                if (y < Ground() - 2) { state = State.Flying; vx = vy = 0; tick.Start(); }
                else Schedule(1500);
                return;
            }
            // velocity from the last ~90 ms of movement
            vx = vy = 0;
            for (int i = trail.Count - 1; i >= 0; i--)
            {
                if (now - trail[i].T > 0.09 || i == 0)
                {
                    double dt = Math.Max(0.016, now - trail[i].T);
                    vx = (x - trail[i].X) / dt; vy = (y - trail[i].Y) / dt;
                    break;
                }
            }
            double max = 4200 * scale;
            vx = Math.Max(-max, Math.Min(max, vx)); vy = Math.Max(-max, Math.Min(max, vy));
            state = State.Flying;
            lastT = now;
            if (Math.Abs(vx) + Math.Abs(vy) > 1400 * scale) Say("weeee!");
        }

        bool InNotch(double px, double py)
        {
            if (NotchZone == null) return false;
            var r = NotchZone();
            r.Inflate(50 * scale, 40 * scale);
            return r.Contains(px, py);
        }

        // ---------------- the one tick loop (only while moving) ----------------
        void OnTick(object sender, EventArgs e)
        {
            double now = clock.Elapsed.TotalSeconds;
            double dt = Math.Min(0.05, Math.Max(0.001, now - lastT));
            lastT = now;
            switch (state)
            {
                case State.Carried:
                    if (!LeftButtonDown()) { Release(); return; }
                    POINT p; Native.GetCursorPos(out p);
                    double nx = p.X - grabX, ny = p.Y - grabY;
                    carryDist += Math.Abs(nx - x) + Math.Abs(ny - y);
                    double sway = (nx - x) / Math.Max(dt, 0.001) / scale * 0.012;
                    x = nx; y = ny;
                    Move();
                    Mascot.SetSway(sway);
                    trail.Add(new Sample { T = now, X = x, Y = y });
                    if (trail.Count > 12) trail.RemoveAt(0);
                    break;
                case State.Flying:
                    Fly(dt);
                    break;
                case State.Walking:
                    double dir = Math.Sign(walkTarget - x);
                    x += dir * walkSpeed * dt;
                    if ((dir > 0 && x >= walkTarget) || (dir < 0 && x <= walkTarget) || dir == 0)
                    {
                        x = walkTarget;
                        StopWalking();
                        state = State.Grounded;
                        tick.Stop();
                        if (playing) ArrivedWhilePlaying(); else Schedule(1500 + rnd.Next(2500));
                    }
                    Move();
                    break;
                case State.GoingHome:
                    double t = Math.Min(1, (now - carryStartT) / 0.45);
                    double e2 = t * t;
                    var target = HomeTarget();
                    x = homeFrom.X + (target.X - homeFrom.X) * e2;
                    y = homeFrom.Y + (target.Y - homeFrom.Y) * e2 - Math.Sin(t * Math.PI) * 60 * scale;
                    Move();
                    if (t >= 1) FinishHome();
                    break;
                default:
                    tick.Stop();
                    break;
            }
        }

        void Fly(double dt)
        {
            var w = Work();
            double g = 3200 * scale;
            vy += g * dt;
            vx *= Math.Max(0, 1 - 0.5 * dt);
            x += vx * dt; y += vy * dt;
            double minX = w.Left - W * scale * 0.25, maxX = w.Right - W * scale * 0.75;
            if (x < minX) { x = minX; vx = Math.Abs(vx) * 0.5; Mascot.Wiggle(); }
            if (x > maxX) { x = maxX; vx = -Math.Abs(vx) * 0.5; Mascot.Wiggle(); }
            if (vy < 0 && clock.Elapsed.TotalSeconds > notchImmuneUntil && InNotch(CenterX(), CenterY())) { GoHome(); return; }
            double top = Native.PrimaryMonitor().Top - (H - Size) * scale;
            if (y < top) { y = top; vy = Math.Abs(vy) * 0.3; }
            Mascot.SetSway(vx / scale * 0.012);
            double ground = Ground();
            if (y >= ground)
            {
                y = ground;
                double impact = vy / scale;
                if (impact > 900)
                {
                    vy = -vy * 0.32;
                    Mascot.Land(Math.Min(1, impact / 2500));
                }
                else
                {
                    Move();
                    Land(Math.Min(1, impact / 900));
                    return;
                }
            }
            Move();
        }

        void Land(double strength)
        {
            state = State.Grounded;
            vx = vy = 0;
            tick.Stop();
            Mascot.SetSway(0);
            Mascot.Land(Math.Max(0.3, strength));
            Mascot.SetMood(context);
            if (strength > 0.8) { Mascot.Flash(Mood.Worried, 900); Say("oof!"); }
            else Mascot.Flash(Mood.Happy, 900);
            Schedule(1400);
        }

        // ---------------- going home ----------------
        Point HomeTarget()
        {
            var r = NotchZone != null ? NotchZone() : new Rect(0, 0, 0, 0);
            return new Point(r.X + r.Width / 2 - W / 2 * scale, r.Y - (H - Size * 0.7) * scale);
        }

        public void GoHome()
        {
            if (!IsOut || state == State.GoingHome) return;
            ai.Stop();
            AbortPlay();
            StopWalking();
            Mascot.SwingLegs(false);
            Mascot.ReleaseMouseCapture();
            state = State.GoingHome;
            homeFrom = new Point(x, y);
            carryStartT = clock.Elapsed.TotalSeconds;
            Mascot.SetMood(Mood.Excited);
            var shrink = new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(450)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
            homeScale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
            homeScale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
            lastT = carryStartT;
            tick.Interval = TimeSpan.FromMilliseconds(16);
            tick.Start();
        }

        void FinishHome()
        {
            tick.Stop(); ai.Stop(); eyes.Stop();
            state = State.Hidden;
            Mascot.Active = false;
            Hide();
            var h = WentHome; if (h != null) h();
        }

        // ---------------- little life on the taskbar ----------------
        void Schedule(int ms)
        {
            ai.Stop();
            ai.Interval = TimeSpan.FromMilliseconds(ms);
            if (!suppressed) ai.Start();
        }

        void StopWalking()
        {
            Mascot.SetWalking(false);
            if (state == State.Walking) state = State.Grounded;
        }

        void WalkTo(double target, double speed)
        {
            var w = Work();
            target = Math.Max(w.Left - W * scale * 0.2, Math.Min(w.Right - W * scale * 0.8, target));
            if (Math.Abs(target - x) < 10 * scale) { if (!playing) Schedule(1500); return; }
            walkTarget = target; walkSpeed = speed * scale;
            FaceDir(target > x ? 1 : -1);
            Mascot.SetWalking(!skating);
            state = State.Walking;
            lastT = clock.Elapsed.TotalSeconds;
            tick.Interval = TimeSpan.FromMilliseconds(33); // 30 fps is plenty for strolling
            tick.Start();
        }

        void NextActivity()
        {
            if (state != State.Grounded || suppressed) return;
            tick.Interval = TimeSpan.FromMilliseconds(16);
            if (swinging) { Mascot.SwingLegs(false); swinging = false; }
            if (Mascot.CurrentMood == Mood.Sleepy) Mascot.SetMood(context);

            POINT c; Native.GetCursorPos(out c);
            double dist = Math.Abs(c.X - CenterX());
            bool cursorNear = dist < 420 * scale && Math.Abs(c.Y - CenterY()) < 300 * scale;
            bool music = MusicPlaying != null && MusicPlaying();

            if (music && context == Mood.Vibing)
            {
                // dance most of the time, sometimes shuffle along to the beat
                if (rnd.NextDouble() < 0.15) { RunPlay(6); return; }
                if (rnd.NextDouble() < 0.7) { Mascot.SetMood(Mood.Vibing); Schedule(5000 + rnd.Next(4000)); }
                else WalkTo(x + rnd.Next(-160, 160) * scale, 45);
                return;
            }
            if (context == Mood.Focus)
            {
                Mascot.SetMood(Mood.Focus);
                if (rnd.NextDouble() < 0.3) Mascot.Nod();
                Schedule(6000 + rnd.Next(5000));
                return;
            }

            if (rnd.NextDouble() < 0.55)
            {
                int pick;
                do { pick = rnd.Next(plays.Length); } while (pick == lastPlay);
                RunPlay(pick);
                return;
            }
            double r = rnd.NextDouble();
            if (cursorNear && r < 0.25) { FaceDir(c.X > CenterX() ? 1 : -1); Mascot.Hello(); Say("hi!"); Schedule(2500); }
            else if (cursorNear && r < 0.4) WalkTo(c.X - W / 2 * scale, 110);
            else if (r < 0.62) WalkTo(x + (rnd.NextDouble() < 0.5 ? -1 : 1) * (80 + rnd.Next(260)) * scale, 55);
            else if (r < 0.70) { Mascot.PlayBall(); Schedule(3600); }
            else if (r < 0.77) { Mascot.SwingLegs(true); swinging = true; Mascot.Flash(Mood.Happy, 4000); Schedule(4500); }
            else if (r < 0.82) { Mascot.Yawn(); Schedule(2500); }
            else if (r < 0.87) { Mascot.Jump(); Mascot.Wiggle(); Schedule(2000); }
            else if (r < 0.94) { Mascot.SetMood(Mood.Sleepy); Say("zzz…"); Schedule(15000 + rnd.Next(15000)); }
            else { FaceDir(rnd.NextDouble() < 0.5 ? 1 : -1); Schedule(3000 + rnd.Next(4000)); }
        }

        ContextMenu BuildMenu()
        {
            var menu = new ContextMenu();
            var panel = new FrameworkElementFactory(typeof(System.Windows.Controls.Primitives.UniformGrid));
            panel.SetValue(System.Windows.Controls.Primitives.UniformGrid.ColumnsProperty, 2);
            menu.ItemsPanel = new ItemsPanelTemplate(panel);
            for (int i = 0; i < PlayNames.Length; i++)
            {
                int idx = i;
                var mi = new MenuItem { Header = PlayNames[i] };
                mi.Click += (s, e) => StartPlay(idx);
                menu.Items.Add(mi);
            }
            var nap = new MenuItem { Header = "Nap time" };
            nap.Click += (s, e) => { AbortPlay(); if (state == State.Walking) StopWalking(); if (state == State.Grounded) { Mascot.SetMood(Mood.Sleepy); Schedule(25000); } };
            var hi = new MenuItem { Header = "Say hi" };
            hi.Click += (s, e) => Mascot.Hello();
            var home = new MenuItem { Header = "Go home" };
            home.Click += (s, e) => GoHome();
            menu.Items.Add(nap); menu.Items.Add(hi); menu.Items.Add(home);
            return menu;
        }

        void TrackCursor()
        {
            if (!IsOut || suppressed || playing || state == State.Carried || state == State.GoingHome) return;
            if (Mascot.ShownMood == Mood.Sleepy) return;
            POINT c; Native.GetCursorPos(out c);
            double dx = (c.X - CenterX()) / (300 * scale), dy = (c.Y - CenterY()) / (300 * scale);
            if (Math.Abs(dx) > 2.5 || Math.Abs(dy) > 2.5) { dx = 0; dy = 0; }
            if (Math.Abs(dx - lastLookX) + Math.Abs(dy - lastLookY) > 0.06)
            {
                lastLookX = dx; lastLookY = dy;
                Mascot.LookAt(dx, dy);
            }
            // hover = hello (with a cooldown)
            bool over = Math.Abs(c.X - CenterX()) < Size * 0.45 * scale && Math.Abs(c.Y - CenterY()) < Size * 0.45 * scale;
            if (over && (DateTime.Now - lastHello).TotalSeconds > 6 && state == State.Grounded)
            {
                lastHello = DateTime.Now;
                Mascot.Hello();
            }
        }
    }
}
