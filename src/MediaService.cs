using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Foundation;
using Windows.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;
using Session = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using Status = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;

namespace NotchIsland
{
    /// <summary>
    /// Talks to Windows' System Media Transport Controls — works with Spotify, browsers (YouTube etc.),
    /// Media Player, and any app that shows up in the Windows media flyout. Purely event driven.
    /// </summary>
    public class MediaService
    {
        public static readonly Color DefaultAccent = Color.FromRgb(0xB8, 0xA6, 0xFF);

        readonly Dispatcher ui;
        GlobalSystemMediaTransportControlsSessionManager mgr;
        Session session;
        TypedEventHandler<Session, MediaPropertiesChangedEventArgs> hProps;
        TypedEventHandler<Session, PlaybackInfoChangedEventArgs> hPlay;
        TypedEventHandler<Session, TimelinePropertiesChangedEventArgs> hTime;

        public event Action Changed;
        public event Action TrackChanged;

        public string Title = "", Artist = "", Album = "", AppId = "", AppName = "";
        public BitmapSource Art;
        public Color Accent = DefaultAccent;
        public bool IsPlaying, CanSeek, CanNext, CanPrev, CanShuffle, CanRepeat, Shuffle;
        public int Repeat; // 0 none, 1 track, 2 list
        public TimeSpan Duration;

        TimeSpan basePos;
        DateTime baseTime = DateTime.UtcNow;
        double rate = 1;
        string lastTrackKey = "";
        int gen;

        public MediaService(Dispatcher d) { ui = d; }

        public bool FakeSession; // snapshot/demo mode only
        public bool HasSession { get { return session != null || FakeSession; } }
        public bool HasTrack { get { return HasSession && Title.Length > 0; } }

        public TimeSpan Position
        {
            get
            {
                var p = basePos;
                if (IsPlaying) p += TimeSpan.FromTicks((long)((DateTime.UtcNow - baseTime).Ticks * rate));
                if (p < TimeSpan.Zero) p = TimeSpan.Zero;
                if (Duration > TimeSpan.Zero && p > Duration) p = Duration;
                return p;
            }
        }

        public async void Start()
        {
            try
            {
                mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                mgr.SessionsChanged += (s, e) => ui.BeginInvoke(new Action(PickSession));
                mgr.CurrentSessionChanged += (s, e) => ui.BeginInvoke(new Action(PickSession));
                PickSession();
            }
            catch (Exception ex) { Debug.WriteLine("SMTC unavailable: " + ex.Message); }
        }

        /// <summary>Prefer whatever is playing (Spotify first), else the session Windows thinks is current.</summary>
        public void PickSession()
        {
            if (mgr == null) return;
            IReadOnlyList<Session> sessions;
            Session cur = null;
            try { sessions = mgr.GetSessions(); cur = mgr.GetCurrentSession(); } catch { return; }
            Session best = null; int bestScore = -1;
            foreach (var s in sessions)
            {
                int score = 0;
                try
                {
                    var st = s.GetPlaybackInfo().PlaybackStatus;
                    if (st == Status.Playing) score += 100;
                    else if (st == Status.Paused) score += 20;
                }
                catch { }
                string id = SafeId(s);
                if (id.IndexOf("spotify", StringComparison.OrdinalIgnoreCase) >= 0) score += 30;
                if (cur != null && id == SafeId(cur)) score += 10;
                if (score > bestScore) { best = s; bestScore = score; }
            }
            if (!ReferenceEquals(best, session))
            {
                Unhook();
                session = best;
                Hook();
                lastTrackKey = "";
                Art = null;
            }
            RefreshPlayback();
            RefreshTimeline();
            RefreshProps();
        }

        static string SafeId(Session s) { try { return s.SourceAppUserModelId ?? ""; } catch { return ""; } }

        void Hook()
        {
            if (session == null) return;
            hProps = (s, e) => ui.BeginInvoke(new Action(RefreshProps));
            hPlay = (s, e) => ui.BeginInvoke(new Action(() => { RefreshPlayback(); RefreshTimeline(); }));
            hTime = (s, e) => ui.BeginInvoke(new Action(RefreshTimeline));
            try
            {
                session.MediaPropertiesChanged += hProps;
                session.PlaybackInfoChanged += hPlay;
                session.TimelinePropertiesChanged += hTime;
            }
            catch { }
        }

        void Unhook()
        {
            if (session == null) return;
            try
            {
                session.MediaPropertiesChanged -= hProps;
                session.PlaybackInfoChanged -= hPlay;
                session.TimelinePropertiesChanged -= hTime;
            }
            catch { }
        }

