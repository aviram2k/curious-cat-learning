using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.CSharp;

namespace Education2k
{
    public static partial class Tests
    {
        static byte[] UpdateStub(string temp, string version, bool fail)
        {
            string code = @"
using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Reflection;
using System.Web.Script.Serialization;
[assembly: AssemblyProduct(@PRODUCT@)]
[assembly: AssemblyInformationalVersion(""@VERSION@"")]
[assembly: AssemblyFileVersion(""@VERSION@.0"")]
class Stub {
 static int Main(string[] args) {
  if (args.Length == 2 && args[0] == ""--hold"") { while (!File.Exists(args[1])) Thread.Sleep(30); return 0; }
  if (@FAIL@) return 23;
  if (args.Length != 3 || args[0] != ""--complete-app-update"") return 24;
  var json = new JavaScriptSerializer();
  var job = json.Deserialize<Dictionary<string,object>>(File.ReadAllText(args[1]));
  string expected = (string)job[""Phase""] == ""RolledBack"" ? (string)job[""OldVersion""] : (string)job[""NewVersion""];
  if (expected != ""@VERSION@"" || (string)job[""Token""] != args[2]) return 25;
  File.WriteAllText(Path.Combine(Path.GetDirectoryName(args[1]), ""ready.json""), args[2] + "":"" + expected);
  try { using (var worker = Process.GetProcessById(Convert.ToInt32(job[""WorkerPid""]))) worker.WaitForExit(30000); } catch (ArgumentException) { }
  var final = json.Deserialize<Dictionary<string,object>>(File.ReadAllText(args[1]));
  if ((string)final[""Phase""] == ""Committed"" || (string)final[""Phase""] == ""RolledBack"") Directory.Delete(Path.GetDirectoryName(args[1]), true);
  return 0;
 }
}";
            code = code.Replace("@PRODUCT@", Store.Json.Serialize(AppInfo.Name)).Replace("@VERSION@", version).Replace("@FAIL@", fail ? "true" : "false");
            string output = Path.Combine(temp, "stub-" + Guid.NewGuid().ToString("N") + ".exe");
            using (var compiler = new CSharpCodeProvider())
            {
                var options = new CompilerParameters(new[] { "System.dll", "System.Core.dll", "System.Web.Extensions.dll" }, output) { GenerateExecutable = true, CompilerOptions = "/target:winexe /optimize" };
                var results = compiler.CompileAssemblyFromSource(options, code);
                if (results.Errors.HasErrors) throw new Exception("Cannot compile synthetic update process: " + String.Join("; ", results.Errors.Cast<CompilerError>().Select(e => e.ErrorText)));
            }
            return File.ReadAllBytes(output);
        }
        static UpdateFixture RuntimeFixture(string version, byte[] executable)
        {
            var fixture = new UpdateFixture(version);
            using (var data = new MemoryStream())
            {
                using (var zip = new ZipArchive(data, ZipArchiveMode.Create, true))
                {
                    string prefix = "CuriousCatLearning-" + version + "-windows-x64/";
                    foreach (string name in new[] { "Education2k.exe", "Education2k.exe.config", "VERSION", "VERSION_DATE", "README.md", "CHANGELOG.md", "LICENSE", "Schemas/exam.json", "Native/pdfium.dll", "Schemas/new-runtime.json" })
                    {
                        byte[] bytes = name == "Education2k.exe" ? executable : Encoding.UTF8.GetBytes(name == "VERSION" ? version : name == "VERSION_DATE" ? "2026-11-25T23:00:00+02:00" : name == "Education2k.exe.config" ? "<configuration><startup><supportedRuntime version=\"v4.0\" sku=\".NETFramework,Version=v4.8\"/></startup></configuration>" : "Synthetic runtime " + version);
                        using (var output = zip.CreateEntry(prefix + name).Open()) output.Write(bytes, 0, bytes.Length);
                    }
                }
                fixture.Package = data.ToArray();
            }
            string hash; using (var sha = SHA256.Create()) hash = UpdateService.Hex(sha.ComputeHash(fixture.Package));
            fixture.Release.assets[0].size = fixture.Package.Length; fixture.Release.assets[0].digest = "sha256:" + hash;
            fixture.Checksum = hash + "  " + fixture.Release.assets[0].name + "\n"; return fixture;
        }
        static string InstallFixture(string temp, UpdateFixture old)
        {
            string app = Path.Combine(temp, "automatic-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(app);
            using (var data = new MemoryStream(old.Package)) using (var zip = new ZipArchive(data, ZipArchiveMode.Read)) foreach (var entry in zip.Entries)
            {
                string name = entry.FullName.Substring(entry.FullName.IndexOf('/') + 1), target = Path.Combine(app, name.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)); using (var input = entry.Open()) using (var output = File.Create(target)) input.CopyTo(output);
            }
            // This file exists only in the new version, exercising removal on rollback.
            File.Delete(Path.Combine(app, "Schemas", "new-runtime.json"));
            var store = new Store(app); var topic = TestTopic(store, "נושא סינתטי לשימור", false); var exam = Fixture(); Store.Write(store.ExamPath(topic, 1), exam);
            var student = store.Login("בדיקת", "עדכון"); var attempt = Answered(store, topic, exam, false); ExamService.Complete(exam, attempt); store.Save(student, attempt);
            Directory.CreateDirectory(Path.Combine(app, "_drafts")); File.WriteAllText(Path.Combine(app, "_drafts", "draft.txt"), "Synthetic draft");
            File.WriteAllText(Path.Combine(app, "codex-location.txt"), "Synthetic local configuration"); File.WriteAllText(Path.Combine(app, "personal-note.txt"), "Leave unmanaged files alone");
            File.WriteAllText(Path.Combine(app, "my-own-backup.zip"), "A pre-existing user archive is never an updater temporary file");
            return app;
        }
        static string PrivateSnapshot(string root)
        {
            return String.Join("\n", Directory.GetFiles(root, "*", SearchOption.AllDirectories).Where(p => !UpdateService.RuntimeFile(p.Substring(root.Length + 1).Replace('\\', '/'))).OrderBy(p => p).Select(p => p.Substring(root.Length) + ":" + AutomaticUpdate.Hash(p)));
        }
        static UpdateJob PrepareFixture(string app, UpdateFixture fixture)
        {
            var release = UpdateService.Parse(Store.Json.Serialize(fixture.Release));
            var service = new UpdateService(app, "1.0.0", fixture);
            return AutomaticUpdate.Prepare(service, release, app, "1.0.0", CancellationToken.None, null).GetAwaiter().GetResult();
        }
        static void RemoveTestUpdateCache(string root)
        {
            // The install path contains a fresh random test id, hence a unique cache.
            string cache = Path.GetFullPath(UpdateService.CacheDirectory(root)), basePath = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CuriousCatLearning", "Updates"));
            if (!Path.GetFileName(root).StartsWith("automatic-") || !cache.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new Exception("Unsafe synthetic cache cleanup");
            if (Directory.Exists(cache)) Directory.Delete(cache, true);
        }
        static void WorkerTest(string app, UpdateJob job, bool rollback, bool interrupted)
        {
            Process parent = null, worker = null; string signal = Path.Combine(Path.GetDirectoryName(app), "exit-" + Guid.NewGuid().ToString("N"));
            try
            {
                if (!interrupted)
                {
                    parent = Process.Start(new ProcessStartInfo(Path.Combine(app, "Education2k.exe"), "--hold \"" + signal + "\"") { UseShellExecute = false, CreateNoWindow = true });
                    job.ParentPid = parent.Id; job.ParentStart = parent.StartTime.ToUniversalTime().Ticks;
                }
                else { job.ParentPid = Int32.MaxValue; job.ParentStart = 0; }
                Store.Write(job.Path, job);
                worker = Process.Start(new ProcessStartInfo(Path.Combine(Path.GetDirectoryName(job.Path), "runner.exe"), (interrupted ? "--recover-app-update" : "--apply-app-update") + " \"" + job.Path + "\" " + job.Token) { UseShellExecute = false, CreateNoWindow = true });
                if (parent != null)
                {
                    Thread.Sleep(400); Assert(!worker.HasExited && File.ReadAllText(Path.Combine(app, "VERSION")).Trim() == "1.0.0", "The separate update helper waits for the old application to close before replacing files");
                    File.WriteAllText(signal, "exit"); Assert(parent.WaitForExit(5000), "The old application closes normally before installation");
                }
                Assert(worker.WaitForExit(25000), "The real update helper and restart finish within the bounded test window");
                var outcome = AutomaticUpdate.LastOutcome(app);
                Assert(worker.ExitCode == 0 && outcome != null && outcome.Status == (rollback ? "rolled-back" : "updated") && File.ReadAllText(Path.Combine(app, "VERSION")).Trim() == (rollback ? "1.0.0" : "1.0.1"), rollback ? "A failing new executable automatically restores and starts the previous version" : "The new executable starts and acknowledges readiness before the update is committed");
                var wait = Stopwatch.StartNew(); while (Directory.Exists(Path.GetDirectoryName(job.Path)) && wait.Elapsed < TimeSpan.FromSeconds(8)) Thread.Sleep(100);
                Assert(!Directory.Exists(Path.GetDirectoryName(job.Path)), "After restart the owned ZIP, staging, backup and helper directory are all removed");
            }
            finally
            {
                if (parent != null) { if (!parent.HasExited) { File.WriteAllText(signal, "exit"); parent.WaitForExit(5000); } parent.Dispose(); }
                if (worker != null) { if (!worker.HasExited) { worker.Kill(); worker.WaitForExit(5000); } worker.Dispose(); }
                if (File.Exists(signal)) File.Delete(signal);
            }
        }
        static void AutomaticUpdateChecks(string temp)
        {
            var old = RuntimeFixture("1.0.0", UpdateStub(temp, "1.0.0", false));
            var next = RuntimeFixture("1.0.1", UpdateStub(temp, "1.0.1", false));
            var broken = RuntimeFixture("1.0.1", UpdateStub(temp, "1.0.1", true));
            string app = InstallFixture(temp, old);
            try
            {
                string before = UpdateSnapshot(app); var job = PrepareFixture(app, next);
                Assert(job.Phase == "Ready" && !File.Exists(Path.Combine(Path.GetDirectoryName(job.Path), "payload.zip")) && UpdateSnapshot(app) == before, "Background preparation deletes the ZIP immediately and leaves installed files and student data untouched");
                Assert(PrepareFixture(app, next).Id == job.Id, "A prepared update is reused across checks without accumulating duplicate packages");
                bool failed = false; try { AutomaticUpdate.Apply(job, count => { if (count == 4) throw new IOException("Synthetic replacement fault"); }); } catch (IOException) { failed = true; }
                var persisted = AutomaticUpdate.Read(job.Path); Assert(failed && persisted.Files.Count(f => f.Started) == 4, "The durable journal records each file before replacing it, including a mid-installation fault");
                AutomaticUpdate.Rollback(persisted); AutomaticUpdate.Cleanup(persisted);
                Assert(UpdateSnapshot(app) == before && !Directory.Exists(Path.GetDirectoryName(job.Path)), "A mid-copy failure restores every original byte, removes newly added runtime files, and cleans its backup");
                job = PrepareFixture(app, next); File.AppendAllText(Path.Combine(Path.GetDirectoryName(job.Path), "staged", "README.md"), "tampered");
                bool refused = false; try { AutomaticUpdate.Apply(job); } catch (IOException) { refused = true; }
                Assert(refused && UpdateSnapshot(app) == before, "A staged file changed after download cannot be installed"); AutomaticUpdate.Cleanup(job);
                var cancellation = new CancellationTokenSource(); next.Chunk = () => cancellation.Cancel();
                Assert(UpdateFails(() => AutomaticUpdate.Prepare(new UpdateService(app, "1.0.0", next), UpdateService.Parse(Store.Json.Serialize(next.Release)), app, "1.0.0", cancellation.Token, null).ContinueWith(t => { return t.GetAwaiter().GetResult().Path; })) && !Directory.GetDirectories(Path.Combine(UpdateService.CacheDirectory(app), "automatic")).Any(), "Cancelled background preparation removes the complete owned download directory"); next.Chunk = null;
                var mismatched = RuntimeFixture("1.0.1", UpdateStub(temp, "1.0.2", false));
                bool wrong = false; try { PrepareFixture(app, mismatched); } catch (InvalidOperationException) { wrong = true; }
                Assert(wrong && UpdateSnapshot(app) == before, "An executable with a different product version is rejected before installation");
                job = PrepareFixture(app, next); bool writeBlocked = false;
                using (var locked = new FileStream(Path.Combine(app, "README.md"), FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    try { AutomaticUpdate.Apply(job); } catch (IOException) { writeBlocked = true; }
                    Assert(writeBlocked && !AutomaticUpdate.Read(job.Path).Files.Any(f => f.Started) && UpdateSnapshot(app) == before, "A locked runtime file is detected before replacing any installed files");
                }
                AutomaticUpdate.Rollback(job); AutomaticUpdate.Cleanup(job);
                Assert(!UpdateService.RuntimeFile("Native/Licenses/CON.txt") && !UpdateService.RuntimeFile("Schemas/exam.json.") && !UpdateService.RuntimeFile("Native/Licenses/file.txt:stream"), "Runtime paths reject Windows devices, ambiguous trailing dots and alternate data streams");
                job = PrepareFixture(app, next);
                try { AutomaticUpdate.Apply(job, count => { if (count == 5) throw new IOException("Synthetic locked rollback"); }); } catch (IOException) { }
                bool blocked = false;
                using (var locked = new FileStream(Path.Combine(app, "README.md"), FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    try { AutomaticUpdate.Rollback(job); } catch (IOException) { blocked = true; }
                    Assert(blocked && AutomaticUpdate.Read(job.Path).Phase == "RollingBack" && Directory.Exists(Path.Combine(Path.GetDirectoryName(job.Path), "backup")), "If restoration is blocked by a locked file, its journal and backup are retained until recovery can finish");
                }
                AutomaticUpdate.Rollback(AutomaticUpdate.Read(job.Path)); AutomaticUpdate.Cleanup(job);
                Assert(UpdateSnapshot(app) == before, "Retrying an interrupted rollback restores the original runtime and preserves user-owned ZIPs");
                job = PrepareFixture(app, next); AutomaticUpdate.Apply(job);
                bool wrongAck = false; try { AutomaticUpdate.Complete(job.Path, "incorrect-token", app, "1.0.1"); } catch (InvalidOperationException) { wrongAck = true; }
                Assert(wrongAck && File.Exists(job.Path), "Startup readiness cannot be acknowledged with an incorrect transaction token");
                string signal = Path.Combine(temp, "completion-exit-" + Guid.NewGuid().ToString("N"));
                using (var simulatedWorker = Process.Start(new ProcessStartInfo(Path.Combine(app, "Education2k.exe"), "--hold \"" + signal + "\"") { UseShellExecute = false, CreateNoWindow = true }))
                {
                    job.WorkerPid = simulatedWorker.Id; job.WorkerStart = simulatedWorker.StartTime.ToUniversalTime().Ticks; Store.Write(job.Path, job);
                    var completion = AutomaticUpdate.Complete(job.Path, job.Token, app, "1.0.1");
                    Assert(File.ReadAllText(Path.Combine(Path.GetDirectoryName(job.Path), "ready.json")) == job.Token + ":1.0.1" && !completion.IsCompleted, "The application's real completion routine acknowledges readiness and waits for the helper to exit before cleanup");
                    job.Phase = "Committed"; Store.Write(job.Path, job); File.WriteAllText(signal, "exit"); simulatedWorker.WaitForExit(5000);
                    Assert(completion.Wait(5000) && !Directory.Exists(Path.GetDirectoryName(job.Path)), "The application's completion routine removes the unlocked helper, backup and staging files after commit");
                }
                File.Delete(signal);
            }
            finally { RemoveTestUpdateCache(app); }
            foreach (int scenario in new[] { 0, 1, 2 })
            {
                app = InstallFixture(temp, old);
                try
                {
                    string data = PrivateSnapshot(app); var job = PrepareFixture(app, scenario == 1 ? broken : next);
                    if (scenario == 2)
                    {
                        try { AutomaticUpdate.Apply(job, count => { if (count == 5) throw new IOException("Synthetic interrupted helper"); }); } catch (IOException) { }
                        job = AutomaticUpdate.Read(job.Path);
                    }
                    WorkerTest(app, job, scenario != 0, scenario == 2);
                    Assert(PrivateSnapshot(app) == data, "Materials, generated exams, student attempts, drafts, Codex configuration and unmanaged files are byte-for-byte preserved through install or rollback");
                    if (scenario != 0) Assert(!File.Exists(Path.Combine(app, "Schemas", "new-runtime.json")), "Rollback removes runtime files that did not exist in the previous version");
                }
                finally { RemoveTestUpdateCache(app); }
            }
        }
    }
}
