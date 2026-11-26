using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace NotchIsland
{
    /// <summary>
    /// `NotchIsland.exe --snapshot <folder>` renders every state to PNGs with demo data and exits.
    /// Never touches the saved pocket, notes or settings.
    /// </summary>
    public partial class NotchWindow
    {
        public static string SnapshotDir;

        async void RunSnapshots()
        {
            try
            {
                Directory.CreateDirectory(SnapshotDir);
                if (Environment.GetEnvironmentVariable("NOTCH_SNAP_ONLY") == "dance") { await DanceTest(); Application.Current.Shutdown(); return; }
                if (Environment.GetEnvironmentVariable("NOTCH_SNAP_ONLY") == "qr")
                {
                    shelf.ReadOnly = true;
                    string[] urls = { "http://192.168.1.20:47800/AbCdEfGhJkLm/", "http://10.0.0.5:47801/x/", "http://192.168.100.200:47805/ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ/" };
                    for (int i = 0; i < urls.Length; i++)
                    {
                        var bmp = QrCode.Render(QrCode.Encode(urls[i]), 8, Colors.Black, Colors.White);
                        var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp));
                        using (var fs = File.Create(System.IO.Path.Combine(SnapshotDir, "qr" + i + ".png"))) enc.Save(fs);
                    }
                    File.WriteAllLines(System.IO.Path.Combine(SnapshotDir, "qr-expected.txt"), urls);
                    // the panel itself, with a sample link (no server is started)
                    shelf.Add(new[] { System.Reflection.Assembly.GetExecutingAssembly().Location });
                    Expand("shelf");
                    E<TextBlock>("PhoneUrl").Text = urls[0];
                    E<Image>("PhoneQr").Source = QrCode.Render(QrCode.Encode(urls[0]), 6, Colors.Black, Colors.White);
                    E<TextBlock>("PhoneStatus").Text = "Scan with your iPhone camera. Phone and laptop need the same Wi-Fi.";
                    E<Grid>("PhonePanel").Visibility = Visibility.Visible;
                    await Shot("19-phone-panel", 900);
                    E<Grid>("PhonePanel").Visibility = Visibility.Collapsed;
                    await Shot("20-shelf-toolbar", 300);
                    Application.Current.Shutdown(); return;
                }
                if (Environment.GetEnvironmentVariable("NOTCH_SNAP_ONLY") == "delivery")
                {
                    shelf.ReadOnly = true;
                    mini.Active = true;
                    var art = FakeArt();
                    string photo = System.IO.Path.Combine(SnapshotDir, "IMG_0042.png");
                    var penc = new PngBitmapEncoder(); penc.Frames.Add(BitmapFrame.Create(art));
                    using (var fs = File.Create(photo)) penc.Save(fs);
                    long total = 6291456;
                    OnUploadStarted(1, "IMG_0042.png", total);
                    await Task.Delay(700);
                    OnUploadProgress(1, 2700000);
                    await Shot("21-receiving", 500);
                    OnUploadProgress(1, total);
                    shelf.Add(new[] { photo });
                    OnUploadFinished(1, photo, true);
                    await Shot("22-unpacking", 330);
                    await Shot("23-delivered", 1100);
                    toastTimer.Stop(); toast = ToastKind.None; ApplyCompact();
                    await Shot("24-new-badge", 900);
                    Application.Current.Shutdown(); return;
                }
                if (Environment.GetEnvironmentVariable("NOTCH_SNAP_ONLY") == "phonetest")
                {
                    shelf.ReadOnly = true;
                    PhoneLink.InboxDir = System.IO.Path.Combine(SnapshotDir, "inbox");
                    shelf.Add(new[] { System.Reflection.Assembly.GetExecutingAssembly().Location });
                    if (!StartPhone()) { File.WriteAllText(System.IO.Path.Combine(SnapshotDir, "url.txt"), "FAILED"); Application.Current.Shutdown(); return; }
                    var got = new System.Text.StringBuilder();
                    phone.Received += p => got.AppendLine("received " + System.IO.Path.GetFileName(p) + " pocket=" + shelf.Items.Count);
                    int progressEvents = 0;
                    bool shot = false;
                    phone.UploadStarted += (id, n, t) => got.AppendLine("started #" + id + " " + n + " total=" + t);
                    phone.UploadProgress += async (id, b) =>
                    {
                        progressEvents++;
                        if (!shot && b > 2000000) { shot = true; got.AppendLine("card visible=" + (deliveryCard.Visibility == Visibility.Visible) + " meta='" + E<TextBlock>("DelMeta").Text + "'"); await Shot("25-live-receiving", 50); }
                    };
                    phone.UploadFinished += (id, p, ok) => got.AppendLine("finished #" + id + " ok=" + ok + " progressEvents=" + progressEvents + " title='" + E<TextBlock>("DelTitle").Text + "'");
                    File.WriteAllText(System.IO.Path.Combine(SnapshotDir, "url.txt"), phone.Url);
                    await Task.Delay(20000);
                    File.WriteAllText(System.IO.Path.Combine(SnapshotDir, "events.txt"), got.ToString());
                    phone.Stop();
                    Application.Current.Shutdown(); return;
                }
                if (Environment.GetEnvironmentVariable("NOTCH_SNAP_ONLY") == "buddycpu")
                {
                    ToggleBuddy();
                    await Task.Delay(5000);
                    var me = System.Diagnostics.Process.GetCurrentProcess();
                    var c0 = me.TotalProcessorTime; var t0 = DateTime.Now;
                    await Task.Delay(40000);
                    me.Refresh();
                    double pct = (me.TotalProcessorTime - c0).TotalMilliseconds / (DateTime.Now - t0).TotalMilliseconds / Environment.ProcessorCount * 100;
                    File.WriteAllText(System.IO.Path.Combine(SnapshotDir, "buddycpu.txt"), string.Format("buddy out & playing, 40s: {0:0.00}% of machine CPU ({1:0}% of one core)", pct, pct * Environment.ProcessorCount));
                    buddy.GoHome(); await Task.Delay(800);
                    Application.Current.Shutdown(); return;
                }
                shelf.ReadOnly = true;
                Settings.ReadOnly = true;
                startedAt = DateTime.Now.AddMinutes(-1);
                mini.Active = true;

                await Shot("01-idle", 700);

                media.FakeSession = true;
                media.Title = "Midnight City"; media.Artist = "M83"; media.Album = "Hurry Up, We're Dreaming";
                media.AppId = "Spotify.exe"; media.AppName = "Spotify";
                media.Art = FakeArt();
                media.Accent = MediaService.ComputeAccent(media.Art);
                media.Duration = TimeSpan.FromSeconds(243);
                media.CanNext = media.CanPrev = media.CanSeek = media.CanShuffle = media.CanRepeat = true;
                media.Shuffle = true;
                media.IsPlaying = true;
                media.Seek(TimeSpan.FromSeconds(97));
                OnMediaChanged();
                await Task.Delay(900);
                vizTimer.Stop();
                double[] fake = { 0.55, 0.95, 0.7, 0.4 };
                for (int i = 0; i < 4; i++) bars[i].ScaleY = fake[i];
                mini.SetBeat(0.5);
                await Shot("02-music", 100);

                ShowToast(ToastKind.Track, 9000, media.Title, media.Artist, null);
                await Shot("03-track-popup", 700);

                ShowToast(ToastKind.Volume, 9000, null, null, null);
                await Shot("04-volume", 700);

                ShowToast(ToastKind.Battery, 9000, "Charging", "78%", BatteryGlyph(78, true));
                await Shot("05-charging", 700);
                toast = ToastKind.None;

                timerTotal = TimeSpan.FromMinutes(25); timerLeft = TimeSpan.FromMinutes(17.4);
                ToggleTimer();
                await Shot("06-timer-compact", 800);

                Expand("home");
                await Shot("07-expanded-home", 900);

                var demo = new[] {
                    System.Reflection.Assembly.GetExecutingAssembly().Location,
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "assets", "notch.ico"),
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "README.md"),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                };
                foreach (var d in demo) { var full = System.IO.Path.GetFullPath(d); if (File.Exists(full) || Directory.Exists(full)) shelf.Add(new[] { full }); }
                SelectTab("shelf");
                await Shot("08-shelf", 1500);

                dragOver = true; E<Grid>("DropOverlay").Visibility = Visibility.Visible; UpdateMood();
                await Shot("09-drop", 700);
                dragOver = false; E<Grid>("DropOverlay").Visibility = Visibility.Collapsed; UpdateMood();

                clips.Clear();
                AddClip("https://open.spotify.com/track/6GyFP1nfCDB8lbD2bG0Hq9");
                AddClip("Meeting moved to 4:30 — same room");
                AddClip("git commit -m \"notch: first light\"");
                clips[1].Pinned = true;
                SelectTab("clip");
                await Shot("10-clipboard", 800);

                SelectTab("timer");
                await Shot("11-timer", 800);

                SelectTab("notes");
                await Shot("12-notes", 600);

                SelectTab("settings");
                await Shot("13-settings", 600);

                await MascotSheet();

                // real buddy test: hop out of the notch, fall onto the taskbar, then fly home
                var log = new System.Text.StringBuilder();
                ToggleBuddy();
                log.AppendLine("after launch: " + buddy.DebugState);
                await Task.Delay(300);
                log.AppendLine("after 0.3s: " + buddy.DebugState);
                SelectTab("home");
                await Task.Delay(2500);
                log.AppendLine("after 2.5s: " + buddy.DebugState + " out=" + buddyOut);
                await Shot("15-buddy-away", 100);
                await RenderElement(buddy.Visual, 170, 150, "16-buddy-window");
                buddy.GoHome();
                await Task.Delay(1200);
                log.AppendLine("after go home: " + buddy.DebugState + " out=" + buddyOut);
                File.WriteAllText(System.IO.Path.Combine(SnapshotDir, "buddy.txt"), log.ToString());

                await DanceTest();
                await FidgetSheet();
                await ToySheet();
            }
            catch (Exception ex)
            {
                File.WriteAllText(System.IO.Path.Combine(SnapshotDir, "error.txt"), ex.ToString());
            }
            Application.Current.Shutdown();
        }

        async Task Shot(string name, int waitMs)
        {
            await Task.Delay(waitMs);
            root.UpdateLayout();
            const double scale = 2;
            int w = (int)(WinW * scale), h = (int)(WinH * scale);
            var content = new RenderTargetBitmap(w, h, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            content.Render(root);
            // put it on a fake wallpaper so the black notch reads like it does on screen
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var bg = new LinearGradientBrush(Color.FromRgb(0xC9, 0xD6, 0xF2), Color.FromRgb(0xF1, 0xD9, 0xE6), 30);
                dc.DrawRectangle(bg, null, new Rect(0, 0, WinW, WinH));
                dc.DrawImage(content, new Rect(0, 0, WinW, WinH));
            }
            var final = new RenderTargetBitmap(w, h, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            final.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(final));
            using (var fs = File.Create(System.IO.Path.Combine(SnapshotDir, name + ".png"))) enc.Save(fs);
        }

        async Task RenderElement(FrameworkElement el, double w, double h, string name)
        {
            await Task.Delay(50);
            var rtb = new RenderTargetBitmap((int)(w * 2), (int)(h * 2), 192, 192, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xC9, 0xD6, 0xF2)), null, new Rect(0, 0, w, h));
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x26)), null, new Rect(0, h - 6, w, 6));
            }
            rtb.Render(dv);
            rtb.Render(el);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(System.IO.Path.Combine(SnapshotDir, name + ".png"))) enc.Save(fs);
        }

        /// <summary>Every look Mochi has, on one sheet.</summary>
        async Task MascotSheet()
        {
            string[] names = { "idle", "happy", "vibing", "sleepy", "hungry", "focus", "worried", "excited",
                               "notes", "pocket", "settings", "party", "snip", "hug", "wave", "walk" };
            var panel = new WrapPanel { Width = 8 * 130, Background = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x14)) };
            var ms = new Mascot[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                var cell = new Grid { Width = 130, Height = 150 };
                var m = new Mascot { Width = 104, Height = 104, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 0, 0) };
                m.Accent = Color.FromRgb(0xFF, 0x7E, 0xB6);
                cell.Children.Add(m);
                cell.Children.Add(new TextBlock { Text = names[i], Foreground = Brushes.Gray, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 8) });
                panel.Children.Add(cell);
                ms[i] = m;
            }
            Mood[] moods = { Mood.Idle, Mood.Happy, Mood.Vibing, Mood.Sleepy, Mood.Hungry, Mood.Focus, Mood.Worried, Mood.Excited };
            for (int i = 0; i < moods.Length; i++) ms[i].SetMood(moods[i]);
            ms[8].SetHeadContext(Prop.Glasses); ms[8].SetHandContext(Prop.Pencil);
            ms[9].SetHandContext(Prop.Folder);
            ms[10].SetHandContext(Prop.Wrench);
            ms[11].SetMood(Mood.Excited); ms[11].FlashHead(Prop.PartyHat, 0);
            ms[12].SetMood(Mood.Excited); ms[12].FlashHand(Prop.Camera, 0);
            ms[13].SetMood(Mood.Happy); ms[13].FlashHand(Prop.Paper, 0);
            ms[15].SetWalking(true); ms[15].Face(-1);
            panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            panel.Arrange(new Rect(panel.DesiredSize));
            await Task.Delay(600);
            ms[14].Wave();
            await Task.Delay(240);
            panel.UpdateLayout();
            var rtb = new RenderTargetBitmap((int)(panel.ActualWidth * 1.5), (int)(panel.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
            rtb.Render(panel);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(System.IO.Path.Combine(SnapshotDir, "14-mochi-sheet.png"))) enc.Save(fs);
        }

        BitmapSource Snap(FrameworkElement el, double w, double h, bool taskbar)
        {
            var rtb = new RenderTargetBitmap((int)(w * 2), (int)(h * 2), 192, 192, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xC9, 0xD6, 0xF2)), null, new Rect(0, 0, w, h));
                if (taskbar) dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x26)), null, new Rect(0, h - 5, w, 5));
            }
            rtb.Render(dv);
            rtb.Render(el);
            rtb.Freeze();
            return rtb;
        }

        void SaveGrid(System.Collections.Generic.List<BitmapSource> shots, string[] labels, int cols, double cw, double ch, string name)
        {
            int rows = (shots.Count + cols - 1) / cols;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, cols * cw, rows * (ch + 18)));
                for (int i = 0; i < shots.Count; i++)
                {
                    double x0 = (i % cols) * cw, y0 = (i / cols) * (ch + 18);
                    dc.DrawImage(shots[i], new Rect(x0 + 2, y0, cw - 4, ch));
                    var ft = new FormattedText(labels[i], System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.DimGray);
                    dc.DrawText(ft, new Point(x0 + cw / 2 - ft.Width / 2, y0 + ch + 1));
                }
            }
            var rtb = new RenderTargetBitmap((int)(cols * cw * 1.5), (int)(rows * (ch + 18) * 1.5), 144, 144, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(System.IO.Path.Combine(SnapshotDir, name))) enc.Save(fs);
        }

        /// <summary>Feeds a noisy 120 bpm fake song and measures jitter (direction flips per second).</summary>
        async Task DanceTest()
        {
            var m = new Mascot { Width = 100, Height = 100 };
            var holder = new Grid(); holder.Children.Add(m);
            holder.Measure(new Size(100, 100)); holder.Arrange(new Rect(0, 0, 100, 100));
            m.Active = true; m.SetMood(Mood.Vibing);
            var r = new Random(7);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var newY = new System.Collections.Generic.List<double>();
            var oldY = new System.Collections.Generic.List<double>();
            while (sw.Elapsed.TotalSeconds < 6)
            {
                double t = sw.Elapsed.TotalSeconds;
                double kick = Math.Exp(-(t % 0.5) * 12);
                double level = Math.Sqrt(Math.Min(1, 0.2 + 0.6 * kick + r.NextDouble() * 0.25));
                m.SetBeat(level);
                await Task.Delay(16);
                newY.Add(m.DebugBob);
                oldY.Add(-level * 6); // what the previous version did every frame
            }
            double secs = sw.Elapsed.TotalSeconds;
            File.WriteAllText(System.IO.Path.Combine(SnapshotDir, "dance.txt"), string.Format(
                "samples={0} over {1:0.0}s, beats detected={2} (song has 12)\r\nNEW: {3:0.0} direction flips/sec, max bounce {4:0.0}px\r\nOLD: {5:0.0} direction flips/sec\r\n",
                newY.Count, secs, m.DebugBeats, Flips(newY) / secs, Max(newY), Flips(oldY) / secs));
        }

        static int Flips(System.Collections.Generic.List<double> ys)
        {
            int flips = 0, dir = 0;
            for (int i = 1; i < ys.Count; i++)
            {
                double d = ys[i] - ys[i - 1];
                if (Math.Abs(d) < 0.05) continue;
                int nd = d > 0 ? 1 : -1;
                if (dir != 0 && nd != dir) flips++;
                dir = nd;
            }
            return flips;
        }

        static double Max(System.Collections.Generic.List<double> ys) { double m = 0; foreach (var y in ys) m = Math.Max(m, Math.Abs(y)); return m; }

        async Task FidgetSheet()
        {
            string[] names = { "look around", "scratch ear", "groom paw", "head tilt", "tail wag", "reach up", "hum", "ear twitch" };
            var ms = new Mascot[names.Length];
            var panel = new WrapPanel { Width = 8 * 130, Background = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x14)) };
            for (int i = 0; i < names.Length; i++)
            {
                var cell = new Grid { Width = 130, Height = 150 };
                ms[i] = new Mascot { Width = 104, Height = 104, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 14, 0, 0), Particles = true };
                cell.Children.Add(ms[i]);
                cell.Children.Add(new TextBlock { Text = names[i], Foreground = Brushes.Gray, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 8) });
                panel.Children.Add(cell);
            }
            panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            panel.Arrange(new Rect(panel.DesiredSize));
            ms[0].LookAround(); ms[1].ScratchEar(); ms[2].Groom(); ms[3].HeadTilt(); ms[4].TailWag(); ms[5].ReachUp(); ms[6].Hum(); ms[7].EarTwitch();
            await Task.Delay(480);
            panel.UpdateLayout();
            var rtb = new RenderTargetBitmap((int)(panel.ActualWidth * 1.5), (int)(panel.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
            rtb.Render(panel);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(System.IO.Path.Combine(SnapshotDir, "17-fidgets.png"))) enc.Save(fs);
        }

        /// <summary>Runs every toy for real on the taskbar and captures each mid-play.</summary>
        async Task ToySheet()
        {
            int[] waits = { 2600, 2900, 2300, 3300, 2700, 1700, 1600, 1300, 900, 1500, 1400, 2400, 1300, 3100, 1900, 1100 };
            var shots = new System.Collections.Generic.List<BitmapSource>();
            ToggleBuddy();
            await Task.Delay(2800);
            var log = new System.Text.StringBuilder();
            for (int i = 0; i < BuddyWindow.PlayNames.Length; i++)
            {
                buddy.StartPlay(i);
                await Task.Delay(waits[i]);
                shots.Add(Snap(buddy.Visual, 300, 190, true));
                log.AppendLine(BuddyWindow.PlayNames[i] + ": " + buddy.DebugState);
            }
            buddy.GoHome();
            await Task.Delay(900);
            log.AppendLine("home: out=" + buddyOut);
            File.WriteAllText(System.IO.Path.Combine(SnapshotDir, "toys.txt"), log.ToString());
            SaveGrid(shots, BuddyWindow.PlayNames, 4, 300, 190, "18-toys.png");
        }

        static BitmapSource FakeArt()
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var g = new LinearGradientBrush(Color.FromRgb(0x2B, 0x1B, 0x5E), Color.FromRgb(0xF0, 0x5A, 0x7E), 60);
                dc.DrawRectangle(g, null, new Rect(0, 0, 240, 240));
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xC8, 0x57)), null, new Point(160, 92), 46, 46);
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x90, 0x10, 0x08, 0x30)), null, new Rect(0, 160, 240, 80));
            }
            var rtb = new RenderTargetBitmap(240, 240, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }
    }
}
