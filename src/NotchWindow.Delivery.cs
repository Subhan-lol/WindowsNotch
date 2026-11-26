using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace NotchIsland
{
    /// <summary>
    /// Files from the iPhone arrive as a little delivery you can watch: Mochi in a courier cap runs in place
    /// hugging a parcel while the progress bar fills, then unboxes it, the file flies into the pocket slot,
    /// a green check pops and the card says where it was saved. A "new" badge stays until you open the notch.
    /// </summary>
    public partial class NotchWindow
    {
        class Delivery { public int Id; public string Name; public long Total = -1, Done; public string Path; public bool Finished, Ok; }

        readonly Dictionary<int, Delivery> deliveries = new Dictionary<int, Delivery>();
        Delivery shownDelivery;
        int newFromPhone;
        Grid deliveryCard;
        Mascot courier;

        void WireDelivery()
        {
            deliveryCard = E<Grid>("DeliveryCard");
            courier = E<Mascot>("DeliveryMascot");
            courier.Particles = true;
            deliveryCard.MouseLeftButtonUp += (s, e) => { e.Handled = true; Expand("shelf"); };
            phone.UploadStarted += OnUploadStarted;
            phone.UploadProgress += OnUploadProgress;
            phone.UploadFinished += OnUploadFinished;
        }

        static string Size(long b)
        {
            if (b < 0) return "";
            if (b < 1024) return b + " B";
            if (b < 1048576) return (b / 1024.0).ToString("0") + " KB";
            if (b < 1073741824) return (b / 1048576.0).ToString("0.0") + " MB";
            return (b / 1073741824.0).ToString("0.00") + " GB";
        }

        int ActiveCount { get { return deliveries.Values.Count(d => !d.Finished); } }

        // ---------------- events from the phone server ----------------
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
            if (toast == ToastKind.Delivery) UpdateProgressUI(d);
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
                // notch hidden (fullscreen app): tell Windows instead so it never goes unnoticed
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

        // ---------------- the card ----------------
        void ShowReceiving(Delivery d)
        {
            shownDelivery = d;
            E<TextBlock>("DelTitle").Text = "Receiving from your iPhone…";
            E<TextBlock>("DelName").Text = d.Name;
            E<Border>("DelFill").Background = (Brush)root.Resources["Accent"];
            E<Border>("DelFill").Width = 0;
            E<Border>("DelThumb").Visibility = Visibility.Collapsed;
            E<Grid>("DelCheck").Visibility = Visibility.Collapsed;
            E<TextBlock>("DelSlotIcon").Visibility = Visibility.Visible;
            E<TextBlock>("DelSlotIcon").Text = "";
            E<UIElement>("DelParcelIcon").Visibility = buddyOut ? Visibility.Visible : Visibility.Collapsed;
            courier.Visibility = buddyOut ? Visibility.Collapsed : Visibility.Visible;
            UpdateProgressUI(d);
            ShowToast(ToastKind.Delivery, 600000, null, null, null);
            if (!buddyOut) { courier.Active = true; courier.SetCarrying(true); }
            else React(m => m.Startle());
        }

        void UpdateProgressUI(Delivery d)
        {
            double frac = d.Total > 0 ? Math.Min(1, (double)d.Done / d.Total) : 0;
            var fill = E<Border>("DelFill");
            double max = E<Grid>("DelTrack").ActualWidth;
            if (max <= 0) max = 210;
            if (d.Total > 0) Anim.To(fill, FrameworkElement.WidthProperty, frac * max, 120);
            else fill.Width = max * 0.35; // unknown size: just show activity
            string more = ActiveCount > 1 ? "  ·  " + (ActiveCount - 1) + " more coming" : "";
            E<TextBlock>("DelMeta").Text = (d.Total > 0 ? (int)(frac * 100) + "%  ·  " + Size(d.Done) + " of " + Size(d.Total) : Size(d.Done) + " so far") + more;
        }

        async void ShowDelivered(Delivery d)
        {
            if (toast != ToastKind.Delivery) { ShowReceiving(d); await Task.Delay(250); }
            shownDelivery = d;
            E<TextBlock>("DelName").Text = d.Name;
            var fill = E<Border>("DelFill");
            if (!d.Ok)
            {
                E<TextBlock>("DelTitle").Text = "Transfer didn't finish";
                E<TextBlock>("DelMeta").Text = "Try sending it again from your iPhone";
                fill.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
                courier.SetCarrying(false); courier.FlashHand(Prop.None, 0); courier.Flash(Mood.Worried, 2500);
                HoldDeliveryCard(3500);
                return;
            }
            double max = E<Grid>("DelTrack").ActualWidth; if (max <= 0) max = 210;
            Anim.To(fill, FrameworkElement.WidthProperty, max, 150);
            fill.Background = new SolidColorBrush(Color.FromRgb(0x7D, 0xF0, 0x9A));
            E<TextBlock>("DelTitle").Text = "Mochi's unpacking…";
            E<TextBlock>("DelMeta").Text = Size(d.Total);
            if (!buddyOut) courier.Present();

            // the file pops out of the parcel and flies into the pocket slot
            var thumb = E<Border>("DelThumb");
            var img = E<ImageBrush>("DelThumbBrush");
            img.ImageSource = null;
            E<TextBlock>("DelThumbIcon").Visibility = Visibility.Visible;
            shelf.ThumbAsync(d.Path, bmp => { if (bmp != null) { img.ImageSource = bmp; E<TextBlock>("DelThumbIcon").Visibility = Visibility.Collapsed; } });
            var tt = E<TranslateTransform>("DelThumbMove");
            var st = E<ScaleTransform>("DelThumbScale");
            var rt = E<RotateTransform>("DelThumbSpin");
            thumb.Visibility = Visibility.Visible;
            Point from = new Point(36, 40), to = new Point(deliveryCard.Width - 52, 31);
            tt.BeginAnimation(TranslateTransform.XProperty, Arc(from.X, to.X, 650, false));
            tt.BeginAnimation(TranslateTransform.YProperty, ArcY(from.Y, 6, to.Y, 650)); // stays inside the notch
            st.BeginAnimation(ScaleTransform.ScaleXProperty, Hop(0.3, 1.25, 1, 650));
            st.BeginAnimation(ScaleTransform.ScaleYProperty, Hop(0.3, 1.25, 1, 650));
            rt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-25, 0, TimeSpan.FromMilliseconds(650)) { EasingFunction = Anim.OutCubic });
            await Task.Delay(650);
            if (toast != ToastKind.Delivery) return;

            // it lands: slot bounces, green check pops, Mochi celebrates
            E<TextBlock>("DelSlotIcon").Visibility = Visibility.Collapsed;
            var slot = E<ScaleTransform>("DelSlotScale");
            slot.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.18, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = Anim.OutElastic });
            slot.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.18, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = Anim.OutElastic });
            var check = E<Grid>("DelCheck");
            var cs = E<ScaleTransform>("DelCheckScale");
            check.Visibility = Visibility.Visible;
            cs.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = Anim.OutBack });
            cs.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = Anim.OutBack });
            if (!buddyOut) courier.Celebrate();
            E<TextBlock>("DelTitle").Text = "Delivered to your pocket ✓";
            E<TextBlock>("DelMeta").Text = Size(d.Total) + "  ·  saved in Downloads › From iPhone" + (ActiveCount > 0 ? "  ·  " + ActiveCount + " more coming" : "");
            if (Settings.Get("Sounds")) { try { System.Media.SystemSounds.Asterisk.Play(); } catch { } }

            // next file in a batch? show it, otherwise let the card linger then tuck away
            var next = deliveries.Values.FirstOrDefault(x => !x.Finished);
            if (next != null) { await Task.Delay(1400); if (toast == ToastKind.Delivery) ShowReceiving(next); return; }
            HoldDeliveryCard(4200);
        }

        void HoldDeliveryCard(int ms)
        {
            toastTimer.Stop();
            toastTimer.Interval = TimeSpan.FromMilliseconds(ms);
            toastTimer.Start();
        }

        /// <summary>Called whenever the card goes away.</summary>
        void EndDeliveryCard()
        {
            courier.SetCarrying(false);
            courier.FlashHand(Prop.None, 0); courier.FlashHead(Prop.None, 0);
            courier.Active = false;
        }

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
