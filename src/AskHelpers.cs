using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace NotchIsland
{
    // ===================================================================================
    // Claude Code in the background
    // ===================================================================================
    public class ClaudeDenial
    {
        public string Tool, Detail, Rule;
    }

    public class ClaudeResult
    {
        public string Text = "", SessionId;
        public bool IsError;
        public List<ClaudeDenial> Denials = new List<ClaudeDenial>();
        public List<string> Actions = new List<string>(); // OPEN:/POCKET:/NOTE:/COPY: lines for Mochi
    }

    /// <summary>
    /// Runs the Claude Code CLI headless (claude -p) for "Ask Mochi". Read-only tools run freely; anything
    /// that changes the computer comes back as a "permission denial" that Mochi shows as Allow / Deny.
    /// </summary>
    public static class ClaudeRunner
    {
        public const string SafeTools = "Read Glob Grep WebSearch WebFetch";

        const string MochiPrompt =
            "You are Mochi's brain: a background helper on the user's Windows laptop, asked from a hotkey or by voice. " +
            "Do the task directly with your tools, make sensible assumptions instead of asking questions, and finish with a reply of at most 2 short sentences, since it is shown in a small notification. " +
            "Mochi can act for you if you end your reply with any of these lines: " +
            "OPEN: <https url, spotify: uri or a file path> to open it; POCKET: <file path> to put a file in the notch pocket; NOTE: <text> to add to the user's notes; COPY: <text> to put text on the clipboard. " +
            "For requests to play music, use web search to find the song's Spotify track link (open.spotify.com/track/ID) and end with OPEN: spotify:track:ID. " +
            "For finding files, search the user's folders and end with one POCKET line per best match (up to 5). " +
            "The user's home folder is the current directory.";

        public static string FindExe()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var candidates = new List<string> { Path.Combine(home, ".local", "bin", "claude.exe") };
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
                if (dir.Trim().Length > 0) candidates.Add(Path.Combine(dir.Trim(), "claude.exe"));
            return candidates.FirstOrDefault(File.Exists);
        }

        public static bool Available { get { return FindExe() != null; } }

        /// <summary>Windows command-line quoting (CommandLineToArgvW rules).</summary>
        public static string Q(string s)
        {
            var sb = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in s)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { sb.Append('\\', slashes * 2 + 1); sb.Append('"'); }
                else { sb.Append('\\', slashes); sb.Append(c); }
                slashes = 0;
            }
            sb.Append('\\', slashes * 2);
            return sb.Append('"').ToString();
        }

        public static Task<ClaudeResult> Run(string prompt, string resumeSession, IEnumerable<string> extraAllowed, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                var exe = FindExe();
                if (exe == null) return new ClaudeResult { IsError = true, Text = "Claude Code isn't installed on this laptop." };
                var allowed = SafeTools.Split(' ').Concat(extraAllowed ?? Enumerable.Empty<string>()).Select(Q);
                var args = "-p --output-format json --append-system-prompt " + Q(MochiPrompt) + " --allowedTools " + string.Join(" ", allowed);
                if (!string.IsNullOrEmpty(resumeSession)) args += " --resume " + Q(resumeSession);
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                    WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                };
                using (var p = Process.Start(psi))
                {
                    using (var stdin = new StreamWriter(p.StandardInput.BaseStream, new UTF8Encoding(false))) stdin.Write(prompt);
                    var errTask = p.StandardError.ReadToEndAsync();
                    var outTask = p.StandardOutput.ReadToEndAsync();
                    var sw = Stopwatch.StartNew();
                    while (!p.WaitForExit(250))
                    {
                        if (ct.IsCancellationRequested || sw.Elapsed > TimeSpan.FromMinutes(6))
                        {
                            try { p.Kill(); } catch { }
                            return new ClaudeResult { IsError = true, Text = ct.IsCancellationRequested ? "Stopped." : "That took too long, so I stopped it." };
                        }
                    }
                    return Parse(outTask.Result, errTask.Result);
                }
            });
        }

        static ClaudeResult Parse(string stdout, string stderr)
        {
            var r = new ClaudeResult();
            try
            {
                var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(stdout) as Dictionary<string, object>;
                if (json == null) throw new FormatException();
                r.Text = (json.ContainsKey("result") ? json["result"] as string : null) ?? "";
                r.SessionId = json.ContainsKey("session_id") ? json["session_id"] as string : null;
                r.IsError = json.ContainsKey("is_error") && json["is_error"] is bool && (bool)json["is_error"];
                var denials = json.ContainsKey("permission_denials") ? json["permission_denials"] as IEnumerable : null;
                if (denials != null)
                    foreach (var d in denials.OfType<Dictionary<string, object>>())
                    {
                        string tool = d.ContainsKey("tool_name") ? d["tool_name"] as string : "?";
                        var input = d.ContainsKey("tool_input") ? d["tool_input"] as Dictionary<string, object> : null;
                        string cmd = input != null && input.ContainsKey("command") ? input["command"] as string : null;
                        string path = input != null && input.ContainsKey("file_path") ? input["file_path"] as string : null;
                        string detail = cmd ?? path ?? (input != null ? string.Join(", ", input.Select(kv => kv.Key + "=" + kv.Value)) : "");
                        string rule = cmd != null && (tool == "Bash" || tool == "PowerShell") ? tool + "(" + cmd + ")" : tool;
                        if (!r.Denials.Any(x => x.Rule == rule)) r.Denials.Add(new ClaudeDenial { Tool = tool, Detail = detail, Rule = rule });
                    }
            }
            catch
            {
                r.IsError = true;
                var msg = (stderr ?? "").Trim();
                if (msg.Length == 0) msg = (stdout ?? "").Trim();
                if (msg.IndexOf("login", StringComparison.OrdinalIgnoreCase) >= 0 || msg.IndexOf("auth", StringComparison.OrdinalIgnoreCase) >= 0)
                    msg = "Claude Code isn't signed in. Run 'claude' once in a terminal to log in.";
                r.Text = msg.Length > 300 ? msg.Substring(0, 300) + "…" : (msg.Length == 0 ? "Claude didn't answer." : msg);
                return r;
            }
            // pull out the action lines meant for Mochi
            var keep = new List<string>();
            foreach (var line in r.Text.Replace("\r", "").Split('\n'))
            {
                var t = line.Trim();
                if (t.StartsWith("OPEN:") || t.StartsWith("POCKET:") || t.StartsWith("NOTE:") || t.StartsWith("COPY:")) r.Actions.Add(t);
                else keep.Add(line);
            }
            r.Text = string.Join("\n", keep).Trim();
            return r;
        }

        /// <summary>Open Claude Code in a visible Windows Terminal tab, so you can watch and approve.</summary>
        public static bool OpenInTerminal(string prompt)
        {
            var exe = FindExe();
            if (exe == null) return false;
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            // wt treats ';' as a command separator
            string safe = prompt.Replace(";", "\\;");
            try
            {
                Process.Start(new ProcessStartInfo("wt.exe", "-w 0 new-tab --title Mochi -d " + Q(home) + " " + Q(exe) + " " + Q(safe)) { UseShellExecute = false });
                return true;
            }
            catch
            {
                try { Process.Start(new ProcessStartInfo("cmd.exe", "/k \"\"" + exe + "\" " + Q(prompt) + "\"") { WorkingDirectory = home, UseShellExecute = true }); return true; }
                catch { return false; }
            }
        }
    }

    // ===================================================================================
    // Finding files (Windows Search index, with a quick folder scan as a fallback)
    // ===================================================================================
    public static class FileFinder
    {
        public static Task<List<string>> Find(string query, int max)
        {
            return Task.Run(() =>
            {
                var words = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 0).Take(5).ToArray();
                if (words.Length == 0) return new List<string>();
                try { var hits = FromIndex(words, max); if (hits.Count > 0) return hits; } catch { }
                return Scan(words, max);
            });
        }

        static List<string> FromIndex(string[] words, int max)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).Replace("'", "''");
            var where = string.Join(" AND ", words.Select(w => "System.FileName LIKE '%" + w.Replace("'", "''").Replace("%", "") + "%'"));
            var sql = "SELECT TOP " + max + " System.ItemPathDisplay FROM SYSTEMINDEX WHERE SCOPE='file:" + home + "' AND " + where +
                      " AND System.ItemType <> 'Directory' ORDER BY System.DateModified DESC";
            var list = new List<string>();
            using (var conn = new OleDbConnection("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';"))
            {
                conn.Open();
                using (var cmd = new OleDbCommand(sql, conn))
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) { var p = r[0] as string; if (!string.IsNullOrEmpty(p) && File.Exists(p)) list.Add(p); }
            }
            return list;
        }

        static List<string> Scan(string[] words, int max)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var roots = new[] { "Desktop", "Documents", "Downloads", "Pictures", "Videos", "Music", "OneDrive" }.Select(d => Path.Combine(home, d)).Where(Directory.Exists);
            var found = new List<Tuple<string, DateTime>>();
            var sw = Stopwatch.StartNew();
            var stack = new Stack<Tuple<string, int>>(roots.Select(r => Tuple.Create(r, 0)));
            while (stack.Count > 0 && sw.ElapsedMilliseconds < 4000)
            {
                var cur = stack.Pop();
                try
                {
                    foreach (var f in Directory.EnumerateFiles(cur.Item1))
                    {
                        var n = Path.GetFileName(f);
                        if (words.All(w => n.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)) found.Add(Tuple.Create(f, File.GetLastWriteTime(f)));
                    }
                    if (cur.Item2 < 5)
                        foreach (var d in Directory.EnumerateDirectories(cur.Item1))
                        {
                            var dn = Path.GetFileName(d);
                            if (dn.StartsWith(".") || dn == "node_modules" || dn == "AppData") continue;
                            stack.Push(Tuple.Create(d, cur.Item2 + 1));
                        }
                }
                catch { }
            }
            return found.OrderByDescending(t => t.Item2).Take(max).Select(t => t.Item1).ToList();
        }
    }

    // ===================================================================================
    // Opening apps / sites, maths
    // ===================================================================================
    public static class Launcher
    {
        static readonly Dictionary<string, string> aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "settings", "ms-settings:" }, { "calculator", "calc.exe" }, { "calc", "calc.exe" }, { "notepad", "notepad.exe" },
            { "file explorer", "explorer.exe" }, { "explorer", "explorer.exe" }, { "files", "explorer.exe" }, { "spotify", "spotify:" },
            { "terminal", "wt.exe" }, { "task manager", "taskmgr.exe" }, { "paint", "mspaint.exe" }, { "camera", "microsoft.windows.camera:" },
            { "snipping tool", "ms-screenclip:" }, { "downloads", "shell:Downloads" }, { "documents", "shell:Personal" }, { "desktop", "shell:Desktop" },
            { "photos", "ms-photos:" }, { "clock", "ms-clock:" }, { "mail", "outlookmail:" }, { "store", "ms-windows-store:" },
            { "bluetooth", "ms-settings:bluetooth" }, { "wifi", "ms-settings:network-wifi" }, { "wi-fi", "ms-settings:network-wifi" }, { "sound settings", "ms-settings:sound" },
        };

        /// <summary>Opens an app (Start menu name), a folder shortcut, or a website. Returns what was opened, or null.</summary>
        public static string Open(string what)
        {
            what = what.Trim().TrimEnd('.', '!');
            if (what.StartsWith("the ", StringComparison.OrdinalIgnoreCase)) what = what.Substring(4);
            if (what.EndsWith(" app", StringComparison.OrdinalIgnoreCase)) what = what.Substring(0, what.Length - 4);
            string target;
            if (aliases.TryGetValue(what, out target)) return Start(target) ? what : null;
            if (!what.Contains(" ") && what.Contains(".") && !what.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return Start((what.StartsWith("http") ? "" : "https://") + what) ? what : null;
            var lnk = FindShortcut(what);
            if (lnk != null) return Start(lnk) ? Path.GetFileNameWithoutExtension(lnk) : null;
            return null;
        }

        static string FindShortcut(string name)
        {
            var dirs = new[] {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) };
            var all = new List<string>();
            foreach (var d in dirs.Where(Directory.Exists))
                try { all.AddRange(Directory.EnumerateFiles(d, "*.lnk", SearchOption.AllDirectories)); } catch { }
            Func<string, string> n = p => Path.GetFileNameWithoutExtension(p);
            return all.FirstOrDefault(p => n(p).Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? all.Where(p => n(p).StartsWith(name, StringComparison.OrdinalIgnoreCase)).OrderBy(p => n(p).Length).FirstOrDefault()
                ?? all.Where(p => n(p).IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0 && n(p).IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) < 0).OrderBy(p => n(p).Length).FirstOrDefault();
        }

        public static bool Start(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); return true; } catch { return false; }
        }

        static readonly string[] Risky = { ".exe", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".msi", ".lnk", ".scr", ".com", ".hta", ".reg", ".jar" };

        /// <summary>For things Claude asks Mochi to open: only web links, spotify: links and non-program files.</summary>
        public static bool SafeToOpen(string target)
        {
            target = target.Trim();
            if (target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return true;
            if (target.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase)) return true;
            if (File.Exists(target) || Directory.Exists(target))
                return !Risky.Contains(Path.GetExtension(target).ToLowerInvariant());
            return false;
        }

        /// <summary>Simple maths: + - * / % ( ) with x, ×, ÷ and "of" for percentages.</summary>
        public static string Calc(string expr)
        {
            var e = expr.ToLowerInvariant().Replace("×", "*").Replace("x", "*").Replace("÷", "/").Replace(",", "").Replace("plus", "+").Replace("minus", "-").Replace("times", "*").Replace("divided by", "/").Trim().TrimEnd('?', '=');
            var pct = System.Text.RegularExpressions.Regex.Match(e, @"^\s*([\d.]+)\s*%\s*of\s*([\d.]+)\s*$");
            if (pct.Success) e = pct.Groups[1].Value + "*" + pct.Groups[2].Value + "/100";
            if (!System.Text.RegularExpressions.Regex.IsMatch(e, @"^[\d\s.+\-*/()%]+$")) return null;
            try
            {
                var v = new DataTable().Compute(e, null);
                double d = Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture);
                if (double.IsInfinity(d) || double.IsNaN(d)) return null;
                return Math.Round(d, 6).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
            }
            catch { return null; }
        }
    }
}
