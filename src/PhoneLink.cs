using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace NotchIsland
{
    /// <summary>
    /// "iPhone drop": a tiny web server on your Wi-Fi so your phone can send files into the notch pocket
    /// (and save pocket files back to the phone). Off unless you turn it on. Every URL must contain the
    /// secret key from the QR code; anything else gets a 404. No cloud, nothing leaves your network.
    /// </summary>
    public class PhoneLink
    {
        public const int BasePort = 47800;
        readonly Dispatcher ui;
        readonly ShelfStore shelf;
        TcpListener listener;
        Thread acceptThread;
        volatile bool running;
        byte[] icon;

        public int Port { get; private set; }
        public string Token { get; private set; }
        public bool Running { get { return running; } }
        public static string InboxDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "From iPhone");

        /// <summary>Raised on the UI thread after a file arrived (path).</summary>
        public event Action<string> Received;
        /// <summary>Raised on the UI thread when a phone opens the page.</summary>
        public event Action PhoneSeen;
        /// <summary>UI thread: (id, file name, total bytes or -1 if unknown).</summary>
        public event Action<int, string, long> UploadStarted;
        /// <summary>UI thread, ~10x a second: (id, bytes received so far).</summary>
        public event Action<int, long> UploadProgress;
        /// <summary>UI thread: (id, saved path or file name, success).</summary>
        public event Action<int, string, bool> UploadFinished;
        static int nextUploadId;

        public PhoneLink(Dispatcher d, ShelfStore s)
        {
            ui = d; shelf = s;
            Token = Settings.GetStr("PhoneToken", null);
            if (string.IsNullOrEmpty(Token) || Token.Length < 10) { Token = NewToken(); Settings.SetStr("PhoneToken", Token); }
        }

        static string NewToken()
        {
            const string chars = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var bytes = new byte[12];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            var sb = new StringBuilder();
            foreach (var b in bytes) sb.Append(chars[b % chars.Length]);
            return sb.ToString();
        }

        /// <summary>New key = old QR codes and home-screen shortcuts stop working.</summary>
        public void ResetToken()
        {
            Token = NewToken();
            Settings.SetStr("PhoneToken", Token);
        }

        public string Url { get { return "http://" + (loopbackOnly ? "127.0.0.1" : LocalIp()) + ":" + Port + "/" + Token + "/"; } }

        public static string LocalIp()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    var props = ni.GetIPProperties();
                    if (!props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any))) continue;
                    foreach (var a in props.UnicastAddresses)
                        if (a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
                            return a.Address.ToString();
                }
            }
            catch { }
            return "127.0.0.1";
        }

        bool loopbackOnly;

        public bool Start(out string error, bool loopback = false)
        {
            loopbackOnly = loopback;
            error = null;
            if (running) return true;
            for (int p = BasePort; p < BasePort + 6; p++)
            {
                try
                {
                    listener = new TcpListener(loopback ? IPAddress.Loopback : IPAddress.Any, p);
                    listener.Start(16);
                    Port = p;
                    running = true;
                    break;
                }
                catch (SocketException ex) { error = ex.Message; listener = null; }
            }
            if (!running) return false;
            error = null;
            if (icon == null) try { icon = RenderIcon(); } catch { }
            acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "PhoneLink" };
            acceptThread.Start();
            return true;
        }

        public void Stop()
        {
            running = false;
            try { if (listener != null) listener.Stop(); } catch { }
            listener = null;
        }

        void AcceptLoop()
        {
            while (running)
            {
                TcpClient c;
                try { c = listener.AcceptTcpClient(); }
                catch { if (!running) return; continue; }
                ThreadPool.QueueUserWorkItem(_ => { try { Handle(c); } catch { } finally { try { c.Close(); } catch { } } });
            }
        }

        // ---------------- tiny HTTP ----------------
        void Handle(TcpClient client)
        {
            client.ReceiveTimeout = 30000; client.SendTimeout = 30000;
            var s = client.GetStream();
            var head = ReadHead(s);
            if (head == null) return;
            var lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var first = lines[0].Split(' ');
            if (first.Length < 2) return;
            string method = first[0], target = first[1];
            long length = 0;
            bool chunked = false, expect = false;
            foreach (var l in lines)
            {
                if (l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) long.TryParse(l.Substring(15).Trim(), out length);
                else if (l.StartsWith("Transfer-Encoding:", StringComparison.OrdinalIgnoreCase) && l.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0) chunked = true;
                else if (l.StartsWith("Expect:", StringComparison.OrdinalIgnoreCase) && l.IndexOf("100-continue", StringComparison.OrdinalIgnoreCase) >= 0) expect = true;
            }

            string path = target, query = "";
            int q = target.IndexOf('?');
            if (q >= 0) { path = target.Substring(0, q); query = target.Substring(q + 1); }
            var parts = path.Trim('/').Split('/');
            if (parts.Length == 0 || !SafeEquals(parts[0], Token)) { Send(s, 404, "text/plain", Encoding.UTF8.GetBytes("Not found")); return; }
            string route = parts.Length > 1 ? parts[1] : "";

            if (method == "GET" && route == "")
            {
                ui.BeginInvoke(new Action(() => { var h = PhoneSeen; if (h != null) h(); }));
                Send(s, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(Page()));
            }
            else if (method == "GET" && route == "icon.png" && icon != null) Send(s, 200, "image/png", icon);
            else if (method == "GET" && route == "files") Send(s, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(FilesJson()));
            else if (method == "GET" && route == "file") SendFile(s, QueryInt(query, "i"));
            else if (method == "POST" && route == "upload")
            {
                if (expect) { var c = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n"); s.Write(c, 0, c.Length); s.Flush(); }
                Upload(s, Query(query, "name"), length, chunked);
            }
            else Send(s, 404, "text/plain", Encoding.UTF8.GetBytes("Not found"));
        }

        static bool SafeEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        static string ReadHead(Stream s)
        {
            var buf = new List<byte>(1024);
            int b;
            while ((b = s.ReadByte()) != -1)
            {
                buf.Add((byte)b);
                int c = buf.Count;
                if (c >= 4 && buf[c - 4] == 13 && buf[c - 3] == 10 && buf[c - 2] == 13 && buf[c - 1] == 10)
                    return Encoding.UTF8.GetString(buf.ToArray(), 0, c - 4);
                if (c > 16384) return null;
            }
            return null;
        }

        static string Query(string query, string key)
        {
            foreach (var kv in query.Split('&'))
            {
                int e = kv.IndexOf('=');
                if (e > 0 && kv.Substring(0, e) == key)
                    try { return Uri.UnescapeDataString(kv.Substring(e + 1).Replace('+', ' ')); } catch { return null; }
            }
            return null;
        }
        static int QueryInt(string query, string key) { int v; return int.TryParse(Query(query, key), out v) ? v : -1; }

        static void Send(Stream s, int code, string type, byte[] body, string extraHeaders = "")
        {
            string status = code == 200 ? "OK" : code == 404 ? "Not Found" : code == 413 ? "Too Large" : "Error";
            var head = "HTTP/1.1 " + code + " " + status + "\r\nContent-Type: " + type + "\r\nContent-Length: " + body.Length +
                       "\r\nCache-Control: no-store\r\nConnection: close\r\n" + extraHeaders + "\r\n";
            var h = Encoding.ASCII.GetBytes(head);
            s.Write(h, 0, h.Length);
            s.Write(body, 0, body.Length);
            s.Flush();
        }

        // ---------------- upload: phone -> pocket ----------------
        void Upload(Stream s, string name, long length, bool chunked)
        {
            if (length <= 0 && !chunked) { Send(s, 400, "text/plain", Encoding.UTF8.GetBytes("Empty")); return; }
            Directory.CreateDirectory(InboxDir);
            string file = CleanName(name);
            string path = Unique(Path.Combine(InboxDir, file));
            string tmp = path + ".part";
            int id = Interlocked.Increment(ref nextUploadId);
            long total = chunked ? -1 : length;
            ui.BeginInvoke(new Action(() => { var h = UploadStarted; if (h != null) h(id, Path.GetFileName(path), total); }));
            var clock = System.Diagnostics.Stopwatch.StartNew();
            long received = 0, lastReport = 0;
            Action<long> progress = n =>
            {
                received += n;
                if (clock.ElapsedMilliseconds - lastReport < 100) return;
                lastReport = clock.ElapsedMilliseconds;
                long r = received;
                ui.BeginInvoke(new Action(() => { var h = UploadProgress; if (h != null) h(id, r); }));
            };
            try
            {
                using (var fs = File.Create(tmp))
                {
                    if (chunked)
                    {
                        while (true)
                        {
                            string sizeLine = ReadLine(s);
                            int semi = sizeLine.IndexOf(';'); if (semi >= 0) sizeLine = sizeLine.Substring(0, semi);
                            long chunk = Convert.ToInt64(sizeLine.Trim(), 16);
                            if (chunk == 0) { ReadLine(s); break; }
                            CopyExactly(s, fs, chunk, progress);
                            ReadLine(s);
                        }
                    }
                    else CopyExactly(s, fs, length, progress);
                }
                File.Move(tmp, path);
            }
            catch
            {
                try { File.Delete(tmp); } catch { }
                ui.BeginInvoke(new Action(() => { var h = UploadFinished; if (h != null) h(id, Path.GetFileName(path), false); }));
                try { Send(s, 400, "text/plain", Encoding.UTF8.GetBytes("Upload failed")); } catch { }
                return;
            }
            ui.BeginInvoke(new Action(() =>
            {
                shelf.Add(new[] { path });
                var h = Received; if (h != null) h(path);
                var f = UploadFinished; if (f != null) f(id, path, true);
            }));
            Send(s, 200, "application/json", Encoding.UTF8.GetBytes("{\"ok\":true,\"name\":\"" + Json(Path.GetFileName(path)) + "\"}"));
        }

        static void CopyExactly(Stream from, Stream to, long count, Action<long> progress)
        {
            var buf = new byte[81920];
            while (count > 0)
            {
                int n = from.Read(buf, 0, (int)Math.Min(buf.Length, count));
                if (n <= 0) throw new IOException("Connection dropped");
                to.Write(buf, 0, n);
                count -= n;
                if (progress != null) progress(n);
            }
        }

        static string ReadLine(Stream s)
        {
            var sb = new StringBuilder();
            int b;
            while ((b = s.ReadByte()) != -1 && b != '\n') if (b != '\r') sb.Append((char)b);
            if (b == -1 && sb.Length == 0) throw new IOException("Connection dropped");
            return sb.ToString();
        }

        static string CleanName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) name = "iPhone " + DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss");
            name = Path.GetFileName(name.Replace('\\', '/').Split('/').Last());
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.Trim().TrimStart('.');
            if (name.Length == 0) name = "iPhone file";
            if (name.Length > 120) name = name.Substring(0, 100) + Path.GetExtension(name);
            return name;
        }

        static string Unique(string path)
        {
            if (!File.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path), n = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
            for (int i = 2; ; i++) { var p = Path.Combine(dir, n + " (" + i + ")" + ext); if (!File.Exists(p)) return p; }
        }

        // ---------------- download: pocket -> phone ----------------
        List<string> PocketFiles()
        {
            List<string> list = null;
            ui.Invoke(new Action(() => list = shelf.Items.Select(i => i.Path).Where(File.Exists).ToList()));
            return list ?? new List<string>();
        }

        string FilesJson()
        {
            var files = PocketFiles();
            var sb = new StringBuilder("[");
            for (int i = 0; i < files.Count; i++)
            {
                long size = 0; try { size = new FileInfo(files[i]).Length; } catch { }
                if (i > 0) sb.Append(',');
                sb.Append("{\"i\":").Append(i).Append(",\"name\":\"").Append(Json(Path.GetFileName(files[i]))).Append("\",\"size\":").Append(size).Append('}');
            }
            return sb.Append(']').ToString();
        }

        void SendFile(Stream s, int index)
        {
            var files = PocketFiles();
            if (index < 0 || index >= files.Count) { Send(s, 404, "text/plain", Encoding.UTF8.GetBytes("Not found")); return; }
            var path = files[index];
            using (var fs = File.OpenRead(path))
            {
                string name = Path.GetFileName(path);
                string ascii = new string(name.Select(ch => ch < 128 && ch != '"' ? ch : '_').ToArray());
                var head = "HTTP/1.1 200 OK\r\nContent-Type: " + Mime(path) + "\r\nContent-Length: " + fs.Length +
                           "\r\nContent-Disposition: attachment; filename=\"" + ascii + "\"; filename*=UTF-8''" + Uri.EscapeDataString(name) +
                           "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
                var h = Encoding.ASCII.GetBytes(head);
                s.Write(h, 0, h.Length);
                fs.CopyTo(s, 81920);
            }
        }

        static string Mime(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".jpg": case ".jpeg": return "image/jpeg";
                case ".png": return "image/png";
                case ".gif": return "image/gif";
                case ".heic": return "image/heic";
                case ".pdf": return "application/pdf";
                case ".mp4": return "video/mp4";
                case ".mov": return "video/quicktime";
                case ".txt": return "text/plain";
                case ".mp3": return "audio/mpeg";
                default: return "application/octet-stream";
            }
        }

        static string Json(string s)
        {
            var sb = new StringBuilder();
            foreach (var c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.ToString();
        }

        string Page()
        {
            using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("phone.html"))
            using (var r = new StreamReader(st, Encoding.UTF8))
                return r.ReadToEnd().Replace("{{HOST}}", Environment.MachineName);
        }

        static byte[] RenderIcon()
        {
            var m = new Mascot { Width = 150, Height = 150, Margin = new System.Windows.Thickness(15, 18, 15, 12) };
            var g = new System.Windows.Controls.Grid { Width = 180, Height = 180, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x14, 0x14, 0x1A)) };
            g.Children.Add(m);
            g.Measure(new System.Windows.Size(180, 180)); g.Arrange(new System.Windows.Rect(0, 0, 180, 180));
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(180, 180, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(g);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using (var ms = new MemoryStream()) { enc.Save(ms); return ms.ToArray(); }
        }
    }
}
