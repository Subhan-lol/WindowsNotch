using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace NotchIsland
{
    /// <summary>Toys Mochi can hold, and the little moves that go with them.</summary>
    public partial class Mascot
    {
        DispatcherTimer puffTimer;

        void BuildMoreProps()
        {
            // bubble wand
            var wand = new Canvas();
            wand.Children.Add(P("M74,80 L88,46", null, B("#C9A4FF"), 2.6));
            wand.Children.Add(At(E(13, 13, Frozen(Color.FromArgb(0x40, 0x9F, 0xD3, 0xFF)), B("#8FD8FF"), 2.4), 82, 35));
            AddProp(Prop.Wand, wand);

            // open book (two paws)
            var book = new Canvas { RenderTransform = Place(0, 33, 66) };
            book.Children.Add(At(new Rectangle { Width = 36, Height = 22, RadiusX = 2, RadiusY = 2, Fill = B("#7C8CFF") }, -1, 1));
            book.Children.Add(P("M0,2 Q8,-1 17,2 L17,20 Q8,17 0,20 Z", B("#FFF8E7"), B("#D9C9A8"), 0.8));
            book.Children.Add(P("M34,2 Q26,-1 17,2 L17,20 Q26,17 34,20 Z", B("#FFF8E7"), B("#D9C9A8"), 0.8));
            book.Children.Add(P("M3,7 L13,6 M3,11 L13,10 M21,6 L31,7 M21,10 L31,11", null, B("#C9BFA8"), 1));
            AddProp(Prop.Book, book);

            // ukulele across the belly
            var uke = new Canvas { RenderTransform = Place(-28, 26, 80) };
            var wood = B("#E8A96B");
            uke.Children.Add(At(E(22, 18, wood, B("#B87A45"), 1), 0, 0));
            uke.Children.Add(At(E(15, 13, wood, B("#B87A45"), 1), 13, 2.5));
            uke.Children.Add(At(E(5, 5, B("#5A3A22")), 11, 6.5));
            uke.Children.Add(At(new Rectangle { Width = 24, Height = 4, Fill = B("#8B5A2B") }, 26, 7));
            uke.Children.Add(At(new Rectangle { Width = 6, Height = 7, RadiusX = 1.5, RadiusY = 1.5, Fill = B("#6B4423") }, 49, 5.5));
            uke.Children.Add(P("M6,8.2 L50,8.2 M6,9.8 L50,9.8", null, Frozen(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)), 0.5));
            AddProp(Prop.Ukulele, uke);

            // onigiri
            var snack = new Canvas { RenderTransform = Place(0, 40, 66) };
            snack.Children.Add(P("M10,0 Q13,0 20,15 Q21.5,20 17,20 L3,20 Q-1.5,20 0,15 Q7,0 10,0 Z", Brushes.White, B("#DADCE6"), 1));
            snack.Children.Add(At(new Rectangle { Width = 10, Height = 8, RadiusX = 1, RadiusY = 1, Fill = B("#2E3B30") }, 5, 12));
            AddProp(Prop.Snack, snack);

            // boba tea
            var boba = new Canvas { RenderTransform = Place(0, 42, 69) };
            boba.Children.Add(At(new Rectangle { Width = 4, Height = 16, RadiusX = 2, RadiusY = 2, Fill = B("#FF8FA6") }, 7.5, -10));
            boba.Children.Add(P("M0,0 L17,0 L15,22 L2,22 Z", B("#F2D2B0"), B("#D8B48E"), 1));
            boba.Children.Add(P("M-1,0 L18,0", null, B("#FFFFFF"), 2.4));
            foreach (var p in new[] { new Point(3.5, 17), new Point(7.5, 18.5), new Point(11, 17), new Point(5.5, 14), new Point(10, 14.5) })
                boba.Children.Add(At(E(3.2, 3.2, B("#3A2A22")), p.X, p.Y));
            AddProp(Prop.Boba, boba);

            // fishing rod (the line itself is drawn by the taskbar buddy)
            var rod = new Canvas();
            rod.Children.Add(P("M73,79 L99,22", null, B("#8B5A2B"), 2.4));
            rod.Children.Add(At(E(6, 6, B("#B5BDCC"), B("#6B7280"), 1), 73, 66));
            AddProp(Prop.Rod, rod);

            // watering can
            var can = new Canvas { RenderTransform = Place(-12, 64, 62) };
            can.Children.Add(P("M0,5 L17,5 L16,19 L1,19 Z", B("#7FC8FF"), B("#5AA9E6"), 1));
            can.Children.Add(P("M16,9 L27,1", null, B("#7FC8FF"), 3));
            can.Children.Add(P("M3,5 Q8.5,-4 14,5", null, B("#5AA9E6"), 2));
            AddProp(Prop.WateringCan, can);

            // balloon on a string
            var bal = new Canvas { RenderTransform = new RotateTransform(0, 75, 74) };
            bal.Children.Add(P("M75,74 C81,52 71,32 80,10", null, B("#9A9AA6"), 1));
            bal.Children.Add(At(E(24, 28, B("#FF6F91")), 68, -19));
            bal.Children.Add(At(E(5, 8, Frozen(Color.FromArgb(0xA0, 0xFF, 0xFF, 0xFF))), 72, -14));
            bal.Children.Add(P("M78,8.5 L82,8.5 L80,5.5 Z", B("#FF6F91")));
            AddProp(Prop.Balloon, bal);

            // delivery parcel (two paws) and a little courier cap
            var parcel = new Canvas { RenderTransform = Place(0, 36, 63) };
            parcel.Children.Add(At(new Rectangle { Width = 28, Height = 22, RadiusX = 2.5, RadiusY = 2.5, Fill = B("#D9A066"), Stroke = B("#B07A45"), StrokeThickness = 1.2 }, 0, 0));
            parcel.Children.Add(At(new Rectangle { Width = 6, Height = 22, Fill = B("#F3D9B1") }, 11, 0));
            parcel.Children.Add(P("M17,9 C16,7 13.5,7.5 14,9.8 C14.5,11.5 17,13 17,13 C17,13 19.5,11.5 20,9.8 C20.5,7.5 18,7 17,9 Z", B("#FF8FA6")));
            AddProp(Prop.Parcel, parcel);
            var cap = new Canvas();
            cap.Children.Add(P("M31,36 C31,21 69,21 69,36 Z", B("#FF5D73"), B("#E04358"), 1));
            cap.Children.Add(P("M62,34.5 Q78,32 84,38 Q72,39.5 62,37.5 Z", B("#E04358")));
            cap.Children.Add(At(E(6, 6, B("#FFE27A")), 47, 20));
            cap.Children.Add(P("M41,29 Q50,25 59,29", null, Frozen(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)), 1.6));
            AddProp(Prop.Cap, cap);

            puffTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            puffTimer.Tick += (s, e) => { puffTimer.Stop(); var m = shown; shown = (Mood)(-1); Show(m); };
        }

        static bool TwoHanded(Prop p) { return p == Prop.Paper || p == Prop.Book || p == Prop.Snack || p == Prop.Boba || p == Prop.Parcel; }

        /// <summary>Little "o" mouth with puffed cheeks (blowing, sipping).</summary>
        public void PuffMouth(int ms)
        {
            smile.Opacity = grin.Opacity = flatMouth.Opacity = 0;
            mouthOpen.Opacity = 1;
            mouthScale.ScaleX = 0.5; mouthScale.ScaleY = 0.45;
            cheeks.Opacity = 1;
            puffTimer.Stop(); puffTimer.Interval = TimeSpan.FromMilliseconds(ms); puffTimer.Start();
        }

        public void Blow()
        {
            PuffMouth(520);
            squash.BeginAnimation(ScaleTransform.ScaleXProperty, Pulse(1, 1.05, 160, 1, Anim.OutCubic));
        }

        public void Sip()
        {
            PuffMouth(800);
            bob.BeginAnimation(TranslateTransform.YProperty, Pulse(0, 1.5, 200, 2, Anim.InOutSine));
            if (Particles) Spawn(Glyph("slurp", Color.FromRgb(0xF2, 0xD2, 0xB0), 8), 62, 50, 10, -14, 1100, 0);
        }

        public void Bite()
        {
            mouthOpen.Opacity = 1; smile.Opacity = 0;
            var a = Pulse(1, 0.1, 90, 2, Anim.InOutSine);
            a.Completed += (s, e) => { var m = shown; shown = (Mood)(-1); Show(m); };
            mouthScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            if (Particles) for (int i = 0; i < 3; i++)
                Spawn(new Ellipse { Width = 2.5, Height = 2.5, Fill = Brushes.White }, 48 + rnd.Next(-4, 6), 72, rnd.Next(-10, 10), 18, 700, i * 60);
        }

        /// <summary>Strum / scribble style arm wiggle for a while.</summary>
        public void Strum(int ms)
        {
            var a = new DoubleAnimation(40, 62, TimeSpan.FromMilliseconds(170))
            { AutoReverse = true, RepeatBehavior = new RepeatBehavior(TimeSpan.FromMilliseconds(ms)), FillBehavior = FillBehavior.Stop, EasingFunction = Anim.InOutSine };
            Timeline.SetDesiredFrameRate(a, 30);
            armR.BeginAnimation(RotateTransform.AngleProperty, a);
            var head = new DoubleAnimation(-4, 4, TimeSpan.FromMilliseconds(340))
            { AutoReverse = true, RepeatBehavior = new RepeatBehavior(TimeSpan.FromMilliseconds(ms)), FillBehavior = FillBehavior.Stop, EasingFunction = Anim.InOutSine };
            Timeline.SetDesiredFrameRate(head, 30);
            tilt.BeginAnimation(RotateTransform.AngleProperty, head);
        }

        public void Note1() { if (Particles) Note(); }

        /// <summary>Courier mode: cap on, hugging a parcel, little legs running in place.</summary>
        public void SetCarrying(bool on)
        {
            if (on)
            {
                FlashHead(Prop.Cap, 0);
                FlashHand(Prop.Parcel, 0);
                footL.BeginAnimation(TranslateTransform.YProperty, Loop(0, -3.5, 140, 0));
                footR.BeginAnimation(TranslateTransform.YProperty, Loop(0, -3.5, 140, 140));
                bob.BeginAnimation(TranslateTransform.YProperty, Loop(0, -2.2, 140, 0));
                Flash(Mood.Focus, 0);
            }
            else
            {
                footL.BeginAnimation(TranslateTransform.YProperty, null);
                footR.BeginAnimation(TranslateTransform.YProperty, null);
                bob.BeginAnimation(TranslateTransform.YProperty, null);
            }
        }

        /// <summary>Opens the parcel and holds the file up proudly.</summary>
        public void Present()
        {
            SetCarrying(false);
            FlashHand(Prop.None, 0);
            Flash(Mood.Excited, 1600);
            armL.BeginAnimation(RotateTransform.AngleProperty, Keys(110, -30, 120, 140, 130, ArmRestL));
            armR.BeginAnimation(RotateTransform.AngleProperty, Keys(110, 30, -120, -140, -130, ArmRestR));
            bob.BeginAnimation(TranslateTransform.YProperty, Pulse(0, -6, 160, 1, Anim.OutCubic));
            if (Particles) Sparkle();
        }

        /// <summary>Quick paw swipe.</summary>
        public void Swat()
        {
            armR.BeginAnimation(RotateTransform.AngleProperty, Keys(70, -70, 80, ArmRestR));
            tilt.BeginAnimation(RotateTransform.AngleProperty, Keys(70, -6, 8, 0));
        }

        /// <summary>Wind up and throw.</summary>
        public void Throw()
        {
            armR.BeginAnimation(RotateTransform.AngleProperty, Keys(110, -150, -165, 70, ArmRestR));
            tilt.BeginAnimation(RotateTransform.AngleProperty, Keys(110, -8, -10, 8, 0));
        }

        /// <summary>Crouch, wiggle the bum, pounce.</summary>
        public void Pounce()
        {
            var crouch = Keys(120, 0.82, 0.8, 0.82, 1.15, 1);
            squash.BeginAnimation(ScaleTransform.ScaleYProperty, crouch);
            tailRot.BeginAnimation(RotateTransform.AngleProperty, Keys(60, 14, -10, 14, -10, 0));
            var hop = Pulse(0, -12, 180, 1, Anim.OutCubic); hop.BeginTime = TimeSpan.FromMilliseconds(420);
            bob.BeginAnimation(TranslateTransform.YProperty, hop);
            Flash(Mood.Excited, 1000);
        }

        public void Tug()
        {
            tilt.BeginAnimation(RotateTransform.AngleProperty, Keys(90, -10, 6, -12, 4, 0));
            armR.BeginAnimation(RotateTransform.AngleProperty, Keys(90, 20, 50, 15, 48));
            Flash(Mood.Excited, 900);
        }

        public void Sniff()
        {
            faceLook.BeginAnimation(TranslateTransform.YProperty, Pulse(0, 1.2, 80, 3, null));
            Flash(Mood.Happy, 1400);
            if (Particles) Hearts(2);
        }

        public void Giggle()
        {
            bob.BeginAnimation(TranslateTransform.YProperty, Pulse(0, -3, 80, 4, Anim.InOutSine));
            Flash(Mood.Happy, 1200);
        }

        public void Dizzy()
        {
            Spin();
            Wiggle();
            Flash(Mood.Worried, 1300);
            if (Particles) Spawn(Glyph("@", Color.FromRgb(0xCF, 0xC8, 0xFF), 12), 70, 30, 4, -10, 1200, 0);
        }

        public void Bonk()
        {
            squash.BeginAnimation(ScaleTransform.ScaleYProperty, Pulse(1, 0.85, 90, 1, null));
            Flash(Mood.Worried, 1100);
            if (Particles) Spawn(Glyph("✦", Color.FromRgb(0xFF, 0xE2, 0x7A), 12), 46, 14, 0, -10, 800, 0);
        }

        /// <summary>The balloon prop sways on its string.</summary>
        public void SwayBalloon(bool on)
        {
            FrameworkElement b;
            if (!props.TryGetValue(Prop.Balloon, out b)) return;
            var r = (RotateTransform)b.RenderTransform;
            if (on) r.BeginAnimation(RotateTransform.AngleProperty, Loop(-7, 7, 900, 0));
            else r.BeginAnimation(RotateTransform.AngleProperty, null);
        }
    }
}
