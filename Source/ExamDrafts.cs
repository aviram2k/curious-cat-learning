using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Education2k
{
    public class ExamDraft
    {
        public string Id, Name = "", Description = "", CreatedUtc, UpdatedUtc;
        public int Step;
        [ScriptIgnore] public string Folder;
    }
    public class DraftImportResult
    {
        public List<string> Added = new List<string>(), Existing = new List<string>(), Errors = new List<string>();
    }
    public class ExamDrafts
    {
        readonly Store store;
        readonly object gate = new object();
        public readonly string Root;
        public ExamDrafts(Store store) { this.store = store; Root = Path.Combine(store.Root, "_drafts"); }
        string Folder(string id)
        {
            if (id == null || !Regex.IsMatch(id, "^[a-f0-9]{32}$")) throw new InvalidOperationException("מזהה הטיוטה אינו תקין.");
            string folder = Path.Combine(Root, id); CheckDirectory(folder); return folder;
        }
        static void CheckDirectory(string folder)
        {
            var current = new DirectoryInfo(folder);
            while (current != null)
            {
                if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("תיקיית הטיוטה אינה יכולה להיות קישור לתיקייה אחרת.");
                current = current.Parent;
            }
        }
        static void CheckFile(string path)
        {
            CheckDirectory(Path.GetDirectoryName(path));
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("אין לשמור קובץ טיוטה דרך קישור לקובץ אחר.");
        }
        void Check(ExamDraft draft)
        {
            if (draft == null || !String.Equals(Path.GetFullPath(Folder(draft.Id)), Path.GetFullPath(draft.Folder), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("תיקיית הטיוטה אינה תקינה.");
            if (!Directory.Exists(draft.Folder)) throw new DirectoryNotFoundException("הטיוטה כבר אינה נמצאת בתיקיית הטיוטות. אפשר לפתוח מחדש את אשף ההוספה.");
        }
        public ExamDraft Create()
        {
            lock (gate)
            {
                var draft = new ExamDraft { Id = Guid.NewGuid().ToString("N"), CreatedUtc = DateTime.UtcNow.ToString("o") };
                draft.Folder = Folder(draft.Id); Directory.CreateDirectory(draft.Folder); Save(draft); return draft;
            }
        }
        public void Save(ExamDraft draft)
        {
            lock (gate)
            {
                Check(draft); draft.UpdatedUtc = DateTime.UtcNow.ToString("o");
                CheckFile(Path.Combine(draft.Folder, "_draft.json"));
                Store.Write(Path.Combine(draft.Folder, "_draft.json"), draft);
                // Only the free-form general description becomes the learning description.
                string path = Path.Combine(draft.Folder, "_describe.txt"), text = draft.Description ?? "";
                CheckFile(path);
                if (!File.Exists(path) || File.ReadAllText(path, Encoding.UTF8) != text) File.WriteAllText(path, text, new UTF8Encoding(false));
            }
        }
        public ExamDraft Load(string id)
        {
            lock (gate)
            {
                string folder = Folder(id), path = Path.Combine(folder, "_draft.json"); ExamDraft draft;
                CheckFile(path); CheckFile(path + ".bak");
                try { draft = Store.Read<ExamDraft>(path); }
                catch (Exception e)
                {
                    if (!(e is IOException) && !(e is ArgumentException) && !(e is InvalidOperationException)) throw;
                    draft = Store.Read<ExamDraft>(path + ".bak");
                }
                if (draft == null || draft.Id != id) throw new InvalidOperationException("קובץ הטיוטה אינו מתאים לתיקייה.");
                draft.Folder = folder; Check(draft); return draft;
            }
        }
        public List<ExamDraft> List()
        {
            var drafts = new List<ExamDraft>(); if (!Directory.Exists(Root)) return drafts;
            CheckDirectory(Root);
            foreach (string folder in Directory.GetDirectories(Root).Where(p => Regex.IsMatch(Path.GetFileName(p), "^[a-f0-9]{32}$")))
            {
                try { drafts.Add(Load(Path.GetFileName(folder))); }
                catch (Exception e) { if (!(e is IOException) && !(e is UnauthorizedAccessException) && !(e is ArgumentException) && !(e is InvalidOperationException)) throw; }
            }
            return drafts.OrderByDescending(d => d.UpdatedUtc).ToList();
        }
        public static string CleanTopicName(string name)
        {
            name = (name ?? "").Trim().Normalize(NormalizationForm.FormC);
            if (name.Length == 0 || name.Length > 80 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Any(Char.IsControl) || name.EndsWith(".") || name == "." || name == ".." || Regex.IsMatch(name, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\\..*)?$", RegexOptions.IgnoreCase))
                throw new InvalidOperationException("יש להזין שם מבחן באורך 1 עד 80 תווים, ללא סימנים כמו / או : וללא נקודה בסוף.");
            return name;
        }
        public void ValidateDetails(ExamDraft draft)
        {
            Check(draft); string name = CleanTopicName(draft.Name);
            if (String.IsNullOrWhiteSpace(draft.Description) || draft.Description.Length > 8000) throw new InvalidOperationException("יש לכתוב תיאור כללי קצר של נושא המבחן והחומר. אין צורך לפרט עמודים או פרקים.");
            string destination = Path.Combine(store.Root, "מבחנים", name);
            if (Directory.Exists(destination) || File.Exists(destination)) throw new InvalidOperationException("כבר קיים מבחן בשם הזה. בחרו שם אחר כדי לשמור על המבחן הקיים.");
        }
        public string[] Files(ExamDraft draft) { lock (gate) { Check(draft); return Store.ReadTopic(draft.Folder).Materials; } }
        static string MaterialName(string name)
        {
            if (String.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || name.StartsWith("_") || name.StartsWith("~$") || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith(".")) throw new InvalidOperationException("השם הזה שמור לקבצי המערכת. יש לתת לקובץ החומר שם אחר.");
            return name;
        }
        public async Task<DraftImportResult> Import(ExamDraft draft, IEnumerable<string> paths, CancellationToken cancellation)
        {
            Check(draft); var result = new DraftImportResult(); var sources = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList(); int index = 0;
            foreach (string source in sources)
            {
                cancellation.ThrowIfCancellationRequested(); index++; string temporary = null;
                try
                {
                    Check(draft);
                    if (Directory.Exists(source)) throw new IOException("יש לבחור קבצים ולא תיקיות.");
                    string name = MaterialName(Path.GetFileName(source)); var original = new FileInfo(source);
                    if (!original.Exists) throw new FileNotFoundException("הקובץ לא נמצא.");
                    string target = Path.Combine(draft.Folder, name);
                    int suffix = 2;
                    while (File.Exists(target) || Directory.Exists(target))
                    {
                        CheckFile(target);
                        if (File.Exists(target) && (String.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase) || SourceImages.Digest(source) == SourceImages.Digest(target))) { result.Existing.Add(Path.GetFileName(target)); target = null; break; }
                        target = Path.Combine(draft.Folder, Path.GetFileNameWithoutExtension(name) + " (" + suffix++ + ")" + Path.GetExtension(name));
                    }
                    if (target == null) continue;
                    temporary = Path.Combine(draft.Folder, "_import_" + Guid.NewGuid().ToString("N") + ".tmp");
                    long length = original.Length; DateTime modified = original.LastWriteTimeUtc;
                    WorkFlow.Activity("מעתיקים קובץ " + index + " מתוך " + sources.Count + ": " + WorkFlow.SafeLabel(name), "מעתיקים את חומרי הלימוד לטיוטה.");
                    using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[81920]; int read; long copied = 0; var watch = Stopwatch.StartNew();
                        while ((read = await input.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(false)) > 0)
                        {
                            await output.WriteAsync(buffer, 0, read, cancellation).ConfigureAwait(false); copied += read;
                            if (watch.ElapsedMilliseconds >= 1000) { WorkFlow.Activity("מעתיקים " + WorkFlow.SafeLabel(name) + " · " + (copied / 1048576.0).ToString("0.0") + " MB"); watch.Restart(); }
                        }
                        await output.FlushAsync(cancellation).ConfigureAwait(false);
                    }
                    original.Refresh();
                    if (!original.Exists || original.Length != length || original.LastWriteTimeUtc != modified) throw new IOException("הקובץ השתנה בזמן ההעתקה. יש לנסות להוסיף אותו שוב לאחר השמירה.");
                    cancellation.ThrowIfCancellationRequested(); File.Move(temporary, target); temporary = null; result.Added.Add(Path.GetFileName(target));
                }
                catch (Exception e)
                {
                    if (!(e is IOException) && !(e is UnauthorizedAccessException) && !(e is ArgumentException) && !(e is InvalidOperationException) && !(e is NotSupportedException)) throw;
                    result.Errors.Add(WorkFlow.SafeLabel(Path.GetFileName(source)) + ": " + e.Message);
                }
                finally { if (temporary != null && File.Exists(temporary)) File.Delete(temporary); }
            }
            Save(draft); return result;
        }
        public void Remove(ExamDraft draft, string name)
        {
            lock (gate)
            {
                Check(draft); MaterialName(name); string path = Path.Combine(draft.Folder, name);
                if (!Files(draft).Contains(path, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("הקובץ אינו חלק מחומרי הטיוטה.");
                string removed = Path.Combine(draft.Folder, "_removed"); CheckDirectory(removed); Directory.CreateDirectory(removed);
                File.Move(path, Path.Combine(removed, Guid.NewGuid().ToString("N") + "-" + name)); Save(draft);
            }
        }
        public async Task<Topic> Complete(ExamDraft draft, ExamService exams, CancellationToken cancellation, Func<MaterialReview, MaterialDecision> confirm)
        {
            lock (gate)
            {
                ValidateDetails(draft); Save(draft);
                if (Files(draft).Length == 0) throw new InvalidOperationException("צריך להוסיף לפחות קובץ אחד של חומר לימוד לפני יצירת המבחן.");
                var source = Store.ReadTopic(draft.Folder); string existing = store.ExamPath(source, 1);
                if (File.Exists(existing) && Store.Read<Exam>(existing).MaterialHash != MaterialReader.Hash(source))
                {
                    // Draft questions have no student references. Preserve an older draft
                    // privately instead of reusing questions from changed learning material.
                    string previous = Path.Combine(draft.Folder, "_previous"); CheckDirectory(previous); Directory.CreateDirectory(previous);
                    File.Move(existing, Path.Combine(previous, "exam-" + Guid.NewGuid().ToString("N") + ".json"));
                }
            }
            var exam = await exams.GetExam(Store.ReadTopic(draft.Folder), 1, cancellation, confirm);
            cancellation.ThrowIfCancellationRequested();
            lock (gate)
            {
                ValidateDetails(draft); var source = Store.ReadTopic(draft.Folder);
                if (exam.MaterialHash != MaterialReader.Hash(source)) throw new InvalidOperationException("חומרי הלימוד השתנו לאחר יצירת השאלות. הטיוטה נשמרה; יש לנסות שוב כדי להכין מבחן מהחומר המעודכן.");
                store.LoadExam(source, 1); // Validate the saved questions and referenced images.
                string destination = Path.Combine(store.Root, "מבחנים", CleanTopicName(draft.Name));
                CheckDirectory(Path.GetDirectoryName(destination)); cancellation.ThrowIfCancellationRequested();
                Directory.Move(draft.Folder, destination);
                WorkFlow.Activity("מבחן מספר 1 מוכן ונוסף לרשימת המבחנים: " + draft.Name);
                return Store.ReadTopic(destination);
            }
        }
    }
}
