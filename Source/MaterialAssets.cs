using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace Education2k
{
    public class SourceVisual
    {
        public string Id, FileName, Label, RelativePath, ImageHash, Text, ParentId;
        public int PageNumber;
        public bool OriginalsChecked;
        public VisualReading Reading;
    }
    public class VisualReading
    {
        public string Id, Status, Text, Description, Caption, Issue;
        public bool ExamSuitable;
    }
    public class VisualResponse { public List<VisualReading> Items = new List<VisualReading>(); }
    public class VisualCache { public int Version; public string ImageHash; public VisualReading Reading; }
    public class PreparedMaterial
    {
        public int Version;
        public string Key;
        public string Limitation;
        public List<SourceVisual> Visuals = new List<SourceVisual>();
    }
    public static class MaterialAssets
    {
        public const int Version = 2;
        public static List<SourceVisual> OriginalPictures(Topic topic, string file, SourceVisual parent)
        {
            var result = new List<SourceVisual>(); string folder = Path.GetDirectoryName(SourceImages.Resolve(topic, parent.RelativePath));
            using (var zip = ZipFile.OpenRead(file))
            {
                string slidePath = MaterialReader.SlidePaths(zip)[parent.PageNumber - 1];
                var entry = zip.GetEntry(MaterialReader.RelationshipPath(slidePath)); if (entry == null) return result;
                var rels = MaterialReader.ReadXml(zip, entry.FullName).Root.Elements().ToDictionary(e => (string)e.Attribute("Id"));
                XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main", r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
                var slide = MaterialReader.ReadXml(zip, slidePath); int number = 0;
                foreach (var blip in slide.Descendants(a + "blip"))
                {
                    XElement rel; if (!rels.TryGetValue((string)blip.Attribute(r + "embed") ?? "", out rel) || (string)rel.Attribute("TargetMode") == "External") continue;
                    var image = zip.GetEntry(MaterialReader.ResolvePart(slidePath, (string)rel.Attribute("Target"))); if (image == null) continue;
                    number++; if (number > 12) throw new InvalidDataException("יש יותר מ־12 תמונות בשקופית. יש לייצא את התמונות החשובות בנפרד.");
                    BitmapFrame frame;
                    using (var input = image.Open()) using (var memory = new MemoryStream())
                    {
                        input.CopyTo(memory); memory.Position = 0;
                        frame = BitmapFrame.Create(memory, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                        frame.Freeze();
                    }
                    string name = String.Format("slide-{0:D4}-picture-{1:D2}.png", parent.PageNumber, number), target = Path.Combine(folder, name);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(frame); using (var output = File.Create(target)) encoder.Save(output);
                    result.Add(new SourceVisual { Id = parent.Id + "-image" + number, ParentId = parent.Id, FileName = parent.FileName, Label = "תמונת מקור " + number + " מתוך " + parent.Label, PageNumber = parent.PageNumber, RelativePath = parent.RelativePath.Substring(0, parent.RelativePath.LastIndexOf('/') + 1) + name, ImageHash = SourceImages.Digest(target), Text = "תמונה מקורית מתוך המצגת, ללא אובייקטים שהונחו מעליה בשקופית." });
                }
            }
            return result;
        }
        public static void RememberPictures(Topic topic, SourceVisual parent, IList<SourceVisual> pictures)
        {
            string manifest = Path.Combine(Path.GetDirectoryName(SourceImages.Resolve(topic, parent.RelativePath)), "document.json");
            var document = Store.Read<PreparedMaterial>(manifest);
            document.Visuals.Single(v => v.Id == parent.Id).OriginalsChecked = true;
            foreach (var picture in pictures)
            {
                int index = document.Visuals.FindIndex(v => v.Id == picture.Id);
                if (index < 0) document.Visuals.Add(picture); else document.Visuals[index] = picture;
            }
            Store.Write(manifest, document);
        }
        public static void PrepareSnapshot(Topic topic, MaterialSnapshot snapshot, CancellationToken cancellation)
        {
            foreach (var file in snapshot.Files.Where(f => Supported(f.Path)))
            {
                cancellation.ThrowIfCancellationRequested();
                string extension = Path.GetExtension(file.Path).ToLowerInvariant();
                if (extension == ".docx" && file.Status == "read") continue;
                try
                {
                    var prepared = Prepare(topic, file.Path, cancellation); file.Visuals = prepared.Visuals;
                    file.LocalLimitation = prepared.Limitation ?? "";
                    file.CanResolveVisuals = String.IsNullOrEmpty(prepared.Limitation) && !file.Detail.Contains("גדול מדי") && file.Status != "unread";
                    // A scanned PDF/image has no local text, but all of its content is available in the rendered pages.
                    if (extension == ".pdf" || new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".gif" }.Contains(extension)) file.CanResolveVisuals = String.IsNullOrEmpty(prepared.Limitation) && !file.Detail.Contains("גדול מדי");
                    file.Status = "partial"; file.MustWarn = true; file.Detail += " הוכנו " + file.Visuals.Count + " תמונות מקור לפענוח חזותי. " + file.LocalLimitation;
                }
                catch (Exception e)
                {
                    if (!(e is IOException) && !(e is UnauthorizedAccessException) && !(e is ArgumentException) && !(e is InvalidOperationException) && !(e is System.Runtime.InteropServices.ExternalException) && !(e is NotSupportedException)) throw;
                    file.MustWarn = true; file.Status = String.IsNullOrWhiteSpace(file.Text) ? "unread" : "partial"; file.Detail += " הכנת התצוגה החזותית נכשלה: " + e.Message;
                }
            }
        }
        public static bool Supported(string file) { return new[] { ".pdf", ".pptx", ".docx", ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".gif" }.Contains(Path.GetExtension(file).ToLowerInvariant()); }
        public static PreparedMaterial Prepare(Topic topic, string file, CancellationToken cancellation)
        {
            string key;
            using (var hash = SHA256.Create()) key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFileName(file) + "|" + SourceImages.Digest(file)))).Replace("-", "").ToLowerInvariant();
            string relative = "_materials/v" + Version + "/" + key, folder = Path.Combine(topic.Folder, relative.Replace('/', Path.DirectorySeparatorChar));
            string manifest = Path.Combine(folder, "document.json");
            if (File.Exists(manifest))
            {
                try
                {
                    var cached = Store.Read<PreparedMaterial>(manifest);
                    if (cached.Version == Version && cached.Key == key && cached.Visuals.Count > 0 && cached.Visuals.All(v => SourceImages.Valid(topic, v)))
                    { WorkFlow.Activity("תמונות המקור כבר מוכנות: " + Path.GetFileName(file)); return cached; }
                }
                catch (Exception e) { if (!(e is IOException) && !(e is ArgumentException) && !(e is InvalidOperationException)) throw; }
            }
            Directory.CreateDirectory(folder); string extension = Path.GetExtension(file).ToLowerInvariant();
            var result = new PreparedMaterial { Version = Version, Key = key };
            if (extension == ".pdf" || extension == ".pptx" || extension == ".docx")
            {
                string pdf = file;
                if (extension != ".pdf")
                {
                    WorkFlow.Activity("מכינים תצוגה מלאה לקריאה: " + Path.GetFileName(file), "ממתינים לייצוא מקומי של המסמך, כולל התמונות והתרשימים.");
                    pdf = Path.Combine(folder, "rendered.pdf"); OfficeRender.Export(file, pdf, cancellation);
                }
                result.Visuals = PdfDocument.Read(pdf, folder, relative, Path.GetFileName(file), key, extension == ".pptx", cancellation);
                if (extension != ".pdf" && File.Exists(pdf)) File.Delete(pdf);
            }
            else
            {
                BitmapFrame frame;
                using (var input = File.OpenRead(file)) frame = BitmapFrame.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(frame);
                string target = Path.Combine(folder, "page-0001.png"); using (var output = File.Create(target)) encoder.Save(output);
                result.Visuals.Add(new SourceVisual { Id = key + "-p1", FileName = Path.GetFileName(file), Label = "תמונה", PageNumber = 1, RelativePath = relative + "/page-0001.png", Text = "", ImageHash = SourceImages.Digest(target) });
                // Multi-frame scans must not silently lose the remaining pages.
                using (var input = File.OpenRead(file)) if (BitmapDecoder.Create(input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames.Count > 1) result.Limitation = "בקובץ תמונה מרובה עמודים נקרא רק העמוד הראשון. יש לייצא את כל העמודים ל־PDF.";
            }
            cancellation.ThrowIfCancellationRequested(); Store.Write(manifest, result); return result;
        }
    }
    public static class OfficeRender
    {
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        public static void Export(string input, string output, CancellationToken cancellation)
        {
            using (var child = Process.Start(new ProcessStartInfo {
                FileName = typeof(Program).Assembly.Location, Arguments = "--render-office " + CodexClient.Quote(input) + " " + CodexClient.Quote(output),
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
            }))
            {
                try
                {
                    var watch = Stopwatch.StartNew();
                    while (!child.WaitForExit(150))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (watch.Elapsed.TotalSeconds > 120) throw new IOException("ייצוא המסמך נמשך זמן רב מדי. סגרו חלונות אישור של Office או שמרו את הקובץ כ־PDF ונסו שוב.");
                    }
                    cancellation.ThrowIfCancellationRequested();
                    if (child.ExitCode != 0 || !File.Exists(output))
                    {
                        string details = File.Exists(output + ".error") ? File.ReadAllText(output + ".error") : "לא התקבלה תצוגה של המסמך.";
                        throw new IOException("לא ניתן לייצא את המסמך לתמונות. נדרשת התקנת " + (Path.GetExtension(input).Equals(".pptx", StringComparison.OrdinalIgnoreCase) ? "PowerPoint" : "Word") + ". אפשר גם לשמור את המקור כ־PDF ולצרף אותו. " + details);
                    }
                }
                finally
                {
                    if (!child.HasExited)
                    {
                        try { child.Kill(); } catch (InvalidOperationException) { }
                        // Only an Office process created by this helper may be stopped.
                        string ownerFile = output + ".owner";
                        if (File.Exists(ownerFile))
                        {
                            try { var owner = File.ReadAllText(ownerFile).Split('|'); using (var office = Process.GetProcessById(Int32.Parse(owner[0]))) if (office.StartTime.ToUniversalTime().Ticks.ToString() == owner[1] && (office.ProcessName == "POWERPNT" || office.ProcessName == "WINWORD")) office.Kill(); }
                            catch (ArgumentException) { } catch (InvalidOperationException) { }
                        }
                    }
                    foreach (var temporary in new[] { output + ".owner", output + ".error" }) if (File.Exists(temporary)) File.Delete(temporary);
                }
            }
        }
        // Runs in an isolated STA helper. An existing Office process is never quit.
        public static int Worker(string input, string output)
        {
            object app = null, document = null;
            bool ownsProcess = false, slides = Path.GetExtension(input).Equals(".pptx", StringComparison.OrdinalIgnoreCase);
            object oldSecurity = null, oldAlerts = null, oldLinks = null;
            try
            {
                var before = Process.GetProcessesByName(slides ? "POWERPNT" : "WINWORD").Select(p => { int id = p.Id; p.Dispose(); return id; }).ToList();
                var type = Type.GetTypeFromProgID(slides ? "PowerPoint.Application" : "Word.Application");
                if (type == null) throw new IOException("יישום Office המתאים אינו מותקן.");
                app = Activator.CreateInstance(type); dynamic office = app;
                uint pid = 0;
                try { GetWindowThreadProcessId(new IntPtr((long)office.HWND), out pid); } catch (Exception) { }
                // PowerPoint may not expose HWND while running without a document window.
                if (pid == 0)
                {
                    var created = Process.GetProcessesByName(slides ? "POWERPNT" : "WINWORD").Select(p => { int id = p.Id; p.Dispose(); return id; }).Where(id => !before.Contains(id)).ToList();
                    if (created.Count == 1) pid = (uint)created[0];
                }
                ownsProcess = pid != 0 && !before.Contains((int)pid);
                if (ownsProcess) using (var process = Process.GetProcessById((int)pid)) File.WriteAllText(output + ".owner", pid + "|" + process.StartTime.ToUniversalTime().Ticks);
                oldSecurity = office.AutomationSecurity; oldAlerts = office.DisplayAlerts;
                office.AutomationSecurity = 3; // msoAutomationSecurityForceDisable
                if (slides)
                {
                    office.DisplayAlerts = 1; // ppAlertsNone
                    document = office.Presentations.Open(input, -1, 0, 0);
                    ((dynamic)document).SaveAs(output, 32, 0); // ppSaveAsPDF, no font embedding
                }
                else
                {
                    if (ownsProcess) office.Visible = false; office.DisplayAlerts = 0;
                    oldLinks = office.Options.UpdateLinksAtOpen;
                    office.Options.UpdateLinksAtOpen = false;
                    document = office.Documents.Open(FileName: input, ConfirmConversions: false, ReadOnly: true, AddToRecentFiles: false, Visible: false, OpenAndRepair: false);
                    ((dynamic)document).ExportAsFixedFormat(output, 17);
                }
                return 0;
            }
            catch (Exception e) { File.WriteAllText(output + ".error", e.Message, Encoding.UTF8); return 1; }
            finally
            {
                if (document != null) { try { if (slides) ((dynamic)document).Close(); else ((dynamic)document).Close(0); } catch { } Marshal.FinalReleaseComObject(document); }
                if (app != null)
                {
                    try { if (oldSecurity != null) ((dynamic)app).AutomationSecurity = oldSecurity; if (oldAlerts != null) ((dynamic)app).DisplayAlerts = oldAlerts; if (oldLinks != null) ((dynamic)app).Options.UpdateLinksAtOpen = oldLinks; if (ownsProcess) ((dynamic)app).Quit(); } catch { }
                    Marshal.FinalReleaseComObject(app);
                }
            }
        }
    }
}
