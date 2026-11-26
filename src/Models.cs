using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace NotchIsland
{
    /// <summary>The notch's black body. Radius is animatable (CornerRadius itself isn't).</summary>
    public class NotchBody : Border
    {
        public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(
            "Radius", typeof(double), typeof(NotchBody), new PropertyMetadata(14.0, (d, e) =>
            {
                double r = (double)e.NewValue;
                ((NotchBody)d).CornerRadius = new CornerRadius(0, 0, r, r);
            }));

        public double Radius { get { return (double)GetValue(RadiusProperty); } set { SetValue(RadiusProperty, value); } }

        public NotchBody() { CornerRadius = new CornerRadius(0, 0, 14, 14); }
    }

    public class ClipItem : INotifyPropertyChanged
    {
        public string Text { get; private set; }
        public string Preview { get; private set; }
        public DateTime Time;
        bool pinned;

        public ClipItem(string text)
        {
            Text = text;
            var p = text.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
            while (p.Contains("  ")) p = p.Replace("  ", " ");
            Preview = p.Length > 140 ? p.Substring(0, 140) + "…" : p;
            Time = DateTime.Now;
        }

        public bool Pinned
        {
            get { return pinned; }
            set { pinned = value; Notify("Pinned"); Notify("PinGlyph"); Notify("PinOpacity"); }
        }
        public string PinGlyph { get { return pinned ? "" : ""; } }
        public double PinOpacity { get { return pinned ? 1.0 : 0.5; } }

        public string Age
        {
            get
            {
                var d = DateTime.Now - Time;
                if (d.TotalSeconds < 60) return "now";
                if (d.TotalMinutes < 60) return (int)d.TotalMinutes + "m";
                if (d.TotalHours < 24) return (int)d.TotalHours + "h";
                return (int)d.TotalDays + "d";
            }
        }
        public void Touch() { Time = DateTime.Now; Notify("Age"); }
        public void RefreshAge() { Notify("Age"); }

        public event PropertyChangedEventHandler PropertyChanged;
        void Notify(string p) { var h = PropertyChanged; if (h != null) h(this, new PropertyChangedEventArgs(p)); }
    }
}
