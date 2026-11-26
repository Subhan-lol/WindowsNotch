using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NotchIsland
{
    public partial class NotchWindow
    {
        BuddyWindow buddy;
        bool buddyOut, mascotPressed, launched;
        Point mascotPress;

        void WireBuddy()
        {
            buddy = new BuddyWindow();
            buddy.NotchZone = NotchScreenRect;
            buddy.MusicPlaying = () => media.IsPlaying && media.HasTrack;
            buddy.WentHome += OnBuddyHome;

            // press on Mochi: a click pets, a drag pulls Mochi out of the notch
            big.MouseLeftButtonDown += (s, e) =>
            {
                if (buddyOut) return;
                mascotPressed = true; launched = false;
                mascotPress = e.GetPosition(this);
                big.CaptureMouse();
                e.Handled = true;
            };
            big.MouseMove += (s, e) =>
            {
                if (!mascotPressed || launched) return;
                if ((e.GetPosition(this) - mascotPress).Length > 10) LaunchBuddy();
            };
            big.MouseLeftButtonUp += (s, e) =>
            {
                if (!mascotPressed) return;
                mascotPressed = false;
                big.ReleaseMouseCapture();
                if (!launched)
                {
                    big.Pet(); pets++;
                    moodText.Text = pets % 10 == 0 ? "ok ok, I love you too ♥" : "hehe ♥  (drag me out to play!)";
                }
                e.Handled = true;
            };
            E<Button>("CallHomeBtn").Click += (s, e) => buddy.GoHome();
        }

        /// <summary>Every "something happened" goes through here so whichever Mochi you can see reacts.</summary>
        void React(Action<Mascot> a)
        {
            if (buddyOut) { buddy.React(a); return; }
            a(expanded ? big : mini);
        }

        Rect NotchScreenRect()
        {
            try
            {
                if (IsVisible && body.IsVisible)
                {
                    var tl = body.PointToScreen(new Point(0, 0));
                    var br = body.PointToScreen(new Point(body.ActualWidth, body.ActualHeight));
                    return new Rect(tl, br);
                }
            }
            catch { }
            var mon = Native.PrimaryMonitor();
            double sc = 1;
            try { sc = Native.GetDpiForWindow(hwnd) / 96.0; } catch { }
            double w = 180 * sc;
            return new Rect((mon.Left + mon.Right) / 2.0 - w / 2, mon.Top, w, 32 * sc);
        }

        void LaunchBuddy()
        {
            launched = true;
            buddyOut = true;
            Settings.Set("BuddyOut", true);
            buddy.Mascot.Accent = big.Accent;
            ApplyBuddyVisibility();
            buddy.LaunchCarried();
            buddy.SetContext(ContextMood());
            pinnedOpen = false;
            Collapse();
        }

        /// <summary>Tray menu / button: send Mochi out to play, or call it home.</summary>
        public void ToggleBuddy()
        {
            if (buddyOut) { buddy.GoHome(); return; }
            if (!IsVisible) return;
            buddyOut = true;
            Settings.Set("BuddyOut", true);
            buddy.Mascot.Accent = big.Accent;
            ApplyBuddyVisibility();
            buddy.DropFromNotch();
            buddy.SetContext(ContextMood());
        }

        public bool BuddyIsOut { get { return buddyOut; } }

        void RestoreBuddy()
        {
            if (!Settings.Get("BuddyOut")) return;
            buddyOut = true;
            ApplyBuddyVisibility();
            buddy.SpawnGrounded(Settings.GetNum("BuddyX", 0.5));
            buddy.SetContext(ContextMood());
        }

        void OnBuddyHome()
        {
            buddyOut = false;
            Settings.Set("BuddyOut", false);
            ApplyBuddyVisibility();
            mini.Active = !expanded && IsVisible;
            mini.Jump();
            mini.Flash(Mood.Happy, 1600);
            if (!expanded) Peek(true);
            Dispatcher.BeginInvoke(new Action(() => { if (!hovering && !expanded) Peek(false); }), System.Windows.Threading.DispatcherPriority.Background);
        }

        void ApplyBuddyVisibility()
        {
            big.Visibility = buddyOut ? Visibility.Collapsed : Visibility.Visible;
            moodText.Visibility = buddyOut ? Visibility.Collapsed : Visibility.Visible;
            E<StackPanel>("BuddyAway").Visibility = buddyOut ? Visibility.Visible : Visibility.Collapsed;
            if (buddyOut) { mini.Active = false; big.Active = false; }
            else if (expanded) big.Active = true;
            ApplyCompact();
        }

        void SaveBuddy()
        {
            if (buddyOut && buddy != null) Settings.SetNum("BuddyX", buddy.SavedFraction);
        }

        /// <summary>What the notch is busy with, for the buddy on the taskbar to go along with.</summary>
        Mood ContextMood()
        {
            if (dragOver) return Mood.Hungry;
            if (timerRunning) return Mood.Focus;
            if (media.IsPlaying && media.HasTrack) return Mood.Vibing;
            if (hasBattery && !charging && batPct >= 0 && batPct <= 15) return Mood.Worried;
            return Mood.Idle;
        }

        /// <summary>Mochi dresses for the tab you're on.</summary>
        void PropsForTab(string name)
        {
            Prop head = Prop.None, hand = Prop.None;
            switch (name)
            {
                case "shelf": hand = Prop.Folder; break;
                case "clip": head = Prop.Glasses; break;
                case "timer": head = Prop.Headband; break;
                case "notes": head = Prop.Glasses; hand = Prop.Pencil; break;
                case "settings": hand = Prop.Wrench; break;
            }
            big.SetHeadContext(head);
            big.SetHandContext(hand);
        }
    }
}
