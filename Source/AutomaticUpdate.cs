using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Education2k
{
    public sealed class UpdateFile
    {
        public string Name, NewHash, OldHash;
        public bool Existed, Started;
    }
    public sealed class UpdateJob
    {
        public string Owner = "CuriousCatLearning.AutoUpdate.1";
        public string Id, InstallRoot, OldVersion, NewVersion, Token, Phase;
        public int ParentPid, WorkerPid;
        public long ParentStart, WorkerStart;
        public bool Demo;
        public List<UpdateFile> Files = new List<UpdateFile>();
        [ScriptIgnore] public string Path;
    }
    public sealed class UpdateOutcome
    {
        public string Version, Status;
    }
    // The helper is a temporary copy of this executable, outside the installation.
    // The journal is flushed before every replacement, so recovery also works after
    // a terminated helper or a power loss. Only the runtime allowlist is writable.
    public static class AutomaticUpdate
    {
        static readonly string[] Phases = { "Downloading", "Ready", "BackingUp", "Applying", "Starting", "RollingBack", "Committed", "RolledBack" };
        static string Normal(string path) { return System.IO.Path.GetFullPath(path).TrimEnd(System.IO.Path.DirectorySeparatorChar); }
        static string Jobs(string root) { return System.IO.Path.Combine(UpdateService.CacheDirectory(root), "automatic"); }
        static string Folder(UpdateJob job) { return System.IO.Path.GetDirectoryName(job.Path); }
        static string InJob(UpdateJob job, string name) { return System.IO.Path.Combine(Folder(job), name); }
        static string FilePath(string root, string relative)
        {
            if (!UpdateService.RuntimeFile(relative)) throw new InvalidOperationException("קובץ אינו חלק מקובצי התוכנה המאושרים.");
            string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar)));
            if (!path.StartsWith(Normal(root) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("נתיב מחוץ לתיקיית העדכון.");
            NoLinks(path); return path;
        }
        static void NoLinks(string path)
        {
            string current = System.IO.Path.GetFullPath(path);
            while (!String.IsNullOrEmpty(current))
            {
                if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("לא ניתן לעדכן דרך קישור לקובץ או לתיקייה.");
                current = System.IO.Path.GetDirectoryName(current);
            }
        }
        public static string Hash(string path) { using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return UpdateService.Hex(sha.ComputeHash(stream)); }
        static bool ValidVersion(string value) { Version version; return value != null && Regex.IsMatch(value, @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$") && Version.TryParse(value, out version); }
        static void CheckExecutable(string path, string version)
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            if (info.ProductName != AppInfo.Name || info.ProductVersion != version) throw new InvalidOperationException("קובץ ההפעלה אינו תואם לתוכנה ולגרסת העדכון.");
        }
        static void AtomicText(string path, string text)
        {
            NoLinks(path); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)); string temporary = path + ".new"; NoLinks(temporary);
            File.WriteAllText(temporary, text, new UTF8Encoding(false));
            using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) stream.Flush(true);
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
        static void Save(UpdateJob job) { AtomicText(job.Path, Store.Json.Serialize(job)); }
        public static UpdateJob Read(string path, string token = null)
        {
            path = System.IO.Path.GetFullPath(path); NoLinks(path);
            if (!File.Exists(path) || new FileInfo(path).Length > 1024 * 1024) throw new InvalidOperationException("מצב העדכון חסר או אינו תקין.");
            var job = Store.Read<UpdateJob>(path); Guid id, nonce;
            if (job == null || job.Owner != "CuriousCatLearning.AutoUpdate.1" || !Guid.TryParseExact(job.Id, "N", out id) || !Guid.TryParseExact(job.Token, "N", out nonce) || token != null && job.Token != token || !ValidVersion(job.OldVersion) || !ValidVersion(job.NewVersion) || !Phases.Contains(job.Phase)) throw new InvalidOperationException("פרטי העדכון אינם תקינים.");
            job.InstallRoot = Normal(job.InstallRoot);
            if (job.InstallRoot == Normal(System.IO.Path.GetPathRoot(job.InstallRoot)) || !String.Equals(path, System.IO.Path.Combine(Jobs(job.InstallRoot), "job-" + job.Id, "job.json"), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("תיקיית העדכון אינה בבעלות התוכנה.");
            NoLinks(job.InstallRoot);
            if (job.Files == null || job.Files.Count > 1000 || job.Files.Any(f => f == null) || job.Files.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != job.Files.Count || job.Files.Any(f => !UpdateService.RuntimeFile(f.Name) || !Regex.IsMatch(f.NewHash ?? "", "^[a-f0-9]{64}$") || f.Existed && !Regex.IsMatch(f.OldHash ?? "", "^[a-f0-9]{64}$"))) throw new InvalidOperationException("רשימת קובצי העדכון אינה תקינה.");
            job.Path = path; return job;
        }
        public static async Task<UpdateJob> Prepare(UpdateService service, AppUpdate release, string installRoot, string oldVersion, CancellationToken cancellation, IProgress<long> progress, bool demo = false)
        {
            string root = Normal(installRoot); NoLinks(root);
            var existing = FindReady(root, release.Version); if (existing != null) return existing;
            var job = new UpdateJob { Id = Guid.NewGuid().ToString("N"), Token = Guid.NewGuid().ToString("N"), InstallRoot = root, OldVersion = oldVersion, NewVersion = release.Version, Phase = "Downloading", Demo = demo };
            using (var process = Process.GetCurrentProcess()) { job.ParentPid = process.Id; job.ParentStart = process.StartTime.ToUniversalTime().Ticks; }
            job.Path = System.IO.Path.Combine(Jobs(root), "job-" + job.Id, "job.json");
            NoLinks(job.Path); Directory.CreateDirectory(Folder(job)); Save(job);
            bool ready = false;
            try
            {
                string zip = InJob(job, "payload.zip"); await service.Download(release, zip, cancellation, progress).ConfigureAwait(false);
                await Task.Run(delegate {
                    cancellation.ThrowIfCancellationRequested();
                    string prefix = "CuriousCatLearning-" + release.Version + "-windows-x64/";
                    using (var archive = ZipFile.OpenRead(zip)) foreach (var entry in archive.Entries)
                    {
                        if (entry.FullName.EndsWith("/")) continue;
                        string name = entry.FullName.Substring(prefix.Length), destination = FilePath(InJob(job, "staged"), name);
                        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination));
                        using (var input = entry.Open()) using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { input.CopyTo(output); output.Flush(true); }
                        job.Files.Add(new UpdateFile { Name = name, NewHash = Hash(destination) }); cancellation.ThrowIfCancellationRequested();
                    }
                    if (!demo)
                    {
                        CheckExecutable(InJob(job, "staged/Education2k.exe"), release.Version);
                        DateTimeOffset.ParseExact(File.ReadAllText(InJob(job, "staged/VERSION_DATE")).Trim(), "yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
                    }
                    File.Copy(typeof(Program).Assembly.Location, InJob(job, "runner.exe"));
                    File.WriteAllText(InJob(job, "runner.exe.config"), "<configuration><startup><supportedRuntime version=\"v4.0\" sku=\".NETFramework,Version=v4.8\"/></startup></configuration>");
                    File.Delete(zip); job.Phase = "Ready"; Save(job); ready = true;
                }, cancellation).ConfigureAwait(false);
                return job;
            }
            finally { if (!ready) Cleanup(job); }
        }
        public static UpdateJob FindReady(string root, string version)
        {
            if (!Directory.Exists(Jobs(root))) return null;
            foreach (string folder in Directory.GetDirectories(Jobs(root), "job-*"))
            {
                NoLinks(folder); string path = System.IO.Path.Combine(folder, "job.json"); if (!File.Exists(path)) continue;
                var job = Read(path);
                if (Alive(job.WorkerPid, job.WorkerStart)) continue;
                if (job.Phase == "Ready" && job.NewVersion == version && job.InstallRoot == Normal(root))
                {
                    try { VerifyStage(job); return job; } catch (IOException) { Cleanup(job); } catch (InvalidOperationException) { Cleanup(job); }
                }
                else if (job.Phase == "Ready" && !Alive(job.WorkerPid, job.WorkerStart)) Cleanup(job);
            }
            return null;
        }
        static void VerifyStage(UpdateJob job)
        {
            if (job.Files.Count == 0 || !job.Files.Any(f => f.Name == "Education2k.exe") || !job.Files.Any(f => f.Name == "VERSION")) throw new InvalidOperationException("קובצי העדכון אינם מוכנים.");
            foreach (var file in job.Files) if (Hash(FilePath(InJob(job, "staged"), file.Name)) != file.NewHash) throw new IOException("קובץ עדכון השתנה לאחר האימות.");
            if (!job.Demo) CheckExecutable(InJob(job, "staged/Education2k.exe"), job.NewVersion);
        }
        static bool Alive(int pid, long started)
        {
            if (pid <= 0 || started <= 0) return false;
            try { using (var process = Process.GetProcessById(pid)) return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == started; }
            catch (ArgumentException) { return false; } catch (InvalidOperationException) { return false; } catch (System.ComponentModel.Win32Exception) { return false; }
        }
        public static Process Start(UpdateJob job, bool recover = false)
        {
            job = Read(job.Path, job.Token); if (job.Demo) throw new InvalidOperationException("חבילת ההדגמה אינה מיועדת להתקנה.");
            if (!recover)
            {
                if (job.Phase != "Ready") throw new InvalidOperationException("העדכון אינו מוכן להתקנה.");
                VerifyStage(job); CheckExecutable(System.IO.Path.Combine(job.InstallRoot, "Education2k.exe"), job.OldVersion);
                using (var current = Process.GetCurrentProcess()) { job.ParentPid = current.Id; job.ParentStart = current.StartTime.ToUniversalTime().Ticks; } Save(job);
            }
            var info = new ProcessStartInfo(InJob(job, "runner.exe"), (recover ? "--recover-app-update" : "--apply-app-update") + " \"" + job.Path + "\" " + job.Token) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Folder(job), WindowStyle = ProcessWindowStyle.Hidden };
            return Process.Start(info);
        }
        static bool OtherInstance(UpdateJob job)
        {
            foreach (var candidate in Process.GetProcessesByName("Education2k")) using (candidate)
            {
                try { if (String.Equals(candidate.MainModule.FileName, System.IO.Path.Combine(job.InstallRoot, "Education2k.exe"), StringComparison.OrdinalIgnoreCase)) return true; }
                catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            }
            return false;
        }
        static void Replace(string source, string target, string hash, string id)
        {
            NoLinks(target); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
            string temporary = target + ".update-" + id; NoLinks(temporary);
            try
            {
                using (var input = File.OpenRead(source)) using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) { input.CopyTo(output); output.Flush(true); }
                if (Hash(temporary) != hash) throw new IOException("אימות קובץ העדכון נכשל.");
                if (File.Exists(target)) File.Replace(temporary, target, null); else File.Move(temporary, target);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        // Tests inject a fault between replacements; production never supplies one.
        public static void Apply(UpdateJob job, Action<int> afterReplacement = null)
        {
            if (job.Demo || job.Phase != "Ready") throw new InvalidOperationException("העדכון אינו מוכן.");
            VerifyStage(job); CheckExecutable(System.IO.Path.Combine(job.InstallRoot, "Education2k.exe"), job.OldVersion);
            job.Phase = "BackingUp"; Save(job);
            // Detect common permission/locking failures before any installed byte changes.
            foreach (var file in job.Files)
            {
                string target = FilePath(job.InstallRoot, file.Name);
                if (File.Exists(target)) using (var check = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)) { }
            }
            foreach (var file in job.Files)
            {
                string target = FilePath(job.InstallRoot, file.Name), backup = FilePath(InJob(job, "backup"), file.Name);
                file.Existed = File.Exists(target);
                if (file.Existed)
                {
                    file.OldHash = Hash(target); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(backup)); File.Copy(target, backup, true);
                    using (var stream = new FileStream(backup, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) stream.Flush(true);
                    if (Hash(backup) != file.OldHash) throw new IOException("לא ניתן לאמת את גיבוי התוכנה.");
                }
            }
            job.Phase = "Applying"; Save(job); int count = 0;
            foreach (var file in job.Files)
            {
                string target = FilePath(job.InstallRoot, file.Name);
                if (file.Existed ? !File.Exists(target) || Hash(target) != file.OldHash : File.Exists(target)) throw new IOException("קובץ התוכנה השתנה במהלך העדכון.");
                file.Started = true; Save(job);
                Replace(FilePath(InJob(job, "staged"), file.Name), target, file.NewHash, job.Id);
                if (afterReplacement != null) afterReplacement(++count);
            }
            job.Phase = "Starting"; Save(job);
        }
        public static void Rollback(UpdateJob job)
        {
            job.Phase = "RollingBack"; Save(job);
            // Check every required backup before changing any installed file.
            foreach (var file in job.Files.Where(f => f.Started && f.Existed)) if (Hash(FilePath(InJob(job, "backup"), file.Name)) != file.OldHash) throw new IOException("גיבוי השחזור אינו תקין; הוא נשמר לבדיקה נוספת.");
            foreach (var file in job.Files.Where(f => f.Started).Reverse())
            {
                string target = FilePath(job.InstallRoot, file.Name);
                if (file.Existed) Replace(FilePath(InJob(job, "backup"), file.Name), target, file.OldHash, job.Id);
                else if (File.Exists(target)) File.Delete(target);
                string temporary = target + ".update-" + job.Id; if (File.Exists(temporary)) File.Delete(temporary);
            }
            CheckExecutable(System.IO.Path.Combine(job.InstallRoot, "Education2k.exe"), job.OldVersion);
            job.Phase = "RolledBack"; Save(job);
        }
        static Process Launch(UpdateJob job)
        {
            string ack = InJob(job, "ready.json"); if (File.Exists(ack)) File.Delete(ack);
            return Process.Start(new ProcessStartInfo(System.IO.Path.Combine(job.InstallRoot, "Education2k.exe"), "--complete-app-update \"" + job.Path + "\" " + job.Token) { UseShellExecute = false, WorkingDirectory = job.InstallRoot });
        }
        static bool WaitReady(UpdateJob job, Process process)
        {
            string expected = job.Phase == "RolledBack" ? job.OldVersion : job.NewVersion;
            var time = Stopwatch.StartNew(); bool ready = false;
            while (time.Elapsed < TimeSpan.FromSeconds(45))
            {
                if (process.HasExited) return false;
                try { if (File.Exists(InJob(job, "ready.json")) && File.ReadAllText(InJob(job, "ready.json")) == job.Token + ":" + expected) { ready = true; break; } } catch (IOException) { }
                Thread.Sleep(100);
            }
            if (ready && !process.WaitForExit(1500)) return true;
            if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); } return false;
        }
        static void Outcome(UpdateJob job, string status) { AtomicText(System.IO.Path.Combine(UpdateService.CacheDirectory(job.InstallRoot), "outcome.json"), Store.Json.Serialize(new UpdateOutcome { Version = job.NewVersion, Status = status })); }
        public static UpdateOutcome LastOutcome(string root)
        {
            try { string path = System.IO.Path.Combine(UpdateService.CacheDirectory(root), "outcome.json"); return File.Exists(path) && new FileInfo(path).Length < 4096 ? Store.Read<UpdateOutcome>(path) : null; } catch (IOException) { return null; } catch (UnauthorizedAccessException) { return null; } catch (InvalidOperationException) { return null; } catch (ArgumentException) { return null; }
        }
        public static void DismissOutcome(string root)
        {
            string path = System.IO.Path.Combine(UpdateService.CacheDirectory(root), "outcome.json"); NoLinks(path); if (File.Exists(path)) File.Delete(path);
        }
        public static int Worker(string path, string token, bool recovery)
        {
            UpdateJob job = null;
            try
            {
                job = Read(path, token);
                if (job.Demo || !String.Equals(Normal(typeof(Program).Assembly.Location), Normal(InJob(job, "runner.exe")), StringComparison.OrdinalIgnoreCase)) return 2;
                using (var mutex = new Mutex(false, "Local\\CuriousCatUpdate-" + new DirectoryInfo(UpdateService.CacheDirectory(job.InstallRoot)).Name))
                {
                    bool owned = false;
                    try
                    {
                        try { owned = mutex.WaitOne(0); } catch (AbandonedMutexException) { owned = true; } if (!owned) return 3;
                        using (var current = Process.GetCurrentProcess()) { job.WorkerPid = current.Id; job.WorkerStart = current.StartTime.ToUniversalTime().Ticks; } Save(job);
                        var wait = Stopwatch.StartNew(); while (Alive(job.ParentPid, job.ParentStart) && wait.Elapsed < TimeSpan.FromSeconds(60)) Thread.Sleep(100);
                        if (Alive(job.ParentPid, job.ParentStart) || OtherInstance(job)) { Outcome(job, "busy"); return 4; }
                        if (!recovery)
                        {
                            bool started = false;
                            try { Apply(job); using (var next = Launch(job)) started = WaitReady(job, next); } catch (Exception) { }
                            if (started) { job.Phase = "Committed"; Save(job); Outcome(job, "updated"); TryRemovePayload(job); return 0; }
                        }
                        Rollback(job); Outcome(job, "rolled-back"); TryRemovePayload(job);
                        using (var previous = Launch(job)) { WaitReady(job, previous); }
                        return 0;
                    }
                    finally { if (owned) mutex.ReleaseMutex(); }
                }
            }
            catch (Exception)
            {
                // Leave a recoverable journal and backups if restoration itself fails.
                if (job != null) { try { Outcome(job, "recovery-needed"); } catch (Exception) { } }
                return 1;
            }
        }
        static void RemovePayload(UpdateJob job)
        {
            foreach (string name in new[] { "payload.zip", "staged", "backup" }) DeleteOwned(job, InJob(job, name));
        }
        static void TryRemovePayload(UpdateJob job)
        {
            try { RemovePayload(job); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        static void DeleteOwned(UpdateJob job, string path)
        {
            string expected = System.IO.Path.Combine(Jobs(job.InstallRoot), "job-" + job.Id);
            if (!String.Equals(Normal(Folder(job)), Normal(expected), StringComparison.OrdinalIgnoreCase) || !(Normal(path) == Normal(expected) || Normal(path).StartsWith(Normal(expected) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) throw new IOException("סירוב למחוק נתיב שאינו בבעלות העדכון.");
            NoLinks(path);
            if (Directory.Exists(path))
            {
                // Enumerate one level at a time; never follow a reparse directory.
                foreach (string child in Directory.GetFileSystemEntries(path)) DeleteOwned(job, child);
                Directory.Delete(path);
            }
            else if (File.Exists(path)) File.Delete(path);
        }
        public static void Cleanup(UpdateJob job) { DeleteOwned(job, Folder(job)); }
        public static Task<UpdateOutcome> Complete(string path, string token, string root, string version)
        {
            var job = Read(path, token);
            if (job.InstallRoot != Normal(root) || version != (job.Phase == "RolledBack" ? job.OldVersion : job.NewVersion) || !(job.Phase == "Starting" || job.Phase == "Committed" || job.Phase == "RolledBack")) throw new InvalidOperationException("אישור ההפעלה אינו תואם לעדכון.");
            AtomicText(InJob(job, "ready.json"), job.Token + ":" + version);
            return Task.Run(delegate {
                var wait = Stopwatch.StartNew(); while (Alive(job.WorkerPid, job.WorkerStart) && wait.Elapsed < TimeSpan.FromMinutes(2)) Thread.Sleep(200);
                if (!Alive(job.WorkerPid, job.WorkerStart)) for (int retry = 0; retry < 10; retry++)
                {
                    try { if (!File.Exists(path)) break; var latest = Read(path, token); if (latest.Phase == "Committed" || latest.Phase == "RolledBack") Cleanup(latest); break; }
                    catch (IOException) { Thread.Sleep(200); } catch (UnauthorizedAccessException) { Thread.Sleep(200); } catch (InvalidOperationException) { break; }
                }
                return LastOutcome(root);
            });
        }
        public static bool RecoverStartup(string root)
        {
            string jobs = Jobs(root); NoLinks(jobs); if (!Directory.Exists(jobs)) return false;
            foreach (string folder in Directory.GetDirectories(jobs, "job-*"))
            {
                NoLinks(folder); string path = System.IO.Path.Combine(folder, "job.json"); if (!File.Exists(path)) continue;
                var job = Read(path); if (Alive(job.WorkerPid, job.WorkerStart)) continue;
                if (job.Phase == "Committed" || job.Phase == "RolledBack" || job.Phase == "Downloading" && !Alive(job.ParentPid, job.ParentStart)) { Cleanup(job); continue; }
                if (new[] { "BackingUp", "Applying", "Starting", "RollingBack" }.Contains(job.Phase))
                {
                    using (var process = Process.GetCurrentProcess()) { job.ParentPid = process.Id; job.ParentStart = process.StartTime.ToUniversalTime().Ticks; } Save(job);
                    using (Start(job, true)) { } return true;
                }
                if (job.Phase == "Ready" && File.Exists(System.IO.Path.Combine(root, "VERSION")) && File.ReadAllText(System.IO.Path.Combine(root, "VERSION")).Trim() != job.OldVersion) Cleanup(job);
            }
            return false;
        }
    }
}
