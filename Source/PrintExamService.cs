using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Education2k
{
    public class PrintExamResult
    {
        public string Folder, PdfPath;
        public ExamPaper Paper;
    }
    public class PrintExamService
    {
        readonly Store store;
        public readonly ExamDrafts Drafts;
        public string Root { get { return Path.Combine(store.Root, "מבחנים", Store.PrintFolderName); } }
        public PrintExamService(Store store) { this.store = store; Drafts = new ExamDrafts(store, true); }
        public void Save(ExamDraft draft)
        {
            draft.Description = "שם המבחן: " + draft.Name + "\nכיתה: " + draft.Classroom + "\nבית ספר: " + draft.School + "\nהכן מבחן בעברית מתוך חומרי הלימוד המצורפים, המותאם לכיתה. אין להשלים היקף מספר שלא צורף.";
            Drafts.Save(draft);
        }
        static Task<T> Sta<T>(Func<T> action, CancellationToken cancellation)
        {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously); var context = ExecutionContext.Capture();
            var thread = new Thread(delegate() {
                Action run = delegate {
                    // Flow activity progress, but never impersonate the UI synchronization
                    // context on the layout thread. UI continuations must post back to WPF.
                    SynchronizationContext.SetSynchronizationContext(null);
                    try { cancellation.ThrowIfCancellationRequested(); completion.TrySetResult(action()); } catch (OperationCanceledException) { completion.TrySetCanceled(); } catch (Exception e) { completion.TrySetException(e); }
                };
                if (context == null) run(); else ExecutionContext.Run(context, delegate { run(); }, null);
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
        }
        void CheckPath(string path)
        {
            string root = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("קובצי ההדפסה חייבים להישאר בתיקיית המבחנים להדפסה.");
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("אין לשמור PDF דרך קישור לקובץ אחר.");
            var folder = new DirectoryInfo(Path.GetDirectoryName(path));
            while (folder != null)
            {
                if (folder.Exists && (folder.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("אין לשמור PDF דרך קישור לתיקייה אחרת."); folder = folder.Parent;
            }
        }
        public async Task<PrintExamResult> Create(ExamDraft draft, ExamService builder, CancellationToken cancellation, Func<MaterialReview, MaterialDecision> confirm)
        {
            var details = PrintExamDetails.From(draft); details.Validate(); draft.Name = details.Name; draft.Classroom = details.Classroom; draft.School = details.School; Save(draft);
            var exam = await Drafts.Prepare(draft, builder, cancellation, confirm); var topic = Store.ReadTopic(draft.Folder);
            WorkFlow.Start("save", "מעצבים את קובץ ה־PDF", "מכינים עמודי שאלות, מקום לתשובות ופתרונות בעמוד חדש.", "pdf");
            string output = Path.Combine(draft.Folder, "_output"), pdf = Path.Combine(output, details.Name + ".pdf"); CheckPath(pdf); Directory.CreateDirectory(output);
            string temporary = Path.Combine(output, "_pdf-" + Guid.NewGuid().ToString("N") + ".tmp");
            ExamPaper paper;
            try
            {
                paper = await Sta(delegate {
                    var document = PrintableExamLayout.Build(topic, exam, details, cancellation); ExamPdfWriter.Write(document, temporary, cancellation); return document;
                }, cancellation);
                cancellation.ThrowIfCancellationRequested(); Drafts.ValidateDetails(draft); CheckPath(pdf); CheckPath(pdf + ".bak");
                if (exam.MaterialHash != MaterialReader.CurrentHash(topic)) throw new InvalidOperationException("החומרים השתנו בזמן העימוד. הטיוטה נשמרה; יש לנסות שוב עם החומר המעודכן.");
                if (File.Exists(pdf)) File.Replace(temporary, pdf, pdf + ".bak"); else File.Move(temporary, pdf);
                Store.Write(Path.Combine(draft.Folder, "_print.json"), new { details.Name, details.Classroom, details.School, PageCount = paper.Pages.Count, paper.AnswerStartPage, CreatedUtc = DateTime.UtcNow.ToString("o"), Pdf = "_output/" + Path.GetFileName(pdf) });
                cancellation.ThrowIfCancellationRequested();
                string destination = Path.Combine(Root, details.Name + " - " + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + draft.Id.Substring(0, 6)); CheckPath(Path.Combine(destination, "_output", Path.GetFileName(pdf)));
                Directory.Move(draft.Folder, destination); pdf = Path.Combine(destination, "_output", Path.GetFileName(pdf));
                WorkFlow.Saved(pdf, "pdf"); WorkFlow.Complete("save", "קובץ ה־PDF מוכן להורדה ולהדפסה.", "pdf");
                return new PrintExamResult { Folder = destination, PdfPath = pdf, Paper = paper };
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
