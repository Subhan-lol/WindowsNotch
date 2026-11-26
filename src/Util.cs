using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;
using Windows.Foundation;

namespace NotchIsland
{
    /// <summary>Lets us 'await' WinRT async operations without the SDK projection assemblies.</summary>
    public static class WinRtAsync
    {
        public static Task<T> AsTaskX<T>(this IAsyncOperation<T> op)
        {
            var tcs = new TaskCompletionSource<T>();
            op.Completed = (o, st) =>
            {
                if (st == AsyncStatus.Completed)
                {
                    try { tcs.TrySetResult(o.GetResults()); } catch (Exception e) { tcs.TrySetException(e); }
                }
                else if (st == AsyncStatus.Error) tcs.TrySetException(o.ErrorCode ?? new Exception("WinRT error"));
                else tcs.TrySetCanceled();
            };
            return tcs.Task;
        }
        public static TaskAwaiter<T> GetAwaiter<T>(this IAsyncOperation<T> op) { return op.AsTaskX().GetAwaiter(); }
    }

    public static class Paths
    {
        public static readonly string Roaming = Ensure(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NotchIsland"));
        public static readonly string ShelfDir = Ensure(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NotchIsland", "Shelf"));
        public static string File(string name) { return Path.Combine(Roaming, name); }
        static string Ensure(string p) { try { Directory.CreateDirectory(p); } catch { } return p; }
    }

    /// <summary>Tiny key=value settings file. Cheap, human-editable, no serializer dependencies.</summary>
    public static class Settings
    {
        static readonly Dictionary<string, string> values = new Dictionary<string, string>();
        static readonly Dictionary<string, bool> defaults = new Dictionary<string, bool>
        {
            { "ExpandOnHover", true }, { "HideInFullscreen", true }, { "VolumeHud", true }, { "BatteryAlerts", true },
            { "ClipboardHistory", true }, { "ScrollVolume", true }, { "AlbumColors", true }, { "Visualizer", true },
            { "TrackPopups", true }, { "Sounds", true }, { "StartWithWindows", false }, { "BuddyOut", false }, { "PhoneDrop", false },
        };
        public static event Action<string> Changed;

        public static void Load()
        {
            try
            {
                var f = Paths.File("settings.ini");
                if (!System.IO.File.Exists(f)) return;
                foreach (var line in System.IO.File.ReadAllLines(f))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) values[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                }
            }
            catch { }
        }
        public static bool ReadOnly; // snapshot mode
        public static void Save()
        {
            if (ReadOnly) return;
            try
            {
                var lines = new List<string>();
                foreach (var kv in values) lines.Add(kv.Key + "=" + kv.Value);
                System.IO.File.WriteAllLines(Paths.File("settings.ini"), lines);
            }
            catch { }
        }
        public static bool Get(string key)
        {
            string v;
            if (values.TryGetValue(key, out v)) return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
            bool d; return defaults.TryGetValue(key, out d) && d;
        }
        public static void Set(string key, bool on)
        {
            values[key] = on ? "1" : "0";
            Save();
            var h = Changed; if (h != null) h(key);
        }
        public static IEnumerable<string> Keys { get { return defaults.Keys; } }

        public static string GetStr(string key, string fallback)
        {
            string v;
            return values.TryGetValue(key, out v) ? v : fallback;
        }
        public static void SetStr(string key, string v)
        {
            values[key] = v ?? "";
            Save();
        }

        public static double GetNum(string key, double fallback)
        {
            string v; double d;
            if (values.TryGetValue(key, out v) && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) return d;
            return fallback;
        }
        public static void SetNum(string key, double v)
        {
            values[key] = v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
            Save();
        }
    }

    public static class Anim
    {
        public static readonly IEasingFunction OutBack = Freeze(new BackEase { Amplitude = 0.32, EasingMode = EasingMode.EaseOut });
        public static readonly IEasingFunction OutBackSoft = Freeze(new BackEase { Amplitude = 0.15, EasingMode = EasingMode.EaseOut });
        public static readonly IEasingFunction OutCubic = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });
        public static readonly IEasingFunction InOutSine = Freeze(new SineEase { EasingMode = EasingMode.EaseInOut });
        public static readonly IEasingFunction OutElastic = Freeze(new ElasticEase { Oscillations = 2, Springiness = 5, EasingMode = EasingMode.EaseOut });

        static IEasingFunction Freeze(EasingFunctionBase e) { e.Freeze(); return e; }

        public static AnimationTimeline To(IAnimatable target, DependencyProperty dp, double to, double ms,
            IEasingFunction ease = null, double delayMs = 0, int fps = 0, EventHandler done = null)
        {
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms));
            if (ease != null) a.EasingFunction = ease;
            if (delayMs > 0) a.BeginTime = TimeSpan.FromMilliseconds(delayMs);
            if (fps > 0) Timeline.SetDesiredFrameRate(a, fps);
            if (done != null) a.Completed += done;
            target.BeginAnimation(dp, a, HandoffBehavior.SnapshotAndReplace);
            return a;
        }

        /// <summary>from -> to and back (a "pulse"), optionally repeated.</summary>
        public static void Pulse(IAnimatable target, DependencyProperty dp, double from, double to, double ms,
            IEasingFunction ease = null, double repeat = 1, int fps = 0, double delayMs = 0)
        {
            var a = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(repeat) };
            if (ease != null) a.EasingFunction = ease;
            if (fps > 0) Timeline.SetDesiredFrameRate(a, fps);
            if (delayMs > 0) a.BeginTime = TimeSpan.FromMilliseconds(delayMs);
            target.BeginAnimation(dp, a, HandoffBehavior.SnapshotAndReplace);
        }

        public static void Stop(IAnimatable target, DependencyProperty dp) { target.BeginAnimation(dp, null); }
    }

    /// <summary>One background STA thread for shell work (thumbnails) so the UI never stutters.</summary>
    public sealed class StaWorker
    {
        readonly BlockingCollection<Action> queue = new BlockingCollection<Action>();
        public StaWorker()
        {
            var t = new Thread(() => { foreach (var job in queue.GetConsumingEnumerable()) { try { job(); } catch { } } });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);
            t.Priority = ThreadPriority.BelowNormal;
            t.Name = "NotchShellWorker";
            t.Start();
        }
        public void Post(Action a) { queue.Add(a); }
    }
}
