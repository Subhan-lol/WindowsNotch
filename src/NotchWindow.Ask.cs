using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace NotchIsland
{
    /// <summary>
    /// "Ask Mochi": Ctrl+Alt+Space opens a little command bar in the notch (press it again to talk instead).
    /// Simple things (music, notes, files, apps, timers, maths) Mochi does instantly; anything else goes to
    /// Claude Code in the background. Read-only tools run freely; anything that changes your computer waits
    /// for your Allow.
    /// </summary>
    public partial class NotchWindow
    {
        const int HotkeyTalk = 0x4E2;
        Voice voice;
        TextBox askBox;
        StackPanel askOut;
        CancellationTokenSource askCts;
        bool askBusy, askNeedsOk;
        string askSession, askLabel;
        List<string> askAllowed = new List<string>();
        IntPtr askPrevWindow;
        DispatcherTimer askTicker;
        Stopwatch askClock = new Stopwatch();
        public static bool AskDryRun; // tests: classify only, don't act

        [DllImport("user32.dll")] static extern bool LockWorkStation();

        void WireAsk()
        {
            askBox = E<TextBox>("AskBox");
            askOut = E<StackPanel>("AskOut");
            voice = new Voice(Dispatcher);
            askTicker = Timer(1000, (s, e) => AskTick());

            askBox.TextChanged += (s, e) => E<TextBlock>("AskHint").Visibility = askBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            askBox.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { e.Handled = true; Submit(askBox.Text); }
                else if (e.Key == Key.Escape && voice.Listening) { e.Handled = true; voice.Cancel(); }
            };
            askBox.PreviewMouseLeftButtonDown += (s, e) => { if (!IsActive) { TakeFocus(); askBox.Focus(); } };
            E<Button>("AskGo").Click += (s, e) => Submit(askBox.Text);
            E<Button>("AskMic").Click += (s, e) => { if (voice.Listening) voice.Stop(); else { if (!IsActive) TakeFocus(); StartListening(); } };
            E<WrapPanel>("AskChips").AddHandler(Button.ClickEvent, new RoutedEventHandler((s, e) =>
            {
                var b = e.OriginalSource as Button;
                if (b == null) return;
                e.Handled = true;
                var t = (string)b.Tag;
                if (!IsActive) TakeFocus();
                if (t.EndsWith(": ")) { askBox.Text = t; FocusAsk(); askBox.CaretIndex = t.Length; }
                else Submit(t);
            }));
            E<Button>("AskAllow").Click += (s, e) => Approve(true);
            E<Button>("AskDeny").Click += (s, e) => Approve(false);

            voice.Partial += t => { askBox.Text = t; askBox.CaretIndex = t.Length; };
            voice.Heard += t =>
            {
                ListeningUI(false);
                if (string.IsNullOrWhiteSpace(t)) return;
                askBox.Text = t;
                Submit(t);
            };
            voice.Failed += msg => { ListeningUI(false); AskClear(); AskLine(msg, true); React(m => m.Flash(Mood.Worried, 1200)); };
            voice.Ended += () => ListeningUI(false);
            voice.Level += lvl =>
            {
                var sc = E<ScaleTransform>("AskMicScale");
                double v = 0.6 + Math.Min(1, lvl / 60.0) * 0.6;
                sc.ScaleX = sc.ScaleY = v;
            };
            AskClear();
            AskLine("Type or talk. I can play music, take notes, find files, open apps, set timers, do maths, and ask Claude about anything else.", true);
        }

        // ---------------- opening the bar ----------------
        void ToggleFromHotkey()
        {
            // press once: Ask bar; press again while it's open: talk (and again to finish talking)
            if (expanded && tab == "ask")
            {
                if (voice.Listening) voice.Stop(); else StartListening();
                return;
            }
            OpenAsk(false);
        }

        public void OpenAsk(bool listen)
        {
            var fg = Native.GetForegroundWindow();
            if (fg != hwnd) askPrevWindow = fg;
            if (!IsVisible) Show();
            Expand("ask");
            if (tab != "ask") SelectTab("ask");
            TakeFocus();
            FocusAsk();
            askBox.SelectAll();
            if (!askBusy && !askNeedsOk && askOut.Children.Count <= 1) E<WrapPanel>("AskChips").Visibility = Visibility.Visible;
            if (listen) StartListening();
        }

        void FocusAsk()
        {
            Dispatcher.BeginInvoke(new Action(() => { askBox.Focus(); Keyboard.Focus(askBox); }), DispatcherPriority.Input);
        }

        /// <summary>Done with an instant action: tuck the notch away and hand focus back to what you were doing.</summary>
        void AskFinishSoon(int ms)
        {
            var prev = askPrevWindow;
            var t = Timer(ms, null);
            t.Tick += (s, e) =>
            {
                t.Stop();
                if (!expanded || tab != "ask" || askBusy || askNeedsOk || CursorOverNotch() || askBox.Text.Length > 0) return;
                pinnedOpen = false;
                Collapse();
                if (prev != IntPtr.Zero) Native.SetForegroundWindow(prev);
            };
            t.Start();
        }

        // ---------------- voice ----------------
        void StartListening()
        {
            if (!Voice.Supported) { AskClear(); AskLine("Voice needs Windows speech recognition (Settings › Time & language › Speech).", true); return; }
            if (!expanded || tab != "ask") OpenAsk(false);
            if (!voice.Start()) return;
            askBox.Text = "";
            ListeningUI(true);
        }

        void ListeningUI(bool on)
        {
            E<TextBlock>("AskHint").Text = on ? "Listening… just talk (I'll go when you pause)" : "Ask Mochi… play lofi, find my resume, note: buy milk";
            Anim.To(E<System.Windows.Shapes.Ellipse>("AskMicLevel"), UIElement.OpacityProperty, on ? 0.35 : 0, 200);
            E<Border>("AskFrame").BorderBrush = on ? (Brush)root.Resources["Accent"] : new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
            E<Button>("AskMic").Content = on ? "" : ""; // stop / mic
            if (on)
            {
                moodText.Text = "listening…";
                if (!buddyOut) { big.EndFlash(); big.Flash(Mood.Excited, 0); big.LookAt(0, -0.3); }
            }
            else if (!askBusy && !buddyOut) big.EndFlash();
        }

        // ---------------- output area ----------------
        void AskClear()
        {
            askOut.Children.Clear();
            E<Grid>("AskApproval").Visibility = Visibility.Collapsed;
        }

        TextBlock AskLine(string text, bool dim)
        {
            var tb = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(2, 6, 0, 0) };
            if (dim) tb.Foreground = (Brush)Application.Current.Resources["FgDim"];
            askOut.Children.Add(tb);
            E<ScrollViewer>("AskScroll").ScrollToEnd();
            return tb;
        }

        /// <summary>Claude's (or a skill's) answer: selectable text plus a Copy button.</summary>
        void AskAnswer(string text)
        {
            var box = new TextBox
            {
                Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["AskBox"],
                FontSize = 13, Margin = new Thickness(2, 6, 0, 0), BorderThickness = new Thickness(0)
            };
            askOut.Children.Add(box);
            var copy = new Button { Content = "Copy", Style = (Style)Application.Current.Resources["Pill"], Padding = new Thickness(10, 3, 10, 3), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
            copy.Click += (s, e) => { try { Clipboard.SetText(text); copy.Content = "Copied ✓"; } catch { } };
            askOut.Children.Add(copy);
            E<ScrollViewer>("AskScroll").ScrollToEnd();
        }

        /// <summary>A clickable result (file, link…) with an optional second action.</summary>
        void AskRow(string title, string sub, string glyph, Action open, string glyph2, string tip2, Action second)
        {
            var g = new Grid { Height = 36, Margin = new Thickness(0, 4, 0, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var bg = new Border { CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)) };
            Grid.SetColumnSpan(bg, 3);
            g.Children.Add(bg);
            var ic = new TextBlock { Text = glyph, FontFamily = (FontFamily)Application.Current.Resources["Icons"], FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) };
            g.Children.Add(ic);
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = title, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis });
            if (!string.IsNullOrEmpty(sub)) sp.Children.Add(new TextBlock { Text = sub, FontSize = 10, Foreground = (Brush)Application.Current.Resources["FgFaint"], TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(sp, 1); g.Children.Add(sp);
            if (second != null)
            {
                var b2 = new Button { Content = glyph2, Style = (Style)Application.Current.Resources["MiniButton"], ToolTip = tip2, Margin = new Thickness(6, 0, 8, 0) };
                b2.Click += (s, e) => { e.Handled = true; second(); b2.Content = ""; };
                Grid.SetColumn(b2, 2); g.Children.Add(b2);
            }
            g.MouseEnter += (s, e) => bg.Background = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
            g.MouseLeave += (s, e) => bg.Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
            g.MouseLeftButtonUp += (s, e) => { if (open != null) open(); };
            askOut.Children.Add(g);
            E<ScrollViewer>("AskScroll").ScrollToEnd();
        }

        /// <summary>Quick confirmation for instant actions. Shows in the panel, or as a card if the notch is closed.</summary>
        void AskDone(string summary, bool tuckAway)
        {
            if (expanded && tab == "ask")
            {
                AskLine(summary, false);
                moodText.Text = "done ✓";
                React(m => m.Nod());
                if (tuckAway) AskFinishSoon(1400);
            }
            else if (!IsVisible) App.Notify("Mochi", summary, null);
            else ShowToast(ToastKind.Info, 2400, summary, askLabel ?? "", "");
        }

        // ---------------- submitting ----------------
        void Submit(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) { FocusAsk(); return; }
            if (voice.Listening) voice.Cancel();
            askBox.Text = "";
            AskClear();
            E<WrapPanel>("AskChips").Visibility = Visibility.Collapsed;
            var you = AskLine("“" + text + "”", true);
            you.FontStyle = FontStyles.Italic;
            askLabel = text.Length > 60 ? text.Substring(0, 60) + "…" : text;
            Touch();
            var intent = Parse(text);
            Dbg("ask: " + intent);
            try { Execute(intent); }
            catch (Exception ex) { AskLine("Oops, that didn't work: " + ex.Message, true); React(m => m.Flash(Mood.Worried, 1200)); }
        }

        // ---------------- Claude in the background ----------------
        async void AskClaude(string prompt, string resume, string spotifyQuery)
        {
            if (!ClaudeRunner.Available)
            {
                AskLine("I need Claude Code for that one. Install it from claude.com/code, sign in once in a terminal, then ask me again.", true);
                return;
            }
            if (askBusy && askCts != null) askCts.Cancel();
            askBusy = true;
            askNeedsOk = false;
            askCts = new CancellationTokenSource();
            var token = askCts;
            var status = AskLine("Asking Claude… (you can close the notch, I'll tell you when it's done)", true);
            askClock.Restart();
            askTicker.Start();
            if (!buddyOut) { big.FlashHead(Prop.Glasses, 0); big.Flash(Mood.Focus, 0); big.LookAround(); }
            moodText.Text = "thinking…";
            if (!expanded) ShowAskWorking();

            var r = await ClaudeRunner.Run(prompt, resume, askAllowed, token.Token);
            if (token != askCts) return; // replaced by a newer request
            askBusy = false;
            askTicker.Stop();
            if (!buddyOut) { big.FlashHead(Prop.None, 0); big.EndFlash(); }
            askSession = r.SessionId ?? askSession;
            askOut.Children.Remove(status);

            if (r.Denials.Count > 0 && !r.IsError)
            {
                askNeedsOk = true;
                askAllowed = askAllowed.Union(r.Denials.Select(d => d.Rule)).ToList();
                if (r.Text.Length > 0) AskLine(r.Text, true);
                E<TextBlock>("AskApprovalText").Text = "Claude wants to " + string.Join(", then ", r.Denials.Select(Describe)) + ". OK?";
                E<Grid>("AskApproval").Visibility = Visibility.Visible;
                React(m => m.Startle());
                moodText.Text = "Claude needs your OK";
                if (!IsVisible) App.Notify("Mochi needs your OK", "Claude wants to " + Describe(r.Denials[0]), () => OpenAsk(false));
                else if (!expanded) ShowAskResultCard("Mochi needs your OK", "Claude wants to " + Describe(r.Denials[0]), "", true);
                return;
            }
            askAllowed = new List<string>();
            bool openedSpotify = false;
            foreach (var a in r.Actions) openedSpotify |= DoAction(a);
            AskAnswer(r.Text.Length > 0 ? r.Text : (r.IsError ? "Claude couldn't do that." : "Done."));
            if (spotifyQuery != null) CheckNowPlaying(spotifyQuery, openedSpotify);
            string first = FirstLine(r.Text.Length > 0 ? r.Text : "Done.");
            if (r.IsError) React(m => m.Flash(Mood.Worried, 1500)); else React(m => m.Celebrate());
            moodText.Text = r.IsError ? "hmm, that didn't work" : "done ✓";
            if (!IsVisible) App.Notify(r.IsError ? "Mochi hit a snag" : "Mochi: done ✓", first, () => OpenAsk(false));
            else if (!expanded) ShowAskResultCard(r.IsError ? "Claude hit a snag" : "Done ✓", first, r.IsError ? "" : "", false);
        }

        static string FirstLine(string t)
        {
            var l = (t ?? "").Replace("\r", "").Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0) ?? "";
            return l.Length > 90 ? l.Substring(0, 90) + "…" : l;
        }

        static string Describe(ClaudeDenial d)
        {
            string detail = d.Detail ?? "";
            if (detail.Length > 140) detail = detail.Substring(0, 140) + "…";
            switch (d.Tool)
            {
                case "Bash": case "PowerShell": return "run: " + detail;
                case "Write": return "create " + Path.GetFileName(detail) + " (in " + Path.GetDirectoryName(detail) + ")";
                case "Edit": case "MultiEdit": return "edit " + Path.GetFileName(detail);
                default: return "use " + d.Tool + (detail.Length > 0 ? " (" + detail + ")" : "");
            }
        }

        void Approve(bool ok)
        {
            E<Grid>("AskApproval").Visibility = Visibility.Collapsed;
            askNeedsOk = false;
            if (!ok)
            {
                askAllowed = new List<string>();
                AskLine("Okay, I told Claude not to.", true);
                React(m => m.Nod());
                return;
            }
            AskLine("Allowed. Claude's continuing…", true);
            AskClaude("I approved that. Go ahead and finish the task.", askSession, null);
        }

        /// <summary>Lines Claude adds for Mochi to act on. Returns true if it opened a Spotify link.</summary>
        bool DoAction(string line)
        {
            int c = line.IndexOf(':');
            string kind = line.Substring(0, c), arg = line.Substring(c + 1).Trim();
            switch (kind)
            {
                case "OPEN":
                    if (Launcher.SafeToOpen(arg)) { Launcher.Start(arg); return arg.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase); }
                    if (arg.Length > 0) AskRow("Open " + Path.GetFileName(arg), arg, "", () => Launcher.Start(arg), null, null, null);
                    return false;
                case "POCKET":
                    if (File.Exists(arg) || Directory.Exists(arg))
                    {
                        shelf.Add(new[] { arg });
                        AskRow(Path.GetFileName(arg), Path.GetDirectoryName(arg), "", () => Launcher.Start(arg), "", "Show in folder",
                            () => { try { Process.Start("explorer.exe", "/select,\"" + arg + "\""); } catch { } });
                    }
                    return false;
                case "NOTE": AppendNote(arg); return false;
                case "COPY": try { Clipboard.SetText(arg); } catch { } return false;
            }
            return false;
        }

        void AskTick()
        {
            if (!askBusy) { askTicker.Stop(); return; }
            int sec = (int)askClock.Elapsed.TotalSeconds;
            if (toast == ToastKind.Delivery && cardKind == CardKind.Ask) E<TextBlock>("DelMeta").Text = "Claude is working  ·  " + sec + "s";
            if (sec % 6 == 0 && !buddyOut && expanded) big.LookAround();
        }

        // ---------------- notch cards for background asks ----------------
        void ShowAskWorking()
        {
            SetupCard(CardKind.Ask, "Mochi's on it…", askLabel ?? "", "");
            var fill = E<Border>("DelFill");
            var a = new DoubleAnimation(0, TrackWidth, TimeSpan.FromMilliseconds(1400)) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = Anim.InOutSine, AutoReverse = true };
            Timeline.SetDesiredFrameRate(a, 20);
            fill.BeginAnimation(FrameworkElement.WidthProperty, a);
            E<TextBlock>("DelMeta").Text = "Claude is working  ·  " + (int)askClock.Elapsed.TotalSeconds + "s";
            if (!buddyOut) { courier.FlashHead(Prop.Glasses, 0); courier.Flash(Mood.Focus, 0); courier.LookAround(); }
            HoldDeliveryCard(600000);
        }

        async void ShowAskResultCard(string title, string line, string slotGlyph, bool needsOk)
        {
            SetupCard(CardKind.Ask, title, line, slotGlyph);
            SetBar(1, !needsOk);
            E<TextBlock>("DelMeta").Text = needsOk ? "Click to review and allow" : "Click to see the full answer";
            if (needsOk) { if (!buddyOut) courier.Startle(); HoldDeliveryCard(600000); return; }
            if (!buddyOut) courier.Present();
            await FlyToSlot(null, "");
            HoldDeliveryCard(7000);
        }
    }
}