        void RefreshPlayback()
        {
            var s = session;
            if (s == null)
            {
                IsPlaying = CanSeek = CanNext = CanPrev = CanShuffle = CanRepeat = false;
                Raise();
                return;
            }
            try
            {
                var pb = s.GetPlaybackInfo();
                var pos = Position;
                bool was = IsPlaying;
                IsPlaying = pb.PlaybackStatus == Status.Playing;
                rate = (pb.PlaybackRate.HasValue && pb.PlaybackRate.Value > 0) ? pb.PlaybackRate.Value : 1;
                if (was != IsPlaying) { basePos = pos; baseTime = DateTime.UtcNow; }
                var c = pb.Controls;
                CanSeek = c.IsPlaybackPositionEnabled;
                CanNext = c.IsNextEnabled;
                CanPrev = c.IsPreviousEnabled;
                CanShuffle = c.IsShuffleEnabled;
                CanRepeat = c.IsRepeatEnabled;
                Shuffle = pb.IsShuffleActive.HasValue && pb.IsShuffleActive.Value;
                Repeat = pb.AutoRepeatMode.HasValue ? (int)pb.AutoRepeatMode.Value : 0;
            }
            catch { }
            Raise();
        }

        void RefreshTimeline()
        {
            var s = session;
            if (s == null) return;
            try
            {
                var t = s.GetTimelineProperties();
                var dur = t.EndTime - t.StartTime;
                Duration = dur > TimeSpan.Zero ? dur : TimeSpan.Zero;
                var when = DateTime.UtcNow;
                var lu = t.LastUpdatedTime;
                if (lu.Year > 2000)
                {
                    var u = lu.UtcDateTime;
                    if (u < when) when = u;
                }
                basePos = t.Position - t.StartTime;
                baseTime = when;
            }
            catch { }
            Raise();
        }

        async void RefreshProps()
        {
            var s = session;
            int g = ++gen;
            if (s == null)
            {
                Title = Artist = Album = AppId = AppName = "";
                Art = null; Accent = DefaultAccent; lastTrackKey = "";
                Raise();
                return;
            }
            try
            {
                var p = await s.TryGetMediaPropertiesAsync();
                if (g != gen || p == null) return;
                string title = p.Title ?? "", artist = p.Artist ?? "";
                if (artist.Length == 0) artist = p.AlbumArtist ?? "";
                // Browsers often put "Artist - Topic" or similar; keep it simple.
                string key = title + "\u0001" + artist;
                bool newTrack = key != lastTrackKey;
                Title = title; Artist = artist; Album = p.AlbumTitle ?? "";
                AppId = SafeId(s); AppName = FriendlyName(AppId);
                if (newTrack || Art == null)
                {
                    lastTrackKey = key;
                    BitmapSource art = null;
                    if (p.Thumbnail != null) art = await LoadThumb(p.Thumbnail);
                    if (g != gen) return;
                    Art = art;
                    Accent = art != null ? ComputeAccent(art) : DefaultAccent;
                }
                Raise();
                if (newTrack && title.Length > 0) { var h = TrackChanged; if (h != null) h(); }
            }
            catch (Exception ex) { Debug.WriteLine(ex.Message); }
        }

        void Raise() { var h = Changed; if (h != null) h(); }

        static async Task<BitmapSource> LoadThumb(IRandomAccessStreamReference r)
        {
            try
            {
                using (var stream = await r.OpenReadAsync())
                {
                    ulong size = stream.Size;
                    if (size == 0 || size > 20000000) return null;
                    var bytes = new byte[size];
                    using (var reader = new DataReader(stream))
                    {
                        await reader.LoadAsync((uint)size);
                        reader.ReadBytes(bytes);
                    }
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.DecodePixelWidth = 240;
                    bi.StreamSource = new MemoryStream(bytes);
                    bi.EndInit();
                    bi.Freeze();
                    return bi;
                }
            }
            catch { return null; }
        }

        /// <summary>Saturation-weighted average of a 24px downscale, then brightened so it glows on black.</summary>
        public static Color ComputeAccent(BitmapSource src)
        {
            try
            {
                var small = new TransformedBitmap(src, new ScaleTransform(24.0 / src.PixelWidth, 24.0 / src.PixelHeight));
                var conv = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
                int w = conv.PixelWidth, h = conv.PixelHeight;
                var px = new byte[w * h * 4];
                conv.CopyPixels(px, w * 4, 0);
                double r = 0, g = 0, b = 0, wt = 0;
                for (int i = 0; i < px.Length; i += 4)
                {
                    double B = px[i], G = px[i + 1], R = px[i + 2];
                    double mx = Math.Max(R, Math.Max(G, B)), mn = Math.Min(R, Math.Min(G, B));
                    double sat = mx <= 0 ? 0 : (mx - mn) / mx, lum = mx / 255.0;
                    double weight = 0.02 + sat * sat * lum;
                    r += R * weight; g += G * weight; b += B * weight; wt += weight;
                }
                if (wt <= 0) return DefaultAccent;
                double hh, ss, vv;
                ToHsv(r / wt, g / wt, b / wt, out hh, out ss, out vv);
                if (ss < 0.08) return Color.FromRgb(0xE8, 0xE8, 0xF0);
                ss = Math.Min(1, Math.Max(ss, 0.45));
                vv = Math.Max(vv, 0.92);
                return FromHsv(hh, ss, vv);
            }
            catch { return DefaultAccent; }
        }

