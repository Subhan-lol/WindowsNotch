using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace NotchIsland
{
    /// <summary>Things Mochi does for fun on the taskbar. Each play is a little async script that
    /// stops cleanly the moment you grab Mochi (playGen changes).</summary>
    public partial class BuddyWindow
    {
        class Toy { public FrameworkElement El; public bool World; public double WorldX; }

        const double ToyGround = H - Size * 0.06;
        readonly List<Toy> toys = new List<Toy>();
        Canvas toyBack, toyFront;
        readonly TranslateTransform sink = new TranslateTransform();
        int playGen, facing = 1, lastPlay = -1;
        bool playing, skating;
        TaskCompletionSource<bool> walkDone;
        Func<int, Task>[] plays;
        public static readonly string[] PlayNames = {
            "Yarn ball", "Butterfly", "Bubbles", "Balloon", "Cardboard box", "Paper plane", "Ukulele", "Snack time",
            "Boba tea", "Story time", "Go fishing", "Juggling", "Laser pointer", "Grow a flower", "Skateboard", "Head ball" };

        void InitPlay()
        {
            plays = new Func<int, Task>[] {
                PlayYarn, PlayButterfly, PlayBubbles, PlayBalloon, PlayBox, PlayPlane, PlayUke, PlaySnack,
                PlayBoba, PlayRead, PlayFishing, PlayJuggle, PlayLaser, PlayFlower, PlaySkate, PlayHeadBall };
        }

        // ================= plumbing =================
        Point M2W(double mx, double my)
        {
            double fx = facing < 0 ? 100 - mx : mx;
            return new Point((W - Size) / 2 + fx * Size / 100, H - Size + my * Size / 100 + sink.Y);
        }

        void FaceDir(int d) { facing = d < 0 ? -1 : 1; Mascot.Face(facing); }

        Toy AddToy(FrameworkElement el, double lx, double ly, bool front, bool world)
        {
            var t = new Toy { El = el, World = world, WorldX = x + lx * scale };
            Canvas.SetLeft(el, lx); Canvas.SetTop(el, ly);
            (front ? toyFront : toyBack).Children.Add(el);
            toys.Add(t);
            return t;
        }

        void RemoveToy(Toy t)
        {
            toyBack.Children.Remove(t.El); toyFront.Children.Remove(t.El);
            toys.Remove(t);
        }

        void UpdateToys()
        {
            for (int i = 0; i < toys.Count; i++)
                if (toys[i].World) Canvas.SetLeft(toys[i].El, (toys[i].WorldX - x) / scale);
        }

        public void StartPlay(int index)
        {
            if (index < 0 || index >= plays.Length || !IsOut) return;
            if (state == State.Walking) StopWalking();
            if (state != State.Grounded) return;
            RunPlay(index);
        }

        async void RunPlay(int index)
        {
            ai.Stop();
            AbortPlay();
            lastPlay = index;
            playing = true;
            int g = ++playGen;
            if (Mascot.CurrentMood == Mood.Sleepy) Mascot.SetMood(Mood.Idle);
            try { await plays[index](g); }
            catch (Exception ex) { Debug.WriteLine(ex); }
            if (g != playGen) return; // interrupted; AbortPlay already cleaned up
            playing = false;
            CleanupPlay();
            Mascot.SetMood(context);
            if (state == State.Grounded) Schedule(3500 + rnd.Next(5000));
        }

        void AbortPlay()
        {
            var w = walkDone; walkDone = null;
            if (w != null) w.TrySetResult(false);
            if (!playing) return;
            playGen++;
            playing = false;
            skating = false;
            CleanupPlay();
        }

        void CleanupPlay()
        {
            toyBack.Children.Clear(); toyFront.Children.Clear(); toys.Clear();
            Mascot.FlashHand(Prop.None, 0); Mascot.FlashHead(Prop.None, 0);
            Mascot.SwayBalloon(false); Mascot.SwingLegs(false); Mascot.SetSway(0);
            sink.BeginAnimation(TranslateTransform.YProperty, null); sink.Y = 0;
            Mascot.EndFlash();
        }

        bool Alive(int g) { return g == playGen && playing && IsOut && !suppressed && (state == State.Grounded || state == State.Walking); }

        async Task<bool> Wait(int ms, int g) { await Task.Delay(ms); return Alive(g); }

        Task WalkToAsync(double target, double speed)
        {
            var tcs = new TaskCompletionSource<bool>();
            walkDone = tcs;
            WalkTo(target, speed);
            if (state != State.Walking) tcs.TrySetResult(true);
            return tcs.Task;
        }

        void ArrivedWhilePlaying()
        {
            var w = walkDone; walkDone = null;
            if (w != null) w.TrySetResult(true);
        }

        double ClampWorld(double worldX, double widthDip)
        {
            var w = WorkArea();
            return Math.Max(w.Left + 10 * scale, Math.Min(w.Right - (widthDip + 10) * scale, worldX));
        }

        /// <summary>Smoothly slide a world toy sideways (rolling balls, the laser dot...).</summary>
        async Task Roll(Toy t, double dxPx, int ms, int g, RotateTransform spin, double radius)
        {
            double start = t.WorldX;
            double end = ClampWorld(start + dxPx, t.El.Width);
            var sw = Stopwatch.StartNew();
            while (true)
            {
                double k = Math.Min(1, sw.ElapsedMilliseconds / (double)ms);
                double e = 1 - (1 - k) * (1 - k) * (1 - k);
                t.WorldX = start + (end - start) * e;
                if (spin != null) spin.Angle = (t.WorldX - start) / scale / radius * 57.3;
                UpdateToys();
                if (k >= 1 || g != playGen) return;
                await Task.Delay(30);
            }
        }

        static DoubleAnimation A(double to, double ms, IEasingFunction ease = null, double delay = 0)
        {
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease, BeginTime = TimeSpan.FromMilliseconds(delay) };
            Timeline.SetDesiredFrameRate(a, 30);
            return a;
        }

        void DropIn(FrameworkElement el, double toTop, double delay)
        {
            var k = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(delay) };
            k.KeyFrames.Add(new DiscreteDoubleKeyFrame(-70, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            k.KeyFrames.Add(new EasingDoubleKeyFrame(toTop, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420)), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
            k.KeyFrames.Add(new EasingDoubleKeyFrame(toTop - 9, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(540)), Anim.OutCubic));
            k.KeyFrames.Add(new EasingDoubleKeyFrame(toTop, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(660)), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
            Timeline.SetDesiredFrameRate(k, 30);
            el.BeginAnimation(Canvas.TopProperty, k);
        }

        void Fade(Toy t, double ms)
        {
            var a = A(0, ms);
            a.Completed += (s, e) => RemoveToy(t);
            t.El.BeginAnimation(UIElement.OpacityProperty, a);
        }

        void SinkTo(double y, double ms) { sink.BeginAnimation(TranslateTransform.YProperty, A(y, ms, Anim.OutBack)); }

        /// <summary>Keyframed flight through points; optional rotation follows the direction of travel.</summary>
        void FlyPath(FrameworkElement el, double[] xs, double[] ys, double stepMs, RotateTransform rot, ScaleTransform flip)
        {
            var ax = new DoubleAnimationUsingKeyFrames(); var ay = new DoubleAnimationUsingKeyFrames();
            var ar = new DoubleAnimationUsingKeyFrames(); var af = new DoubleAnimationUsingKeyFrames();
            double px = Canvas.GetLeft(el), py = Canvas.GetTop(el);
            var spline = new KeySpline(0.35, 0.1, 0.65, 0.9);
            for (int i = 0; i < xs.Length; i++)
            {
                var kt = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(stepMs * (i + 1)));
                ax.KeyFrames.Add(new SplineDoubleKeyFrame(xs[i], kt, spline));
                ay.KeyFrames.Add(new SplineDoubleKeyFrame(ys[i], kt, spline));
                double dx = xs[i] - px, dy = ys[i] - py;
                bool left = dx < 0;
                double ang = Math.Atan2(dy, Math.Abs(dx)) * 180 / Math.PI;
                ar.KeyFrames.Add(new LinearDoubleKeyFrame(left ? -ang : ang, kt));
                af.KeyFrames.Add(new DiscreteDoubleKeyFrame(left ? -1 : 1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(stepMs * i + 1))));
                px = xs[i]; py = ys[i];
            }
            foreach (var t in new Timeline[] { ax, ay, ar, af }) Timeline.SetDesiredFrameRate(t, 30);
            el.BeginAnimation(Canvas.LeftProperty, ax);
            el.BeginAnimation(Canvas.TopProperty, ay);
            if (rot != null) rot.BeginAnimation(RotateTransform.AngleProperty, ar);
            if (flip != null) flip.BeginAnimation(ScaleTransform.ScaleXProperty, af);
        }

        /// <summary>Mochi's eyes follow a toy for a while.</summary>
        async void Watch(FrameworkElement el, int ms, int g)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms && g == playGen)
            {
                var eye = M2W(50, 58);
                double ex = Canvas.GetLeft(el) + el.ActualWidth / 2 - eye.X, ey = Canvas.GetTop(el) + el.ActualHeight / 2 - eye.Y;
                if (!double.IsNaN(ex) && !double.IsNaN(ey)) Mascot.LookAt(ex / 70, ey / 60);
                await Task.Delay(100);
            }
        }

        void Say(string s, string hex)
        {
            var tb = BuddyToys.Text(s, hex, 11);
            var head = M2W(78, 8);
            var t = AddToy(tb, head.X, head.Y, true, false);
            tb.BeginAnimation(Canvas.TopProperty, A(head.Y - 18, 1200, Anim.OutCubic));
            Fade(t, 1300);
        }

        // ================= the plays =================
        async Task PlayYarn(int g)
        {
            var yarn = BuddyToys.Yarn();
            var spin = (RotateTransform)yarn.Tag;
            int side = rnd.Next(2) == 0 ? -1 : 1;
            var t = AddToy(yarn, W / 2 + side * 75 - 9, ToyGround - 18, false, true);
            DropIn(yarn, ToyGround - 18, 0);
            bool rolling = false;
            yarn.MouseLeftButtonDown += async (s, e) =>
            {
                e.Handled = true;
                if (rolling || g != playGen) return;
                rolling = true;
                POINT c; Native.GetCursorPos(out c);
                int d = c.X > t.WorldX + 9 * scale ? -1 : 1;
                Mascot.Startle();
                await Roll(t, d * 110 * scale, 800, g, spin, 9);
                rolling = false;
            };
            FaceDir(side); Mascot.Flash(Mood.Excited, 900); Mascot.LookAt(side * 0.8, 0.6);
            if (!await Wait(1000, g)) return;
            for (int i = 0; i < 4; i++)
            {
                while (rolling) { if (!await Wait(100, g)) return; }
                double ballC = t.WorldX + 9 * scale;
                int dir = ballC > CenterX() ? 1 : -1;
                await WalkToAsync(ballC - dir * 44 * scale - W / 2 * scale, 90);
                if (!Alive(g)) return;
                FaceDir(dir);
                Mascot.Swat();
                if (!await Wait(170, g)) return;
                int kdir = rnd.NextDouble() < 0.8 ? dir : -dir;
                rolling = true;
                await Roll(t, kdir * (60 + rnd.Next(80)) * scale, 900, g, spin, 9);
                rolling = false;
                if (!Alive(g)) return;
                if (rnd.NextDouble() < 0.35) Mascot.Giggle();
            }
            Mascot.Pounce();
            if (!await Wait(900, g)) return;
            Mascot.Flash(Mood.Happy, 1800); Mascot.Hearts(2);
            await Wait(1800, g);
        }

        async Task PlayButterfly(int g)
        {
            ScaleTransform wings;
            var bf = BuddyToys.Butterfly(out wings);
            int from = rnd.Next(2) == 0 ? -1 : 1;
            AddToy(bf, from < 0 ? -20 : W + 2, 30, true, false);
            bool shoo = false;
            bf.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true; shoo = true;
                FlyPath(bf, new[] { from < 0 ? -40.0 : W + 40 }, new[] { -30.0 }, 900, null, null);
            };
            var head = M2W(50, 30);
            FlyPath(bf, new[] { head.X + 60, head.X + 15, head.X - 55, head.X - 15, head.X + 45, head.X - 5 },
                        new[] { 40.0, 72, 48, 92, 58, 80 }, 700, null, null);
            Watch(bf, 4300, g);
            FaceDir(-from); Mascot.Flash(Mood.Excited, 1500);
            if (!await Wait(2200, g) || shoo) { await Wait(900, g); return; }
            Mascot.Pounce();
            if (!await Wait(1300, g) || shoo) { await Wait(900, g); return; }
            Mascot.Swat();
            if (!await Wait(900, g) || shoo) { await Wait(900, g); return; }
            var ear = M2W(70, 12);
            FlyPath(bf, new[] { ear.X - 9 }, new[] { ear.Y - 12 }, 900, null, null);
            if (!await Wait(950, g) || shoo) { await Wait(900, g); return; }
            var slow = new DoubleAnimation(1, 0.3, TimeSpan.FromMilliseconds(500)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            wings.BeginAnimation(ScaleTransform.ScaleXProperty, slow);
            Mascot.LookAt(0.4, -1); Mascot.Flash(Mood.Happy, 2800); Mascot.Hearts(1);
            if (!await Wait(2800, g) || shoo) { await Wait(900, g); return; }
            FlyPath(bf, new[] { from < 0 ? W + 30.0 : -30.0 }, new[] { -20.0 }, 1400, null, null);
            Mascot.Wave(); Say("bye!", "#CFC8FF");
            await Wait(1500, g);
        }

        async Task PlayBubbles(int g)
        {
            Mascot.FlashHand(Prop.Wand, 0);
            if (!await Wait(500, g)) return;
            for (int i = 0; i < 7; i++)
            {
                Mascot.Blow();
                var ring = M2W(88.5, 41.5);
                SpawnBubble(ring);
                if (rnd.NextDouble() < 0.5) SpawnBubble(ring);
                Mascot.LookAt(facing * 0.6, -0.8);
                if (!await Wait(650 + rnd.Next(350), g)) return;
            }
            Mascot.FlashHand(Prop.None, 0);
            Mascot.LookAt(0, -1); Mascot.Giggle();
            await Wait(2400, g);
        }

        void SpawnBubble(Point at)
        {
            double d = 8 + rnd.Next(11);
            var b = BuddyToys.Bubble(d);
            var t = AddToy(b, at.X - d / 2, at.Y - d / 2, true, false);
            double dur = 2300 + rnd.Next(1500);
            bool popped = false;
            Action pop = () =>
            {
                if (popped) return; popped = true;
                var sc = (ScaleTransform)b.RenderTransform;
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, A(1.6, 120));
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, A(1.6, 120));
                Fade(t, 130);
            };
            var up = A(at.Y - 80 - rnd.Next(80), dur, new QuadraticEase { EasingMode = EasingMode.EaseOut });
            up.Completed += (s, e) => pop();
            b.BeginAnimation(Canvas.TopProperty, up);
            var side = new DoubleAnimationUsingKeyFrames();
            double drift = facing * (10 + rnd.Next(50));
            for (int k = 1; k <= 4; k++)
                side.KeyFrames.Add(new SplineDoubleKeyFrame(at.X - d / 2 + drift * k / 4 + (k % 2 == 0 ? -8 : 8), KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(dur * k / 4)), new KeySpline(0.4, 0, 0.6, 1)));
            Timeline.SetDesiredFrameRate(side, 30);
            b.BeginAnimation(Canvas.LeftProperty, side);
            b.MouseLeftButtonDown += (s, e) => { e.Handled = true; pop(); Mascot.Giggle(); };
        }

        async Task PlayBalloon(int g)
        {
            Mascot.FlashHand(Prop.Balloon, 0);
            Mascot.SwayBalloon(true);
            Mascot.Flash(Mood.Happy, 2500);
            if (!await Wait(2600, g)) return;
            var lift = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            lift.KeyFrames.Add(new EasingDoubleKeyFrame(-18, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(800)), Anim.InOutSine));
            lift.KeyFrames.Add(new EasingDoubleKeyFrame(-14, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1400)), Anim.InOutSine));
            lift.KeyFrames.Add(new EasingDoubleKeyFrame(-18, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2000)), Anim.InOutSine));
            lift.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2600)), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
            sink.BeginAnimation(TranslateTransform.YProperty, lift);
            Mascot.SwingLegs(true); Mascot.Flash(Mood.Excited, 2600); Say("whoa!", "#FFE27A");
            if (!await Wait(2650, g)) return;
            Mascot.SwingLegs(false); Mascot.Land(0.4);
            if (!await Wait(900, g)) return;
            // oops — lets go of the string
            Mascot.FlashHand(Prop.None, 0); Mascot.SwayBalloon(false);
            var top = M2W(facing > 0 ? 68 : 92, -19);
            var bal = BuddyToys.Balloon();
            var t = AddToy(bal, top.X, top.Y, false, false);
            bal.BeginAnimation(Canvas.TopProperty, A(-70, 3200, new QuadraticEase { EasingMode = EasingMode.EaseIn }));
            bal.BeginAnimation(Canvas.LeftProperty, A(top.X + facing * 40, 3200, Anim.InOutSine));
            Mascot.Flash(Mood.Worried, 1300); Mascot.LookAt(0, -1);
            if (!await Wait(1400, g)) return;
            Mascot.Wave(); Mascot.Flash(Mood.Happy, 1500);
            await Wait(2000, g);
        }

        async Task PlayBox(int g)
        {
            Canvas back, front;
            BuddyToys.Box(out back, out front);
            int side = rnd.Next(2) == 0 ? -1 : 1;
            double bx = W / 2 + side * 82 - 38;
            var tb = AddToy(back, bx, ToyGround - 30, false, true);
            var tf = AddToy(front, bx, ToyGround - 30, true, true);
            DropIn(back, ToyGround - 30, 0); DropIn(front, ToyGround - 30, 0);
            FaceDir(side); Mascot.Flash(Mood.Excited, 1000); Mascot.LookAt(side, 0.5);
            if (!await Wait(1100, g)) return;
            await WalkToAsync(tf.WorldX + 38 * scale - W / 2 * scale, 70);
            if (!Alive(g)) return;
            Mascot.Jump();
            if (!await Wait(330, g)) return;
            SinkTo(26, 180);
            var wob = (RotateTransform)front.RenderTransform;
            wob.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimationUsingKeyFrames
            {
                KeyFrames = { new LinearDoubleKeyFrame(-4, KeyTime.FromPercent(0.25)), new LinearDoubleKeyFrame(4, KeyTime.FromPercent(0.5)), new LinearDoubleKeyFrame(-3, KeyTime.FromPercent(0.75)), new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)) },
                Duration = TimeSpan.FromMilliseconds(600)
            });
            if (!await Wait(1500, g)) return;
            SinkTo(-6, 300); Mascot.LookAt(-1, 0);
            if (!await Wait(900, g)) return;
            Mascot.LookAt(1, 0);
            if (!await Wait(900, g)) return;
            SinkTo(26, 150);
            if (!await Wait(1400, g)) return;
            SinkTo(-16, 160); Mascot.Startle(); Mascot.Sparkle(); Say("boo!", "#FFE27A");
            if (!await Wait(420, g)) return;
            SinkTo(4, 260); Mascot.Flash(Mood.Happy, 2500);
            if (!await Wait(2500, g)) return;
            Mascot.Jump(); SinkTo(0, 220);
            if (!await Wait(500, g)) return;
            Fade(tb, 400); Fade(tf, 400);
            await Wait(450, g);
        }

        async Task PlayPlane(int g)
        {
            Mascot.FlashHand(Prop.Paper, 0); Mascot.Wiggle();
            if (!await Wait(900, g)) return;
            Mascot.FlashHand(Prop.None, 0); Mascot.Throw();
            if (!await Wait(260, g)) return;
            var start = M2W(80, 55);
            var pl = BuddyToys.Plane();
            var flip = new ScaleTransform(1, 1, 12, 7); var rot = new RotateTransform(0, 12, 7);
            var tg = new TransformGroup(); tg.Children.Add(flip); tg.Children.Add(rot); pl.RenderTransform = tg;
            var t = AddToy(pl, start.X, start.Y, true, false);
            int d = facing; double cx = W / 2;
            FlyPath(pl, new[] { cx + d * 80, cx + d * 128, cx + d * 95, cx + d * 20, cx - d * 70, cx - d * 40 },
                        new[] { 60.0, 28, 12, 40, 92, ToyGround - 14 }, 620, rot, flip);
            Watch(pl, 3800, g);
            Mascot.Flash(Mood.Excited, 3800);
            if (!await Wait(3900, g)) return;
            Mascot.Giggle(); Mascot.Hearts(1);
            if (!await Wait(1500, g)) return;
            Fade(t, 400);
            await Wait(450, g);
        }

        async Task PlayUke(int g)
        {
            Mascot.FlashHand(Prop.Ukulele, 0);
            Mascot.Flash(Mood.Happy, 0);
            if (!await Wait(400, g)) return;
            for (int i = 0; i < 3; i++)
            {
                Mascot.Strum(1800);
                for (int k = 0; k < 3; k++) { Mascot.Note1(); if (!await Wait(600, g)) return; }
            }
            Mascot.Flash(Mood.Excited, 1300); Mascot.Sparkle(); Say("ta-da!", "#FFE27A");
            await Wait(1400, g);
        }

        async Task PlaySnack(int g)
        {
            Mascot.FlashHand(Prop.Snack, 0);
            Mascot.Flash(Mood.Excited, 600);
            if (!await Wait(600, g)) return;
            for (int i = 0; i < 3; i++) { Mascot.Bite(); if (!await Wait(900, g)) return; }
            Mascot.FlashHand(Prop.None, 0);
            Mascot.Sniff(); Say("yum!", "#FFB36B");
            await Wait(1800, g);
        }

        async Task PlayBoba(int g)
        {
            Mascot.FlashHand(Prop.Boba, 0);
            if (!await Wait(500, g)) return;
            for (int i = 0; i < 3; i++) { Mascot.Sip(); if (!await Wait(1400, g)) return; }
            Mascot.FlashHand(Prop.None, 0);
            Mascot.Flash(Mood.Happy, 1500); Mascot.Giggle(); Say("ahh~", "#F2D2B0");
            await Wait(1600, g);
        }

        async Task PlayRead(int g)
        {
            Mascot.FlashHead(Prop.Glasses, 0); Mascot.FlashHand(Prop.Book, 0);
            Mascot.LookAt(0, 0.8);
            for (int i = 0; i < 4; i++)
            {
                if (!await Wait(2300, g)) return;
                Mascot.Nod(); Mascot.LookAt(0, 0.8);
            }
            if (rnd.NextDouble() < 0.4)
            {
                Mascot.Flash(Mood.Sleepy, 5000);
                if (!await Wait(5000, g)) return;
                Mascot.Startle();
            }
            else { Mascot.Flash(Mood.Happy, 1300); Mascot.Hearts(1); }
            await Wait(1300, g);
        }

        async Task PlayFishing(int g)
        {
            Mascot.FlashHand(Prop.Rod, 0);
            Mascot.Throw();
            if (!await Wait(500, g)) return;
            var tip = M2W(99, 22);
            double lineX = tip.X + facing * 16;
            var line = new Path { Stroke = new SolidColorBrush(Color.FromArgb(0xC0, 0xC8, 0xC8, 0xD2)), StrokeThickness = 0.9, Data = new LineGeometry(tip, new Point(lineX, H - 3)), IsHitTestVisible = false };
            var tl = AddToy(line, 0, 0, false, false);
            var bob = new Canvas { Width = 7, Height = 7 };
            bob.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = Brushes.White, Stroke = Brushes.Gray, StrokeThickness = 0.6 });
            bob.Children.Add(new Path { Data = Geometry.Parse("M0,3.5 A3.5,3.5 0 0 1 7,3.5 Z"), Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x5D, 0x73)) });
            var tbob = AddToy(bob, lineX - 3.5, H - 9, false, false);
            var bobAnim = new DoubleAnimation(H - 9, H - 7.5, TimeSpan.FromMilliseconds(700)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            Timeline.SetDesiredFrameRate(bobAnim, 20);
            bob.BeginAnimation(Canvas.TopProperty, bobAnim);
            Mascot.LookAt(facing * 0.7, 0.9);
            if (!await Wait(2500 + rnd.Next(3500), g)) return;
            var dip = new DoubleAnimationUsingKeyFrames { KeyFrames = { new LinearDoubleKeyFrame(H - 4, KeyTime.FromPercent(0.2)), new LinearDoubleKeyFrame(H - 9, KeyTime.FromPercent(0.45)), new LinearDoubleKeyFrame(H - 3, KeyTime.FromPercent(0.7)), new LinearDoubleKeyFrame(H - 9, KeyTime.FromPercent(1)) }, Duration = TimeSpan.FromMilliseconds(700) };
            bob.BeginAnimation(Canvas.TopProperty, dip);
            Mascot.Startle(); Say("!", "#FFE27A");
            if (!await Wait(750, g)) return;
            Mascot.Tug();
            if (!await Wait(450, g)) return;
            RemoveToy(tl); RemoveToy(tbob);
            bool boot = rnd.NextDouble() < 0.3;
            var fish = BuddyToys.Fish(boot);
            var tf = AddToy(fish, lineX - 12, H - 8, true, false);
            var land = M2W(50, 64);
            fish.BeginAnimation(Canvas.LeftProperty, A(land.X - 12, 700, Anim.OutCubic));
            fish.BeginAnimation(Canvas.TopProperty, new DoubleAnimationUsingKeyFrames
            {
                KeyFrames = { new EasingDoubleKeyFrame(40, KeyTime.FromPercent(0.5), Anim.OutCubic), new EasingDoubleKeyFrame(land.Y - 7, KeyTime.FromPercent(1), new QuadraticEase { EasingMode = EasingMode.EaseIn }) },
                Duration = TimeSpan.FromMilliseconds(700)
            });
            ((RotateTransform)fish.RenderTransform).BeginAnimation(RotateTransform.AngleProperty, A(360, 700));
            if (!await Wait(760, g)) return;
            Mascot.FlashHand(Prop.None, 0);
            if (boot)
            {
                Mascot.Dizzy(); Say("…a boot?", "#CFC8FF");
                if (!await Wait(1400, g)) return;
                Mascot.Throw();
                fish.BeginAnimation(Canvas.LeftProperty, A(facing > 0 ? W + 30 : -40, 700));
                fish.BeginAnimation(Canvas.TopProperty, A(-20, 700, Anim.OutCubic));
                await Wait(900, g);
                return;
            }
            Mascot.Flash(Mood.Excited, 1800); Mascot.Hearts(2); Say("a fish!", "#FFB36B");
            ((RotateTransform)fish.RenderTransform).BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-15, 15, TimeSpan.FromMilliseconds(160)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(5) });
            if (!await Wait(1800, g)) return;
            if (rnd.NextDouble() < 0.5) { Mascot.Chomp(); if (!await Wait(400, g)) return; RemoveToy(tf); }
            else
            {
                Mascot.Wave(); Say("swim free~", "#8FD8FF");
                fish.BeginAnimation(Canvas.LeftProperty, A(lineX - 12, 600));
                fish.BeginAnimation(Canvas.TopProperty, A(H + 10, 600, new QuadraticEase { EasingMode = EasingMode.EaseIn }));
            }
            await Wait(1600, g);
        }

        async Task PlayJuggle(int g)
        {
            string[] cols = { "#FF7EB6", "#FFE27A", "#7FD8FF" };
            var l = M2W(14, 72); var r = M2W(86, 72);
            double apex = H - Size - 32;
            var balls = new Toy[3];
            for (int i = 0; i < 3; i++)
            {
                var b = BuddyToys.Ball(cols[i]);
                balls[i] = AddToy(b, l.X - 5, l.Y - 5, true, false);
                b.Opacity = 0;
                var delay = TimeSpan.FromMilliseconds(i * 400);
                b.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.Zero) { BeginTime = delay });
                var ax = new DoubleAnimation(l.X - 5, r.X - 5, TimeSpan.FromMilliseconds(600)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = delay };
                var ay = new DoubleAnimation(l.Y - 5, apex, TimeSpan.FromMilliseconds(300)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = delay, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                Timeline.SetDesiredFrameRate(ax, 30); Timeline.SetDesiredFrameRate(ay, 30);
                b.BeginAnimation(Canvas.LeftProperty, ax);
                b.BeginAnimation(Canvas.TopProperty, ay);
            }
            Mascot.JuggleArms(5000);
            Mascot.Flash(Mood.Excited, 5000);
            Mascot.LookAt(0, -1);
            if (!await Wait(5000, g)) return;
            // ...and it all comes tumbling down
            for (int i = 0; i < 3; i++)
            {
                var b = balls[i].El;
                double cur = Canvas.GetLeft(b), top = Canvas.GetTop(b);
                b.BeginAnimation(Canvas.LeftProperty, A(cur + rnd.Next(-40, 40), 700));
                var fall = new DoubleAnimationUsingKeyFrames();
                fall.KeyFrames.Add(new EasingDoubleKeyFrame(i == 1 ? H - Size + 4 : ToyGround - 10, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(420)), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
                fall.KeyFrames.Add(new EasingDoubleKeyFrame(ToyGround - 22, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(560)), Anim.OutCubic));
                fall.KeyFrames.Add(new EasingDoubleKeyFrame(ToyGround - 10, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(700)), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
                b.BeginAnimation(Canvas.TopProperty, fall);
            }
            if (!await Wait(420, g)) return;
            Mascot.Bonk(); Say("ow", "#CFC8FF");
            if (!await Wait(1400, g)) return;
            Mascot.Giggle();
            if (!await Wait(900, g)) return;
            foreach (var b in balls) Fade(b, 400);
            await Wait(450, g);
        }

        async Task PlayLaser(int g)
        {
            var dot = BuddyToys.Dot();
            int side = rnd.Next(2) == 0 ? -1 : 1;
            var t = AddToy(dot, W / 2 + side * 70, ToyGround - 9, true, true);
            dot.Opacity = 0; dot.BeginAnimation(UIElement.OpacityProperty, A(1, 200));
            Mascot.Flash(Mood.Excited, 1000); Say("!", "#FF5D73");
            for (int i = 0; i < 4; i++)
            {
                double dc = t.WorldX + 6 * scale;
                int dir = dc > CenterX() ? 1 : -1;
                FaceDir(dir); Mascot.LookAt(dir * 0.8, 0.8);
                Mascot.Pounce();
                if (!await Wait(450, g)) return;
                await WalkToAsync(dc - W / 2 * scale, 170);
                if (!Alive(g)) return;
                int jump = rnd.Next(2) == 0 ? -1 : 1;
                await Roll(t, jump * (60 + rnd.Next(80)) * scale, 160, g, null, 1);
                if (!await Wait(250, g)) return;
            }
            Fade(t, 200);
            Mascot.Face(-facing);
            if (!await Wait(500, g)) return;
            Mascot.Face(facing); Say("?", "#CFC8FF");
            Mascot.Flash(Mood.Worried, 1200);
            await Wait(1400, g);
        }

        async Task PlayFlower(int g)
        {
            ScaleTransform grow;
            var fl = BuddyToys.Flower(out grow);
            int side = rnd.Next(2) == 0 ? -1 : 1;
            var t = AddToy(fl, W / 2 + side * 75 - 15, ToyGround - 68, false, true);
            DropIn(fl, ToyGround - 68, 0);
            if (!await Wait(800, g)) return;
            double potC = t.WorldX + 15 * scale;
            await WalkToAsync(potC - side * 48 * scale - W / 2 * scale, 70);
            if (!Alive(g)) return;
            FaceDir(side);
            Mascot.FlashHand(Prop.WateringCan, 0);
            if (!await Wait(400, g)) return;
            for (int i = 0; i < 10; i++)
            {
                var spout = M2W(90, 57);
                var drop = BuddyToys.Drop();
                var td = AddToy(drop, spout.X, spout.Y, true, false);
                double potLocal = (potC - x) / scale;
                drop.BeginAnimation(Canvas.LeftProperty, A(potLocal - 1.5 + rnd.Next(-4, 4), 420));
                drop.BeginAnimation(Canvas.TopProperty, A(ToyGround - 26, 420, new QuadraticEase { EasingMode = EasingMode.EaseIn }));
                Fade(td, 480);
                if (i == 4)
                {
                    grow.BeginAnimation(ScaleTransform.ScaleXProperty, A(1, 1500, Anim.OutBack));
                    grow.BeginAnimation(ScaleTransform.ScaleYProperty, A(1, 1500, Anim.OutBack));
                }
                if (!await Wait(160, g)) return;
            }
            if (!await Wait(900, g)) return;
            Mascot.FlashHand(Prop.None, 0);
            Mascot.Sniff();
            if (!await Wait(2600, g)) return;
            Fade(t, 500);
            await Wait(550, g);
        }

        async Task PlaySkate(int g)
        {
            var board = BuddyToys.Skateboard();
            var t = AddToy(board, W / 2 - 32, ToyGround - 6, false, false);
            DropIn(board, ToyGround - 6, 0);
            if (!await Wait(750, g)) return;
            Mascot.Jump();
            if (!await Wait(250, g)) return;
            SinkTo(-6, 180);
            skating = true;
            int dir = rnd.Next(2) == 0 ? -1 : 1;
            for (int leg = 0; leg < 2; leg++)
            {
                FaceDir(dir); Mascot.SetSway(dir * 7); Mascot.Flash(Mood.Excited, 0);
                await WalkToAsync(x + dir * (220 + rnd.Next(160)) * scale, 200);
                if (!Alive(g)) return;
                Mascot.SetSway(0);
                if (!await Wait(400, g)) return;
                dir = -dir;
            }
            skating = false;
            Mascot.Jump(); Mascot.Spin(); Say("yeah!", "#FFE27A");
            if (!await Wait(600, g)) return;
            SinkTo(0, 200);
            Mascot.Flash(Mood.Happy, 1200);
            if (!await Wait(500, g)) return;
            Fade(t, 300);
            await Wait(350, g);
        }

        async Task PlayHeadBall(int g)
        {
            Mascot.PlayBall();
            await Wait(3600, g);
        }
    }
}
