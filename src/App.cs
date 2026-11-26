using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace NotchIsland
{
    public static class App
    {
        static WinForms.NotifyIcon tray;

        [STAThread]
        public static void Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--snapshot") NotchWindow.SnapshotDir = args[1];
            bool created;
            using (var mutex = new Mutex(true, "Local\\NotchIsland.SingleInstance", out created))
            {
                if (!created && NotchWindow.SnapshotDir == null) return;
                Settings.Load();
                if (NotchWindow.SnapshotDir != null) Settings.ReadOnly = true; // tests never touch real settings
                System.Windows.Media.Animation.Timeline.DesiredFrameRateProperty.OverrideMetadata(
                    typeof(System.Windows.Media.Animation.Timeline), new FrameworkPropertyMetadata { DefaultValue = 40 });

                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.DispatcherUnhandledException += (s, e) => { Log(e.Exception); e.Handled = true; };
                AppDomain.CurrentDomain.UnhandledException += (s, e) => Log(e.ExceptionObject as Exception);

                using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("Styles.xaml"))
                    app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(st));

                // Keep the startup entry pointing at wherever the exe lives now.
                if (Settings.Get("StartWithWindows")) SetStartup(true);

                var win = new NotchWindow();
                if (NotchWindow.SnapshotDir == null) CreateTray(win);
                app.Exit += (s, e) => { if (tray != null) { tray.Visible = false; tray.Dispose(); } };
                win.Show();
                app.Run();
            }
        }

        static Action balloonClick;

        /// <summary>A Windows notification from the tray icon, for when the notch itself is hidden.</summary>
        public static void Notify(string title, string text, Action onClick)
        {
            if (tray == null) return;
            balloonClick = onClick;
            tray.BalloonTipTitle = title;
            tray.BalloonTipText = text;
            tray.BalloonTipIcon = WinForms.ToolTipIcon.None;
            tray.ShowBalloonTip(5000);
        }

        static void CreateTray(NotchWindow win)
        {
            tray = new WinForms.NotifyIcon { Text = "Notch Island", Visible = true };
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("notch.ico"))
                    tray.Icon = new System.Drawing.Icon(s, WinForms.SystemInformation.SmallIconSize);
            }
            catch { tray.Icon = System.Drawing.SystemIcons.Application; }

            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("Open notch      Ctrl+Alt+Space", null, (s, e) => win.OpenFromTray());
            var hide = new WinForms.ToolStripMenuItem("Hide notch") { CheckOnClick = true };
            hide.Click += (s, e) => win.SetUserHidden(hide.Checked);
            menu.Items.Add(hide);
            var startup = new WinForms.ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = Settings.Get("StartWithWindows") };
            startup.Click += (s, e) => Settings.Set("StartWithWindows", startup.Checked);
            menu.Opening += (s, e) => startup.Checked = Settings.Get("StartWithWindows");
            menu.Items.Add(startup);
            var buddyItem = new WinForms.ToolStripMenuItem("Let Mochi out to play");
            buddyItem.Click += (s, e) => win.ToggleBuddy();
            menu.Opening += (s, e) => buddyItem.Text = win.BuddyIsOut ? "Call Mochi home" : "Let Mochi out to play";
            menu.Items.Add(buddyItem);
            menu.Items.Add("Open data folder", null, (s, e) => { try { System.Diagnostics.Process.Start("explorer.exe", "\"" + Paths.Roaming + "\""); } catch { } });
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("Quit", null, (s, e) => win.Quit());
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == WinForms.MouseButtons.Left) win.OpenFromTray(); };
            tray.BalloonTipClicked += (s, e) => { var a = balloonClick; balloonClick = null; if (a != null) a(); };
        }

        public static void SetStartup(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (k == null) return;
                    if (on) k.SetValue("NotchIsland", "\"" + Assembly.GetExecutingAssembly().Location + "\"");
                    else k.DeleteValue("NotchIsland", false);
                }
            }
            catch { }
        }

        static void Log(Exception ex)
        {
            if (ex == null) return;
            try { File.AppendAllText(Paths.File("error.log"), DateTime.Now.ToString("s") + "  " + ex + Environment.NewLine + Environment.NewLine); } catch { }
        }
    }
}
