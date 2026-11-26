using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace NotchIsland
{
    public class ShelfItem : INotifyPropertyChanged
    {
        public string Path { get; private set; }
        public string Name { get; private set; }
        public string Detail { get; private set; }
        public bool Owned { get; private set; } // created by us (snips/snippets) -> deleted when removed
        ImageSource thumb;
        public ImageSource Thumb { get { return thumb; } set { thumb = value; Notify("Thumb"); } }
        public event PropertyChangedEventHandler PropertyChanged;
        void Notify(string p) { var h = PropertyChanged; if (h != null) h(this, new PropertyChangedEventArgs(p)); }

        public ShelfItem(string path)
        {
            Path = path;
            Name = System.IO.Path.GetFileName(path.TrimEnd('\\'));
            if (string.IsNullOrEmpty(Name)) Name = path;
            Owned = path.StartsWith(Paths.ShelfDir, StringComparison.OrdinalIgnoreCase);
            try
            {
                if (Directory.Exists(path)) Detail = "Folder";
                else
                {
                    var fi = new FileInfo(path);
                    var ext = fi.Extension.TrimStart('.').ToUpperInvariant();
                    Detail = (ext.Length > 0 ? ext + " · " : "") + HumanSize(fi.Length);
                }
            }
            catch { Detail = ""; }
        }

        public bool Exists { get { return File.Exists(Path) || Directory.Exists(Path); } }

        static string HumanSize(long b)
        {
            if (b < 1024) return b + " B";
            if (b < 1024 * 1024) return (b / 1024.0).ToString("0.#") + " KB";
            if (b < 1024L * 1024 * 1024) return (b / 1048576.0).ToString("0.#") + " MB";
            return (b / 1073741824.0).ToString("0.##") + " GB";
        }
    }

    /// <summary>The "pocket": holds references to files (never copies them) and survives restarts.</summary>
    public class ShelfStore
    {
        public readonly ObservableCollection<ShelfItem> Items = new ObservableCollection<ShelfItem>();
        readonly StaWorker worker = new StaWorker();
        readonly Dispatcher ui;
        static readonly string saveFile = Paths.File("shelf.txt");
        public event Action Changed;

        public ShelfStore(Dispatcher d) { ui = d; }

        public void Load()
        {
            try
            {
                if (!File.Exists(saveFile)) return;
                foreach (var line in File.ReadAllLines(saveFile))
                {
                    var p = line.Trim();
                    if (p.Length > 0 && (File.Exists(p) || Directory.Exists(p))) AddOne(p);
                }
            }
            catch { }
            CleanOrphans();
        }

        public bool ReadOnly; // snapshot mode: never touch the user's saved pocket
        void Save()
        {
            if (ReadOnly) { var h0 = Changed; if (h0 != null) h0(); return; }
            try { File.WriteAllLines(saveFile, Items.Select(i => i.Path).ToArray()); } catch { }
            var h = Changed; if (h != null) h();
        }

        public int Add(IEnumerable<string> paths)
        {
            int n = 0;
            foreach (var p in paths) if (AddOne(p)) n++;
            if (n > 0) Save();
            return n;
        }

        bool AddOne(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return false;
            if (Items.Any(i => string.Equals(i.Path, p, StringComparison.OrdinalIgnoreCase))) return false;
            var item = new ShelfItem(p);
            Items.Insert(0, item);
            LoadThumb(item);
            return true;
        }

        public void Remove(ShelfItem item)
        {
            if (!Items.Remove(item)) return;
            DeleteIfOwned(item);
            Save();
        }

        public void Clear()
        {
            foreach (var i in Items.ToList()) DeleteIfOwned(i);
            Items.Clear();
            Save();
        }

        /// <summary>Removes items whose files were moved/deleted (e.g. after a drag-out "move").</summary>
        public void Prune()
        {
            bool any = false;
            foreach (var i in Items.ToList()) if (!i.Exists) { Items.Remove(i); any = true; }
            if (any) Save();
        }

        static void DeleteIfOwned(ShelfItem i)
        {
            if (!i.Owned) return;
            try { if (File.Exists(i.Path)) File.Delete(i.Path); } catch { }
        }

        void CleanOrphans()
        {
            try
            {
                var keep = new HashSet<string>(Items.Select(i => i.Path), StringComparer.OrdinalIgnoreCase);
                foreach (var f in Directory.GetFiles(Paths.ShelfDir))
                    if (!keep.Contains(f) && File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-1)) File.Delete(f);
            }
            catch { }
        }

        // ---- creating owned files from non-file drops ----
        public string SaveText(string text)
        {
            string trimmed = text.Trim();
            bool url = Uri.IsWellFormedUriString(trimmed, UriKind.Absolute) && !trimmed.Contains("\n") &&
                       (trimmed.StartsWith("http://") || trimmed.StartsWith("https://"));
            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss");
            string path;
            if (url)
            {
                string host = "Link";
                try { host = new Uri(trimmed).Host.Replace("www.", ""); } catch { }
                path = Unique(System.IO.Path.Combine(Paths.ShelfDir, host + " " + stamp + ".url"));
                File.WriteAllText(path, "[InternetShortcut]\r\nURL=" + trimmed + "\r\n");
            }
            else
            {
                path = Unique(System.IO.Path.Combine(Paths.ShelfDir, "Snippet " + stamp + ".txt"));
                File.WriteAllText(path, text);
            }
            return path;
        }

        public string SaveImage(BitmapSource img, string prefix)
        {
            string path = Unique(System.IO.Path.Combine(Paths.ShelfDir, prefix + " " + DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss") + ".png"));
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(img));
            using (var fs = File.Create(path)) enc.Save(fs);
            return path;
        }

        static string Unique(string path)
        {
            if (!File.Exists(path)) return path;
            string dir = System.IO.Path.GetDirectoryName(path), name = System.IO.Path.GetFileNameWithoutExtension(path), ext = System.IO.Path.GetExtension(path);
            for (int i = 2; ; i++) { var p = System.IO.Path.Combine(dir, name + " (" + i + ")" + ext); if (!File.Exists(p)) return p; }
        }

        /// <summary>Thumbnail for any path, computed off the UI thread, delivered on it.</summary>
        public void ThumbAsync(string path, Action<BitmapSource> done)
        {
            worker.Post(() =>
            {
                var bmp = Thumbs.Get(path, 96);
                ui.BeginInvoke(new Action(() => done(bmp)));
            });
        }

        // ---- thumbnails on a background STA thread ----
        void LoadThumb(ShelfItem item)
        {
            worker.Post(() =>
            {
                var bmp = Thumbs.Get(item.Path, 96);
                if (bmp != null) ui.BeginInvoke(new Action(() => item.Thumb = bmp), DispatcherPriority.Background);
            });
        }
    }

    public static class Thumbs
    {
        public static BitmapSource Get(string path, int px)
        {
            IShellItemImageFactory f = null;
            IntPtr hbmp = IntPtr.Zero;
            try
            {
                Native.SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out f);
                var sz = new SIZE { cx = px, cy = px };
                if (f.GetImage(sz, 0, out hbmp) == 0 && hbmp != IntPtr.Zero)
                {
                    var b = FromHBitmap(hbmp);
                    if (b != null) return b;
                }
            }
            catch { }
            finally
            {
                if (hbmp != IntPtr.Zero) Native.DeleteObject(hbmp);
                if (f != null) Marshal.ReleaseComObject(f);
            }
            return Icon(path);
        }

        /// <summary>Converts a shell HBITMAP keeping its alpha channel (CreateBitmapSourceFromHBitmap drops it).</summary>
        static BitmapSource FromHBitmap(IntPtr hbmp)
        {
            DIBSECTION ds;
            if (Native.GetObject(hbmp, Marshal.SizeOf(typeof(DIBSECTION)), out ds) == 0) return null;
            var bm = ds.dsBm;
            if (bm.bmBitsPixel != 32 || bm.bmBits == IntPtr.Zero || bm.bmWidth <= 0 || bm.bmHeight <= 0)
            {
                var s = Imaging.CreateBitmapSourceFromHBitmap(hbmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                s.Freeze(); return s;
            }
            int w = bm.bmWidth, h = bm.bmHeight, stride = bm.bmWidthBytes;
            var raw = new byte[stride * h];
            Marshal.Copy(bm.bmBits, raw, 0, raw.Length);
            bool bottomUp = ds.dsBmih.biHeight > 0;
            byte[] px = raw;
            if (bottomUp)
            {
                px = new byte[raw.Length];
                for (int y = 0; y < h; y++) Buffer.BlockCopy(raw, (h - 1 - y) * stride, px, y * stride, stride);
            }
            bool hasAlpha = false;
            for (int i = 3; i < px.Length; i += 4) if (px[i] != 0) { hasAlpha = true; break; }
            var bmp = BitmapSource.Create(w, h, 96, 96, hasAlpha ? PixelFormats.Pbgra32 : PixelFormats.Bgr32, null, px, stride);
            bmp.Freeze();
            return bmp;
        }

        static BitmapSource Icon(string path)
        {
            var info = new SHFILEINFO();
            const uint SHGFI_ICON = 0x100, SHGFI_LARGEICON = 0;
            try
            {
                Native.SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(info), SHGFI_ICON | SHGFI_LARGEICON);
                if (info.hIcon == IntPtr.Zero) return null;
                var s = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                s.Freeze();
                return s;
            }
            catch { return null; }
            finally { if (info.hIcon != IntPtr.Zero) Native.DestroyIcon(info.hIcon); }
        }
    }
}
