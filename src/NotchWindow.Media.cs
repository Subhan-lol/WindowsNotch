using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace NotchIsland
{
    public partial class NotchWindow
    {
        DispatcherTimer vizTimer, posTimer;
        readonly ScaleTransform[] bars = new ScaleTransform[4];
        readonly double[] barNoise = new double[4];
        readonly Random rnd = new Random();
        double level;
        double scrubFrac;
        bool volScrubbing;
        Grid seekBar, volBar;
        Border seekFill, volFill;
        TranslateTransform knobShift;
        ScaleTransform knobScale;

        void WireMedia()
        {
            for (int i = 0; i < 4; i++) bars[i] = (ScaleTransform)E<Rectangle>("Bar" + i).RenderTransform;
            seekBar = E<Grid>("SeekBar"); volBar = E<Grid>("VolBar");
            seekFill = E<Border>("SeekFill"); volFill = E<Border>("VolFill");
            knobShift = E<TranslateTransform>("KnobShift"); knobScale = E<ScaleTransform>("KnobScale");

            // ~30 fps visualizer, runs only while music plays and the bars/mascot are on screen
            vizTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            vizTimer.Tick += VizTick;
            posTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
            posTimer.Tick += (s, e) => UpdatePosition();

            media.Changed += OnMediaChanged;
            media.TrackChanged += OnTrackChanged;
            audio.VolumeChanged += OnVolumeChanged;

            E<Button>("PlayBtn").Click += (s, e) => { media.PlayPause(); React(m => m.Nod()); };
            E<Button>("NextBtn").Click += (s, e) => { media.Next(); React(m => m.Spin()); };
            E<Button>("PrevBtn").Click += (s, e) => { media.Prev(); React(m => m.Wiggle()); };
            E<Button>("ShuffleBtn").Click += (s, e) => { media.ToggleShuffle(); React(m => m.Spin()); };
            E<Button>("RepeatBtn").Click += (s, e) => { media.CycleRepeat(); React(m => m.Wiggle()); };
            E<Button>("MuteBtn").Click += (s, e) => { bool wasMuted = audio.Muted; audio.ToggleMute(); React(m => { if (wasMuted) m.Hello(); else m.Shh(1200); }); };
            E<Border>("ArtBox").MouseLeftButtonUp += (s, e) => { media.OpenSourceApp(); pinnedOpen = false; ScheduleCollapse(150); };

            // ---- seek bar: click or drag anywhere on it ----
            seekBar.MouseEnter += (s, e) => KnobGrow(true);
            seekBar.MouseLeave += (s, e) => { if (!scrubbing) KnobGrow(false); };
            seekBar.MouseLeftButtonDown += (s, e) =>
            {
                if (!media.HasTrack || media.Duration <= TimeSpan.Zero) return;
                scrubbing = true;
                seekBar.CaptureMouse();
                Scrub(e.GetPosition(seekBar).X);
                e.Handled = true;
            };
            seekBar.MouseMove += (s, e) => { if (scrubbing) Scrub(e.GetPosition(seekBar).X); };
            seekBar.MouseLeftButtonUp += (s, e) =>
            {
                if (!scrubbing) return;
                scrubbing = false;
                seekBar.ReleaseMouseCapture();
                media.Seek(TimeSpan.FromTicks((long)(scrubFrac * media.Duration.Ticks)));
                if (!seekBar.IsMouseOver) KnobGrow(false);
                e.Handled = true;
            };
            seekBar.LostMouseCapture += (s, e) => { scrubbing = false; };

            // ---- volume bar ----
            volBar.MouseLeftButtonDown += (s, e) => { volScrubbing = true; volBar.CaptureMouse(); VolScrub(e.GetPosition(volBar).X); e.Handled = true; };
            volBar.MouseMove += (s, e) => { if (volScrubbing) VolScrub(e.GetPosition(volBar).X); };
            volBar.MouseLeftButtonUp += (s, e) => { volScrubbing = false; volBar.ReleaseMouseCapture(); };
            volBar.LostMouseCapture += (s, e) => { volScrubbing = false; };
        }

        void KnobGrow(bool on)
        {
            Anim.To(knobScale, ScaleTransform.ScaleXProperty, on ? 1.0 : 0.7, 140, Anim.OutBack);
            Anim.To(knobScale, ScaleTransform.ScaleYProperty, on ? 1.0 : 0.7, 140, Anim.OutBack);
        }

        void Scrub(double x)
        {
            double w = Math.Max(1, seekBar.ActualWidth);
            scrubFrac = Math.Max(0, Math.Min(1, x / w));
            SetSeekVisual(scrubFrac);
            E<TextBlock>("PosText").Text = FormatTime(TimeSpan.FromTicks((long)(scrubFrac * media.Duration.Ticks)));
            if (!buddyOut) big.LookAt(0.5 + scrubFrac * 0.5, 0.6); // watches the playhead
        }

        void SetSeekVisual(double frac)
        {
            double w = seekBar.ActualWidth;
            seekFill.Width = Math.Max(0, frac * w);
            knobShift.X = frac * w - 6;
        }

        void VolScrub(double x)
        {
            double f = Math.Max(0, Math.Min(1, x / Math.Max(1, volBar.ActualWidth)));
            audio.SetVolume((float)f);
            volFill.Width = f * volBar.ActualWidth;
        }

        void OnMediaChanged()
        {
            UpdateMood();
            UpdateAccent();
            if (expanded) UpdateMediaUI();
            else if (toast == ToastKind.None || toast == ToastKind.Track) ApplyCompact();
            UpdateViz();
        }

        void OnTrackChanged()
        {
            if ((DateTime.Now - startedAt).TotalSeconds < 4) return;
            if (expanded || buddyOut) React(m => { m.Spin(); m.Hearts(1); });
            if (expanded) return;
            if (Settings.Get("TrackPopups") && media.IsPlaying)
                ShowToast(ToastKind.Track, 3400, media.Title, media.Artist, null);
        }

        void OnVolumeChanged(bool self)
        {
            UpdateVolumeUI();
            if (!expanded && Settings.Get("VolumeHud") && (DateTime.Now - startedAt).TotalSeconds > 3)
                ShowToast(ToastKind.Volume, 1500, null, null, null);
            if (!audio.Muted && audio.Volume >= 0.92) React(m => m.CoverEars(1100));
            else if (audio.Muted || audio.Volume <= 0.001) React(m => m.Shh(1100));
        }

        string VolumeGlyph()
        {
            if (audio.Muted || audio.Volume <= 0.001) return "";
            if (audio.Volume < 0.34) return "";
            if (audio.Volume < 0.67) return "";
            return "";
        }

        void UpdateVolumeUI()
        {
            E<Button>("MuteBtn").Content = VolumeGlyph();
            if (!volScrubbing) volFill.Width = (audio.Muted ? 0 : audio.Volume) * Math.Max(76, volBar.ActualWidth);
            if (toast == ToastKind.Volume) ApplyCompact();
        }

        DispatcherTimer accentFade;
        Color accentFrom, accentTo;
        int accentStep;

        /// <summary>
        /// Resource brushes get frozen by WPF, so the glow fades by swapping in a few fresh brushes
        /// (10 steps, once per song) instead of a live ColorAnimation.
        /// </summary>
        void UpdateAccent()
        {
            var target = Settings.Get("AlbumColors") ? media.Accent : MediaService.DefaultAccent;
            big.Accent = target; mini.Accent = target;
            if (buddy != null) buddy.Mascot.Accent = target;
            if (target == accentTo && (accentColor == target || (accentFade != null && accentFade.IsEnabled))) return;
            accentFrom = accentColor; accentTo = target; accentStep = 0;
            if (accentFade == null)
            {
                accentFade = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(45) };
                accentFade.Tick += (s, e) =>
                {
                    accentStep++;
                    double t = Math.Min(1, accentStep / 10.0);
                    t = t * t * (3 - 2 * t);
                    accentColor = Color.FromRgb(
                        (byte)(accentFrom.R + (accentTo.R - accentFrom.R) * t),
                        (byte)(accentFrom.G + (accentTo.G - accentFrom.G) * t),
                        (byte)(accentFrom.B + (accentTo.B - accentFrom.B) * t));
                    root.Resources["Accent"] = new SolidColorBrush(accentColor);
                    if (accentStep >= 10) { accentFade.Stop(); accentColor = accentTo; }
                };
            }
            if (!IsVisible || SnapshotDir != null)
            {
                accentColor = target; root.Resources["Accent"] = new SolidColorBrush(target);
                return;
            }
            accentFade.Start();
        }

        void UpdateMediaUI()
        {
            bool has = media.HasTrack;
            if (expanded && tab == "home")
            {
                E<TextBlock>("TitleText").Text = has ? media.Title : "Nothing playing";
                E<TextBlock>("ArtistText").Text = has ? (media.Artist.Length > 0 ? media.Artist : media.Album) : "Play a song in Spotify (or anything) and it shows up here";
                string src = media.AppName.Length > 0 ? media.AppName.ToUpperInvariant() : "";
                if (has && media.Album.Length > 0 && media.Album != media.Title) src += (src.Length > 0 ? "  ·  " : "") + media.Album;
                E<TextBlock>("SourceText").Text = src;
                E<ImageBrush>("ArtBrush").ImageSource = media.Art;
                E<TextBlock>("ArtPlaceholder").Visibility = media.Art == null ? Visibility.Visible : Visibility.Collapsed;
                E<Button>("PlayBtn").Content = media.IsPlaying ? "" : "";
                E<Button>("NextBtn").IsEnabled = media.CanNext;
                E<Button>("PrevBtn").IsEnabled = media.CanPrev;
                var sh = E<Button>("ShuffleBtn"); var rp = E<Button>("RepeatBtn");
                sh.IsEnabled = media.CanShuffle; rp.IsEnabled = media.CanRepeat;
                Brush accent = new SolidColorBrush(accentTo == default(Color) ? accentColor : accentTo);
                sh.Foreground = media.Shuffle ? accent : (Brush)Application.Current.Resources["FgDim"];
                rp.Foreground = media.Repeat != 0 ? accent : (Brush)Application.Current.Resources["FgDim"];
                rp.Content = media.Repeat == 1 ? "" : "";
                seekBar.Opacity = has && media.Duration > TimeSpan.Zero ? 1 : 0.35;
                UpdatePosition();
                UpdateVolumeUI();
                if (!posTimer.IsEnabled) posTimer.Start();
            }
            else posTimer.Stop();
        }

        void UpdatePosition()
        {
            if (!expanded || tab != "home") { posTimer.Stop(); return; }
            if (scrubbing) return;
            var d = media.Duration;
            var p = media.Position;
            E<TextBlock>("PosText").Text = FormatTime(p);
            E<TextBlock>("DurText").Text = d > TimeSpan.Zero ? FormatTime(d) : "–:––";
            SetSeekVisual(d > TimeSpan.Zero ? p.TotalMilliseconds / d.TotalMilliseconds : 0);
        }

        // ---------------- visualizer ----------------
        void UpdateViz()
        {
            bool want = media.IsPlaying && media.HasTrack && IsVisible && !hiddenFullscreen && Settings.Get("Visualizer");
            if (want && !vizTimer.IsEnabled) vizTimer.Start();
            else if (!want && vizTimer.IsEnabled)
            {
                vizTimer.Stop();
                level = 0;
                for (int i = 0; i < 4; i++) bars[i].ScaleY = 0.2;
                mini.SetBeat(0); big.SetBeat(0);
                if (buddy != null) buddy.Mascot.SetBeat(0);
            }
        }

        void VizTick(object sender, EventArgs e)
        {
            double p = audio.Peak();
            // fast attack, slow release, plus a little shaping so quiet songs still move
            p = Math.Sqrt(Math.Max(0, Math.Min(1, p)));
            level = p > level ? level + (p - level) * 0.6 : level * 0.86;
            if (!expanded)
            {
                for (int i = 0; i < 4; i++)
                {
                    barNoise[i] += (rnd.NextDouble() - barNoise[i]) * 0.35;
                    double v = level * (0.45 + 0.75 * barNoise[i]);
                    double target = 0.18 + 0.82 * Math.Min(1, v);
                    bars[i].ScaleY += (target - bars[i].ScaleY) * 0.55; // eased, not jumpy
                }
                mini.SetBeat(level);
            }
            else big.SetBeat(level);
            if (buddyOut) buddy.Mascot.SetBeat(level);
        }
    }
}
