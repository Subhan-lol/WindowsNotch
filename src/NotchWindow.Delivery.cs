using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NotchIsland
{
    /// <summary>
    /// Everything that travels between the iPhone and the laptop shows up as a little delivery you can watch:
    /// Mochi in a courier cap hugs a parcel while a progress bar fills, then unpacks it; the file (or note)
    /// flies into the slot on the right — the pocket for things arriving, a phone for things leaving —
    /// a green check pops and the card says what happened. Works for files, links and plain text, both ways.
    /// </summary>
    public partial class NotchWindow
    {
        class Delivery { public int Id; public string Name; public long Total = -1, Done; public string Path; public bool Finished, Ok; }
        enum CardKind { InFile, InText, Out, Ask }

        const string GlyphPocket = "", GlyphPhone = "", GlyphClipboard = "", GlyphDoc = "", GlyphLink = "";

        readonly Dictionary<int, Delivery> deliveries = new Dictionary<int, Delivery>();
        readonly Dictionary<int, PhoneLink.OutItem> pendingOut = new Dictionary<int, PhoneLink.OutItem>();
        Delivery shownDelivery;
        PhoneLink.OutItem cardOut;
        CardKind cardKind;
        string cardLink;
        int newFromPhone;
        Grid deliveryCard;
        Mascot courier;

        void WireDelivery()
        {
            deliveryCard = E<Grid>("DeliveryCard");
            courier = E<Mascot>("DeliveryMascot");
            courier.Particles = true;
            deliveryCard.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                if (cardKind == CardKind.InText && cardLink != null) { OpenLink(cardLink); return; }
                if (cardKind == CardKind.Ask) { OpenAsk(false); return; }
                Expand(cardKind == CardKind.InText ? "clip" : "shelf");
            };
            phone.UploadStarted += OnUploadStarted;
            phone.UploadProgress += OnUploadProgress;
            phone.UploadFinished += OnUploadFinished;
            phone.TextReceived += OnTextReceived;
            phone.OutboxSeen += OnOutboxSeen;
            phone.OutProgress += OnOutProgress;
            phone.OutSaved += OnOutSaved;
        }

        static void OpenLink(string url)
        {
            if (!PhoneLink.IsLink(url)) return;
            try { Process.Start(new ProcessStartInfo(url.Trim()) { UseShellExecute = true }); } catch { }
        }

        static string Size(long b)
        {
            if (b < 0) return "";
            if (b < 1024) return b + " B";
            if (b < 1048576) return (b / 1024.0).ToString("0") + " KB";
            if (b < 1073741824) return (b / 1048576.0).ToString("0.0") + " MB";
            return (b / 1073741824.0).ToString("0.00") + " GB";
        }

        static string Preview(string text)
        {
            var t = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return t.Length > 70 ? t.Substring(0, 70) + "…" : t;
        }

        int ActiveCount { get { return deliveries.Values.Count(d => !d.Finished); } }

        // =============== the card ===============
        void SetupCard(CardKind kind, string title, string name, string slotGlyph)
        {
            cardKind = kind;
            E<TextBlock>("DelTitle").Text = title;
            E<TextBlock>("DelName").Text = name;
            E<TextBlock>("DelMeta").Text = "";
            var fill = E<Border>("DelFill");
            fill.BeginAnimation(FrameworkElement.WidthProperty, null);
            fill.Background = (Brush)root.Resources["Accent"];
            fill.Width = 0;
            E<Border>("DelThumb").Visibility = Visibility.Collapsed;
            E<Grid>("DelCheck").Visibility = Visibility.Collapsed;
            var slot = E<TextBlock>("DelSlotIcon");
            slot.Text = slotGlyph; slot.Visibility = Visibility.Visible;
            E<UIElement>("DelParcelIcon").Visibility = buddyOut ? Visibility.Visible : Visibility.Collapsed;
            courier.Visibility = buddyOut ? Visibility.Collapsed : Visibility.Visible;
            ShowToast(ToastKind.Delivery, 600000, null, null, null);
            if (!buddyOut) { courier.Active = true; courier.Face(kind == CardKind.Out ? 1 : 1); }
        }

        double TrackWidth { get { double w = E<Grid>("DelTrack").ActualWidth; return w > 0 ? w : 210; } }

        void SetBar(double frac, bool green)
        {
            var fill = E<Border>("DelFill");
            if (green) fill.Background = new SolidColorBrush(Color.FromRgb(0x7D, 0xF0, 0x9A));
            Anim.To(fill, FrameworkElement.WidthProperty, Math.Max(0, Math.Min(1, frac)) * TrackWidth, 150);
        }

        /// <summary>The file (thumbnail, or a little paper/link icon) pops out of Mochi's parcel and lands in the slot.</summary>
        async Task FlyToSlot(string path, string glyph)
        {
            var thumb = E<Border>("DelThumb");
            var img = E<ImageBrush>("DelThumbBrush");
            var icon = E<TextBlock>("DelThumbIcon");
            img.ImageSource = null;
            icon.Text = glyph; icon.Visibility = Visibility.Visible;
            if (path != null) shelf.ThumbAsync(path, bmp => { if (bmp != null) { img.ImageSource = bmp; icon.Visibility = Visibility.Collapsed; } });
            var tt = E<TranslateTransform>("DelThumbMove");
            var st = E<ScaleTransform>("DelThumbScale");
            var rt = E<RotateTransform>("DelThumbSpin");
            thumb.Visibility = Visibility.Visible;
            Point from = new Point(36, 40), to = new Point(deliveryCard.Width - 52, 31);
            tt.BeginAnimation(TranslateTransform.XProperty, Arc(from.X, to.X, 650, false));
            tt.BeginAnimation(TranslateTransform.YProperty, ArcY(from.Y, 6, to.Y, 650));
            st.BeginAnimation(ScaleTransform.ScaleXProperty, Hop(0.3, 1.25, 1, 650));
            st.BeginAnimation(ScaleTransform.ScaleYProperty, Hop(0.3, 1.25, 1, 650));
            rt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-25, 0, TimeSpan.FromMilliseconds(650)) { EasingFunction = Anim.OutCubic });
            await Task.Delay(650);
            if (toast != ToastKind.Delivery) return;
            E<TextBlock>("DelSlotIcon").Visibility = Visibility.Collapsed;
            var slot = E<ScaleTransform>("DelSlotScale");
            slot.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.18, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = Anim.OutElastic });
            slot.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.18, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = Anim.OutElastic });
            var cs = E<ScaleTransform>("DelCheckScale");
            E<Grid>("DelCheck").Visibility = Visibility.Visible;
            cs.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = Anim.OutBack });
            cs.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = Anim.OutBack });
            if (!buddyOut) courier.Celebrate();
            if (Settings.Get("Sounds")) { try { System.Media.SystemSounds.Asterisk.Play(); } catch { } }
        }

        void HoldDeliveryCard(int ms)
        {
            toastTimer.Stop();
            toastTimer.Interval = TimeSpan.FromMilliseconds(ms);
            toastTimer.Start();
        }

        void EndDeliveryCard()
        {
            courier.SetCarrying(false);
            courier.FlashHand(Prop.None, 0); courier.FlashHead(Prop.None, 0);
            courier.Active = false;
        }

        // =============== iPhone -> laptop: files ===============
        void OnUploadStarted(int id, string name, long total)
        {
            var d = new Delivery { Id = id, Name = name, Total = total };
            deliveries[id] = d;
            if (shownDelivery == null || shownDelivery.Finished) shownDelivery = d;
            if (!IsVisible) return;
            if (expanded)
            {
                if (tab != "shelf") SelectTab("shelf");
                if (!buddyOut) big.SetCarrying(true); else React(m => m.Startle());
                moodText.Text = "incoming from your iPhone…";
                return;
            }
            ShowReceiving(d);
        }

        void OnUploadProgress(int id, long done)
        {
            Delivery d;
            if (!deliveries.TryGetValue(id, out d)) return;
            d.Done = done;
            if (d != shownDelivery) return;
            if (expanded && !buddyOut)
            {
                moodText.Text = d.Total > 0 ? "receiving… " + (int)(100.0 * done / d.Total) + "%" : "receiving… " + Size(done);
                return;
            }
            if (toast == ToastKind.Delivery && cardKind == CardKind.InFile) UpdateProgressUI(d);
        }

        void OnUploadFinished(int id, string pathOrName, bool ok)
        {
            Delivery d;
            if (!deliveries.TryGetValue(id, out d)) d = new Delivery { Id = id, Name = System.IO.Path.GetFileName(pathOrName) };
            d.Finished = true; d.Ok = ok; d.Path = ok ? pathOrName : null;
            if (ok && d.Total < 0) { try { d.Total = new FileInfo(pathOrName).Length; } catch { } }
            deliveries.Remove(id);
            if (ok && !expanded) newFromPhone++;

            if (!IsVisible)
            {
                if (ok) App.Notify("From your iPhone ✓", d.Name + " is in your notch pocket", () => { if (!IsVisible) SetUserHidden(false); OpenFromTray(); SelectTab("shelf"); });
                else App.Notify("iPhone transfer failed", d.Name + " didn't arrive. Try sending it again.", null);
                return;
            }
            if (expanded)
            {
                if (!buddyOut) { big.Present(); if (ok) big.Celebrate(); else big.Flash(Mood.Worried, 1500); }
                else React(m => { if (ok) m.Celebrate(); else m.Flash(Mood.Worried, 1500); });
                moodText.Text = ok ? "delivered: " + d.Name + " ✓" : "that one didn't make it, try again";
                if (ok) { if (tab != "shelf") SelectTab("shelf"); shelfScroll.ScrollToLeftEnd(); }
                return;
            }
            if (buddyOut) React(m => { if (ok) { m.FlashHand(Prop.Parcel, 900); m.Celebrate(); } else m.Flash(Mood.Worried, 1500); });
            ShowDelivered(d);
        }

        void ShowReceiving(Delivery d)
        {
            shownDelivery = d;
            SetupCard(CardKind.InFile, "Receiving from your iPhone…", d.Name, GlyphPocket);
            UpdateProgressUI(d);
            if (!buddyOut) courier.SetCarrying(true); else React(m => m.Startle());
        }

        void UpdateProgressUI(Delivery d)
        {
            double frac = d.Total > 0 ? Math.Min(1, (double)d.Done / d.Total) : 0.35;
            SetBar(frac, false);
            string more = ActiveCount > 1 ? "  ·  " + (ActiveCount - 1) + " more coming" : "";
            E<TextBlock>("DelMeta").Text = (d.Total > 0 ? (int)(frac * 100) + "%  ·  " + Size(d.Done) + " of " + Size(d.Total) : Size(d.Done) + " so far") + more;
        }

        async void ShowDelivered(Delivery d)
        {
            if (toast != ToastKind.Delivery || cardKind != CardKind.InFile) { ShowReceiving(d); await Task.Delay(250); }
            shownDelivery = d;
            E<TextBlock>("DelName").Text = d.Name;
            if (!d.Ok)
            {
                E<TextBlock>("DelTitle").Text = "Transfer didn't finish";
                E<TextBlock>("DelMeta").Text = "Try sending it again from your iPhone";
                E<Border>("DelFill").Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
                courier.SetCarrying(false); courier.FlashHand(Prop.None, 0); courier.Flash(Mood.Worried, 2500);
                HoldDeliveryCard(3500);
                return;
            }
            SetBar(1, true);
            E<TextBlock>("DelTitle").Text = "Mochi's unpacking…";
            E<TextBlock>("DelMeta").Text = Size(d.Total);
            if (!buddyOut) courier.Present();
            await FlyToSlot(d.Path, GlyphDoc);
            if (toast != ToastKind.Delivery) return;
            E<TextBlock>("DelTitle").Text = "Delivered to your pocket ✓";
            E<TextBlock>("DelMeta").Text = Size(d.Total) + "  ·  saved in Downloads › From iPhone" + (ActiveCount > 0 ? "  ·  " + ActiveCount + " more coming" : "");
            var next = deliveries.Values.FirstOrDefault(x => !x.Finished);
            if (next != null) { await Task.Delay(1400); if (toast == ToastKind.Delivery) ShowReceiving(next); return; }
            HoldDeliveryCard(4200);
        }

        // =============== iPhone -> laptop: links & text ===============
        void OnTextReceived(string text)
        {
            bool link = PhoneLink.IsLink(text);
            if (SnapshotDir == null) { try { Clipboard.SetText(text); } catch { } } // tests never touch your clipboard
            if (!IsVisible)
            {
                App.Notify(link ? "Link from your iPhone" : "Text from your iPhone", Preview(text) + "  (copied to clipboard)",
                    () => { if (link) OpenLink(text); else { if (!IsVisible) SetUserHidden(false); OpenFromTray(); SelectTab("clip"); } });
                return;
            }
            if (expanded)
            {
                SelectTab("clip");
                moodText.Text = link ? "got a link from your iPhone, it's on your clipboard" : "copied your iPhone's text ✓";
                React(m => { m.FlashHand(Prop.Paper, 900); m.Nod(); });
                return;
            }
            ShowTextCard(text, link);
        }

        async void ShowTextCard(string text, bool link)
        {
            SetupCard(CardKind.InText, link ? "Link from your iPhone" : "Text from your iPhone", Preview(text), GlyphClipboard);
            cardLink = link ? text.Trim() : null;
            SetBar(1, true);
            if (!buddyOut) { courier.FlashHead(Prop.Cap, 0); courier.FlashHand(Prop.Paper, 0); }
            else React(m => { m.FlashHand(Prop.Paper, 900); m.Nod(); });
            await Task.Delay(380);
            if (toast != ToastKind.Delivery || cardKind != CardKind.InText) return;
            if (!buddyOut) courier.Present();
            await FlyToSlot(null, link ? GlyphLink : GlyphDoc);
            if (toast != ToastKind.Delivery || cardKind != CardKind.InText) return;
            E<TextBlock>("DelMeta").Text = "Copied to your clipboard ✓" + (link ? "  ·  click to open" : "");
            HoldDeliveryCard(link ? 6000 : 4500);
        }

        // =============== laptop -> iPhone ===============
        /// <summary>Hand files to Mochi to carry to the phone.</summary>
        public void SendFilesToPhone(IEnumerable<string> paths)
        {
            if (!EnsurePhone()) return;
            PhoneLink.OutItem last = null;
            foreach (var p in paths.Where(File.Exists)) { last = phone.SendFile(p); pendingOut[last.Id] = last; }
            if (last != null) AnnounceOutgoing(last);
        }

        public void SendTextToPhone(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || !EnsurePhone()) return;
            var it = phone.SendText(text);
            pendingOut[it.Id] = it;
            AnnounceOutgoing(it);
        }

        bool EnsurePhone()
        {
            if (phone.Running) return true;
            if (!StartPhone()) { Expand("shelf"); ShowPhonePanel(); return false; }
            return true;
        }

        void AnnounceOutgoing(PhoneLink.OutItem it)
        {
            bool online = phone.PhoneOnline;
            if (expanded)
            {
                moodText.Text = online ? "on my way to your iPhone!" : "open Notch Drop on your iPhone and I'll deliver it";
                if (!buddyOut) { big.FlashHead(Prop.Cap, 2500); big.FlashHand(Prop.Parcel, 2500); big.Jump(); }
                else React(m => { m.FlashHand(Prop.Parcel, 1500); m.Jump(); });
                if (!online) { Expand("shelf"); ShowPhonePanel(); }
                return;
            }
            ShowOutCard(it);
        }

        void ShowOutCard(PhoneLink.OutItem it)
        {
            bool online = phone.PhoneOnline;
            cardOut = it;
            SetupCard(CardKind.Out, online ? "Delivering to your iPhone…" : "Waiting for your iPhone…", it.Kind == "file" ? it.Name : Preview(it.Text), GlyphPhone);
            SetBar(0.35, false);
            int waiting = pendingOut.Count;
            E<TextBlock>("DelMeta").Text = (online ? "Mochi's on the way" : "Open Notch Drop on your iPhone to receive it") + (waiting > 1 ? "  ·  " + waiting + " waiting" : "");
            if (!buddyOut) courier.SetCarrying(true); else React(m => { m.FlashHand(Prop.Parcel, 1500); m.Jump(); });
            HoldDeliveryCard(online ? 15000 : 7000); // if the phone isn't there, tuck away; it's delivered the moment the page checks in
        }

        void OnOutboxSeen(int id)
        {
            PhoneLink.OutItem it;
            if (!pendingOut.TryGetValue(id, out it)) return;
            pendingOut.Remove(id);
            string what = it.Kind == "file" ? it.Name : (it.Kind == "link" ? "your link" : "your text");
            if (!IsVisible) { App.Notify("Delivered to your iPhone ✓", what, null); return; }
            if (expanded)
            {
                moodText.Text = "delivered " + what + " to your iPhone ✓";
                if (!buddyOut) { big.Present(); big.Celebrate(); } else React(m => m.Celebrate());
                return;
            }
            if (buddyOut) React(m => m.Celebrate());
            ShowOutDelivered(it);
        }

        async void ShowOutDelivered(PhoneLink.OutItem it)
        {
            if (toast != ToastKind.Delivery || cardKind != CardKind.Out || cardOut != it) { ShowOutCard(it); await Task.Delay(300); }
            cardOut = it;
            E<TextBlock>("DelTitle").Text = "Mochi's dropping it off…";
            SetBar(1, true);
            if (!buddyOut) courier.Present();
            await FlyToSlot(it.Kind == "file" ? it.Path : null, it.Kind == "file" ? GlyphDoc : it.Kind == "link" ? GlyphLink : GlyphDoc);
            if (toast != ToastKind.Delivery || cardOut != it) return;
            E<TextBlock>("DelTitle").Text = "Delivered to your iPhone ✓";
            E<TextBlock>("DelMeta").Text = it.Kind == "file" ? "Tap Save on your iPhone to keep it" : it.Kind == "link" ? "Tap Open or Copy on your iPhone" : "Tap Copy on your iPhone";
            var next = pendingOut.Values.FirstOrDefault();
            if (next != null && phone.PhoneOnline) { await Task.Delay(1400); if (toast == ToastKind.Delivery) ShowOutDelivered(next); return; }
            HoldDeliveryCard(4500);
        }

        void OnOutProgress(int id, long sent, long total)
        {
            if (toast != ToastKind.Delivery || cardKind != CardKind.Out || cardOut == null || cardOut.Id != id) return;
            double frac = total > 0 ? (double)sent / total : 0;
            E<TextBlock>("DelMeta").Text = "Saving on your iPhone…  " + (int)(frac * 100) + "%";
            HoldDeliveryCard(5000);
        }

        void OnOutSaved(int id)
        {
            var it = phone.FindOut(id);
            string name = it != null ? it.Name : "File";
            if (toast == ToastKind.Delivery && cardKind == CardKind.Out && cardOut != null && cardOut.Id == id)
            {
                E<TextBlock>("DelTitle").Text = "Saved on your iPhone ✓";
                E<TextBlock>("DelMeta").Text = name + "  ·  in Files › Downloads";
                if (!buddyOut) courier.Pet();
                HoldDeliveryCard(3500);
                return;
            }
            if (expanded) { moodText.Text = "your iPhone saved " + name + " ✓"; return; }
            if (IsVisible) ShowToast(ToastKind.Info, 2600, "Saved on your iPhone ✓", name, GlyphPhone);
        }

        // =============== animation helpers ===============
        static DoubleAnimation Arc(double from, double to, double ms, bool easeIn)
        {
            var a = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = easeIn ? (IEasingFunction)new QuadraticEase { EasingMode = EasingMode.EaseIn } : Anim.InOutSine };
            Timeline.SetDesiredFrameRate(a, 60);
            return a;
        }
        static DoubleAnimationUsingKeyFrames ArcY(double from, double peak, double to, double ms)
        {
            var k = new DoubleAnimationUsingKeyFrames();
            k.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            k.KeyFrames.Add(new EasingDoubleKeyFrame(peak, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms * 0.45)), Anim.OutCubic));
            k.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), new QuadraticEase { EasingMode = EasingMode.EaseIn }));
            Timeline.SetDesiredFrameRate(k, 60);
            return k;
        }
        static DoubleAnimationUsingKeyFrames Hop(double from, double mid, double to, double ms)
        {
            var k = new DoubleAnimationUsingKeyFrames();
            k.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            k.KeyFrames.Add(new EasingDoubleKeyFrame(mid, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms * 0.45)), Anim.OutCubic));
            k.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), Anim.InOutSine));
            Timeline.SetDesiredFrameRate(k, 60);
            return k;
        }
    }
}
