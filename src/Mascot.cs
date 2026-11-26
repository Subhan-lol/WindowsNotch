using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace NotchIsland
{
    public enum Mood { Idle, Happy, Vibing, Sleepy, Hungry, Focus, Worried, Excited }
    public enum Prop { None, Headphones, PartyHat, Nightcap, Glasses, Headband, Pencil, Wrench, Camera, Paper, Folder,
                       Wand, Book, Ukulele, Snack, Boba, Rod, WateringCan, Balloon, Parcel, Cap }

    /// <summary>
    /// Mochi — a round little cream cat-blob with big shiny eyes, ears, tiny arms, feet and a tail.
    /// Pure vector shapes. This file builds the art; Mascot.Behavior.cs makes it alive.
    /// </summary>
    public partial class Mascot : Viewbox
    {
        static readonly Color Ink = Color.FromRgb(0x2A, 0x22, 0x38);
        static readonly SolidColorBrush InkBrush = Frozen(Ink);
        static readonly SolidColorBrush Line = Frozen(Color.FromArgb(0x60, 0x6B, 0x4A, 0x3A));
        static readonly SolidColorBrush Pink = Frozen(Color.FromRgb(0xFF, 0xB4, 0xC6));
        static readonly SolidColorBrush Tongue = Frozen(Color.FromRgb(0xFF, 0x7E, 0x95));

        readonly Canvas root = new Canvas { Width = 100, Height = 100 };
        readonly Canvas fx = new Canvas { Width = 100, Height = 100, IsHitTestVisible = false };
        readonly Canvas body = new Canvas { Width = 100, Height = 100 };

        // whole-body transforms
        readonly ScaleTransform flip = new ScaleTransform(1, 1, 50, 50);
        readonly ScaleTransform squash = new ScaleTransform(1, 1, 50, 92);
        readonly ScaleTransform breathe = new ScaleTransform(1, 1, 50, 92);
        readonly RotateTransform tilt = new RotateTransform(0, 50, 92);
        readonly TranslateTransform bob = new TranslateTransform();

        // parts
        readonly RotateTransform earL = new RotateTransform(0, 29, 42), earR = new RotateTransform(0, 71, 42);
        readonly RotateTransform tailRot = new RotateTransform(0, 84, 80);
        readonly TranslateTransform footL = new TranslateTransform(), footR = new TranslateTransform();
        readonly RotateTransform armL = new RotateTransform(18, 13, 62), armR = new RotateTransform(-18, 87, 62);
        readonly TranslateTransform faceLook = new TranslateTransform(), eyeLook = new TranslateTransform();
        readonly ScaleTransform eyeScale = new ScaleTransform(1, 1, 50, 60);
        readonly ScaleTransform blink = new ScaleTransform(1, 1);
        readonly ScaleTransform mouthScale = new ScaleTransform(1, 1, 50, 70);

        readonly Canvas eyes = new Canvas(), mouthOpen = new Canvas();
        UIElement happyEyes, sleepEyes, browFocus, browWorried, smile, flatMouth, grin, cheeks;
        readonly Dictionary<Prop, FrameworkElement> props = new Dictionary<Prop, FrameworkElement>();
        readonly SolidColorBrush cupBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0xA6, 0xFF));

        public Mascot()
        {
            Stretch = Stretch.Uniform;
            Child = root;
            root.ClipToBounds = false;

            var tg = new TransformGroup();
            tg.Children.Add(flip); tg.Children.Add(squash); tg.Children.Add(breathe); tg.Children.Add(tilt); tg.Children.Add(bob);
            body.RenderTransform = tg;

            BuildTail();
            BuildFeet();
            BuildEars();
            BuildBody();
            BuildFace();
            BuildArms();
            BuildProps();

            root.Children.Add(body);
            root.Children.Add(fx);
            InitBehavior();
        }

        // ---------------- helpers ----------------
        static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        static SolidColorBrush B(string hex) { return Frozen((Color)ColorConverter.ConvertFromString(hex)); }
        static T At<T>(T el, double x, double y) where T : UIElement { Canvas.SetLeft(el, x); Canvas.SetTop(el, y); return el; }
        static Path P(string data, Brush fill, Brush stroke = null, double th = 0)
        {
            var p = new Path { Data = Geometry.Parse(data), Fill = fill };
            if (stroke != null)
            {
                p.Stroke = stroke; p.StrokeThickness = th;
                p.StrokeStartLineCap = p.StrokeEndLineCap = PenLineCap.Round; p.StrokeLineJoin = PenLineJoin.Round;
            }
            return p;
        }
        static Path S(string data, double th, Brush stroke = null) { var p = P(data, null, stroke ?? InkBrush, th); p.Opacity = 0; return p; }
        static Ellipse E(double w, double h, Brush fill, Brush stroke = null, double th = 0)
        {
            return new Ellipse { Width = w, Height = h, Fill = fill, Stroke = stroke, StrokeThickness = th };
        }
        static Brush BodyFill()
        {
            var g = new RadialGradientBrush { GradientOrigin = new Point(0.38, 0.28), Center = new Point(0.45, 0.4), RadiusX = 0.78, RadiusY = 0.78 };
            g.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xFE, 0xFB), 0));
            g.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xF2, 0xE8), 0.55));
            g.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xDD, 0xCC), 1));
            g.Freeze();
            return g;
        }

        // ---------------- body parts ----------------
        void BuildTail()
        {
            var t = new Canvas { RenderTransform = tailRot };
            t.Children.Add(P("M82,82 C97,83 101,68 94,61 C90,57 85,60 88,64", null, Line, 9.5));
            t.Children.Add(P("M82,82 C97,83 101,68 94,61 C90,57 85,60 88,64", null, B("#FFEEDF"), 7));
            body.Children.Add(t);
        }

        void BuildFeet()
        {
            var fill = B("#FFE6D6");
            var l = At(E(17, 10, fill, Line, 1.4), 27, 84); l.RenderTransform = footL;
            var r = At(E(17, 10, fill, Line, 1.4), 56, 84); r.RenderTransform = footR;
            body.Children.Add(l); body.Children.Add(r);
        }

        void BuildEars()
        {
            var fill = BodyFill();
            var l = new Canvas { RenderTransform = earL };
            l.Children.Add(P("M17,49 C13,35 15,20 22,13 C31,17 39,25 43,33 Z", fill, Line, 1.6));
            l.Children.Add(P("M21,43 C19,34 20,25 24,20 C29,23 34,28 37,34 Z", Pink));
            var r = new Canvas { RenderTransform = earR };
            r.Children.Add(P("M83,49 C87,35 85,20 78,13 C69,17 61,25 57,33 Z", fill, Line, 1.6));
            r.Children.Add(P("M79,43 C81,34 80,25 76,20 C71,23 66,28 63,34 Z", Pink));
            body.Children.Add(l); body.Children.Add(r);
        }

        void BuildBody()
        {
            body.Children.Add(P("M50,28 C75,28 91,44 91,64 C91,83 74,91 50,91 C26,91 9,83 9,64 C9,44 25,28 50,28 Z", BodyFill(), Line, 1.6));
            var belly = At(E(36, 22, B("#FFFFFF")), 32, 67);
            belly.Opacity = 0.5;
            body.Children.Add(belly);
        }

        void BuildFace()
        {
            var face = new Canvas { RenderTransform = faceLook };
            var ch = new Canvas();
            ch.Children.Add(At(E(13, 7.5, B("#FF9AB0")), 18.5, 65));
            ch.Children.Add(At(E(13, 7.5, B("#FF9AB0")), 68.5, 65));
            ch.Opacity = 0.55; cheeks = ch;
            face.Children.Add(ch);

            var eg = new TransformGroup(); eg.Children.Add(eyeScale); eg.Children.Add(eyeLook);
            eyes.RenderTransform = eg;
            eyes.Children.Add(Eye(29.5));
            eyes.Children.Add(Eye(57.5));
            face.Children.Add(eyes);

            happyEyes = S("M30,61 Q36,53.5 42,61 M58,61 Q64,53.5 70,61", 3);
            sleepEyes = S("M30,59.5 Q36,64.5 42,59.5 M58,59.5 Q64,64.5 70,59.5", 2.6);
            browFocus = S("M29,49 L40.5,52.5 M71,49 L59.5,52.5", 2.4);
            browWorried = S("M29.5,52.5 L40.5,48.5 M70.5,52.5 L59.5,48.5", 2.4);
            smile = S("M45.5,67.2 Q47.7,70.6 50,67.6 Q52.3,70.6 54.5,67.2", 2.0);
            flatMouth = S("M46.5,68.6 L53.5,68.6", 2.2);
            var g = new Canvas();
            g.Children.Add(P("M44.5,66.8 Q50,76.5 55.5,66.8 Q50,68.4 44.5,66.8 Z", InkBrush));
            g.Children.Add(P("M47,71.6 Q50,74.4 53,71.6 Q50,70.4 47,71.6 Z", Tongue));
            g.Opacity = 0; grin = g;
            mouthOpen.RenderTransform = mouthScale;
            mouthOpen.Children.Add(At(E(8.5, 9.5, InkBrush), 45.75, 65.6));
            mouthOpen.Children.Add(At(E(5, 3.4, Tongue), 47.5, 70.4));
            mouthOpen.Opacity = 0;
            foreach (var e in new[] { happyEyes, sleepEyes, browFocus, browWorried, smile, flatMouth, grin }) face.Children.Add(e);
            face.Children.Add(mouthOpen);
            // tiny nose
            face.Children.Add(P("M48.3,64.4 Q50,63.4 51.7,64.4 Q50.9,66.3 50,66.5 Q49.1,66.3 48.3,64.4 Z", B("#FF8FA6")));
            body.Children.Add(face);
        }

        Canvas Eye(double x)
        {
            var c = new Canvas { Width = 13, Height = 15, RenderTransform = blink, RenderTransformOrigin = new Point(0.5, 0.55) };
            var iris = new LinearGradientBrush(Color.FromRgb(0x1C, 0x16, 0x30), Color.FromRgb(0x4D, 0x3E, 0x80), 90);
            iris.Freeze();
            c.Children.Add(E(13, 15, iris));
            c.Children.Add(At(E(5.4, 5.4, Brushes.White), 6.4, 2));
            c.Children.Add(At(E(2.5, 2.5, Brushes.White), 2.5, 9.6));
            var glint = At(E(5, 2, B("#9C8CFF")), 4, 12.2); glint.Opacity = 0.6;
            c.Children.Add(glint);
            return At(c, x, 52.5);
        }

        void BuildArms()
        {
            var fill = BodyFill();
            var l = At(E(11, 17, fill, Line, 1.5), 7.5, 60); l.RenderTransform = armL;
            var r = At(E(11, 17, fill, Line, 1.5), 81.5, 60); r.RenderTransform = armR;
            // arms are positioned in body space, transforms are relative to their own box -> use offset centres
            armL.CenterX = 5.5; armL.CenterY = 2; armR.CenterX = 5.5; armR.CenterY = 2;
            body.Children.Add(l); body.Children.Add(r);
        }

        // ---------------- props ----------------
        void AddProp(Prop p, FrameworkElement el)
        {
            el.Visibility = Visibility.Collapsed;
            el.IsHitTestVisible = false;
            props[p] = el;
            body.Children.Add(el);
        }

        void BuildProps()
        {
            var dark = B("#34303F");

            var hp = new Canvas();
            hp.Children.Add(P("M15,55 C15,16 85,16 85,55", null, dark, 5));
            hp.Children.Add(At(new Rectangle { Width = 12, Height = 20, RadiusX = 5, RadiusY = 5, Fill = cupBrush, Stroke = dark, StrokeThickness = 1.5 }, 8, 46));
            hp.Children.Add(At(new Rectangle { Width = 12, Height = 20, RadiusX = 5, RadiusY = 5, Fill = cupBrush, Stroke = dark, StrokeThickness = 1.5 }, 80, 46));
            var shine1 = At(new Rectangle { Width = 3, Height = 10, RadiusX = 1.5, RadiusY = 1.5, Fill = Brushes.White, Opacity = 0.45 }, 10.5, 49);
            var shine2 = At(new Rectangle { Width = 3, Height = 10, RadiusX = 1.5, RadiusY = 1.5, Fill = Brushes.White, Opacity = 0.45 }, 82.5, 49);
            hp.Children.Add(shine1); hp.Children.Add(shine2);
            AddProp(Prop.Headphones, hp);

            var hat = new Canvas();
            hat.Children.Add(P("M50,5 L38.5,32 Q50,36 61.5,32 Z", B("#FF7EB6"), B("#E0569A"), 1.2));
            hat.Children.Add(P("M44.2,19 L55.8,19 M41.8,26 L58.2,26", null, B("#FFE27A"), 2.4));
            hat.Children.Add(At(E(9, 9, B("#FFE27A")), 45.5, 0));
            AddProp(Prop.PartyHat, hat);

            var cap = new Canvas();
            cap.Children.Add(P("M29,37 C29,19 45,10 61,13 C76,16 85,29 91,45 C84,39 76,34 68,33 C70,35 70,36 70,37 Z", B("#8C9BFF"), B("#6E7DE0"), 1.2));
            cap.Children.Add(At(new Rectangle { Width = 44, Height = 8, RadiusX = 4, RadiusY = 4, Fill = B("#E9ECFF") }, 28, 32));
            cap.Children.Add(At(E(10, 10, Brushes.White), 86, 41));
            AddProp(Prop.Nightcap, cap);

            var gl = new Canvas();
            var rim = B("#3B3352");
            gl.Children.Add(At(E(17, 17, Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), rim, 2.2), 27.5, 51.5));
            gl.Children.Add(At(E(17, 17, Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), rim, 2.2), 55.5, 51.5));
            gl.Children.Add(P("M44.5,59 Q50,56 55.5,59", null, rim, 2));
            AddProp(Prop.Glasses, gl);

            var band = new Canvas();
            band.Children.Add(P("M12,47 Q50,33 88,47", null, B("#FF5D73"), 6.5));
            band.Children.Add(P("M86,46 L97,40 M86,47 L96,53", null, B("#FF5D73"), 3.2));
            AddProp(Prop.Headband, band);

            var pencil = new Canvas { RenderTransform = Place(28, 74, 50) };
            pencil.Children.Add(At(new Rectangle { Width = 5.5, Height = 19, Fill = B("#FFC94D") }, 0, 4.5));
            pencil.Children.Add(At(new Rectangle { Width = 5.5, Height = 4.5, RadiusX = 1.5, RadiusY = 1.5, Fill = B("#FF9AB0") }, 0, 0));
            pencil.Children.Add(At(new Rectangle { Width = 5.5, Height = 1.6, Fill = B("#C9C9D6") }, 0, 4.2));
            pencil.Children.Add(P("M0,23.5 L5.5,23.5 L2.75,29 Z", B("#F3D9B1")));
            pencil.Children.Add(P("M1.8,27 L3.7,27 L2.75,29 Z", InkBrush));
            AddProp(Prop.Pencil, pencil);

            var wrench = new Canvas { RenderTransform = Place(-25, 80, 52) };
            var steel = B("#B5BDCC");
            wrench.Children.Add(At(new Rectangle { Width = 5, Height = 22, RadiusX = 2.5, RadiusY = 2.5, Fill = steel }, 2, 8));
            wrench.Children.Add(At(E(13, 13, steel), -2, -3));
            wrench.Children.Add(At(new Rectangle { Width = 4, Height = 6, Fill = B("#1A1A22") }, 2.5, -4));
            AddProp(Prop.Wrench, wrench);

            var cam = new Canvas { RenderTransform = Place(0, 58, 63) };
            cam.Children.Add(At(new Rectangle { Width = 8, Height = 5, RadiusX = 1.5, RadiusY = 1.5, Fill = B("#3A3A48") }, 4, 1));
            cam.Children.Add(At(new Rectangle { Width = 24, Height = 16, RadiusX = 4, RadiusY = 4, Fill = B("#3A3A48") }, 0, 4));
            cam.Children.Add(At(E(11, 11, B("#9FD3FF"), B("#20202A"), 2.2), 6.5, 6.5));
            cam.Children.Add(At(E(3, 3, B("#FFE27A")), 18.5, 6.5));
            AddProp(Prop.Camera, cam);

            var paper = new Canvas { RenderTransform = Place(-6, 39, 67) };
            paper.Children.Add(P("M0,0 L14,0 L20,6 L20,25 L0,25 Z", Brushes.White, B("#C9CEDC"), 1.2));
            paper.Children.Add(P("M14,0 L14,6 L20,6", null, B("#C9CEDC"), 1.2));
            paper.Children.Add(P("M4,11 L16,11 M4,15.5 L16,15.5 M4,20 L12,20", null, B("#B8BED0"), 1.5));
            AddProp(Prop.Paper, paper);

            var folder = new Canvas { RenderTransform = Place(-8, 63, 64) };
            folder.Children.Add(P("M0,3 Q0,0 3,0 L9,0 L12,3 L22,3 Q25,3 25,6 L25,18 Q25,21 22,21 L3,21 Q0,21 0,18 Z", B("#F0B443")));
            folder.Children.Add(P("M0,8 L25,8 L25,18 Q25,21 22,21 L3,21 Q0,21 0,18 Z", B("#FFD77F")));
            AddProp(Prop.Folder, folder);
            BuildMoreProps();
        }

        static Transform Place(double angle, double x, double y)
        {
            var g = new TransformGroup();
            g.Children.Add(new RotateTransform(angle));
            g.Children.Add(new TranslateTransform(x, y));
            return g;
        }
    }
}
