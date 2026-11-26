using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NotchIsland
{
    /// <summary>The pocket's "iPhone" button: QR code panel + catching files that arrive over Wi-Fi.</summary>
    public partial class NotchWindow
    {
        PhoneLink phone;

        void WirePhone()
        {
            phone = new PhoneLink(Dispatcher, shelf);
            phone.Received += p => E<TextBlock>("PhoneStatus").Text = "Got " + System.IO.Path.GetFileName(p) + " ✓";
            phone.PhoneSeen += () =>
            {
                E<TextBlock>("PhoneStatus").Text = "iPhone connected ✓  Pick files on your phone.";
                React(m => m.Hello());
            };
            E<Button>("ShelfPhone").Click += (s, e) => ShowPhonePanel();
            E<Button>("PhoneDone").Click += (s, e) => E<Grid>("PhonePanel").Visibility = Visibility.Collapsed;
            E<Button>("PhoneOff").Click += (s, e) => Settings.Set("PhoneDrop", false);
            E<Button>("PhoneNewKey").Click += (s, e) =>
            {
                phone.ResetToken();
                UpdatePhonePanel();
                E<TextBlock>("PhoneStatus").Text = "New key made. Old QR codes and home-screen icons stop working.";
            };
            WireDelivery();
            if (Settings.Get("PhoneDrop") && SnapshotDir == null) StartPhone();
        }

        bool StartPhone()
        {
            if (phone.Running) return true;
            string err;
            bool loopback = Environment.GetEnvironmentVariable("NOTCH_PHONE_LOOPBACK") == "1";
            if (!phone.Start(out err, loopback))
            {
                E<TextBlock>("PhoneStatus").Text = "Couldn't start: " + err;
                return false;
            }
            if (!Settings.Get("PhoneDrop")) Settings.Set("PhoneDrop", true);
            return true;
        }

        void ShowPhonePanel()
        {
            if (!StartPhone()) { E<Grid>("PhonePanel").Visibility = Visibility.Visible; return; }
            UpdatePhonePanel();
            E<Grid>("PhonePanel").Visibility = Visibility.Visible;
            React(m => m.Wave());
        }

        void UpdatePhonePanel()
        {
            string url = phone.Running ? phone.Url : "";
            E<TextBlock>("PhoneUrl").Text = url;
            E<Image>("PhoneQr").Source = url.Length > 0 ? QrCode.Render(QrCode.Encode(url), 6, Colors.Black, Colors.White) : null;
            E<TextBlock>("PhoneStatus").Text = phone.Running
                ? "Scan with your iPhone camera (same Wi‑Fi as this laptop)."
                : "iPhone drop is off.";
        }

        void StopPhone()
        {
            if (phone != null) phone.Stop();
            E<Grid>("PhonePanel").Visibility = Visibility.Collapsed;
        }

        void OnPhoneFile(string path)
        {
            string name = Path.GetFileName(path);
            React(m => m.Chomp());
            E<TextBlock>("PhoneStatus").Text = "Got " + name + " ✓";
            if (!expanded) ShowToast(ToastKind.Info, 2800, "From your iPhone", name, "");
            else
            {
                moodText.Text = "caught it from your iPhone!";
                if (tab != "shelf") SelectTab("shelf");
            }
        }
    }
}
