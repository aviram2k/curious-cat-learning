using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Education2k
{
    public static partial class Tests
    {
        static Exam PrintFixture(Topic topic)
        {
            var exam = Fixture(); exam.Title = "מבחן במדעים - מבנה האטום ומולקולות";
            string[] prompts = { "הדגם שלפניכם מציג חלקיקים בצבעים שונים. כל צבע מייצג סוג אחר של אטום. כמה סוגי אטומים מופיעים בדגם?", "מהו המטען החשמלי של אלקטרון?", "באיזה חלק של האטום נמצאים הפרוטונים?", "מה מייצג המספר האטומי של יסוד?" };
            string[][] options = { new[] { "שני סוגים", "שלושה סוגים", "סוג אחד", "ארבעה סוגים" }, new[] { "מטען שלילי", "מטען חיובי", "ללא מטען", "מטען המשתנה בכל מצב" }, new[] { "בגרעין האטום", "מסביב לגרעין", "מחוץ לאטום", "בין אטומים בלבד" }, new[] { "מספר הפרוטונים בגרעין", "מספר הנויטרונים בלבד", "מספר המולקולות בחומר", "מספר מצבי הצבירה" } };
            for (int i = 0; i < 4; i++) { exam.Questions[i].Prompt = prompts[i]; exam.Questions[i].Options = options[i].ToList(); exam.Questions[i].ExpectedAnswer = options[i][0] + ". התשובה נובעת ממבנה החלקיקים ומהגדרת התכונה שנשאלה."; }
            var table = exam.Questions[4]; table.Prompt = "השלימו את ההשוואה בין חלקיקי האטום."; table.Rows = new List<string> { "פרוטון", "אלקטרון", "נויטרון" }; table.Columns = new List<string> { "המטען החשמלי", "המיקום באטום" };
            table.CorrectCells = new List<List<string>> { new List<string> { "חיובי", "בגרעין" }, new List<string> { "שלילי", "מסביב לגרעין" }, new List<string> { "ללא מטען", "בגרעין" } }; table.ExpectedAnswer = "הגרעין מכיל פרוטונים ונויטרונים. האלקטרונים נמצאים מסביב לגרעין. סימני המטענים מסבירים את כוחות המשיכה והדחייה בין החלקיקים.";
            var open = exam.Questions[5]; open.Context = "נוסחת המים היא H₂O. האות H מסמלת מימן והאות O מסמלת חמצן.";
            open.Parts = new List<QuestionPart> {
                new QuestionPart { Id = "a", Type = "text", Points = 3, Prompt = "כמה אטומי מימן וכמה אטומי חמצן יש במולקולת מים אחת?", ExpectedAnswer = "במולקולת מים אחת יש שני אטומי מימן ואטום חמצן אחד. המספר הקטן בנוסחה מציין את מספר האטומים מן הסוג שלפניו." },
                new QuestionPart { Id = "b", Type = "table", Points = 3, Prompt = "השלימו את פרטי האטומים בנוסחה.", ExpectedAnswer = "H הוא הסימול של מימן, ו־O הוא הסימול של חמצן. כאשר אין מספר קטן לצד הסימול, הכוונה לאטום אחד.", Rows = new List<string> { "מימן", "חמצן" }, Columns = new List<string> { "סימול", "מספר האטומים" }, CorrectCells = new List<List<string>> { new List<string> { "H", "2" }, new List<string> { "O", "1" } } },
                new QuestionPart { Id = "c", Type = "text", Points = 4, Prompt = "מדוע מים נחשבים לתרכובת ולא ליסוד?", ExpectedAnswer = "מים הם תרכובת משום שבכל מולקולה שלהם יש אטומים משני סוגים שונים הקשורים זה לזה. יסוד מכיל סוג אחד של אטומים בלבד." }
            }; open.ExpectedAnswer = "מים בנויים משני אטומי מימן ומאטום חמצן אחד בכל מולקולה. שני סוגי האטומים קשורים זה לזה, ולכן מים הם תרכובת.";
            string imageFolder = Path.Combine(topic.Folder, "_materials", "example"); Directory.CreateDirectory(imageFolder); string image = Path.Combine(imageFolder, "figure.png");
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 720, 220)); var pen = new Pen(Brushes.SlateGray, 6);
                dc.DrawLine(pen, new Point(240, 135), new Point(360, 75)); dc.DrawLine(pen, new Point(360, 75), new Point(480, 135));
                dc.DrawEllipse(Brushes.CornflowerBlue, pen, new Point(240, 135), 43, 43); dc.DrawEllipse(Brushes.IndianRed, pen, new Point(360, 75), 53, 53); dc.DrawEllipse(Brushes.CornflowerBlue, pen, new Point(480, 135), 43, 43);
            }
            var bitmap = new RenderTargetBitmap(720, 220, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var output = File.Create(image)) encoder.Save(output);
            exam.Images.Add(new ExamImage { Id = "example", RelativePath = "_materials/example/figure.png", ImageHash = SourceImages.Digest(image), Caption = "דגם חלקיקים לדוגמה", Source = "תרשים סינתטי לבדיקה" }); exam.Questions[0].ImageIds.Add("example");
            exam.MaterialHash = MaterialReader.Hash(topic); Rules.Validate(exam); return exam;
        }
        static ExamDraft PrintDraftFixture(PrintExamService prints, Store store, string name = "דוגמה לבדיקה - מדעים")
        {
            var draft = prints.Drafts.Create(); draft.Name = name; draft.Classroom = "ח׳"; draft.School = "בית ספר לדוגמה"; prints.Save(draft);
            File.WriteAllText(Path.Combine(draft.Folder, "חומר לדוגמה.txt"), "חומר סינתטי לבדיקה בלבד: האטום מורכב מפרוטונים, מנויטרונים ומאלקטרונים. מים בנויים ממימן ומחמצן. נוסחת המים היא H₂O.", Encoding.UTF8);
            var topic = Store.ReadTopic(draft.Folder); Store.Write(store.ExamPath(topic, 1), PrintFixture(topic)); return draft;
        }
        static void PrintingChecks(string temp)
        {
            var store = new Store(Path.Combine(temp, "printing")); var prints = new PrintExamService(store); var regular = TestTopic(store, "נושא קיים", false); string regularHash = MaterialReader.Hash(regular);
            var draft = PrintDraftFixture(prints, store, regular.Name); var topic = Store.ReadTopic(draft.Folder); var exam = store.LoadExam(topic, 1);
            Assert(draft.Folder.StartsWith(Path.Combine(store.Root, "מבחנים", Store.PrintFolderName) + Path.DirectorySeparatorChar) && store.Topics().Single().Name == regular.Name, "Print materials and drafts live under exams/printable exams and never appear as student topics");
            var resumed = new PrintExamService(store).Drafts.Load(draft.Id);
            Assert(resumed.Name == draft.Name && resumed.Classroom == draft.Classroom && resumed.School == draft.School, "Print drafts retain exam name, class and school across resume");
            Reject(() => new PrintExamDetails { Name = "שם", Classroom = "", School = "בית ספר" }.Validate(), "A printable exam requires a class and school before generation");
            var regularDrafts = new ExamDrafts(store); var reserved = regularDrafts.Create(); reserved.Name = Store.PrintFolderName; reserved.Description = "תיאור"; regularDrafts.Save(reserved);
            Reject(() => regularDrafts.ValidateDetails(reserved), "The print workspace name cannot be used as an ordinary student topic");
            var paper = PrintableExamLayout.Build(topic, exam, PrintExamDetails.From(draft), CancellationToken.None);
            Assert(paper.AnswerStartPage > 1 && paper.Pages.Take(paper.AnswerStartPage - 1).All(p => !p.Answers) && paper.Pages.Skip(paper.AnswerStartPage - 1).All(p => p.Answers), "Solutions start on a fresh page after every student question page");
            Assert(paper.Pages.All(p => p.Text.Contains(ExamPaper.ProjectUrl) && p.Text.Any(t => t.Contains("נוצר עם Codex")) && p.Marks.Any(m => m.Image != null)) && paper.Pages.All(p => p.Drawing.IsFrozen), "Every printable page contains branding, project link and Codex attribution, with frozen drawings ready for printing");
            Assert(paper.Pages.All(p => p.Marks.Where(m => m.Shape != null).All(m => m.Shape.Bounds.IsEmpty || m.Shape.Bounds.Top >= -0.1 && m.Shape.Bounds.Bottom <= ExamPaper.Height + 0.1)), "Printable glyphs, ruled writing areas and tables stay within the page boundaries");
            var hidden = Store.Json.Deserialize<Exam>(Store.Json.Serialize(exam)); hidden.Questions[5].Parts[0].ExpectedAnswer += " ANSWER_ONLY_SENTINEL"; hidden.Questions[0].Hint += " HINT_ONLY_SENTINEL";
            var hiddenPaper = PrintableExamLayout.Build(topic, hidden, PrintExamDetails.From(draft), CancellationToken.None);
            Assert(!hiddenPaper.Pages.Take(hiddenPaper.AnswerStartPage - 1).SelectMany(p => p.Text).Any(t => t.Contains("ANSWER_ONLY_SENTINEL")) && hiddenPaper.Pages.Skip(hiddenPaper.AnswerStartPage - 1).SelectMany(p => p.Text).Any(t => t.Contains("ANSWER_ONLY_SENTINEL")) && !hiddenPaper.Pages.SelectMany(p => p.Text).Any(t => t.Contains("HINT_ONLY_SENTINEL")), "Student pages contain no reference answers or hints, while the solution key retains complete answers");
            var longExam = Store.Json.Deserialize<Exam>(Store.Json.Serialize(exam)); longExam.Questions[1].Prompt = String.Join(" ", Enumerable.Repeat("טקסט ארוך בעברית לבדיקה של מעבר מסודר בין עמודים ללא חיתוך.", 100));
            var longPaper = PrintableExamLayout.Build(topic, longExam, PrintExamDetails.From(draft), CancellationToken.None);
            Assert(longPaper.Pages.Count > paper.Pages.Count && longPaper.Pages.All(p => p.Marks.Where(m => m.Shape != null).All(m => m.Shape.Bounds.IsEmpty || m.Shape.Bounds.Bottom <= ExamPaper.Height + 0.1)), "Long Hebrew questions paginate onto additional pages without clipping the footer");
            var model = new FakeModel { Reply = schema => { throw new Exception("A saved print draft must not need a new model request."); } };
            var result = prints.Create(draft, new ExamService(store, model), CancellationToken.None, r => MaterialDecision.Continue).GetAwaiter().GetResult();
            Assert(File.Exists(result.PdfPath) && result.PdfPath.StartsWith(prints.Root + Path.DirectorySeparatorChar) && !Directory.Exists(draft.Folder) && model.Calls.Count == 0, "A completed printable exam atomically publishes its PDF and materials in the requested workspace without regenerating saved questions");
            string spool = Path.Combine(result.Folder, "_output", "print-test.xps");
            using (var xps = new System.Windows.Xps.Packaging.XpsDocument(spool, FileAccess.ReadWrite))
            {
                var printDocument = ExamPrintDocument.Create(result.Paper, 1, result.Paper.AnswerStartPage - 1, ExamPaper.Width, ExamPaper.Height);
                System.Windows.Xps.Packaging.XpsDocument.CreateXpsDocumentWriter(xps).Write(printDocument.DocumentPaginator);
                Assert(xps.GetFixedDocumentSequence().DocumentPaginator.PageCount == result.Paper.AnswerStartPage - 1, "Background-built pages serialize through the native print pipeline with only the requested student pages");
            }
            var finished = Store.ReadTopic(result.Folder); SourceImages.Check(finished, store.LoadExam(finished, 1));
            Assert(finished.Materials.Length == 1 && !finished.Materials.Any(p => p.EndsWith(".pdf")) && MaterialReader.Hash(finished) == exam.MaterialHash && MaterialReader.Hash(regular) == regularHash && Directory.GetDirectories(Path.Combine(store.Root, "תלמידים")).Length == 0, "PDF output does not become source material, and export preserves image caches, ordinary exams and student records");
            var decoded = PdfDocument.Read(result.PdfPath, null, null, "generated.pdf", "", false, CancellationToken.None);
            Assert(decoded.Count == result.Paper.Pages.Count, "The application's native reader can reopen every page of its generated PDF");
            var second = PrintDraftFixture(prints, store, regular.Name); var secondResult = prints.Create(second, new ExamService(store, model), CancellationToken.None, r => MaterialDecision.Continue).GetAwaiter().GetResult();
            Assert(secondResult.Folder != result.Folder && File.Exists(secondResult.PdfPath) && File.Exists(result.PdfPath), "Repeated creation with the same exam name makes independent printable exams without a quantity cap or overwriting earlier PDFs");
            var uiDraft = PrintDraftFixture(prints, store); int uiThread = Thread.CurrentThread.ManagedThreadId; var previousContext = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                var uiCompletion = CheckPrintContinuation(prints, uiDraft, new ExamService(store, model)); var frame = new DispatcherFrame(); var dispatcher = Dispatcher.CurrentDispatcher;
                uiCompletion.ContinueWith(t => dispatcher.BeginInvoke(new Action(delegate { frame.Continue = false; })), TaskScheduler.Default);
                Dispatcher.PushFrame(frame);
                Assert(uiCompletion.GetAwaiter().GetResult() == uiThread, "PDF creation resumes on the WPF UI thread so the ready screen can appear after background layout");
            }
            finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
            var stoppedDraft = PrintDraftFixture(prints, store); var stopped = new CancellationTokenSource(); stopped.Cancel(); bool cancelled = false;
            try { prints.Create(stoppedDraft, new ExamService(store, model), stopped.Token, r => MaterialDecision.Continue).GetAwaiter().GetResult(); } catch (OperationCanceledException) { cancelled = true; }
            Assert(cancelled && Directory.Exists(stoppedDraft.Folder) && !Directory.GetFiles(stoppedDraft.Folder, "*.tmp", SearchOption.AllDirectories).Any(), "Cancelling print preparation retains its draft without publishing a partial PDF");
            var writeDraft = PrintDraftFixture(prints, store); var writeStop = new CancellationTokenSource(); cancelled = false;
            using (WorkFlow.Use(new CancelPrintWrite(writeStop)))
            {
                try { prints.Create(writeDraft, new ExamService(store, model), writeStop.Token, r => MaterialDecision.Continue).GetAwaiter().GetResult(); } catch (OperationCanceledException) { cancelled = true; }
            }
            Assert(cancelled && Directory.Exists(writeDraft.Folder) && !Directory.GetFiles(writeDraft.Folder, "*.tmp", SearchOption.AllDirectories).Any() && !Directory.GetFiles(writeDraft.Folder, "*.pdf", SearchOption.AllDirectories).Any(), "Cancelling after PDF file creation removes the incomplete file while retaining source material for retry");
            var fresh = prints.Drafts.Create(); fresh.Name = "יצירה חדשה"; fresh.Classroom = "ח"; fresh.School = "בית ספר"; prints.Save(fresh); File.WriteAllText(Path.Combine(fresh.Folder, "חומר.txt"), "חומר סינתטי לקריאה");
            var freshModel = new FakeModel { Reply = schema => schema == "materials" ? (object)TestReview(MaterialReader.Inspect(Store.ReadTopic(fresh.Folder))) : Fixture() };
            var freshResult = prints.Create(fresh, new ExamService(store, freshModel), CancellationToken.None, r => MaterialDecision.Continue).GetAwaiter().GetResult();
            Assert(freshModel.Calls.SequenceEqual(new[] { "materials", "exam" }) && File.Exists(freshResult.PdfPath), "A new printable exam runs the full source-review and question-generation pipeline before exporting PDF");
            var warning = prints.Drafts.Create(); warning.Name = "בעיה במקור"; warning.Classroom = "ח"; warning.School = "בית ספר"; prints.Save(warning); File.WriteAllText(Path.Combine(warning.Folder, "חומר.txt"), "חומר חלקי לבדיקה");
            var warningModel = new FakeModel { Reply = schema => schema == "materials" ? (object)TestReview(MaterialReader.Inspect(Store.ReadTopic(warning.Folder)), true) : Fixture() };
            Assert(Declines(() => prints.Create(warning, new ExamService(store, warningModel), CancellationToken.None, r => MaterialDecision.Cancel).GetAwaiter().GetResult()) && !warningModel.Calls.Contains("exam") && !Directory.GetFiles(warning.Folder, "*.pdf", SearchOption.AllDirectories).Any(), "Declining a source-reading warning stops before generating questions or PDF and keeps the print draft");
            using (var progress = new WorkState("PDF", "pdf", store.Root, DateTime.UtcNow))
            {
                progress.Apply(new WorkUpdate { Kind = "saved", Role = "exam", FileName = "_exam_0001.json" }); Assert(!progress.HasResult && progress.FileStatus.Contains("עדיין"), "PDF progress does not claim completion when only intermediate questions are saved");
                progress.Apply(new WorkUpdate { Kind = "saved", Role = "pdf", FileName = "exam.pdf" }); Assert(progress.HasResult && progress.ResultFile == "exam.pdf", "PDF progress announces completion only after the final PDF has been saved");
            }
        }
        static async Task<int> CheckPrintContinuation(PrintExamService prints, ExamDraft draft, ExamService builder)
        {
            await prints.Create(draft, builder, CancellationToken.None, r => MaterialDecision.Continue); return Thread.CurrentThread.ManagedThreadId;
        }
        sealed class CancelPrintWrite : IProgress<WorkUpdate>
        {
            readonly CancellationTokenSource stop;
            public CancelPrintWrite(CancellationTokenSource stop) { this.stop = stop; }
            public void Report(WorkUpdate update) { if ((update.Message ?? "").Contains("משלימים כתיבה לדיסק")) stop.Cancel(); }
        }
        public static int PrintExample(string root)
        {
            var store = new Store(root); var prints = new PrintExamService(store); var draft = PrintDraftFixture(prints, store); var model = new FakeModel { Reply = schema => { throw new Exception("The sample uses a saved synthetic exam."); } };
            var result = prints.Create(draft, new ExamService(store, model), CancellationToken.None, r => MaterialDecision.Continue).GetAwaiter().GetResult();
            Store.Write(Path.Combine(prints.Root, "_example-result.json"), new { result.PdfPath, result.Folder, PageCount = result.Paper.Pages.Count, result.Paper.AnswerStartPage, Synthetic = true }); return 0;
        }
        public static string PreparePrintPreview(string root)
        {
            string previewRoot = Path.Combine(root, "Build", "UiPrint-" + Guid.NewGuid().ToString("N")); var store = new Store(previewRoot); var prints = new PrintExamService(store); Directory.CreateDirectory(prints.Root);
            File.WriteAllText(Path.Combine(prints.Root, "חומר סינתטי לדוגמה.txt"), "מים בנויים ממימן ומחמצן. האטום מכיל גרעין ואלקטרונים. חומר לדוגמה לבדיקת יצירת PDF בלבד.", Encoding.UTF8); return previewRoot;
        }
        public static Task<PrintExamResult> PrintReadyPreview(Store store, CancellationToken cancellation)
        {
            var prints = new PrintExamService(store); var draft = PrintDraftFixture(prints, store); var model = new FakeModel { Reply = schema => { throw new Exception("The preview uses a saved synthetic exam."); } };
            return prints.Create(draft, new ExamService(store, model), cancellation, r => MaterialDecision.Continue);
        }
    }
}
