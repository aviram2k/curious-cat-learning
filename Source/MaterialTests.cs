using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Education2k
{
    public static partial class Tests
    {
        sealed class FakeModel : IModelClient
        {
            public readonly List<string> Calls = new List<string>();
            public Func<string, object> Reply;
            public Task<T> Ask<T>(string prompt, string schema, bool search, int timeoutSeconds, CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested(); Calls.Add(schema); return Task.FromResult((T)Reply(schema));
            }
        }
        static void ZipText(ZipArchive zip, string path, string value)
        {
            using (var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false))) writer.Write(value);
        }
        static void PresentationFixture(string path)
        {
            const string p = "http://schemas.openxmlformats.org/presentationml/2006/main";
            const string a = "http://schemas.openxmlformats.org/drawingml/2006/main";
            const string r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            string slide = "<p:sld xmlns:p=\"" + p + "\" xmlns:a=\"" + a + "\"><p:cSld><p:spTree>";
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                ZipText(zip, "ppt/presentation.xml", "<p:presentation xmlns:p=\"" + p + "\" xmlns:r=\"" + r + "\"><p:sldIdLst><p:sldId r:id=\"a\"/><p:sldId r:id=\"b\"/><p:sldId r:id=\"c\"/></p:sldIdLst></p:presentation>");
                ZipText(zip, "ppt/_rels/presentation.xml.rels", "<Relationships><Relationship Id=\"a\" Target=\"slides/slide2.xml\"/><Relationship Id=\"b\" Target=\"slides/slide1.xml\"/><Relationship Id=\"c\" Target=\"slides/slide10.xml\"/></Relationships>");
                ZipText(zip, "ppt/slides/slide2.xml", slide + "<p:sp><a:p><a:r><a:t>קטע ראשון</a:t></a:r></a:p></p:sp><p:graphicFrame><a:tbl><a:p><a:r><a:t>תא בטבלה</a:t></a:r></a:p></a:tbl></p:graphicFrame></p:spTree></p:cSld></p:sld>");
                ZipText(zip, "ppt/slides/slide1.xml", slide + "<p:sp><a:p><a:r><a:t>קטע שני</a:t></a:r></a:p></p:sp><p:pic/></p:spTree></p:cSld></p:sld>");
                ZipText(zip, "ppt/slides/slide10.xml", slide + "<p:sp><a:p><a:r><a:t>קטע שלישי</a:t></a:r></a:p></p:sp></p:spTree></p:cSld></p:sld>");
                ZipText(zip, "ppt/slides/_rels/slide2.xml.rels", "<Relationships><Relationship Type=\"" + r + "/notesSlide\" Target=\"../notesSlides/notesSlide1.xml\"/></Relationships>");
                ZipText(zip, "ppt/notesSlides/notesSlide1.xml", "<p:notes xmlns:p=\"" + p + "\" xmlns:a=\"" + a + "\"><p:sp><p:ph type=\"body\"/><a:p><a:r><a:t>הערת מורה</a:t></a:r></a:p></p:sp><p:sp><p:ph type=\"sldNum\"/><a:p><a:r><a:t>9999</a:t></a:r></a:p></p:sp></p:notes>");
            }
        }
        static MaterialReview TestReview(MaterialSnapshot snapshot, bool forcePartial = false)
        {
            return new MaterialReview { Summary = "דוח מפורט על בדיקת המקורות.", Files = snapshot.Files.Select(f => new MaterialFileReview {
                FileName = f.FileName, Status = forcePartial || f.MustWarn ? "partial" : "read", Explanation = "הטקסט שנקרא ומגבלותיו: " + f.Detail,
                Impact = "ייתכן שחלק מהחומר לא נכלל במבחן.", Recommendation = "יש לצרף עותק טקסט נגיש של החומר החסר."
            }).ToList() };
        }
        static bool Declines(Action action)
        {
            try { action(); return false; } catch (MaterialDecisionDeclinedException) { return true; }
        }
        static Topic TestTopic(Store store, string name, bool presentation)
        {
            string folder = Path.Combine(store.Root, "מבחנים", name); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "_describe.txt"), "כיתה ח, חומר מצורף למבחן", Encoding.UTF8);
            if (presentation) PresentationFixture(Path.Combine(folder, "מקור.pptx"));
            else File.WriteAllText(Path.Combine(folder, "סיכום.txt"), "חומר קריא בעברית", Encoding.UTF8);
            return store.Topics().Single(t => t.Name == name);
        }
        static void MaterialSafety(string temp, string sampleRoot)
        {
            var store = new Store(Path.Combine(temp, "material-safety")); var topic = TestTopic(store, "מצגת", true);
            string text = MaterialReader.Extract(topic.Materials.Single());
            Assert(text.IndexOf("קטע ראשון") < text.IndexOf("קטע שני") && text.IndexOf("קטע שני") < text.IndexOf("קטע שלישי"), "PowerPoint extraction follows presentation order, not ZIP names");
            Assert(text.Contains("תא בטבלה") && text.Contains("הערת מורה") && !text.Contains("9999"), "PowerPoint table text and speaker notes are read without note slide numbers");
            var snapshot = MaterialReader.Inspect(topic);
            Assert(snapshot.Files.Single().MustWarn && snapshot.Files.Single().Status == "partial" && snapshot.Files.Single().Detail.Contains("שקופית 2"), "Unparsed PowerPoint images are identified by slide instead of silently treated as read");
            var fake = new FakeModel { Reply = schema => schema == "materials" ? (object)TestReview(snapshot) : Fixture() };
            var service = new ExamService(store, fake);
            Assert(Declines(() => service.GetExam(topic, 1, CancellationToken.None, review => MaterialDecision.Cancel).GetAwaiter().GetResult()) && !fake.Calls.Contains("exam") && !File.Exists(store.ExamPath(topic, 1)), "Declining a source warning prevents question generation and exam persistence");
            Assert(Declines(() => service.GetQuiz(topic, 1, CancellationToken.None, review => MaterialDecision.Cancel).GetAwaiter().GetResult()) && !File.Exists(store.ExamPath(topic, 1, true)) && !File.Exists(store.ExamPath(topic, 1)), "Declining before a new quiz creates neither a quiz nor its full source exam");
            Reject(() => service.GetExam(topic, 1, CancellationToken.None).GetAwaiter().GetResult(), "A source warning without an explicit decision cannot be bypassed by CLI callers");
            int decisions = 0; var quiz = service.GetQuiz(topic, 1, CancellationToken.None, review => { decisions++; return MaterialDecision.Continue; }).GetAwaiter().GetResult();
            Assert(decisions == 1 && quiz.SourceReview.RequiresDecision && store.LoadExam(topic, 1).SourceReview.RequiresDecision, "Explicit approval preserves the source warning in both shared full exams and quizzes");
            int calls = fake.Calls.Count;
            Assert(Declines(() => service.GetQuiz(topic, 1, CancellationToken.None, review => MaterialDecision.Cancel).GetAwaiter().GetResult()) && fake.Calls.Count == calls, "Cached quizzes still require a decision without generating more questions");
            Assert(Declines(() => service.GetExam(topic, 1, CancellationToken.None, review => MaterialDecision.Cancel).GetAwaiter().GetResult()), "Cached full and assisted exams cannot silently hide existing source warnings");
            var incorrectReport = TestReview(snapshot); incorrectReport.Files[0].Status = "read";
            Reject(() => MaterialReviewService.Validate(incorrectReport, snapshot, false), "The model cannot declare locally unparsed content fully read");
            var incompleteReport = TestReview(snapshot); incompleteReport.Files.Clear();
            Reject(() => MaterialReviewService.Validate(incompleteReport, snapshot, false), "The material review must account for every supplied source");

            var legacyTopic = TestTopic(store, "ישן", false); var legacySnapshot = MaterialReader.Inspect(legacyTopic);
            var legacy = Fixture(); legacy.Introduction = "המצגת המקומית לא הייתה נגישה לקריאה. המבחן מבוסס על מקורות חלופיים.";
            Store.Write(store.ExamPath(legacyTopic, 1), legacy); Store.Write(store.ExamPath(legacyTopic, 1, true), QuizBuilder.Create(legacy));
            string oldExam = File.ReadAllText(store.ExamPath(legacyTopic, 1));
            var legacyFake = new FakeModel { Reply = schema => { if (schema != "materials") throw new Exception("Legacy exam must not be regenerated."); return TestReview(legacySnapshot, true); } };
            var legacyService = new ExamService(store, legacyFake);
            Assert(MaterialReviewService.LegacyWarning(legacy) && Declines(() => legacyService.GetExam(legacyTopic, 1, CancellationToken.None, review => MaterialDecision.Cancel).GetAwaiter().GetResult()) && File.ReadAllText(store.ExamPath(legacyTopic, 1)) == oldExam, "Legacy introduction warnings produce an AI report without rewriting saved questions");
            Assert(Declines(() => legacyService.GetQuiz(legacyTopic, 1, CancellationToken.None, review => MaterialDecision.Cancel).GetAwaiter().GetResult()), "Legacy quiz checks its full exam when its own introduction omits the source warning");
            Assert(store.ExamFiles(legacyTopic).Count() == 1, "Source review sidecars are excluded from exam version enumeration and upgrades");

            var clean = TestTopic(store, "נקי", false); var cleanSnapshot = MaterialReader.Inspect(clean);
            var cleanFake = new FakeModel { Reply = schema => schema == "materials" ? (object)TestReview(cleanSnapshot) : Fixture() };
            var cleanExam = new ExamService(store, cleanFake).GetExam(clean, 1, CancellationToken.None, review => { throw new Exception("A fully read source needs no warning."); }).GetAwaiter().GetResult();
            Assert(!cleanExam.SourceReview.RequiresDecision && cleanFake.Calls.SequenceEqual(new[] { "materials", "exam" }), "Clean materials are checked before generation without an unnecessary decision screen");
            var late = TestTopic(store, "בעיה נוספת", false); var lateSnapshot = MaterialReader.Inspect(late);
            var lateFake = new FakeModel { Reply = schema => {
                if (schema == "materials") return (object)TestReview(lateSnapshot);
                var result = Fixture(); result.SourceIssues.Add(new MaterialIssue { FileName = "סיכום.txt", Explanation = "נושא שנדרש מפנה לקטע שלא צורף.", Impact = "השאלות אינן מכסות את הקטע החסר.", Recommendation = "יש לצרף את הקטע." }); return result;
            } };
            Assert(Declines(() => new ExamService(store, lateFake).GetExam(late, 1, CancellationToken.None, review => MaterialDecision.Cancel).GetAwaiter().GetResult()) && !File.Exists(store.ExamPath(late, 1)), "New issues discovered during generation require approval before any exam is saved");
            var introOnly = TestTopic(store, "אזהרה במבוא", false); var introSnapshot = MaterialReader.Inspect(introOnly); int introReports = 0;
            var introFake = new FakeModel { Reply = schema => { if (schema == "materials") return (object)TestReview(introSnapshot, ++introReports > 1); var result = Fixture(); result.Introduction = "חומר המקור לא נקרא במלואו ולכן נעשה שימוש בחומר חלופי."; return result; } };
            Assert(Declines(() => new ExamService(store, introFake).GetExam(introOnly, 1, CancellationToken.None, review => MaterialDecision.Cancel).GetAwaiter().GetResult()) && introReports == 2 && !File.Exists(store.ExamPath(introOnly, 1)), "An unexpected source warning only in the generated introduction still blocks saving pending approval");
            var changed = TestTopic(store, "מקור שהשתנה", true); var changedSnapshot = MaterialReader.Inspect(changed);
            var changedFake = new FakeModel { Reply = schema => schema == "materials" ? (object)TestReview(changedSnapshot) : Fixture() };
            Reject(() => new ExamService(store, changedFake).GetExam(changed, 1, CancellationToken.None, review => { File.AppendAllText(changed.Materials.Single(), "changed"); return MaterialDecision.Continue; }).GetAwaiter().GetResult(), "Changing materials while the user decides invalidates the approval");
            Assert(!changedFake.Calls.Contains("exam") && !File.Exists(store.ExamPath(changed, 1)), "A changed source cannot generate or persist questions under the previous approval");
            var added = TestTopic(store, "קובץ שנוסף", true); var addedSnapshot = MaterialReader.Inspect(added);
            var addedFake = new FakeModel { Reply = schema => schema == "materials" ? (object)TestReview(addedSnapshot) : Fixture() };
            Reject(() => new ExamService(store, addedFake).GetExam(added, 1, CancellationToken.None, review => { File.WriteAllText(Path.Combine(added.Folder, "נוסף.txt"), "חומר נוסף"); return MaterialDecision.Continue; }).GetAwaiter().GetResult(), "Adding a source while deciding invalidates the previous source approval");
            var described = TestTopic(store, "תיאור שהשתנה", true); var describedSnapshot = MaterialReader.Inspect(described);
            var describedFake = new FakeModel { Reply = schema => schema == "materials" ? (object)TestReview(describedSnapshot) : Fixture() };
            Reject(() => new ExamService(store, describedFake).GetExam(described, 1, CancellationToken.None, review => { File.WriteAllText(Path.Combine(described.Folder, "_describe.txt"), "נושאים אחרים למבחן", Encoding.UTF8); return MaterialDecision.Continue; }).GetAwaiter().GetResult(), "Editing the description while deciding invalidates the previous source approval");

            string broken = Path.Combine(temp, "broken.pptx"); File.WriteAllText(broken, "not a ZIP");
            var brokenTopic = new Topic { Folder = temp, Description = "חומר בדיקה", Materials = new[] { broken } };
            Assert(MaterialReader.Inspect(brokenTopic).Files.Single().Status == "unread", "Corrupt source files become material findings for the AI report instead of crashing extraction");
            using (var locked = new FileStream(clean.Materials.Single(), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Assert(MaterialReader.Inspect(clean).Files.Single().Status == "unread", "Locked source files can be fingerprinted and reported as unread");
            string utf16 = Path.Combine(temp, "hebrew-utf16.txt"); File.WriteAllText(utf16, "עברית תקינה", Encoding.Unicode);
            Assert(MaterialReader.ReadText(utf16) == "עברית תקינה", "Windows UTF-16 Hebrew text files are decoded correctly");
            var missing = new Topic { Folder = temp, Description = "תיאור בלבד", Materials = new string[0] };
            Assert(MaterialReader.Inspect(missing).Files.Single().MustWarn, "A topic with no learning files requires a source decision");
            var noDescription = TestTopic(store, "ללא תיאור", false); File.Move(Path.Combine(noDescription.Folder, "_describe.txt"), Path.Combine(noDescription.Folder, "_description-backup.txt"));
            Assert(MaterialReader.Inspect(store.Topics().Single(t => t.Name == noDescription.Name)).Files.Any(f => f.FileName == "_describe.txt" && f.MustWarn), "Missing topic descriptions become source review findings");
            File.WriteAllText(clean.Materials.Single(), new string('א', 410000), Encoding.UTF8);
            Assert(MaterialReader.Inspect(clean).Files.Single().MustWarn, "Oversized source text is explicitly marked partial rather than silently truncated");

            var realPptx = new Store(sampleRoot).Topics().SelectMany(t => t.Materials).FirstOrDefault(f => Path.GetExtension(f).Equals(".pptx", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(f).Contains("לידת אירופה"));
            if (realPptx != null)
            {
                string extracted = MaterialReader.Extract(realPptx);
                Assert(extracted.Contains("שקופית 19") && extracted.Contains("התפתחות השפות") && extracted.Contains("הצו המדיולני"), "Actual history PowerPoint yields all 19 slides including Hebrew historical content");
                File.WriteAllText(Path.Combine(sampleRoot, "Build", "history-material.txt"), extracted, Encoding.UTF8);
            }
        }
        public static int LiveMaterials(string root)
        {
            var store = new Store(root); var topic = store.Topics().First(t => t.Name.Contains("היסטוריה") && t.Materials.Any(f => Path.GetExtension(f).Equals(".pptx", StringComparison.OrdinalIgnoreCase)));
            var snapshot = MaterialReader.Inspect(topic); var reviewService = new MaterialReviewService(new CodexClient(root));
            var current = reviewService.Inspect(topic, snapshot, CancellationToken.None).GetAwaiter().GetResult();
            var visuals = snapshot.Files.SelectMany(f => f.Visuals).ToList();
            if (visuals.Count < 20 || visuals.Any(v => v.Reading == null) || !current.Files.Any(f => f.Explanation.Any(c => c >= '\u0590' && c <= '\u05ff'))) throw new InvalidOperationException("Live history check did not account for every PDF page and presentation slide.");
            File.WriteAllText(Path.Combine(root, "Build", "material-review-live.txt"), "PASS: Real Codex inspected " + visuals.Count + " source page images, " + visuals.Count(v => v.Reading.Status == "read") + " fully read; " + visuals.Count(v => v.Reading.ExamSuitable) + " suitable for use in exams.\n" + Store.Json.Serialize(current), Encoding.UTF8); return 0;
        }
    }
}