        static void ToHsv(double r, double g, double b, out double h, out double s, out double v)
        {
            r /= 255; g /= 255; b /= 255;
            double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b)), d = mx - mn;
            v = mx; s = mx <= 0 ? 0 : d / mx; h = 0;
            if (d > 0)
            {
                if (mx == r) h = ((g - b) / d) % 6;
                else if (mx == g) h = (b - r) / d + 2;
                else h = (r - g) / d + 4;
                h *= 60; if (h < 0) h += 360;
            }
        }

        static Color FromHsv(double h, double s, double v)
        {
            double c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; } else if (h < 120) { r = x; g = c; } else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; } else if (h < 300) { r = x; b = c; } else { r = c; b = x; }
            return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
        }

        public static string FriendlyName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            string l = id.ToLowerInvariant();
            if (l.Contains("spotify")) return "Spotify";
            if (l.Contains("chrome")) return "Chrome";
            if (l.Contains("msedge") || l.Contains("microsoftedge")) return "Edge";
            if (l.Contains("firefox")) return "Firefox";
            if (l.Contains("opera")) return "Opera";
            if (l.Contains("brave")) return "Brave";
            if (l.Contains("zunemusic") || l.Contains("media player")) return "Media Player";
            if (l.Contains("vlc")) return "VLC";
            if (l.Contains("applemusic") || l.Contains("itunes")) return "Apple Music";
            if (l.Contains("discord")) return "Discord";
            string n = id;
            int bang = n.LastIndexOf('!'); if (bang >= 0) n = n.Substring(bang + 1);
            if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 4);
            return n.Length > 0 ? char.ToUpper(n[0]) + n.Substring(1) : n;
        }

        // ---------- commands ----------
        public async void PlayPause()
        {
            var s = session;
            if (s == null) { LaunchSpotify(); return; }
            try { await s.TryTogglePlayPauseAsync(); } catch { }
        }
        public async void Next() { var s = session; if (s == null) return; try { await s.TrySkipNextAsync(); } catch { } }
        public async void Prev() { var s = session; if (s == null) return; try { await s.TrySkipPreviousAsync(); } catch { } }
        public async void Seek(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            if (Duration > TimeSpan.Zero && t > Duration) t = Duration;
            basePos = t; baseTime = DateTime.UtcNow;
            Raise();
            var s = session; if (s == null) return;
            try { await s.TryChangePlaybackPositionAsync(t.Ticks); } catch { }
        }
        public async void ToggleShuffle()
        {
            var s = session; if (s == null) return;
            try { await s.TryChangeShuffleActiveAsync(!Shuffle); } catch { }
        }
        public async void CycleRepeat()
        {
            var s = session; if (s == null) return;
            // none -> all -> one -> none (Spotify order)
            var next = Repeat == 0 ? MediaPlaybackAutoRepeatMode.List : Repeat == 2 ? MediaPlaybackAutoRepeatMode.Track : MediaPlaybackAutoRepeatMode.None;
            try { await s.TryChangeAutoRepeatModeAsync(next); } catch { }
        }

        public void OpenSourceApp()
        {
            string id = AppId;
            if (string.IsNullOrEmpty(id)) { LaunchSpotify(); return; }
            try
            {
                if (id.Contains("!")) { Process.Start("explorer.exe", "shell:AppsFolder\\" + id); return; }
                string name = Path.GetFileNameWithoutExtension(id);
                foreach (var p in Process.GetProcessesByName(name))
                {
                    var h = p.MainWindowHandle;
                    if (h != IntPtr.Zero)
                    {
                        if (Native.IsIconic(h)) Native.ShowWindow(h, 9);
                        Native.SetForegroundWindow(h);
                        return;
                    }
                }
                if (id.IndexOf("spotify", StringComparison.OrdinalIgnoreCase) >= 0) LaunchSpotify();
            }
            catch { }
        }

        public static void LaunchSpotify()
        {
            try { Process.Start(new ProcessStartInfo("spotify:") { UseShellExecute = true }); } catch { }
        }
    }
}
