using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Education2k
{
    public static partial class Tests
    {
        sealed class UpdateFixture : IUpdateFallbackTransport
        {
            public readonly List<string> Requests = new List<string>();
            public UpdateService.ReleaseInfo Release;
            public byte[] Package;
            public string Checksum;
            public bool Offline, FailDownload, RateLimited;
            public Action Chunk;
            public UpdateFixture(string version, string extra = null, string packageVersion = null)
            {
                using (var data = new MemoryStream())
                {
                    using (var archive = new ZipArchive(data, ZipArchiveMode.Create, true))
                    {
                        string prefix = "CuriousCatLearning-" + version + "-windows-x64/";
                        foreach (string name in new[] { "Education2k.exe", "Education2k.exe.config", "VERSION", "VERSION_DATE", "README.md", "CHANGELOG.md", "LICENSE", "Schemas/exam.json", "Native/pdfium.dll" })
                        {
                            var entry = archive.CreateEntry(prefix + name); using (var output = new StreamWriter(entry.Open())) output.Write(name == "VERSION" ? packageVersion ?? version : "Synthetic update test fixture. Not an executable.");
                        }
                        if (extra != null) using (var output = new StreamWriter(archive.CreateEntry(prefix + extra).Open())) output.Write("Synthetic test content");
                    }
                    Package = data.ToArray();
                }
                string nameZip = "CuriousCatLearning-" + version + "-windows-x64.zip", digest;
                using (var sha = SHA256.Create()) digest = UpdateService.Hex(sha.ComputeHash(Package));
                Checksum = digest + "  " + nameZip + "\n";
                string prefixUrl = "https://github.com/" + UpdateService.Project + "/releases/download/v" + version + "/";
                Release = new UpdateService.ReleaseInfo { tag_name = "v" + version, assets = new List<UpdateService.ReleaseAsset> {
                    new UpdateService.ReleaseAsset { name = nameZip, size = Package.Length, state = "uploaded", digest = "sha256:" + digest, browser_download_url = prefixUrl + nameZip },
                    new UpdateService.ReleaseAsset { name = "SHA256SUMS.txt", size = Encoding.UTF8.GetByteCount(Checksum), state = "uploaded", browser_download_url = prefixUrl + "SHA256SUMS.txt" }
                } };
            }
            public async Task Copy(Uri uri, Stream output, long limit, CancellationToken cancellation, IProgress<long> progress)
            {
                Requests.Add(uri.AbsoluteUri); await Task.Delay(1, cancellation).ConfigureAwait(false);
                if (Offline) throw new IOException("Synthetic offline response");
                if (RateLimited && uri.AbsoluteUri == UpdateService.LatestUrl) throw new UpdateQuotaException();
                byte[] bytes = uri.AbsoluteUri == UpdateService.LatestUrl ? Encoding.UTF8.GetBytes(Store.Json.Serialize(Release)) : uri.AbsolutePath.EndsWith("SHA256SUMS.txt") ? Encoding.UTF8.GetBytes(Checksum) : Package;
                if (bytes.Length > limit) throw new IOException("Synthetic size limit");
                int half = bytes.Length / 2; await output.WriteAsync(bytes, 0, half, cancellation).ConfigureAwait(false);
                if (uri.AbsolutePath.EndsWith(".zip")) { if (Chunk != null) Chunk(); if (FailDownload) throw new IOException("Synthetic interrupted download"); }
                cancellation.ThrowIfCancellationRequested(); await output.WriteAsync(bytes, half, bytes.Length - half, cancellation).ConfigureAwait(false);
                if (progress != null) progress.Report(bytes.Length);
            }
            public Task<string> PublicRelease(CancellationToken cancellation)
            {
                Requests.Add(UpdateService.PublicLatestUrl); cancellation.ThrowIfCancellationRequested();
                return Task.FromResult(UpdateHttpTransport.PublicMetadata(new Uri("https://github.com/" + UpdateService.Project + "/releases/tag/" + Release.tag_name)));
            }
        }
        static string UpdateSnapshot(string root)
        {
            using (var sha = SHA256.Create()) return String.Join("\n", Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal).Select(p => p.Substring(root.Length) + ":" + UpdateService.Hex(sha.ComputeHash(File.ReadAllBytes(p)))));
        }
        static bool UpdateFails(Func<Task<string>> action)
        {
            try { action().GetAwaiter().GetResult(); return false; } catch (InvalidOperationException) { return true; } catch (IOException) { return true; } catch (OperationCanceledException) { return true; }
        }
        static void UpdateChecks(string temp)
        {
            string app = Path.Combine(temp, "update-app"), cache = Path.Combine(temp, "update-cache"), downloads = Path.Combine(temp, "update-downloads");
            var store = new Store(app); var topic = TestTopic(store, "חומר בדיקה לעדכון", false); var exam = Fixture(); Store.Write(store.ExamPath(topic, 1), exam);
            var student = store.Login("בדיקת", "שימור"); var attempt = Answered(store, topic, exam, false); ExamService.Complete(exam, attempt); store.Save(student, attempt);
            Directory.CreateDirectory(Path.Combine(app, "_drafts", "existing")); File.WriteAllText(Path.Combine(app, "_drafts", "existing", "draft.txt"), "Synthetic saved draft");
            File.WriteAllText(Path.Combine(app, "codex-location.txt"), "Synthetic local configuration"); File.WriteAllText(Path.Combine(app, "VERSION"), "0.1.9");
            Directory.CreateDirectory(downloads); string before = UpdateSnapshot(app); DateTime time = new DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);
            var fixture = new UpdateFixture("0.1.10"); var service = new UpdateService(app, "0.1.9", fixture, cache, () => time);
            var release = service.Check(CancellationToken.None).GetAwaiter().GetResult();
            Assert(release != null && release.Version == "0.1.10" && service.ShouldNotify(release), "Update comparison is numeric, so patch 10 is newer than patch 9");
            var restored = new UpdateService(app, "0.1.9", fixture, cache, () => time.AddHours(2)); restored.Check(CancellationToken.None).GetAwaiter().GetResult();
            Assert(fixture.Requests.Count == 1, "The once-per-day update check persists across application restarts without a GitHub account");
            service.Defer(release); Assert(!service.ShouldNotify(release) && !new UpdateService(app, "0.1.9", fixture, cache, () => time.AddHours(1)).ShouldNotify(release), "Later hides this update for a day, including after restarting");
            time = time.AddDays(1).AddMinutes(1); Assert(service.ShouldNotify(release), "A deferred update can be offered again the following day");
            var installed = new UpdateService(app, "0.1.10", fixture, cache, () => time); Assert(installed.Check(CancellationToken.None).GetAwaiter().GetResult() == null, "An installed or older release never appears as an available update");
            Assert(UpdateService.NoticeVisible(true, false, true) && !UpdateService.NoticeVisible(false, false, true) && !UpdateService.NoticeVisible(true, true, true) && !UpdateService.NoticeVisible(true, false, false), "Update notices are limited to idle home screens and do not interrupt exams or preparation");
            string destination = Path.Combine(downloads, release.PackageName); service.Download(release, destination, CancellationToken.None).GetAwaiter().GetResult();
            Assert(File.ReadAllBytes(destination).SequenceEqual(fixture.Package) && !Directory.GetFiles(downloads, "*.part").Any(), "Download saves only a checksum-verified runtime ZIP and removes its temporary file");
            Assert(UpdateSnapshot(app) == before && store.History(student).Single().Score == attempt.Score && File.ReadAllText(Path.Combine(app, "VERSION")) == "0.1.9", "Checking, deferring and downloading leave all materials, exams, student results, drafts, local configuration and installed version byte-for-byte unchanged");
            Assert(fixture.Requests.All(u => u == UpdateService.LatestUrl || u == release.PackageUrl || u == release.ChecksumUrl), "Update requests contain only fixed public release URLs, with no student names, answers or local file paths");
            int calls = fixture.Requests.Count;
            Assert(UpdateFails(() => service.Download(release, Path.Combine(topic.Folder, "update.zip"), CancellationToken.None)) && UpdateFails(() => service.Download(release, destination, CancellationToken.None)) && fixture.Requests.Count == calls && File.ReadAllBytes(destination).SequenceEqual(fixture.Package), "Downloads cannot write into the application or student-material tree, or overwrite any existing file");
            var badHash = new UpdateFixture("0.1.11"); var badRelease = UpdateService.Parse(Store.Json.Serialize(badHash.Release)); badHash.Package[badHash.Package.Length / 2] ^= 1;
            var hashService = new UpdateService(app, "0.1.9", badHash, Path.Combine(temp, "hash-cache")); string rejected = Path.Combine(downloads, "rejected.zip");
            Assert(UpdateFails(() => hashService.Download(badRelease, rejected, CancellationToken.None)) && !File.Exists(rejected) && !Directory.GetFiles(downloads, "*.part").Any(), "A corrupted download is rejected without publishing a ZIP or retaining a partial file");
            var cancel = new CancellationTokenSource(); fixture.Chunk = () => cancel.Cancel();
            Assert(UpdateFails(() => service.Download(release, rejected, cancel.Token)) && !File.Exists(rejected) && !Directory.GetFiles(downloads, "*.part").Any(), "Cancellation during transfer removes only the new temporary download"); fixture.Chunk = null;
            fixture.FailDownload = true; Assert(UpdateFails(() => service.Download(release, rejected, CancellationToken.None)) && !File.Exists(rejected) && !Directory.GetFiles(downloads, "*.part").Any(), "Interrupted downloads leave no misleading completed package"); fixture.FailDownload = false;
            foreach (var unsafeFixture in new[] { new UpdateFixture("0.1.11", "../escaped.txt"), new UpdateFixture("0.1.11", "תלמידים/profile.json"), new UpdateFixture("0.1.11", null, "0.1.10") })
            {
                var unsafeRelease = UpdateService.Parse(Store.Json.Serialize(unsafeFixture.Release)); var unsafeService = new UpdateService(app, "0.1.9", unsafeFixture, Path.Combine(temp, Guid.NewGuid().ToString("N")));
                Assert(UpdateFails(() => unsafeService.Download(unsafeRelease, rejected, CancellationToken.None)) && !File.Exists(rejected), "A valid checksum cannot authorize traversal, student data or a mismatched version in a runtime package");
            }
            var metadata = new UpdateFixture("0.1.11"); metadata.Release.prerelease = true; Assert(UpdateService.Parse(Store.Json.Serialize(metadata.Release)) == null, "Prerelease updates are ignored");
            metadata.Release.prerelease = false; metadata.Release.draft = true; Assert(UpdateService.Parse(Store.Json.Serialize(metadata.Release)) == null, "Unpublished drafts are ignored");
            metadata.Release.draft = false; metadata.Release.assets[0].browser_download_url = "https://example.com/update.zip"; Assert(UpdateService.Parse(Store.Json.Serialize(metadata.Release)) == null, "Foreign download URLs cannot be taken from release metadata");
            metadata.Release.assets[0].browser_download_url = "https://github.com/another/repo/releases/download/v0.1.11/update.zip"; Assert(UpdateService.Parse(Store.Json.Serialize(metadata.Release)) == null, "Only this project's correctly named Windows package is accepted");
            Assert(UpdateService.Parse("{broken") == null && UpdateService.Parse("null") == null && !UpdateHttpTransport.AllowedAddress(new Uri("http://github.com/")) && !UpdateHttpTransport.AllowedAddress(new Uri("https://github.com@evil.example/")), "Malformed metadata, insecure transport and deceptive host names are rejected");
            var offline = new UpdateFixture("0.1.11") { Offline = true }; string offlineCache = Path.Combine(temp, "offline-cache");
            Assert(new UpdateService(app, "0.1.9", offline, offlineCache, () => time).Check(CancellationToken.None).GetAwaiter().GetResult() == null, "Unavailable GitHub is a quiet background failure rather than a learning error");
            new UpdateService(app, "0.1.9", offline, offlineCache, () => time.AddMinutes(1)).Check(CancellationToken.None).GetAwaiter().GetResult();
            Assert(offline.Requests.Count == 1, "Offline checks are throttled across restarts instead of repeatedly contacting GitHub");
            var limited = new UpdateFixture("0.1.11") { RateLimited = true }; var limitedService = new UpdateService(app, "0.1.9", limited, Path.Combine(temp, "limited-cache"));
            var fallbackRelease = limitedService.Check(CancellationToken.None).GetAwaiter().GetResult();
            Assert(fallbackRelease != null && fallbackRelease.UnknownSize && limited.Requests.SequenceEqual(new[] { UpdateService.LatestUrl, UpdateService.PublicLatestUrl }), "API quota exhaustion falls back to GitHub's public latest-release redirect without a token");
            limitedService.Download(fallbackRelease, Path.Combine(downloads, "fallback.zip"), CancellationToken.None).GetAwaiter().GetResult();
            Assert(File.ReadAllBytes(Path.Combine(downloads, "fallback.zip")).SequenceEqual(limited.Package) && UpdateHttpTransport.PublicMetadata(new Uri("https://github.com/other/repo/releases/tag/v1.0.0")) == null && UpdateHttpTransport.PublicMetadata(new Uri("https://github.com/" + UpdateService.Project + "/releases/tag/v1.0.0-preview")) == null, "Public fallback downloads still require the matching checksum, safe package and exact project release tag");
            Assert(UpdateSnapshot(app) == before, "All failed and cancelled update paths preserve the complete application data tree");
        }
        public static string PrepareUpdatePreview(string root)
        {
            string preview = Path.Combine(root, "Build", "UiAppUpdate-" + Guid.NewGuid().ToString("N"), "app"); Directory.CreateDirectory(preview); return preview;
        }
        public static UpdateService UpdatePreviewService(string root)
        {
            var current = new Version(AppInfo.Version); string next = current.Major + "." + current.Minor + "." + (current.Build + 1);
            return new UpdateService(root, AppInfo.Version, new UpdateFixture(next), Path.Combine(Path.GetDirectoryName(root), "cache"));
        }
        public static int LiveUpdateCheck(string root)
        {
            string temporary = Path.Combine(Path.GetTempPath(), "Education2k-update-check-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
            try
            {
                var service = new UpdateService(root, "0.0.0", null, Path.Combine(temporary, "cache")); var release = service.Check(CancellationToken.None).GetAwaiter().GetResult();
                if (!service.LastCheckSucceeded || release == null) throw new InvalidOperationException("The public update check did not return a validated release: " + service.LastCheckError);
                string downloaded = service.Download(release, Path.Combine(temporary, release.PackageName), CancellationToken.None).GetAwaiter().GetResult();
                Directory.CreateDirectory(Path.Combine(root, "Build")); Store.Write(Path.Combine(root, "Build", "app-update-check.json"), new { Passed = true, PublicRelease = release.Version, Bytes = new FileInfo(downloaded).Length, ChecksumAndArchiveVerified = true, InstalledFilesChanged = false }); return 0;
            }
            finally { if (Path.GetFullPath(temporary).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(temporary).StartsWith("Education2k-update-check-")) Directory.Delete(temporary, true); }
        }
    }
}
