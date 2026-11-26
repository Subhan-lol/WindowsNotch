using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace NotchIsland
{
    public partial class NotchWindow
    {
        DispatcherTimer dragLeaveTimer;
        Point dragStart;
        ShelfItem pressedItem;
        bool pressedDragAll;
        DateTime snipUntil = DateTime.MinValue;
        ItemsControl shelfList;
        ScrollViewer shelfScroll;

        void WireShelf()
        {
            shelfList = E<ItemsControl>("ShelfList");
            shelfScroll = E<ScrollViewer>("ShelfScroll");
            shelfList.ItemsSource = shelf.Items;
            shelf.Items.CollectionChanged += (s, e) => UpdateShelfUI();
            shelf.Changed += UpdateMood;

            dragLeaveTimer = Timer(160, (s, e) =>
            {
                dragLeaveTimer.Stop();
                EndDragOver();
                if (!CursorOverNotch()) ScheduleCollapse(300);
            });

            // ---- drop INTO the notch ----
            hoverZone.DragEnter += OnDragEnterOver;
            hoverZone.DragOver += OnDragEnterOver;
            hoverZone.DragLeave += (s, e) => { if (dragOver) { dragLeaveTimer.Stop(); dragLeaveTimer.Start(); } };
            hoverZone.Drop += OnDrop;

            // ---- tiles: buttons, double-click, drag OUT ----
            shelfList.AddHandler(Button.ClickEvent, new RoutedEventHandler(OnShelfButton));
            shelfList.PreviewMouseLeftButtonDown += (s, e) =>
            {
                pressedItem = null;
                if (IsInButton(e.OriginalSource as DependencyObject, shelfList)) return;
                var item = Ctx(e.OriginalSource) as ShelfItem;
                if (item == null) return;
                if (e.ClickCount == 2) { Open(item.Path); return; }
                pressedItem = item;
                dragStart = e.GetPosition(this);
            };
            shelfList.PreviewMouseMove += (s, e) =>
            {
                if (pressedItem == null || e.LeftButton != MouseButtonState.Pressed) return;
                if ((e.GetPosition(this) - dragStart).Length < 6) return;
                var item = pressedItem; pressedItem = null;
                BeginDragOut(new[] { item.Path });
            };
            shelfList.PreviewMouseLeftButtonUp += (s, e) => pressedItem = null;
            shelfScroll.PreviewMouseWheel += (s, e) =>
            {
                shelfScroll.ScrollToHorizontalOffset(shelfScroll.HorizontalOffset - e.Delta * 0.6);
                e.Handled = true;
            };

            var dragAll = E<Button>("ShelfDragAll");
            dragAll.PreviewMouseLeftButtonDown += (s, e) => { pressedDragAll = true; dragStart = e.GetPosition(this); };
            dragAll.PreviewMouseMove += (s, e) =>
            {
                if (!pressedDragAll || e.LeftButton != MouseButtonState.Pressed) return;
                if ((e.GetPosition(this) - dragStart).Length < 6) return;
                pressedDragAll = false;
                var paths = shelf.Items.Select(i => i.Path).ToArray();
                if (paths.Length > 0) BeginDragOut(paths);
            };
            dragAll.Click += (s, e) => { pressedDragAll = false; moodText.Text = "drag that button, not click it :)"; React(m => m.Wiggle()); };
            E<Button>("ShelfPaste").Click += (s, e) => PasteIntoShelf();
            E<Button>("ShelfSnip").Click += (s, e) => StartSnip();
            E<Button>("ShelfClear").Click += (s, e) => { if (shelf.Items.Count > 0) { shelf.Clear(); React(m => { m.Flash(Mood.Worried, 900); m.Wiggle(); }); } };
        }

        static object Ctx(object src)
        {
            var fe = src as FrameworkElement;
            if (fe != null) return fe.DataContext;
            var fce = src as FrameworkContentElement;
            return fce != null ? fce.DataContext : null;
        }

        static bool IsInButton(DependencyObject d, DependencyObject stop)
        {
            while (d != null && d != stop)
            {
                if (d is Button) return true;
                d = (d is Visual || d is System.Windows.Media.Media3D.Visual3D) ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        void UpdateShelfUI()
        {
            int n = shelf.Items.Count;
            E<Grid>("ShelfEmpty").Visibility = n == 0 ? Visibility.Visible : Visibility.Collapsed;
            shelfScroll.Visibility = n == 0 ? Visibility.Collapsed : Visibility.Visible;
            E<TextBlock>("ShelfCount").Text = n == 0 ? "" : n + (n == 1 ? " item" : " items") + " in the pocket · double-click to open";
            E<Button>("ShelfDragAll").Visibility = n > 1 ? Visibility.Visible : Visibility.Collapsed;
            E<Button>("ShelfClear").Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void OnShelfButton(object sender, RoutedEventArgs e)
        {
            var btn = e.OriginalSource as Button;
            if (btn == null) return;
            var item = btn.DataContext as ShelfItem;
            if (item == null) return;
            if ((string)btn.Tag == "remove") { shelf.Remove(item); React(m => m.Wave()); }
            else if ((string)btn.Tag == "reveal")
            {
                try { Process.Start("explorer.exe", "/select,\"" + item.Path + "\""); } catch { }
                pinnedOpen = false; ScheduleCollapse(150);
            }
            e.Handled = true;
        }

        static void Open(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
        }

        // ---------------- drag in ----------------
        static bool Acceptable(IDataObject d)
        {
            try
            {
                return d.GetDataPresent(DataFormats.FileDrop) || d.GetDataPresent(DataFormats.Bitmap) ||
                       d.GetDataPresent(DataFormats.UnicodeText) || d.GetDataPresent(DataFormats.Text);
            }
            catch { return false; }
        }

        void OnDragEnterOver(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (draggingOut || !Acceptable(e.Data)) { e.Effects = DragDropEffects.None; return; }
            e.Effects = (e.AllowedEffects & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.Link;
            dragLeaveTimer.Stop();
            if (dragOver) return;
            dragOver = true;
            Touch();
            if (!IsVisible) Show();
            Expand("shelf");
            string hint = "Let go to drop it in the pocket";
            try
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 1) hint = "Let go to drop " + files.Length + " files in the pocket";
                else if (files == null) hint = "Let go and I'll keep it as a file";
            }
            catch { }
            E<TextBlock>("DropHint").Text = hint;
            E<Grid>("DropOverlay").Visibility = Visibility.Visible;
            UpdateMood();
            React(m => m.Jump());
        }

        void EndDragOver()
        {
            if (!dragOver) return;
            dragOver = false;
            E<Grid>("DropOverlay").Visibility = Visibility.Collapsed;
            UpdateMood();
        }

        void OnDrop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            dragLeaveTimer.Stop();
            if (draggingOut) { EndDragOver(); return; }
            int n = 0;
            try { n = AddFromData(e.Data); } catch (Exception ex) { Debug.WriteLine(ex); }
            EndDragOver();
            if (n > 0)
            {
                React(m => m.Chomp());
                moodText.Text = n == 1 ? "nom! got it" : "nom nom! got " + n;
                shelfScroll.ScrollToLeftEnd();
            }
            else moodText.Text = "already had that one";
            e.Effects = DragDropEffects.Copy;
            ScheduleCollapse(1600);
        }

        int AddFromData(IDataObject d)
        {
            if (d.GetDataPresent(DataFormats.FileDrop))
            {
                var files = d.GetData(DataFormats.FileDrop) as string[];
                if (files != null) return shelf.Add(files);
            }
            if (d.GetDataPresent(DataFormats.Bitmap))
            {
                var img = d.GetData(DataFormats.Bitmap) as BitmapSource;
                if (img != null) return shelf.Add(new[] { shelf.SaveImage(img, "Image") });
            }
            string text = null;
            if (d.GetDataPresent(DataFormats.UnicodeText)) text = d.GetData(DataFormats.UnicodeText) as string;
            else if (d.GetDataPresent(DataFormats.Text)) text = d.GetData(DataFormats.Text) as string;
            if (!string.IsNullOrWhiteSpace(text)) return shelf.Add(new[] { shelf.SaveText(text) });
            return 0;
        }

        void PasteIntoShelf()
        {
            int n = 0;
            try
            {
                if (Clipboard.ContainsFileDropList())
                {
                    var list = Clipboard.GetFileDropList();
                    var arr = new string[list.Count]; list.CopyTo(arr, 0);
                    n = shelf.Add(arr);
                }
                else if (Clipboard.ContainsImage())
                {
                    var img = Clipboard.GetImage();
                    if (img != null) n = shelf.Add(new[] { shelf.SaveImage(img, "Pasted") });
                }
                else if (Clipboard.ContainsText())
                    n = shelf.Add(new[] { shelf.SaveText(Clipboard.GetText()) });
            }
            catch { }
            if (n > 0) { React(m => m.Chomp()); moodText.Text = "nom! pasted"; }
            else { React(m => m.Flash(Mood.Worried, 900)); moodText.Text = "clipboard's empty"; }
        }

        void StartSnip()
        {
            snipUntil = DateTime.Now.AddSeconds(90);
            React(m => m.Snap());
            pinnedOpen = false;
            Collapse();
            try { Process.Start(new ProcessStartInfo("ms-screenclip:") { UseShellExecute = true }); }
            catch { snipUntil = DateTime.MinValue; }
        }

        /// <summary>Called from the clipboard listener; returns true if it consumed a pending snip.</summary>
        bool TryCatchSnip()
        {
            if (DateTime.Now > snipUntil) return false;
            try
            {
                if (!Clipboard.ContainsImage()) return false;
                var img = Clipboard.GetImage();
                if (img == null) return false;
                snipUntil = DateTime.MinValue;
                shelf.Add(new[] { shelf.SaveImage(img, "Snip") });
                ShowToast(ToastKind.Info, 2600, "Snip saved to the pocket", "open the notch to drag it anywhere", "");
                React(m => m.Snap());
                return true;
            }
            catch { return false; }
        }

        // ---------------- drag out ----------------
        void BeginDragOut(string[] paths)
        {
            paths = paths.Where(p => System.IO.File.Exists(p) || System.IO.Directory.Exists(p)).ToArray();
            if (paths.Length == 0) { shelf.Prune(); return; }
            draggingOut = true;
            React(m => m.Give());
            moodText.Text = paths.Length == 1 ? "here you go!" : "take them all!";
            var data = new DataObject(DataFormats.FileDrop, paths);
            DragDropEffects result = DragDropEffects.None;
            try { result = DragDrop.DoDragDrop(shelfList, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link); }
            catch { }
            draggingOut = false;
            if (result != DragDropEffects.None) React(m => { m.Hearts(2); m.Nod(); });
            shelf.Prune();
            hovering = CursorOverNotch();
            if (!hovering) ScheduleCollapse(250);
        }
    }
}
