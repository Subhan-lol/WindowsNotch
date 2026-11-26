using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace NotchIsland
{
    public partial class Mascot
    {
        const double ArmRestL = 18, ArmRestR = -18;
        DispatcherTimer ambient, flashTimer, handTimer, headTimer;
        readonly Random rnd = new Random(Environment.TickCount ^ 0x5eed);
        Mood baseMood = Mood.Idle, shown = Mood.Excited;
        Prop headContext = Prop.None, handContext = Prop.None, handFlash = Prop.None, headFlash = Prop.None;
        bool active, beatApplied, walking;
        Color accent = Color.FromRgb(0xB8, 0xA6, 0xFF);

        /// <summary>Floating notes/hearts/z's. Only where there's room (expanded notch, taskbar).</summary>
        public bool Particles;

        public Color Accent
        {
            get { return accent; }
            set { accent = value; cupBrush.Color = value; }
        }

        void InitBehavior()
        {
            ambient = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(3) };
            ambient.Tick += OnAmbient;
            flashTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            flashTimer.Tick += (s, e) => { flashTimer.Stop(); Show(baseMood); };
            handTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            handTimer.Tick += (s, e) => { handTimer.Stop(); handFlash = Prop.None; ApplyProps(); };
            headTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            headTimer.Tick += (s, e) => { headTimer.Stop(); headFlash = Prop.None; ApplyProps(); };
            Show(Mood.Idle);
        }

        // =============== state ===============
        public bool Active
        {
            get { return active; }
            set
            {
                if (active == value) return;
                active = value;
                if (active) ambient.Start(); else { ambient.Stop(); fx.Children.Clear(); }
                UpdateBreathing();
            }
        }

        public Mood CurrentMood { get { return baseMood; } }
        public Mood ShownMood { get { return shown; } }

        public void SetMood(Mood m)
        {
            if (baseMood == m) return;
            var old = baseMood;
            baseMood = m;
            if (!flashTimer.IsEnabled) Show(m);
            if (old == Mood.Sleepy && m != Mood.Sleepy && active) Jump();
        }

        public void Flash(Mood m, int ms)
        {
            Show(m);
            flashTimer.Stop();
            if (ms > 0) { flashTimer.Interval = TimeSpan.FromMilliseconds(ms); flashTimer.Start(); }
        }

        public void EndFlash() { flashTimer.Stop(); Show(baseMood); }

        /// <summary>Hat/glasses that go with what you're doing (tab, timer...). None = follow the mood.</summary>
        public void SetHeadContext(Prop p) { headContext = p; ApplyProps(); }
        /// <summary>Something to hold that goes with what you're doing.</summary>
        public void SetHandContext(Prop p) { handContext = p; ApplyProps(); }

        public void FlashHand(Prop p, int ms)
        {
            handFlash = p; handTimer.Stop();
            if (ms > 0) { handTimer.Interval = TimeSpan.FromMilliseconds(ms); handTimer.Start(); }
            ApplyProps();
        }
        public void FlashHead(Prop p, int ms)
        {
            headFlash = p; headTimer.Stop();
            if (ms > 0) { headTimer.Interval = TimeSpan.FromMilliseconds(ms); headTimer.Start(); }
            ApplyProps();
        }

        void Show(Mood m)
        {
            if (m == shown) { ApplyProps(); return; }
            shown = m;
            bool open = m == Mood.Idle || m == Mood.Hungry || m == Mood.Focus || m == Mood.Worried || m == Mood.Excited;
            eyes.Opacity = open ? 1 : 0;
            happyEyes.Opacity = (m == Mood.Happy || m == Mood.Vibing) ? 1 : 0;
            sleepEyes.Opacity = m == Mood.Sleepy ? 1 : 0;
            browFocus.Opacity = m == Mood.Focus ? 1 : 0;
            browWorried.Opacity = m == Mood.Worried ? 1 : 0;
            smile.Opacity = (m == Mood.Idle || m == Mood.Vibing || m == Mood.Happy) ? 1 : 0;
            grin.Opacity = m == Mood.Excited ? 1 : 0;
            flatMouth.Opacity = (m == Mood.Focus || m == Mood.Worried) ? 1 : 0;
            mouthOpen.Opacity = (m == Mood.Hungry || m == Mood.Sleepy) ? 1 : 0;
            if (m == Mood.Sleepy) { mouthScale.ScaleX = 0.5; mouthScale.ScaleY = 0.4; } else { mouthScale.ScaleX = 1; mouthScale.ScaleY = 1; }
            cheeks.Opacity = (m == Mood.Happy || m == Mood.Vibing || m == Mood.Excited) ? 0.85 : 0.5;
            double sx = 1, sy = 1;
            if (m == Mood.Hungry || m == Mood.Excited) { sx = 1.12; sy = 1.12; }
            else if (m == Mood.Focus) sy = 0.82;
            Anim.To(eyeScale, ScaleTransform.ScaleXProperty, sx, 160, Anim.OutBack);
            Anim.To(eyeScale, ScaleTransform.ScaleYProperty, sy, 160, Anim.OutBack);
            // ears: perk up when excited/hungry, droop when sleepy/worried
            double ear = (m == Mood.Sleepy || m == Mood.Worried) ? 14 : (m == Mood.Hungry || m == Mood.Excited) ? -6 : 0;
            Anim.To(earL, RotateTransform.AngleProperty, -ear, 220, Anim.OutBack);
            Anim.To(earR, RotateTransform.AngleProperty, ear, 220, Anim.OutBack);
            if (m != Mood.Vibing && beatApplied) ResetBeat();
            if (m == Mood.Worried && Particles) Sweat();
            if (m == Mood.Hungry) ArmsUp(true); else if (!walking) ArmsUp(false);
            ApplyProps();
        }

        void ApplyProps()
        {
            Prop head = headFlash != Prop.None ? headFlash : headContext;
            if (head == Prop.None)
            {
                if (shown == Mood.Vibing) head = Prop.Headphones;
                else if (shown == Mood.Sleepy) head = Prop.Nightcap;
                else if (shown == Mood.Focus) head = Prop.Headband;
            }
            if (shown == Mood.Sleepy && headFlash == Prop.None) head = Prop.Nightcap;
            Prop hand = handFlash != Prop.None ? handFlash : handContext;
            if (shown == Mood.Sleepy) hand = Prop.None;
            foreach (var kv in props)
            {
                bool on = kv.Key == head || kv.Key == hand;
                kv.Value.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            }
            if (walking || shown == Mood.Hungry) return;
            double l = ArmRestL, r = ArmRestR;
            if (TwoHanded(hand)) { l = -42; r = 42; }
            else if (hand == Prop.Ukulele) { l = -25; r = 50; }
            else if (hand != Prop.None) r = 48;
            Anim.To(armL, RotateTransform.AngleProperty, l, 220, Anim.OutBack);
            Anim.To(armR, RotateTransform.AngleProperty, r, 220, Anim.OutBack);
        }

        void ArmsUp(bool up)
        {
            Anim.To(armL, RotateTransform.AngleProperty, up ? 150 : ArmRestL, 220, Anim.OutBack);
            Anim.To(armR, RotateTransform.AngleProperty, up ? -150 : ArmRestR, 220, Anim.OutBack);
        }

        // =============== looking / music / walking ===============
        public void LookAt(double dx, double dy)
        {
            dx = Math.Max(-1, Math.Min(1, dx)); dy = Math.Max(-1, Math.Min(1, dy));
            if (flip.ScaleX < 0) dx = -dx;
            eyeLook.X = dx * 3.4; eyeLook.Y = dy * 2.6;
            faceLook.X = dx * 1.6; faceLook.Y = dy * 1.0;
        }

        // dance + fidgets live in Mascot.Moves.cs

        /// <summary>Face left (-1) or right (+1). A quick squish makes it look like a turn.</summary>
        public void Face(int dir)
        {
            double target = dir < 0 ? -1 : 1;
            if (Math.Abs(flip.ScaleX - target) < 0.01) return;
            Anim.To(flip, ScaleTransform.ScaleXProperty, target, 160, Anim.OutCubic);
        }

        public void SetWalking(bool on)
        {
            if (walking == on) return;
            walking = on;
            if (on)
            {
                footL.BeginAnimation(TranslateTransform.YProperty, Loop(0, -4, 150, 0));
                footR.BeginAnimation(TranslateTransform.YProperty, Loop(0, -4, 150, 150));
                bob.BeginAnimation(TranslateTransform.YProperty, Loop(0, -2, 150, 0));
                armL.BeginAnimation(RotateTransform.AngleProperty, Loop(ArmRestL - 12, ArmRestL + 14, 300, 0));
                armR.BeginAnimation(RotateTransform.AngleProperty, Loop(ArmRestR - 14, ArmRestR + 12, 300, 0));
                tailRot.BeginAnimation(RotateTransform.AngleProperty, Loop(-6, 8, 300, 0));
            }
            else
            {
                foreach (var t in new Animatable[] { footL, footR, bob }) t.BeginAnimation(TranslateTransform.YProperty, null);
                armL.BeginAnimation(RotateTransform.AngleProperty, null);
                armR.BeginAnimation(RotateTransform.AngleProperty, null);
                tailRot.BeginAnimation(RotateTransform.AngleProperty, null);
                ApplyProps();
            }
        }

        /// <summary>Little legs swinging, like sitting on a ledge.</summary>
        public void SwingLegs(bool on)
        {
            if (on)
            {
                footL.BeginAnimation(TranslateTransform.XProperty, Loop(-3, 3, 420, 0));
                footR.BeginAnimation(TranslateTransform.XProperty, Loop(3, -3, 420, 0));
            }
            else
            {
                footL.BeginAnimation(TranslateTransform.XProperty, null);
                footR.BeginAnimation(TranslateTransform.XProperty, null);
            }
        }

        /// <summary>Lean with movement (being carried / thrown). Degrees.</summary>
        public void SetSway(double deg) { tilt.Angle = Math.Max(-35, Math.Min(35, deg)); }

        static DoubleAnimation Loop(double from, double to, double ms, double delay)
        {
            var a = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = Anim.InOutSine, BeginTime = TimeSpan.FromMilliseconds(delay) };
            Timeline.SetDesiredFrameRate(a, 30);
            return a;
        }

        static DoubleAnimation Pulse(double from, double to, double ms, double repeat, IEasingFunction ease)
        {
            var a = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
            { AutoReverse = true, RepeatBehavior = new RepeatBehavior(repeat), FillBehavior = FillBehavior.Stop, EasingFunction = ease };
            return a;
        }

        static DoubleAnimationUsingKeyFrames Keys(double stepMs, params double[] vals)
        {
            var k = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            for (int i = 0; i < vals.Length; i++)
                k.KeyFrames.Add(new EasingDoubleKeyFrame(vals[i], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(stepMs * (i + 1)))) { EasingFunction = Anim.InOutSine });
            return k;
        }

        // =============== reactions ===============
        public void Blink()
        {
            blink.BeginAnimation(ScaleTransform.ScaleYProperty, Pulse(1, 0.08, 70, rnd.NextDouble() < 0.22 ? 2 : 1, null));
        }

        public void Wave()
        {
            armR.BeginAnimation(RotateTransform.AngleProperty, Keys(110, -140, -105, -145, -105, -140, armR.Angle));
        }

        public void Hello()
        {
            if (shown == Mood.Sleepy) { Startle(); return; }
            if (baseMood == Mood.Hungry) return;
            if (baseMood != Mood.Vibing) Flash(Mood.Happy, 1100);
            Wave();
            bob.BeginAnimation(TranslateTransform.YProperty, Pulse(0, -5, 140, 1, Anim.OutCubic));
        }

        public void Startle()
        {
            Jump();
            Flash(Mood.Excited, 900);
            if (Particles) Spawn(Glyph("!", Color.FromRgb(0xFF, 0xE2, 0x7A), 18), 70, 18, 4, -10, 900, 0);
        }

        public void Pet()
        {
            squash.BeginAnimation(ScaleTransform.ScaleYProperty, Pulse(1, 0.82, 150, 1, Anim.OutCubic));
            squash.BeginAnimation(ScaleTransform.ScaleXProperty, Pulse(1, 1.12, 150, 1, Anim.OutCubic));
            earL.BeginAnimation(RotateTransform.AngleProperty, Pulse(0, 16, 160, 1, Anim.OutCubic));
            earR.BeginAnimation(RotateTransform.AngleProperty, Pulse(0, -16, 160, 1, Anim.OutCubic));
            Flash(Mood.Happy, 1700);
            if (Particles) Hearts(3);
        }

        /// <summary>Hugs the file, then nom nom nom.</summary>
        public void Chomp()
        {
            FlashHand(Prop.Paper, 520);
            Show(Mood.Hungry);
            var a = Pulse(1, 0.06, 95, 3, Anim.InOutSine);
            a.BeginTime = TimeSpan.FromMilliseconds(420);
            a.Completed += (s, e) => { Flash(Mood.Happy, 1600); if (Particles) { Sparkle(); Hearts(2); } };
            mouthScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            var sq = Pulse(1, 0.9, 95, 3, Anim.InOutSine); sq.BeginTime = TimeSpan.FromMilliseconds(420);
            var sx = Pulse(1, 1.06, 95, 3, Anim.InOutSine); sx.BeginTime = TimeSpan.FromMilliseconds(420);
            squash.BeginAnimation(ScaleTransform.ScaleYProperty, sq);
            squash.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
        }

        /// <summary>"Here you go!" — offers a file.</summary>
        public void Give()
        {
            FlashHand(Prop.Paper, 900);
            Flash(Mood.Excited, 900);
            armR.BeginAnimation(RotateTransform.AngleProperty, Keys(120, 60, 75, 60, 42));
        }

        public void Jump()
        {
            bob.BeginAnimation(TranslateTransform.YProperty, Pulse(0, -14, 210, 1, Anim.OutCubic));
            var land = Pulse(1, 0.86, 90, 1, null); land.BeginTime = TimeSpan.FromMilliseconds(420);
            squash.BeginAnimation(ScaleTransform.ScaleYProperty, land);
        }

        public void Land(double strength)
        {
            var sq = Pulse(1, Math.Max(0.65, 1 - 0.25 * strength), 110, 1, Anim.OutCubic);
            var sx = Pulse(1, 1 + 0.18 * strength, 110, 1, Anim.OutCubic);
            squash.BeginAnimation(ScaleTransform.ScaleYProperty, sq);
            squash.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
        }

        public void Celebrate()
        {
            FlashHead(Prop.PartyHat, 3200);
            Flash(Mood.Excited, 3000);
            bob.BeginAnimation(TranslateTransform.YProperty, Pulse(0, -12, 200, 3, Anim.OutCubic));
            armL.BeginAnimation(RotateTransform.AngleProperty, Keys(150, 150, 120, 150, 120, 150, ArmRestL));
            armR.BeginAnimation(RotateTransform.AngleProperty, Keys(150, -150, -120, -150, -120, -150, ArmRestR));
            if (Particles) { Confetti(); Hearts(2); }
        }

        public void Nod()
        {
            bob.BeginAnimation(TranslateTransform.YProperty, Pulse(0, 3, 90, 2, Anim.InOutSine));
            if (shown != Mood.Vibing && shown != Mood.Focus) Flash(Mood.Happy, 700);
        }

        public void Wiggle()
        {
            tilt.BeginAnimation(RotateTransform.AngleProperty, Keys(90, -11, 10, -7, 4, 0));
        }

        /// <summary>A full twirl (next song, shuffle...).</summary>
        public void Spin()
        {
            var k = Keys(90, 0.2, -1, 0.2, 1);
            flip.BeginAnimation(ScaleTransform.ScaleXProperty, k);
            if (Particles) Sparkle();
        }

        public void CoverEars(int ms)
        {
            Flash(Mood.Worried, ms);
            var l = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop, Duration = TimeSpan.FromMilliseconds(ms) };
            l.KeyFrames.Add(new EasingDoubleKeyFrame(160, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160))));
            l.KeyFrames.Add(new EasingDoubleKeyFrame(160, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms - 150))));
            l.KeyFrames.Add(new EasingDoubleKeyFrame(ArmRestL, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
            var r = l.Clone(); for (int i = 0; i < 2; i++) r.KeyFrames[i].Value = -160; r.KeyFrames[2].Value = ArmRestR;
            armL.BeginAnimation(RotateTransform.AngleProperty, l);
            armR.BeginAnimation(RotateTransform.AngleProperty, r);
        }

        public void Shh(int ms)
        {
            Flash(Mood.Focus, ms);
            armR.BeginAnimation(RotateTransform.AngleProperty, Keys(ms / 4.0, 75, 75, 75, ArmRestR));
            if (Particles) Spawn(Glyph("shh", Color.FromRgb(0xCF, 0xC8, 0xFF), 10), 66, 50, 8, -14, 1200, 0);
        }

        /// <summary>Scribbles with the pencil (typing notes).</summary>
        public void Scribble()
        {
            if (armR.HasAnimatedProperties) return;
            armR.BeginAnimation(RotateTransform.AngleProperty, Keys(70, 56, 40, 58, 42, 48));
        }

        /// <summary>Click! (screenshots)</summary>
        public void Snap()
        {
            FlashHand(Prop.Camera, 1300);
            Flash(Mood.Excited, 1300);
            if (!Particles) return;
            var flash = new Ellipse { Width = 30, Height = 30, Fill = new RadialGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255)) };
            Spawn(flash, 52, 55, 0, 0, 500, 250);
        }

        public void Yawn()
        {
            Flash(Mood.Sleepy, 1500);
            mouthScale.BeginAnimation(ScaleTransform.ScaleYProperty, Keys(250, 1.6, 2.2, 2.2, 1.2, 1));
            mouthScale.BeginAnimation(ScaleTransform.ScaleXProperty, Keys(250, 1.3, 1.5, 1.5, 1.1, 1));
            armL.BeginAnimation(RotateTransform.AngleProperty, Keys(250, 120, 165, 165, 80, ArmRestL));
            armR.BeginAnimation(RotateTransform.AngleProperty, Keys(250, -120, -165, -165, -80, ArmRestR));
        }

        public void Bolt()
        {
            Flash(Mood.Excited, 1400);
            Jump();
            if (Particles) Spawn(Glyph("⚡", Color.FromRgb(0x7D, 0xF0, 0x9A), 20), 72, 14, 4, -14, 1500, 0);
        }

        /// <summary>Bounces a ball on its head a few times.</summary>
        public void PlayBall()
        {
            if (!Particles) return;
            var ball = new Canvas { Width = 14, Height = 14 };
            ball.Children.Add(new Ellipse { Width = 14, Height = 14, Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x8F, 0x6B)) });
            ball.Children.Add(new Path { Data = Geometry.Parse("M0,7 Q7,3 14,7 M7,0 Q4,7 7,14"), Stroke = Brushes.White, StrokeThickness = 1.4 });
            ball.IsHitTestVisible = false;
            var tt = new TranslateTransform(43, 14);
            var rot = new RotateTransform(0, 7, 7);
            var g = new TransformGroup(); g.Children.Add(rot); g.Children.Add(tt);
            ball.RenderTransform = g;
            fx.Children.Add(ball);
            var y = new DoubleAnimationUsingKeyFrames();
            for (int i = 0; i < 4; i++)
            {
                y.KeyFrames.Add(new EasingDoubleKeyFrame(-14, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(i * 560 + 280))) { EasingFunction = Anim.OutCubic });
                y.KeyFrames.Add(new EasingDoubleKeyFrame(14, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(i * 560 + 560))) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
            }
            y.KeyFrames.Add(new EasingDoubleKeyFrame(80, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(4 * 560 + 400))));
            Timeline.SetDesiredFrameRate(y, 30);
            y.Completed += (s, e) => fx.Children.Remove(ball);
            tt.BeginAnimation(TranslateTransform.YProperty, y);
            var x = new DoubleAnimation(43, 75, TimeSpan.FromMilliseconds(4 * 560 + 400)) { BeginTime = TimeSpan.FromMilliseconds(4 * 560 - 100) };
            tt.BeginAnimation(TranslateTransform.XProperty, x);
            rot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 720, TimeSpan.FromMilliseconds(2640)));
            bob.BeginAnimation(TranslateTransform.YProperty, Pulse(0, 3, 90, 4, null) );
            Flash(Mood.Happy, 2600);
        }

        // =============== ambient life ===============
        // OnAmbient lives in Mascot.Moves.cs

        // =============== particles ===============
        static TextBlock Glyph(string s, Color c, double size)
        {
            return new TextBlock { Text = s, FontFamily = new FontFamily("Segoe UI Symbol"), FontWeight = FontWeights.Bold, FontSize = size, Foreground = new SolidColorBrush(c) };
        }

        void Spawn(UIElement el, double x, double y, double dx, double dy, double ms, double delay)
        {
            var tt = new TranslateTransform(x, y);
            var st = new ScaleTransform(0.4, 0.4);
            var g = new TransformGroup(); g.Children.Add(st); g.Children.Add(tt);
            el.RenderTransform = g; el.Opacity = 0; el.IsHitTestVisible = false;
            fx.Children.Add(el);
            var begin = TimeSpan.FromMilliseconds(delay);
            var dur = TimeSpan.FromMilliseconds(ms);
            var mx = new DoubleAnimation(x, x + dx, dur) { BeginTime = begin, EasingFunction = Anim.OutCubic };
            var my = new DoubleAnimation(y, y + dy, dur) { BeginTime = begin, EasingFunction = Anim.OutCubic };
            var sc = new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(ms * 0.35)) { BeginTime = begin, EasingFunction = Anim.OutBack };
            var op = new DoubleAnimationUsingKeyFrames { BeginTime = begin };
            op.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms * 0.15))));
            op.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms * 0.6))));
            op.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(dur)));
            foreach (var t in new Timeline[] { mx, my, sc, op }) Timeline.SetDesiredFrameRate(t, 30);
            op.Completed += (s, e) => fx.Children.Remove(el);
            tt.BeginAnimation(TranslateTransform.XProperty, mx);
            tt.BeginAnimation(TranslateTransform.YProperty, my);
            st.BeginAnimation(ScaleTransform.ScaleXProperty, sc);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, sc);
            el.BeginAnimation(UIElement.OpacityProperty, op);
        }

        void Note()
        {
            bool right = rnd.NextDouble() < 0.5;
            Spawn(Glyph(rnd.NextDouble() < 0.5 ? "♪" : "♫", accent, 17), right ? 80 : 4, 24, right ? 14 : -12, -32, 1800, 0);
        }
        void Zzz()
        {
            var c = Color.FromRgb(0xCF, 0xC8, 0xFF);
            Spawn(Glyph("z", c, 13), 74, 18, 10, -24, 2200, 0);
            Spawn(Glyph("z", c, 9), 70, 26, 8, -18, 2000, 500);
        }
        public void Hearts(int n)
        {
            for (int i = 0; i < n; i++)
                Spawn(Glyph("♥", Color.FromRgb(0xFF, 0x6F, 0x91), 13 + rnd.Next(6)), 30 + rnd.Next(40), 22, rnd.Next(-16, 16), -28 - rnd.Next(14), 1400, i * 180);
        }
        public void Sparkle()
        {
            for (int i = 0; i < 3; i++)
                Spawn(Glyph("✦", Color.FromRgb(0xFF, 0xE2, 0x7A), 10 + rnd.Next(6)), 12 + rnd.Next(74), 26 + rnd.Next(30), rnd.Next(-14, 14), -18, 1000, i * 120);
        }
        void Confetti()
        {
            var colors = new[] { "#FF7EB6", "#FFE27A", "#7DF09A", "#8FD8FF", "#B8A6FF" };
            for (int i = 0; i < 10; i++)
            {
                var r = new Rectangle { Width = 4, Height = 6, RadiusX = 1, RadiusY = 1, Fill = B(colors[i % colors.Length]) };
                Spawn(r, 46 + rnd.Next(-6, 6), 30, rnd.Next(-46, 46), -20 - rnd.Next(26), 1300, i * 40);
            }
        }
        void Sweat()
        {
            var drop = new Path { Data = Geometry.Parse("M4,0 C4,0 8,5 8,7.5 A4,4 0 1 1 0,7.5 C0,5 4,0 4,0 Z"), Fill = B("#8FD8FF") };
            Spawn(drop, 82, 36, 2, 14, 1600, 0);
        }
    }
}
