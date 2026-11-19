using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Education2k
{
    public interface IModelClient
    {
        Task<T> Ask<T>(string prompt, string schema, bool search, int timeoutSeconds, CancellationToken cancellation);
    }
    public class CodexClient : IVisualModelClient
    {
        private readonly string root;
        public CodexClient(string root) { this.root = root; }
        public static string Quote(string argument) { return "\"" + argument.Replace("\"", "\\\"") + "\""; }
        public static string Executable()
        {
            var custom = Environment.GetEnvironmentVariable("EDUCATION2K_CODEX");
            if (!String.IsNullOrEmpty(custom) && File.Exists(custom)) return custom;
            var savedLocation = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "codex-location.txt");
            if (File.Exists(savedLocation)) { var saved = File.ReadAllText(savedLocation).Trim(); if (File.Exists(saved)) return saved; }
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                try { var p = Path.Combine(dir.Trim('"'), "codex.exe"); if (File.Exists(p)) return p; } catch (ArgumentException) { }
            }
            var windowsApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
            try
            {
                var candidates = Directory.GetDirectories(windowsApps, "OpenAI.Codex_*").OrderByDescending(d => d);
                foreach (var dir in candidates) { var p = Path.Combine(dir, "app", "resources", "codex.exe"); if (File.Exists(p)) return p; }
            }
            catch (UnauthorizedAccessException) { }
            // Explorer-launched apps do not inherit Codex's augmented PATH. Appx lookup also
            // works when WindowsApps cannot be enumerated by the desktop user's token.
            try
            {
                using (var query = Process.Start(new ProcessStartInfo {
                    FileName = "powershell.exe", Arguments = "-NoProfile -NonInteractive -Command \"(Get-AppxPackage -Name OpenAI.Codex | Sort-Object Version -Descending | Select-Object -First 1).InstallLocation\"",
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                }))
                {
                    var location = query.StandardOutput.ReadToEnd().Trim(); query.WaitForExit(15000);
                    var p = Path.Combine(location, "app", "resources", "codex.exe"); if (File.Exists(p)) return p;
                }
            }
            catch (Exception) { }
            throw new InvalidOperationException("Codex לא נמצא במחשב. יש להתקין את Codex ולהתחבר לחשבון ChatGPT, ואז ללחוץ על בדיקת חיבור. ניתן גם להגדיר EDUCATION2K_CODEX עם הנתיב ל־codex.exe.");
        }
        static async Task<string> ReadOutput(StreamReader reader, CodexEventStream events)
        {
            if (events == null) return await reader.ReadToEndAsync();
            string line;
            while ((line = await reader.ReadLineAsync()) != null) events.Accept(line);
            return events.FinalText ?? "";
        }
        static async Task<string> ReadDiagnostic(StreamReader reader, bool report)
        {
            var tail = new StringBuilder(); string line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (report) CodexEventStream.Diagnostic(line);
                tail.AppendLine(line); if (tail.Length > 6000) tail.Remove(0, tail.Length - 6000);
            }
            return tail.ToString();
        }
        private Task<string> ProcessAsync(string args, string input, int timeoutSeconds, CancellationToken cancellation, bool streamEvents = false)
        {
            return Task.Run(async delegate {
                using (var p = new Process())
                {
                    p.StartInfo = new ProcessStartInfo {
                        FileName = Executable(), Arguments = args, WorkingDirectory = root, UseShellExecute = false,
                        CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                        RedirectStandardInput = input != null, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
                    };
                    p.Start();
                    var events = streamEvents ? new CodexEventStream() : null;
                    if (streamEvents) WorkFlow.Activity("Codex הופעל במחשב; שולחים את הבקשה.", "שולחים את הבקשה ל־Codex.");
                    var output = ReadOutput(p.StandardOutput, events);
                    var error = ReadDiagnostic(p.StandardError, streamEvents);
                    try
                    {
                        if (input != null) { var data = Encoding.UTF8.GetBytes(input); await p.StandardInput.BaseStream.WriteAsync(data, 0, data.Length); p.StandardInput.Close(); }
                        if (streamEvents) WorkFlow.Activity("הבקשה נשלחה ל־Codex.", "ממתינים לעיבוד הבקשה ב־Codex.");
                        var watch = Stopwatch.StartNew();
                        while (!p.WaitForExit(150))
                        {
                            cancellation.ThrowIfCancellationRequested();
                            if (watch.Elapsed.TotalSeconds > timeoutSeconds) throw new TimeoutException("הפנייה ל־Codex נמשכה זמן רב. אפשר לבדוק את החיבור ולנסות שוב. מבחן שלא הושלם אינו נשמר.");
                        }
                        cancellation.ThrowIfCancellationRequested();
                        string text = await output;
                        string diagnostic = await error;
                        if (p.ExitCode != 0 || (events != null && events.Failed))
                        {
                            if (events != null && !String.IsNullOrEmpty(events.ErrorMessage)) diagnostic += "\n" + events.ErrorMessage;
                            string reason = diagnostic.IndexOf("usage limit", StringComparison.OrdinalIgnoreCase) >= 0 || diagnostic.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0 ? "מכסת השימוש בחשבון הושגה. יש להמתין לחידוש המכסה או לבדוק את חשבון Codex." :
                                "הפנייה ל־Codex לא הושלמה. יש לבדוק חיבור לאינטרנט והתחברות לחשבון. התשובות נשמרו וניתן לנסות שוב.";
                            throw new InvalidOperationException(reason + "\nפרטים טכניים: " + diagnostic.Substring(Math.Max(0, diagnostic.Length - 1800)));
                        }
                        return text.Trim();
                    }
                    finally { if (!p.HasExited) { try { p.Kill(); } catch (InvalidOperationException) { } } }
                }
            }, cancellation);
        }
        public async Task<string> Check(CancellationToken cancellation)
        {
            string status = await ProcessAsync("login status", null, 15, cancellation);
            // login status also writes its human-readable response to stderr on some versions.
            await Ask<Ping>("Reply with Message set to the Hebrew word מוכן. Do not use any tools.", "ping", false, 90, cancellation);
            return status;
        }
        public Task<string> Login(CancellationToken cancellation) { return ProcessAsync("login", null, 240, cancellation); }
        public async Task<T> Ask<T>(string prompt, string schema, bool search, int timeoutSeconds, CancellationToken cancellation)
        { return await AskCore<T>(prompt, schema, search, null, timeoutSeconds, cancellation); }
        public Task<T> AskImages<T>(string prompt, string schema, System.Collections.Generic.IList<string> images, int timeoutSeconds, CancellationToken cancellation)
        { return AskCore<T>(prompt, schema, false, images, timeoutSeconds, cancellation); }
        async Task<T> AskCore<T>(string prompt, string schema, bool search, System.Collections.Generic.IList<string> images, int timeoutSeconds, CancellationToken cancellation)
        {
            var schemaFile = Path.Combine(root, "Schemas", schema + ".json");
            if (!File.Exists(schemaFile)) throw new FileNotFoundException("חסר קובץ הנדרש להפעלת התוכנה: " + schemaFile);
            string args = "--no-daemon --ask-for-approval never " + (search ? "--search " : "") +
                "exec --json --ignore-user-config --skip-git-repo-check --ephemeral --sandbox read-only --color never --output-schema " + Quote(schemaFile) +
                (images == null ? "" : String.Concat(images.Select(path => " --image " + Quote(path)))) + " -";
            string text = await ProcessAsync(args, prompt, timeoutSeconds, cancellation, true);
            try { return Store.Json.Deserialize<T>(text); }
            catch (Exception e) { throw new InvalidOperationException("התקבלה תשובה שלא ניתן לקרוא מ־Codex. לא נשמר מבחן חלקי. אפשר לנסות שוב.", e); }
        }
        public class Ping { public string Message; }
    }
}
