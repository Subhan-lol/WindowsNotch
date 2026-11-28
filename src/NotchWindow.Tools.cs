using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace NotchIsland
{
    public partial class NotchWindow
    {
        // clipboard
        readonly ObservableCollection<ClipItem> clips = new ObservableCollection<ClipItem>();
        DispatcherTimer clipDebounce;
        DateTime ignoreClipUntil = DateTime.MinValue;
        uint fmtExclude, fmtCanInclude;
        const int MaxClips = 25;

        // timer
        bool timerRunning;
        TimeSpan timerTotal = TimeSpan.FromMinutes(25), timerLeft = TimeSpan.FromMinutes(25);
        DateTime timerEnd;
        DispatcherTimer timerTick;

        // notes / clock / battery
        DispatcherTimer notesSave, clockTimer, batteryPoll;
        bool hasBattery, charging, warned20, warned10;
        int batPct = -1;

        void WireTools()
        {
            // ---- tabs ----
            foreach (var c in E<StackPanel>("Tabs").Children)
            {
                var rb = c as RadioButton;
                if (rb != null) rb.Checked += (s, e) => { var t = (string)((RadioButton)s).Tag; if (t != tab) SelectTab(t); };
            }

            // ---- clipboard ----
            fmtExclude = Native.RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
            fmtCanInclude = Native.RegisterClipboardFormat("CanIncludeInClipboardHistory");
            clipDebounce = Timer(120, (s, e) => { clipDebounce.Stop(); ReadClipboard(true); });
            var clipList = E<ItemsControl>("ClipList");
            clipList.ItemsSource = clips;
            clipList.AddHandler(Button.ClickEvent, new RoutedEventHandler(OnClipButton));
            clipList.MouseLeftButtonUp += (s, e) =>
            {
                var item = Ctx(e.OriginalSource) as ClipItem;
                if (item == null) return;
                CopyBack(item);
            };
            E<Button>("ClipClear").Click += (s, e) => ClearClips();
            clips.CollectionChanged += (s, e) => UpdateClipUI();
            UpdateClipUI();

            // ---- timer ----
            timerTick = Timer(1000, (s, e) => TimerTick());
            E<WrapPanel>("TimerPresets").AddHandler(Button.ClickEvent, new RoutedEventHandler(OnTimerPreset));
            E<Button>("TimerStart").Click += (s, e) => ToggleTimer();
            E<Button>("TimerReset").Click += (s, e) => { timerRunning = false; timerTick.Stop(); timerLeft = timerTotal; UpdateTimerUI(); ApplyCompact(); UpdateMood(); };
            UpdateTimerUI();

            // ---- notes ----
            var notes = E<TextBox>("NotesBox");
            try { var f = Paths.File("notes.txt"); if (File.Exists(f)) notes.Text = File.ReadAllText(f); } catch { }
            E<TextBlock>("NotesHint").Visibility = notes.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            notesSave = Timer(800, (s, e) => { notesSave.Stop(); SaveNotesNow(); });
            notes.TextChanged += (s, e) =>
            {
                E<TextBlock>("NotesHint").Visibility = notes.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                notesSave.Stop(); notesSave.Start();
                if (expanded && tab == "notes" && !buddyOut) big.Scribble();
            };
            notes.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (!IsActive) { TakeFocus(); notes.Focus(); }
            };

            // ---- settings ----
            BuildSettings();
            E<Button>("QuitBtn").Click += (s, e) => Quit();

            // ---- clock & battery ----
            clockTimer = Timer(10000, (s, e) => UpdateClock());
            batteryPoll = Timer(120000, (s, e) => RefreshBattery(false));
            batteryPoll.Start();
        }

        void SaveNotesNow()
        {
            if (SnapshotDir != null) return;
            try
            {
                var notes = root.FindName("NotesBox") as TextBox;
                if (notes != null) File.WriteAllText(Paths.File("notes.txt"), notes.Text);
            }
            catch { }
        }

        // ================= clipboard =================
        void OnClipboardUpdate()
        {
            clipDebounce.Stop();
            clipDebounce.Start();
        }

        void ReadClipboard(bool retry)
        {
            if (DateTime.Now < ignoreClipUntil) return;
            try
            {
                if (TryCatchSnip()) return;
                if (!Settings.Get("ClipboardHistory")) return;
                // Respect password managers & apps that opt out of clipboard history.
                if (Native.IsClipboardFormatAvailable(fmtExclude)) return;
                if (Native.IsClipboardFormatAvailable(fmtCanInclude))
                {
                    var ms = Clipboard.GetData("CanIncludeInClipboardHistory") as MemoryStream;
                    if (ms != null && ms.Length >= 4) { var b = ms.ToArray(); if (BitConverter.ToInt32(b, 0) == 0) return; }
                }
                if (!Clipboard.ContainsText()) return;
                var text = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(text) || text.Length > 200000) return;
                AddClip(text);
            }
            catch (COMException)
            {
                // clipboard busy (another app has it open) — try once more shortly
                if (retry) Dispatcher.BeginInvoke(new Action(() => ReadClipboard(false)), DispatcherPriority.Background);
            }
            catch { }
        }

        void AddClip(string text)
        {
            var existing = clips.FirstOrDefault(c => c.Text == text);
            if (existing != null)
            {
                existing.Touch();
                if (!existing.Pinned) { clips.Remove(existing); clips.Insert(clips.Count(c => c.Pinned), existing); }
            }
            else clips.Insert(clips.Count(c => c.Pinned), new ClipItem(text));
            var unpinned = clips.Where(c => !c.Pinned).ToList();
            for (int i = MaxClips; i < unpinned.Count; i++) clips.Remove(unpinned[i]);
            React(m => m.Nod());
        }

        void CopyBack(ClipItem item)
        {
            try
            {
                ignoreClipUntil = DateTime.Now.AddMilliseconds(600);
                Clipboard.SetText(item.Text);
                item.Touch();
                if (!item.Pinned) { clips.Remove(item); clips.Insert(clips.Count(c => c.Pinned), item); }
                React(m => { m.Nod(); m.FlashHand(Prop.Paper, 800); });
                moodText.Text = "copied! paste away";
            }
            catch { moodText.Text = "clipboard was busy, try again"; }
        }

        void OnClipButton(object sender, RoutedEventArgs e)
        {
            var btn = e.OriginalSource as Button;
            if (btn == null) return;
            var item = btn.DataContext as ClipItem;
            if (item == null) return;
            if ((string)btn.Tag == "pin")
            {
                if (!item.Pinned) React(m => m.Hello());
                item.Pinned = !item.Pinned;
                clips.Remove(item);
                clips.Insert(item.Pinned ? 0 : clips.Count(c => c.Pinned), item);
            }
            else if ((string)btn.Tag == "delete") { clips.Remove(item); React(m => m.Wiggle()); }
            else if ((string)btn.Tag == "phone") SendTextToPhone(item.Text);
            e.Handled = true;
        }

        void ClearClips()
        {
            foreach (var c in clips.Where(c => !c.Pinned).ToList()) clips.Remove(c);
        }

        void UpdateClipUI()
        {
            var empty = root.FindName("ClipEmpty") as TextBlock;
            if (empty != null) empty.Visibility = clips.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void RefreshClipAges() { foreach (var c in clips) c.RefreshAge(); }

        // ================= timer =================
        void OnTimerPreset(object sender, RoutedEventArgs e)
        {
            var btn = e.OriginalSource as Button;
            if (btn == null) return;
            string tag = (string)btn.Tag;
            int v;
            if (tag.StartsWith("+") || tag.StartsWith("-"))
            {
                if (!int.TryParse(tag, out v)) return;
                var d = TimeSpan.FromMinutes(v);
                var left = (timerRunning ? timerEnd - DateTime.Now : timerLeft) + d;
                if (left < TimeSpan.FromSeconds(10)) left = TimeSpan.FromSeconds(10);
                if (timerRunning) timerEnd = DateTime.Now + left;
                timerLeft = left;
                if (timerLeft > timerTotal) timerTotal = timerLeft;
            }
            else if (int.TryParse(tag, out v))
            {
                timerTotal = timerLeft = TimeSpan.FromMinutes(v);
                timerEnd = DateTime.Now + timerLeft;
                if (!timerRunning) ToggleTimer();
            }
            UpdateTimerUI();
            ApplyCompact();
            e.Handled = true;
        }

        void ToggleTimer()
        {
            if (timerRunning)
            {
                timerLeft = timerEnd - DateTime.Now;
                timerRunning = false;
                timerTick.Stop();
                React(m => m.Flash(Mood.Happy, 700));
            }
            else
            {
                if (timerLeft <= TimeSpan.Zero) timerLeft = timerTotal;
                timerEnd = DateTime.Now + timerLeft;
                timerRunning = true;
                timerTick.Start();
                React(m => { m.Jump(); m.FlashHead(Prop.Headband, 1500); });
            }
            UpdateTimerUI();
            ApplyCompact();
            UpdateMood();
        }

        void TimerTick()
        {
            timerLeft = timerEnd - DateTime.Now;
            if (timerLeft <= TimeSpan.Zero) { TimerDone(); return; }
            UpdateTimerUI();
            if (!expanded && toast == ToastKind.None)
            {
                E<TextBlock>("CRightText").Text = FormatTime(timerLeft);
                SetRing(E<Ellipse>("CRingArc"), 19, 2.4, timerLeft.TotalSeconds / timerTotal.TotalSeconds);
            }
            if ((int)timerLeft.TotalSeconds % 15 == 0) UpdateMood();
        }

        void TimerDone()
        {
            timerRunning = false;
            timerTick.Stop();
            timerLeft = TimeSpan.Zero;
            UpdateTimerUI();
            if (Settings.Get("Sounds")) { try { SystemSounds.Asterisk.Play(); } catch { } }
            if (IsVisible)
            {
                Expand("timer");
                React(m => m.Celebrate());
                moodText.Text = "time's up! great work ✦";
                ScheduleCollapse(7000);
            }
            UpdateMood();
        }

        void UpdateTimerUI()
        {
            var left = timerRunning ? timerEnd - DateTime.Now : timerLeft;
            E<TextBlock>("TimerBig").Text = FormatTime(left);
            E<TextBlock>("TimerLabel").Text = timerRunning ? "focusing" : left <= TimeSpan.Zero ? "done!" : left < timerTotal ? "paused" : "ready";
            E<Button>("TimerStart").Content = timerRunning ? "Pause" : (left > TimeSpan.Zero && left < timerTotal ? "Resume" : "Start");
            double frac = timerTotal.TotalSeconds > 0 ? left.TotalSeconds / timerTotal.TotalSeconds : 0;
            SetRing(E<Ellipse>("TimerRing"), 140, 7, frac);
            SetRing(E<Ellipse>("CRingArc"), 19, 2.4, frac);
        }

        /// <summary>Progress ring via dash array — no geometry rebuilds.</summary>
        static void SetRing(Ellipse e, double size, double thickness, double frac)
        {
            frac = Math.Max(0, Math.Min(1, frac));
            double len = Math.PI * (size - thickness) / thickness;
            e.Visibility = frac <= 0.001 ? Visibility.Hidden : Visibility.Visible;
            e.StrokeDashArray = new DoubleCollection { frac * len, len + 10 };
        }

        // ================= settings =================
        void BuildSettings()
        {
            var grid = E<UniformGrid>("SettingsGrid");
            var labels = new Dictionary<string, string>
            {
                { "ExpandOnHover", "Open on hover" }, { "HideInFullscreen", "Hide in fullscreen" },
                { "VolumeHud", "Volume pop-ups" }, { "TrackPopups", "Song change pop-ups" },
                { "BatteryAlerts", "Charging & low battery" }, { "ScrollVolume", "Scroll = volume" },
                { "ClipboardHistory", "Clipboard history" }, { "Visualizer", "Music visualizer" },
                { "AlbumColors", "Colors from album art" }, { "Sounds", "Timer chime" },
                { "StartWithWindows", "Start with Windows" }, { "PhoneDrop", "iPhone drop (Wi-Fi)" },
            };
            foreach (var kv in labels)
            {
                var key = kv.Key;
                var cb = new CheckBox { Content = kv.Value, IsChecked = Settings.Get(key), Style = (Style)Application.Current.Resources["Switch"], Tag = key };
                cb.Checked += (s, e) => Settings.Set(key, true);
                cb.Unchecked += (s, e) => Settings.Set(key, false);
                grid.Children.Add(cb);
            }
        }

        /// <summary>Keep a settings switch in step when the value changes elsewhere (e.g. the iPhone panel).</summary>
        void SyncSettingSwitch(string key)
        {
            foreach (var c in E<UniformGrid>("SettingsGrid").Children)
            {
                var cb = c as CheckBox;
                if (cb != null && (string)cb.Tag == key && cb.IsChecked != Settings.Get(key)) cb.IsChecked = Settings.Get(key);
            }
        }

        // ================= clock & battery =================
        void UpdateClock()
        {
            if (!expanded) { clockTimer.Stop(); return; }
            if (!clockTimer.IsEnabled) clockTimer.Start();
            E<TextBlock>("ClockText").Text = DateTime.Now.ToString("ddd d MMM  ·  h:mm tt");
        }

        void RefreshBattery(bool fromEvent)
        {
            SYSTEM_POWER_STATUS s;
            if (!Native.GetSystemPowerStatus(out s)) return;
            hasBattery = (s.BatteryFlag & 128) == 0 && s.BatteryFlag != 255 && s.BatteryLifePercent != 255;
            var glyph = E<TextBlock>("BatteryGlyph"); var txt = E<TextBlock>("BatteryText");
            if (!hasBattery) { glyph.Text = ""; txt.Text = ""; return; }
            bool nowCharging = s.ACLineStatus == 1;
            int pct = s.BatteryLifePercent;
            string g = BatteryGlyph(pct, nowCharging);
            if (Settings.Get("BatteryAlerts") && batPct >= 0)
            {
                if (nowCharging != charging)
                {
                    charging = nowCharging;
                    if (nowCharging) { ShowToast(ToastKind.Battery, 2600, "Charging", pct + "%", g); React(m => m.Bolt()); }
                    else ShowToast(ToastKind.Battery, 2000, "On battery", pct + "%", g);
                }
                else if (!nowCharging && pct <= 10 && !warned10) { warned10 = warned20 = true; ShowToast(ToastKind.Info, 4500, "Battery at " + pct + "%", "plug me in soon!", g); React(m => m.Flash(Mood.Worried, 3000)); }
                else if (!nowCharging && pct <= 20 && !warned20) { warned20 = true; ShowToast(ToastKind.Info, 3500, "Battery at " + pct + "%", "might want a charger", g); React(m => m.Flash(Mood.Worried, 2000)); }
            }
            if (nowCharging) { warned10 = warned20 = false; }
            charging = nowCharging;
            batPct = pct;
            glyph.Text = g;
            glyph.Foreground = charging ? Brush(0x7D, 0xF0, 0x9A) : pct <= 20 ? Brush(0xFF, 0x6B, 0x6B) : (Brush)Application.Current.Resources["Fg"];
            txt.Text = pct + "%";
            if (fromEvent) UpdateMood();
        }

        static string BatteryGlyph(int pct, bool charging)
        {
            int step = Math.Max(0, Math.Min(10, (int)Math.Round(pct / 10.0)));
            return ((char)((charging ? 0xEBAB : 0xEBA0) + step)).ToString();
        }
    }
}
