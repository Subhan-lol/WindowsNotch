using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace NotchIsland
{
    /// <summary>Vector toys that sit on the taskbar next to Mochi. Sizes are in DIPs.</summary>
    static class BuddyToys
    {
        static readonly Random rnd = new Random();
        static SolidColorBrush B(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
        static Path P(string d, Brush fill, Brush stroke = null, double th = 0)
        {
            var p = new Path { Data = Geometry.Parse(d), Fill = fill, Stroke = stroke, StrokeThickness = th };
            p.StrokeStartLineCap = p.StrokeEndLineCap = PenLineCap.Round; p.StrokeLineJoin = PenLineJoin.Round;
            return p;
        }
        static T At<T>(T el, double x, double y) where T : UIElement { Canvas.SetLeft(el, x); Canvas.SetTop(el, y); return el; }

        public static Canvas Yarn()
        {
            var c = new Canvas { Width = 18, Height = 18, Cursor = System.Windows.Input.Cursors.Hand, Background = Brushes.Transparent };
            var spin = new Canvas { Width = 18, Height = 18, RenderTransform = new RotateTransform(0, 9, 9) };
            spin.Children.Add(new Ellipse { Width = 18, Height = 18, Fill = B("#FF8FB1"), Stroke = B("#E0668F"), StrokeThickness = 1 });
            spin.Children.Add(P("M3,6 Q9,2 15,6 M2,10 Q9,6 16,10 M4,14 Q9,11 14,14 M6,2 Q4,9 7,16", null, B("#FFD3E2"), 1.1));
            c.Children.Add(spin);
            c.Children.Add(P("M16,13 Q22,16 20,18 Q26,19 30,17", null, B("#FF8FB1"), 1.2));
            c.Tag = spin.RenderTransform;
            return c;
        }

        public static Canvas Butterfly(out ScaleTransform wings)
        {
            string[] palette = { "#B8A6FF", "#FFB36B", "#7FD8FF", "#FF8FB1", "#9BE89E" };
            var col = B(palette[rnd.Next(palette.Length)]);
            var c = new Canvas { Width = 18, Height = 14, Cursor = System.Windows.Input.Cursors.Hand, Background = Brushes.Transparent };
            wings = new ScaleTransform(1, 1, 9, 7);
            var w = new Canvas { RenderTransform = wings };
            w.Children.Add(At(new Ellipse { Width = 8, Height = 9, Fill = col }, 1, 0));
            w.Children.Add(At(new Ellipse { Width = 8, Height = 9, Fill = col }, 9, 0));
            w.Children.Add(At(new Ellipse { Width = 6, Height = 6, Fill = col, Opacity = 0.8 }, 3, 7));
            w.Children.Add(At(new Ellipse { Width = 6, Height = 6, Fill = col, Opacity = 0.8 }, 9, 7));
            c.Children.Add(w);
            c.Children.Add(At(new Ellipse { Width = 2.4, Height = 10, Fill = B("#3A3350") }, 7.8, 2));
            c.Children.Add(P("M9,2.5 L7,-1 M9,2.5 L11,-1", null, B("#3A3350"), 0.8));
            var flap = new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(110)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            Timeline.SetDesiredFrameRate(flap, 30);
            wings.BeginAnimation(ScaleTransform.ScaleXProperty, flap);
            return c;
        }

        public static Canvas Bubble(double d)
        {
            var c = new Canvas { Width = d, Height = d, Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
            var g = new RadialGradientBrush { GradientOrigin = new Point(0.35, 0.3) };
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), 0));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0x30, 0xB8, 0xE6, 0xFF), 0.75));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0xB0, 0xE0, 0xC8, 0xFF), 1));
            c.Children.Add(new Ellipse { Width = d, Height = d, Fill = g, Stroke = new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xFF, 0xFF)), StrokeThickness = 0.8 });
            c.Children.Add(At(new Ellipse { Width = d * 0.28, Height = d * 0.18, Fill = Brushes.White, Opacity = 0.85, RenderTransform = new RotateTransform(-30) }, d * 0.2, d * 0.2));
            c.RenderTransform = new ScaleTransform(1, 1, d / 2, d / 2);
            return c;
        }

        public static Canvas Balloon()
        {
            var c = new Canvas { Width = 22, Height = 50 };
            c.Children.Add(new Ellipse { Width = 20, Height = 24, Fill = B("#FF6F91") });
            c.Children.Add(At(new Ellipse { Width = 4, Height = 7, Fill = Brushes.White, Opacity = 0.6 }, 4, 4));
            c.Children.Add(P("M8.5,24 L11.5,24 L10,21.5 Z", B("#FF6F91")));
            c.Children.Add(P("M10,24 C14,32 6,40 11,50", null, B("#9A9AA6"), 0.9));
            return c;
        }

        /// <summary>Box back (behind Mochi) and front (in front), 76 x 30.</summary>
        public static void Box(out Canvas back, out Canvas front)
        {
            back = new Canvas { Width = 76, Height = 30 };
            back.Children.Add(P("M2,4 L74,4 L70,10 L6,10 Z", B("#8E5E3A")));
            front = new Canvas { Width = 76, Height = 30, RenderTransform = new RotateTransform(0, 38, 30) };
            front.Children.Add(P("M-8,-6 L6,8 L2,10 L-10,-2 Z", B("#C98F57"), B("#A8743F"), 0.8));
            front.Children.Add(P("M84,-6 L70,8 L74,10 L86,-2 Z", B("#C98F57"), B("#A8743F"), 0.8));
            front.Children.Add(new Rectangle { Width = 76, Height = 30, RadiusX = 2, RadiusY = 2, Fill = B("#D9A066"), Stroke = B("#B07A45"), StrokeThickness = 1 });
            front.Children.Add(P("M0,8 L76,8", null, B("#C68C52"), 1));
            front.Children.Add(P("M38,14 C36,11 31,12 32,16 C33,19 38,22 38,22 C38,22 43,19 44,16 C45,12 40,11 38,14 Z", B("#FF8FA6")));
        }

        public static Canvas Plane()
        {
            var c = new Canvas { Width = 24, Height = 14, RenderTransform = new RotateTransform(0, 12, 7) };
            c.Children.Add(P("M0,6 L24,0 L9,9 Z", Brushes.White, B("#C9CEDC"), 0.8));
            c.Children.Add(P("M9,9 L24,0 L11,13 Z", B("#E8EBF4"), B("#C9CEDC"), 0.8));
            return c;
        }

        public static Ellipse Ball(string hex)
        {
            return new Ellipse { Width = 10, Height = 10, Fill = B(hex), Stroke = B("#FFFFFF"), StrokeThickness = 1 };
        }

        public static Canvas Dot()
        {
            var c = new Canvas { Width = 12, Height = 12 };
            c.Children.Add(new Ellipse { Width = 12, Height = 12, Fill = new RadialGradientBrush(Color.FromArgb(0xA0, 0xFF, 0x30, 0x30), Color.FromArgb(0, 0xFF, 0x30, 0x30)) });
            c.Children.Add(At(new Ellipse { Width = 5, Height = 5, Fill = B("#FF2D2D") }, 3.5, 3.5));
            return c;
        }

        /// <summary>Pot (30 x 24) with a flower that grows out of it. Grow by animating the returned scale 0..1.</summary>
        public static Canvas Flower(out ScaleTransform grow)
        {
            var c = new Canvas { Width = 30, Height = 70 };
            grow = new ScaleTransform(0, 0, 15, 48);
            var plant = new Canvas { RenderTransform = grow };
            plant.Children.Add(P("M15,48 C13,36 17,28 15,18", null, B("#5FBF7A"), 2.2));
            plant.Children.Add(P("M15,38 C9,36 6,32 7,29 C11,30 14,33 15,38 Z", B("#86E6A4")));
            plant.Children.Add(P("M15,33 C21,31 24,27 23,24 C19,25 16,28 15,33 Z", B("#86E6A4")));
            for (int i = 0; i < 5; i++)
            {
                var petal = new Ellipse { Width = 8, Height = 10, Fill = B("#FF9AC1"), RenderTransform = new RotateTransform(i * 72, 4, 11) };
                plant.Children.Add(At(petal, 11, 7));
            }
            plant.Children.Add(At(new Ellipse { Width = 7, Height = 7, Fill = B("#FFD45C") }, 11.5, 14.5));
            c.Children.Add(plant);
            c.Children.Add(P("M2,46 L28,46 L25,68 L5,68 Z", B("#E07A5F"), B("#C4614A"), 1));
            c.Children.Add(At(new Rectangle { Width = 30, Height = 5, RadiusX = 2, RadiusY = 2, Fill = B("#EE8A6E") }, 0, 44));
            return c;
        }

        public static Canvas Fish(bool boot)
        {
            var c = new Canvas { Width = 24, Height = 14, RenderTransform = new RotateTransform(0, 12, 7) };
            if (boot)
            {
                c.Children.Add(P("M4,0 L12,0 L12,8 L22,9 Q24,14 20,14 L4,14 Z", B("#7A5A44"), B("#5A4030"), 1));
                c.Children.Add(P("M4,3 L12,3", null, B("#5A4030"), 1));
                return c;
            }
            c.Children.Add(P("M2,7 Q10,-1 18,7 Q10,15 2,7 Z", B("#FFB36B"), B("#E0904A"), 0.8));
            c.Children.Add(P("M18,7 L24,2 L23,7 L24,12 Z", B("#FF9A4D")));
            c.Children.Add(At(new Ellipse { Width = 2.4, Height = 2.4, Fill = B("#2A2238") }, 5, 5));
            return c;
        }

        public static Canvas Skateboard()
        {
            var c = new Canvas { Width = 64, Height = 10 };
            c.Children.Add(new Rectangle { Width = 64, Height = 5, RadiusX = 2.5, RadiusY = 2.5, Fill = B("#FF8F6B"), Stroke = B("#D96A48"), StrokeThickness = 1 });
            c.Children.Add(At(new Ellipse { Width = 6, Height = 6, Fill = B("#3A3A48") }, 8, 4));
            c.Children.Add(At(new Ellipse { Width = 6, Height = 6, Fill = B("#3A3A48") }, 50, 4));
            return c;
        }

        public static Ellipse Drop()
        {
            return new Ellipse { Width = 3, Height = 4, Fill = B("#7FC8FF") };
        }

        public static TextBlock Text(string s, string hex, double size)
        {
            return new TextBlock { Text = s, FontFamily = new FontFamily("Segoe UI Symbol"), FontWeight = FontWeights.Bold, FontSize = size, Foreground = B(hex), IsHitTestVisible = false };
        }
    }
}
