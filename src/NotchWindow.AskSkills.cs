using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace NotchIsland
{
    public class AskIntent
    {
        public string Kind, Arg, Arg2;
        public double Num;
        public override string ToString() { return Kind + (Arg != null ? " [" + Arg + "]" : "") + (Arg2 != null ? " [" + Arg2 + "]" : "") + (Num != 0 ? " " + Num : ""); }
    }

    /// <summary>What Mochi understands without needing AI, and how each thing is done.</summary>
    public partial class NotchWindow
    {
        static readonly string[,] NumberWords = {
            { "one", "1" }, { "two", "2" }, { "three", "3" }, { "four", "4" }, { "five", "5" }, { "six", "6" }, { "seven", "7" }, { "eight", "8" },
            { "nine", "9" }, { "ten", "10" }, { "fifteen", "15" }, { "twenty", "20" }, { "thirty", "30" }, { "forty five", "45" }, { "forty", "40" },
            { "fifty", "50" }, { "sixty", "60" }, { "ninety", "90" }, { "a hundred", "100" }, { "hundred", "100" }, { "an hour", "1 hour" }, { "half an hour", "30 minutes" } };

        static string Normalize(string text)
        {
            var t = Regex.Replace(text.Trim(), @"\s+", " ");
            t = t.TrimEnd('.', '!');
            t = Regex.Replace(t, @"^(?:(?:hey|hi|ok|okay|yo)\s+)?mochi[,:!]?\s*", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"^(?:please\s+|(?:can|could|would|will) you\s+(?:please\s+)?)", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"[,]?\s+please$", "", RegexOptions.IgnoreCase);
            for (int i = 0; i < NumberWords.GetLength(0); i++)
                t = Regex.Replace(t, @"\b" + NumberWords[i, 0] + @"\b", NumberWords[i, 1], RegexOptions.IgnoreCase);
            return t;
        }

        static Match M(string text, string pattern) { return Regex.Match(text, "^" + pattern + "$", RegexOptions.IgnoreCase); }

        static AskIntent I(string kind, string arg = null, string arg2 = null, double num = 0) { return new AskIntent { Kind = kind, Arg = arg, Arg2 = arg2, Num = num }; }

        public static AskIntent Parse(string raw)
        {
            string t = Normalize(raw);
            Match m;
            // explicit routes first
            if ((m = M(t, @"(?:hey |ask )?claude[,:]?\s+(.+)")).Success) return I("claude", m.Groups[1].Value);
            if ((m = M(t, @"(?:open )?(?:claude )?(?:in )?(?:the )?terminal[,:]?\s+(.+)")).Success) return I("terminal", m.Groups[1].Value);
            if ((m = M(t, @"(?:help|what can you do\??)")).Success) return I("help");

            // music
            if ((m = M(t, @"(?:pause|stop)(?: the)?(?: music| song| spotify| playing)?")).Success) return I("pause");
            if ((m = M(t, @"(?:resume|play|unpause|continue)(?: the)?(?: music| song| spotify)?")).Success) return I("resume");
            if ((m = M(t, @"(?:next|skip)(?: this)?(?: song| track| one)?")).Success) return I("next");
            if ((m = M(t, @"(?:previous|prev|back|go back|last)(?: song| track| one)?")).Success) return I("prev");
            if ((m = M(t, @"(?:play|put on|listen to|queue up)\s+(?:some\s+|the song\s+|song\s+)?(.+?)(?:\s+on spotify)?")).Success) return I("play", m.Groups[1].Value);
            if ((m = M(t, @"(?:set )?(?:the )?volume(?: to)? (\d{1,3})(?: ?%| percent)?")).Success) return I("volume", null, null, double.Parse(m.Groups[1].Value));
            if ((m = M(t, @"(?:turn it up|louder|volume up|turn up the volume|increase volume)")).Success) return I("volume+");
            if ((m = M(t, @"(?:turn it down|quieter|softer|volume down|turn down the volume|lower the volume|decrease volume)")).Success) return I("volume-");
            if ((m = M(t, @"(mute|unmute)(?: the)?(?: sound| audio| volume)?")).Success) return I(m.Groups[1].Value.ToLowerInvariant());

            // notes
            if ((m = M(t, @"(?:new note|write (?:a |me a )?new note|create (?:a )?(?:new )?note|make (?:a )?(?:new )?note)(?: that| saying| about| with)?[:,]?\s*(.*)")).Success) return I("newnote", m.Groups[1].Value);
            if ((m = M(t, @"(?:note|take (?:a )?note|write (?:a |down )?note|jot(?: down)?|add (?:a )?note|remember(?: that)?|write down)(?: that| saying)?[:,]?\s+(.+)")).Success) return I("note", m.Groups[1].Value);

            // timers / reminders
            if ((m = M(t, @"remind me in (\d+(?:\.\d+)?)\s*(s|sec|secs|seconds?|m|min|mins|minutes?|h|hr|hrs|hours?)\s+(?:to\s+|that\s+|about\s+)?(.+)")).Success)
                return I("timer", m.Groups[3].Value, m.Groups[2].Value, double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
            if ((m = M(t, @"(?:set )?(?:a )?(?:timer|alarm|countdown)(?: for)? (\d+(?:\.\d+)?)\s*(s|sec|secs|seconds?|m|min|mins|minutes?|h|hr|hrs|hours?)?")).Success ||
                (m = M(t, @"(\d+(?:\.\d+)?)\s*(s|sec|secs|seconds?|m|min|mins|minutes?|h|hr|hrs|hours?)\s+timer")).Success)
                return I("timer", null, m.Groups[2].Value, double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
            if ((m = M(t, @"(?:stop|cancel) (?:the )?timer")).Success) return I("stoptimer");

            // files
            if ((m = M(t, @"(?:find|locate|where(?:'s| is)|search (?:my computer for|my files for|my laptop for))\s+(?:me\s+)?(?:my\s+|the\s+|a\s+)?(.+?)(?:\s+files?|\s+documents?)?(?:\s+on my (?:laptop|computer|pc))?")).Success) return I("find", m.Groups[1].Value);

            // apps & web
            if ((m = M(t, @"(?:google|search the web for|search google for|look up|search for|search)\s+(.+)")).Success) return I("web", m.Groups[1].Value);
            if ((m = M(t, @"(?:open|launch|start|run)\s+(?:the\s+|my\s+)?(.+)")).Success) return I("open", m.Groups[1].Value);

            // phone, clipboard, maths, misc
            if ((m = M(t, @"send (?:my |the )?clipboard to (?:my )?(?:phone|iphone)")).Success) return I("phoneclip");
            if ((m = M(t, @"send\s+(.+?)\s+to (?:my )?(?:phone|iphone)")).Success) return I("phone", m.Groups[1].Value);
            if ((m = M(t, @"copy\s+(.+)")).Success) return I("copy", m.Groups[1].Value);
            if ((m = M(t, @"(?:lock|lock (?:my |the )?(?:pc|laptop|computer|screen))")).Success) return I("lock");
            if ((m = M(t, @"(?:what(?:'s| is)|calculate|calc|compute)?\s*([\d\s.,+\-*/x×÷()%]+(?:\s*(?:plus|minus|times|divided by|of)\s*[\d\s.,+\-*/x×÷()%]+)*)\??")).Success
                && Regex.IsMatch(m.Groups[1].Value, @"\d") && Regex.IsMatch(m.Groups[1].Value, @"[+\-*/x×÷%]|plus|minus|times|divided|of") && Launcher.Calc(m.Groups[1].Value) != null)
                return I("calc", m.Groups[1].Value.Trim());

            // anything else: Claude
            return I("claude", raw.Trim());
        }

        static double Seconds(double n, string unit)
        {
            unit = (unit ?? "").ToLowerInvariant();
            if (unit.StartsWith("s")) return n;
            if (unit.StartsWith("h")) return n * 3600;
            return n * 60;
        }

        void Execute(AskIntent it)
        {
            if (AskDryRun) { AskLine("(test) " + it, true); return; }
            switch (it.Kind)
            {
                case "help":
                    AskAnswer("Things I can do right away:\n• play <song>, pause, next, previous, volume 40, mute\n• note <text>, new note <text>\n• find <file name>, open <app or website>, google <anything>\n• timer 10 min, remind me in 20 min to stretch\n• what's 18% of 240, copy <text>, send <text> to my phone, lock\n• claude <anything> runs Claude in the background; terminal <anything> opens Claude in Windows Terminal\nTip: press Ctrl+Alt+Space twice to talk.");
                    break;
                case "pause": if (media.IsPlaying) media.PlayPause(); AskDone("Paused ⏸", true); break;
                case "resume": if (!media.IsPlaying) media.PlayPause(); AskDone("Playing ▶", true); break;
                case "next": media.Next(); React(m => m.Spin()); AskDone("Next song ⏭", true); break;
                case "prev": media.Prev(); AskDone("Previous song ⏮", true); break;
                case "volume": audio.SetVolume((float)Math.Max(0, Math.Min(100, it.Num)) / 100f); AskDone("Volume " + (int)it.Num + "%", true); break;
                case "volume+": audio.SetVolume(audio.Volume + 0.1f); AskDone("Volume " + (int)Math.Round(Math.Min(1, audio.Volume) * 100) + "%", true); break;
                case "volume-": audio.SetVolume(audio.Volume - 0.1f); AskDone("Volume " + (int)Math.Round(Math.Max(0, audio.Volume) * 100) + "%", true); break;
                case "mute": if (!audio.Muted) audio.ToggleMute(); React(m => m.Shh(1100)); AskDone("Muted 🔇", true); break;
                case "unmute": if (audio.Muted) audio.ToggleMute(); AskDone("Sound's back on", true); break;
                case "play": PlaySong(it.Arg); break;
                case "note": AppendNote(it.Arg); AskDone("Added to your notes ✓", true); break;
                case "newnote": NewNote(it.Arg); break;
                case "timer": StartTimerFor(Seconds(it.Num, it.Arg2), it.Arg); break;
                case "stoptimer": if (timerRunning) ToggleTimer(); timerLeft = timerTotal; UpdateTimerUI(); timerLabel = null; AskDone("Timer stopped", true); break;
                case "find": FindFiles(it.Arg); break;
                case "web": Launcher.Start("https://www.google.com/search?q=" + Uri.EscapeDataString(it.Arg)); AskDone("Searching the web for “" + it.Arg + "”", true); break;
                case "open":
                    var opened = Launcher.Open(it.Arg);
                    if (opened != null) AskDone("Opening " + opened, true);
                    else if (ClaudeRunner.Available) { AskLine("I couldn't find an app called “" + it.Arg + "”, so I'm asking Claude…", true); AskClaude("Open this for me on Windows: " + it.Arg, null, null); }
                    else AskLine("I couldn't find an app called “" + it.Arg + "”.", true);
                    break;
                case "phoneclip": E<Button>("PhoneSendClip").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); AskDone("Sending your clipboard to your iPhone", false); break;
                case "phone": SendTextToPhone(it.Arg); AskDone("Sending to your iPhone: " + it.Arg, false); break;
                case "copy": try { Clipboard.SetText(it.Arg); } catch { } AskDone("Copied ✓", true); break;
                case "lock": AskDone("Locking…", false); LockWorkStation(); break;
                case "calc":
                    var res = Launcher.Calc(it.Arg);
                    if (res == null) { AskLine("I couldn't work that out.", true); break; }
                    try { Clipboard.SetText(res); } catch { }
                    AskAnswer(it.Arg + " = " + res);
                    AskLine("(copied the answer)", true);
                    React(m => m.Nod());
                    break;
                case "terminal":
                    if (ClaudeRunner.OpenInTerminal(it.Arg)) AskDone("Opened Claude in Terminal with your request", true);
                    else AskLine("I need Claude Code installed to do that.", true);
                    break;
                default: AskClaude(it.Arg, null, null); break;
            }
        }

        // ---------------- skills ----------------
        void PlaySong(string query)
        {
            var q = (query ?? "").Trim();
            if (q.Length == 0 || Regex.IsMatch(q, @"^(music|spotify|something|a song|songs)$", RegexOptions.IgnoreCase))
            {
                if (!media.IsPlaying) { if (media.HasTrack) media.PlayPause(); else MediaService.LaunchSpotify(); }
                AskDone("Playing ▶", true);
                return;
            }
            Launcher.Start("spotify:search:" + Uri.EscapeDataString(q));
            if (!buddyOut) big.FlashHead(Prop.Headphones, 4000);
            if (ClaudeRunner.Available)
            {
                AskLine("Opened Spotify search for “" + q + "”. Finding the exact song so it plays…", true);
                AskClaude("Play this on Spotify: " + q, null, q);
            }
            else AskDone("Opened Spotify search for “" + q + "”, tap play", true);
        }

        /// <summary>After opening a Spotify link, check what's actually playing and say so honestly.</summary>
        async void CheckNowPlaying(string query, bool opened)
        {
            if (!opened) { AskLine("Claude couldn't pin down the exact song, so it's open in Spotify search. Tap play.", true); return; }
            await Task.Delay(4000);
            media.PickSession();
            await Task.Delay(800);
            var words = query.ToLowerInvariant().Split(' ').Where(w => w.Length > 2).ToList();
            string now = (media.Title + " " + media.Artist).ToLowerInvariant();
            if (media.IsPlaying && words.Any(w => now.Contains(w)))
                AskDone("Now playing: " + media.Title + (media.Artist.Length > 0 ? " · " + media.Artist : "") + " ♪", true);
            else
                AskLine("It's open in Spotify. If it didn't start by itself, tap play.", true);
        }

        void AppendNote(string text)
        {
            var notes = E<TextBox>("NotesBox");
            string line = "• " + text.Trim();
            notes.Text = notes.Text.Length == 0 ? line : notes.Text.TrimEnd() + Environment.NewLine + line;
            SaveNotesNow();
            React(m => { m.FlashHand(Prop.Pencil, 1200); m.Scribble(); });
        }

        void NewNote(string text)
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mochi Notes");
            Directory.CreateDirectory(dir);
            string title = string.Join(" ", (text ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Take(5));
            foreach (var c in Path.GetInvalidFileNameChars()) title = title.Replace(c, ' ');
            title = title.Trim();
            string name = (title.Length > 0 ? title : "Note") + " " + DateTime.Now.ToString("yyyy-MM-dd HH.mm") + ".txt";
            string path = Path.Combine(dir, name);
            File.WriteAllText(path, text ?? "");
            shelf.Add(new[] { path });
            React(m => { m.FlashHand(Prop.Pencil, 1200); m.Scribble(); });
            AskLine("Saved a new note ✓ (also in your pocket)", false);
            AskRow(name, dir, "", () => Launcher.Start(path), "", "Show in folder", () => { try { Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { } });
            if (!expanded) ShowToast(ToastKind.Info, 2600, "New note saved ✓", name, "");
        }

        void StartTimerFor(double seconds, string label)
        {
            if (seconds < 1) { AskLine("How long should the timer be?", true); return; }
            timerTotal = timerLeft = TimeSpan.FromSeconds(seconds);
            timerEnd = DateTime.Now + timerLeft;
            timerLabel = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
            if (!timerRunning) ToggleTimer(); else { UpdateTimerUI(); ApplyCompact(); }
            AskDone((timerLabel != null ? "I'll remind you to " + timerLabel + " in " : "Timer set for ") + FormatTime(timerLeft) + " ⏱", true);
        }

        async void FindFiles(string query)
        {
            var status = AskLine("Looking for “" + query + "”…", true);
            if (!buddyOut) { big.FlashHead(Prop.Glasses, 0); big.LookAround(); }
            moodText.Text = "searching…";
            var hits = await FileFinder.Find(query, 8);
            askOut.Children.Remove(status);
            if (!buddyOut) big.FlashHead(Prop.None, 0);
            if (hits.Count == 0)
            {
                AskLine("I couldn't find a file called “" + query + "”." + (ClaudeRunner.Available ? " Try: claude find " + query + " (Claude can dig deeper)." : ""), true);
                React(m => m.Flash(Mood.Worried, 1200));
                if (!expanded) ShowToast(ToastKind.Info, 2600, "No files found", query, "");
                return;
            }
            AskLine("Found " + hits.Count + (hits.Count == 1 ? " file" : " files") + ". Click to open, or put one in your pocket:", false);
            foreach (var h in hits)
            {
                var p = h;
                AskRow(Path.GetFileName(p), Path.GetDirectoryName(p), "", () => Launcher.Start(p), "", "Put in pocket", () => shelf.Add(new[] { p }));
            }
            React(m => m.Nod());
            moodText.Text = "found " + hits.Count + "!";
            if (!expanded) ShowAskResultCard("Found " + hits.Count + (hits.Count == 1 ? " file" : " files"), "for “" + query + "”", "", false);
        }
    }
}
