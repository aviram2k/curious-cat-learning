using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Education2k
{
    public interface IUpdateTransport
    {
        Task Copy(Uri address, Stream output, long limit, CancellationToken cancellation, IProgress<long> progress);
    }
    public interface IUpdateFallbackTransport : IUpdateTransport { Task<string> PublicRelease(CancellationToken cancellation); }
    public sealed class UpdateQuotaException : IOException { }
    public sealed class UpdateHttpTransport : IUpdateFallbackTransport
    {
        public async Task Copy(Uri address, Stream output, long limit, CancellationToken cancellation, IProgress<long> progress)
        {
            using (var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false, SslProtocols = SslProtocols.Tls12 })
            using (var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan })
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                timeout.CancelAfter(address.Host == "api.github.com" ? TimeSpan.FromSeconds(10) : TimeSpan.FromMinutes(5));
                for (int redirect = 0; redirect < 6; redirect++)
                {
                    if (!AllowedAddress(address)) throw new InvalidOperationException("כתובת ההורדה אינה כתובת GitHub מאושרת.");
                    using (var request = new HttpRequestMessage(HttpMethod.Get, address))
                    {
                        request.Headers.UserAgent.ParseAdd("CuriousCatLearning/" + AppInfo.Version);
                        request.Headers.Accept.ParseAdd("application/vnd.github+json");
                        if (address.Host == "api.github.com") request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
                        using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                        {
                            int status = (int)response.StatusCode;
                            if (address.AbsoluteUri == UpdateService.LatestUrl && (status == 403 || status == 429)) throw new UpdateQuotaException();
                            if (status == 301 || status == 302 || status == 303 || status == 307 || status == 308)
                            {
                                if (response.Headers.Location == null) throw new IOException("GitHub לא החזיר כתובת הורדה.");
                                address = new Uri(address, response.Headers.Location); continue;
                            }
                            response.EnsureSuccessStatusCode();
                            if (response.Content.Headers.ContentLength > limit) throw new IOException("קובץ העדכון גדול מהצפוי.");
                            using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                            {
                                byte[] buffer = new byte[65536]; long total = 0; int count;
                                while ((count = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) > 0)
                                {
                                    total += count; if (total > limit) throw new IOException("קובץ העדכון גדול מהצפוי.");
                                    await output.WriteAsync(buffer, 0, count, timeout.Token).ConfigureAwait(false); if (progress != null) progress.Report(total);
                                }
                            }
                            return;
                        }
                    }
                }
                throw new IOException("לא ניתן להשלים את ההפניה להורדה מ־GitHub.");
            }
        }
        public static bool AllowedAddress(Uri uri)
        {
            return uri != null && uri.IsAbsoluteUri && uri.Scheme == "https" && uri.Port == 443 && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 &&
                new[] { "api.github.com", "github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com" }.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
        }
        public static string PublicMetadata(Uri target)
        {
            string prefix = "https://github.com/" + UpdateService.Project + "/releases/tag/";
            if (!AllowedAddress(target) || !target.AbsoluteUri.StartsWith(prefix, StringComparison.Ordinal)) return null;
            string tag = target.AbsoluteUri.Substring(prefix.Length); if (!Regex.IsMatch(tag, @"^v?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$")) return null;
            string version = tag.TrimStart('v'), name = "CuriousCatLearning-" + version + "-windows-x64.zip", download = "https://github.com/" + UpdateService.Project + "/releases/download/" + tag + "/";
            return Store.Json.Serialize(new UpdateService.ReleaseInfo { tag_name = tag, public_redirect = true, assets = new List<UpdateService.ReleaseAsset> {
                new UpdateService.ReleaseAsset { name = name, state = "uploaded", browser_download_url = download + name },
                new UpdateService.ReleaseAsset { name = "SHA256SUMS.txt", state = "uploaded", size = 107, browser_download_url = download + "SHA256SUMS.txt" }
            } });
        }
        public async Task<string> PublicRelease(CancellationToken cancellation)
        {
            using (var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false, SslProtocols = SslProtocols.Tls12 })
            using (var client = new HttpClient(handler))
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(10)); Uri address = new Uri(UpdateService.PublicLatestUrl);
                using (var request = new HttpRequestMessage(HttpMethod.Head, address))
                {
                    request.Headers.UserAgent.ParseAdd("CuriousCatLearning/" + AppInfo.Version);
                    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                    {
                        int status = (int)response.StatusCode;
                        if (status < 300 || status > 399 || response.Headers.Location == null) throw new IOException("לא התקבל קישור ציבורי לריליס האחרון.");
                        string json = PublicMetadata(new Uri(address, response.Headers.Location)); if (json == null) throw new IOException("קישור הריליס הציבורי אינו תקין."); return json;
                    }
                }
            }
        }
    }
    public sealed class AppUpdate
    {
        public string Tag, Version, PackageName, PackageUrl, ChecksumUrl, Digest;
        public long Size;
        public bool UnknownSize;
    }
    public sealed class UpdateService
    {
        public const string Project = "aviram2k/curious-cat-learning";
        public const string LatestUrl = "https://api.github.com/repos/" + Project + "/releases/latest";
        public const string PublicLatestUrl = "https://github.com/" + Project + "/releases/latest";
        public const long PackageLimit = 128L * 1024 * 1024;
        readonly string installRoot, installedVersion, cachePath;
        readonly IUpdateTransport transport;
        readonly Func<DateTime> now;
        readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        UpdateCache state = new UpdateCache();
        public bool LastCheckSucceeded { get; private set; }
        public string LastCheckError { get; private set; }
        public sealed class UpdateCache { public string CheckedUtc, ReleaseJson, DeferredVersion, DeferredUntilUtc; }
        public sealed class ReleaseAsset { public string name, browser_download_url, state, digest; public long size; }
        public sealed class ReleaseInfo { public string tag_name; public bool draft, prerelease, public_redirect; public List<ReleaseAsset> assets; }
        public UpdateService(string root, string version, IUpdateTransport transport = null, string cacheDirectory = null, Func<DateTime> clock = null)
        {
            installRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar); installedVersion = version;
            this.transport = transport ?? new UpdateHttpTransport(); now = clock ?? (() => DateTime.UtcNow);
            cachePath = Path.Combine(cacheDirectory ?? CacheDirectory(installRoot), "check.json");
            if (IsWithin(cachePath, installRoot)) throw new InvalidOperationException("מצב העדכונים נשמר בנפרד מתיקיית התוכנה והנתונים.");
            try { if (File.Exists(cachePath) && new FileInfo(cachePath).Length <= 1024 * 1024) state = Store.Read<UpdateCache>(cachePath) ?? new UpdateCache(); } catch (Exception e) { if (!CacheError(e)) throw; }
        }
        static bool CacheError(Exception e) { return e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is InvalidOperationException || e is System.Security.SecurityException; }
        static bool IsWithin(string path, string root) { return String.Equals(path, root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase); }
        public static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
        public static string CacheDirectory(string root)
        {
            string key; using (var sha = SHA256.Create()) key = Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant()))).Substring(0, 24);
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CuriousCatLearning", "Updates", key);
        }
        public static bool RuntimeFile(string name)
        {
            if (String.IsNullOrEmpty(name) || name.Contains('\\') || name.Contains(':') || name.Split('/').Any(p => p.Length == 0 || p == "." || p == ".." || p.EndsWith(".") || p.EndsWith(" ") || Regex.IsMatch(p, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", RegexOptions.IgnoreCase) || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)) return false;
            var parts = name.Split('/');
            return new[] { "Education2k.exe", "Education2k.exe.config", "VERSION", "VERSION_DATE", "README.md", "CHANGELOG.md", "LICENSE" }.Contains(name) ||
                parts.Length == 2 && parts[0] == "Schemas" && Path.GetExtension(name) == ".json" ||
                new[] { "Native/pdfium.dll", "Native/pdfium-version.json", "Native/README.md" }.Contains(name) || parts.Length == 3 && parts[0] == "Native" && parts[1] == "Licenses";
        }
        static Version Number(string text)
        {
            Version value; return text != null && Regex.IsMatch(text, @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$") && Version.TryParse(text, out value) ? value : null;
        }
        static bool AddressMatches(string address, string expected)
        {
            Uri uri; return Uri.TryCreate(address, UriKind.Absolute, out uri) && UpdateHttpTransport.AllowedAddress(uri) && uri.AbsoluteUri == expected;
        }
        public static AppUpdate Parse(string json)
        {
            try
            {
                var release = Store.Json.Deserialize<ReleaseInfo>(json);
                if (release == null || release.draft || release.prerelease || release.tag_name == null || release.assets == null) return null;
                string version = release.tag_name.StartsWith("v", StringComparison.Ordinal) ? release.tag_name.Substring(1) : release.tag_name;
                if (Number(version) == null) return null;
                string name = "CuriousCatLearning-" + version + "-windows-x64.zip";
                var packages = release.assets.Where(a => a != null && a.name == name && a.state == "uploaded").ToList();
                var checksums = release.assets.Where(a => a != null && a.name == "SHA256SUMS.txt" && a.state == "uploaded").ToList();
                if (packages.Count != 1 || checksums.Count != 1) return null;
                var package = packages[0]; var checksum = checksums[0];
                string prefix = "https://github.com/" + Project + "/releases/download/" + release.tag_name + "/";
                if (package.size < 0 || package.size == 0 && !release.public_redirect || package.size > PackageLimit || checksum.size <= 0 || checksum.size > 16384 || !AddressMatches(package.browser_download_url, prefix + name) || !AddressMatches(checksum.browser_download_url, prefix + "SHA256SUMS.txt")) return null;
                if (!String.IsNullOrEmpty(package.digest) && !Regex.IsMatch(package.digest, "^sha256:[a-f0-9]{64}$")) return null;
                return new AppUpdate { Tag = release.tag_name, Version = version, PackageName = name, PackageUrl = package.browser_download_url, ChecksumUrl = checksum.browser_download_url, Size = package.size, Digest = package.digest, UnknownSize = package.size == 0 && release.public_redirect };
            }
            catch (Exception e) { if (!CacheError(e)) throw; return null; }
        }
        AppUpdate Eligible(AppUpdate release) { return release != null && Number(installedVersion) != null && Number(release.Version) != null && Number(release.Version).CompareTo(Number(installedVersion)) > 0 ? release : null; }
        bool SaveState()
        {
            string temporary = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
                File.WriteAllText(temporary, Store.Json.Serialize(state), new UTF8Encoding(false));
                if (File.Exists(cachePath)) File.Replace(temporary, cachePath, null); else File.Move(temporary, cachePath); return true;
            }
            catch (Exception e) { if (!CacheError(e)) throw; return false; }
            finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
        static bool Recent(string text, DateTime time)
        {
            DateTime stamp; return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out stamp) && stamp.ToUniversalTime() <= time && time - stamp.ToUniversalTime() < TimeSpan.FromDays(1);
        }
        public async Task<AppUpdate> Check(CancellationToken cancellation)
        {
            await gate.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                var cached = Eligible(Parse(state.ReleaseJson)); DateTime time = now();
                if (Recent(state.CheckedUtc, time)) return cached;
                state.CheckedUtc = time.ToString("o"); if (!SaveState()) return cached;
                try
                {
                    using (var memory = new MemoryStream())
                    {
                        string json = null; bool limited = false;
                        try { await transport.Copy(new Uri(LatestUrl), memory, 512 * 1024, cancellation, null).ConfigureAwait(false); json = Encoding.UTF8.GetString(memory.ToArray()); }
                        catch (UpdateQuotaException) { limited = true; }
                        if (limited) { var fallback = transport as IUpdateFallbackTransport; if (fallback == null) throw new UpdateQuotaException(); json = await fallback.PublicRelease(cancellation).ConfigureAwait(false); }
                        var release = Parse(json);
                        if (release == null) { LastCheckError = "The public release did not contain a valid Windows package and checksum."; return cached; }
                        state.ReleaseJson = json; LastCheckSucceeded = true; SaveState(); return Eligible(release);
                    }
                }
                catch (Exception e)
                {
                    if (!(e is IOException) && !(e is HttpRequestException) && !(e is OperationCanceledException) && !(e is WebException)) throw;
                    LastCheckError = e.ToString(); if (cancellation.IsCancellationRequested) throw new OperationCanceledException(cancellation); return cached;
                }
            }
            finally { gate.Release(); }
        }
        public bool ShouldNotify(AppUpdate release)
        {
            DateTime until; return Eligible(release) != null && !(state.DeferredVersion == release.Version && DateTime.TryParse(state.DeferredUntilUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out until) && until.ToUniversalTime() > now());
        }
        public void Defer(AppUpdate release) { state.DeferredVersion = release.Version; state.DeferredUntilUtc = now().AddDays(1).ToString("o"); SaveState(); }
        public static bool NoticeVisible(bool home, bool busy, bool eligible) { return home && !busy && eligible; }
        public void ValidateDestination(string destination)
        {
            string full = Path.GetFullPath(destination);
            if (!String.Equals(Path.GetExtension(full), ".zip", StringComparison.OrdinalIgnoreCase) || IsWithin(full, installRoot)) throw new InvalidOperationException("יש לשמור את קובץ ה־ZIP מחוץ לתיקיית התוכנה, למשל בתיקיית ההורדות.");
            if (File.Exists(full) || Directory.Exists(full)) throw new InvalidOperationException("כבר קיים קובץ בשם הזה. בחרו שם חדש כדי לשמור על הקובץ הקיים.");
            var folder = new DirectoryInfo(Path.GetDirectoryName(full)); if (!folder.Exists) throw new InvalidOperationException("יש לבחור תיקיית הורדה קיימת.");
            while (folder != null) { if ((folder.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("יש לבחור תיקייה רגילה להורדה, ללא קישור לתיקייה אחרת."); folder = folder.Parent; }
        }
        static string ExpectedHash(string text, string name)
        {
            var values = text.Split('\n').Select(l => Regex.Match(l.Trim(), @"^([a-fA-F0-9]{64})\s+\*?(.+)$")).Where(m => m.Success && m.Groups[2].Value == name).ToList();
            if (values.Count != 1) throw new InvalidOperationException("קובץ האימות אינו מכיל חתימה יחידה לחבילת העדכון."); return values[0].Groups[1].Value.ToLowerInvariant();
        }
        public async Task<string> Download(AppUpdate release, string destination, CancellationToken cancellation, IProgress<long> progress = null)
        {
            // Revalidate mutable/cache metadata before allowing any file download.
            var canonical = Parse(Store.Json.Serialize(new ReleaseInfo { tag_name = release.Tag, public_redirect = release.UnknownSize, assets = new List<ReleaseAsset> {
                new ReleaseAsset { name = release.PackageName, browser_download_url = release.PackageUrl, state = "uploaded", size = release.Size, digest = release.Digest },
                new ReleaseAsset { name = "SHA256SUMS.txt", browser_download_url = release.ChecksumUrl, state = "uploaded", size = 107 }
            } }));
            if (canonical == null || canonical.Version != release.Version || Eligible(canonical) == null) throw new InvalidOperationException("פרטי העדכון אינם תקינים.");
            ValidateDestination(destination); cancellation.ThrowIfCancellationRequested();
            string temporary = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(destination)), ".CuriousCatLearning-" + Guid.NewGuid().ToString("N") + ".part");
            try
            {
                string expected;
                using (var checksum = new MemoryStream()) { await transport.Copy(new Uri(canonical.ChecksumUrl), checksum, 16384, cancellation, null).ConfigureAwait(false); expected = ExpectedHash(Encoding.UTF8.GetString(checksum.ToArray()), canonical.PackageName); }
                if (!String.IsNullOrEmpty(canonical.Digest) && canonical.Digest != "sha256:" + expected) throw new InvalidOperationException("קובץ האימות אינו תואם לפרטי הריליס. ההורדה נעצרה.");
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                {
                    await transport.Copy(new Uri(canonical.PackageUrl), output, canonical.UnknownSize ? PackageLimit : canonical.Size, cancellation, progress).ConfigureAwait(false);
                    await output.FlushAsync(cancellation).ConfigureAwait(false);
                }
                await Task.Run(delegate {
                    cancellation.ThrowIfCancellationRequested();
                    using (var input = File.OpenRead(temporary)) using (var sha = SHA256.Create()) if (!canonical.UnknownSize && input.Length != canonical.Size || Hex(sha.ComputeHash(input)) != expected) throw new InvalidOperationException("בדיקת שלמות העדכון נכשלה. הקובץ החלקי לא נשמר.");
                    ValidatePackage(temporary, canonical.Version); cancellation.ThrowIfCancellationRequested();
                }, cancellation).ConfigureAwait(false);
                ValidateDestination(destination); cancellation.ThrowIfCancellationRequested(); File.Move(temporary, Path.GetFullPath(destination)); return Path.GetFullPath(destination);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public static void ValidatePackage(string path, string version)
        {
            string prefix = "CuriousCatLearning-" + version + "-windows-x64/";
            var rootFiles = new[] { "Education2k.exe", "Education2k.exe.config", "VERSION", "VERSION_DATE", "README.md", "CHANGELOG.md", "LICENSE" };
            using (var archive = ZipFile.OpenRead(path))
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long size = 0;
                if (archive.Entries.Count > 1000) throw new InvalidOperationException("מבנה חבילת העדכון אינו מוכר.");
                foreach (var entry in archive.Entries)
                {
                    string name = entry.FullName;
                    if (!name.StartsWith(prefix, StringComparison.Ordinal) || name.Contains('\\') || name.Contains(':') || name.Split('/').Any(s => s == "." || s == "..")) throw new InvalidOperationException("חבילת העדכון מכילה נתיב שאינו מאושר.");
                    name = name.Substring(prefix.Length); if (name.Length == 0 || name.EndsWith("/")) continue;
                    size += entry.Length;
                    if (!RuntimeFile(name) || !names.Add(name) || size > 256L * 1024 * 1024 || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidOperationException("החבילה כוללת תוכן שאינו שייך לקובצי התוכנה. העדכון לא נשמר.");
                }
                if (!rootFiles.All(names.Contains) || !names.Contains("Native/pdfium.dll") || !names.Contains("Schemas/exam.json")) throw new InvalidOperationException("בחבילת העדכון חסרים קובצי תוכנה.");
                var versionEntry = archive.GetEntry(prefix + "VERSION"); if (versionEntry == null || versionEntry.Length > 64) throw new InvalidOperationException("חסרה גרסה בחבילת העדכון.");
                using (var input = new StreamReader(versionEntry.Open())) if (input.ReadToEnd().Trim() != version) throw new InvalidOperationException("גרסת החבילה אינה תואמת לריליס.");
            }
        }
    }
}
