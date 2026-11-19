using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Education2k
{
    public static partial class Tests
    {
        static ExamDraft DraftFixture(ExamDrafts drafts, string name, bool material = true)
        {
            var draft = drafts.Create(); draft.Name = name; draft.Description = "תיאור כללי בעברית של הנושא והחומר המצורף."; drafts.Save(draft);
            if (material) File.WriteAllText(Path.Combine(draft.Folder, "סיכום.txt"), "מים הם חומר. מים יכולים להופיע במצב מוצק, נוזל או גז. קרח הוא מים במצב מוצק.", Encoding.UTF8);
            return draft;
        }
        static FakeModel DraftModel(ExamDraft draft)
        {
            return new FakeModel { Reply = schema => schema == "materials" ? (object)TestReview(MaterialReader.Inspect(Store.ReadTopic(draft.Folder))) : Fixture() };
        }
        static void DraftChecks(string temp)
        {
            var store = new Store(Path.Combine(temp, "wizard")); var drafts = new ExamDrafts(store);
            var draft = DraftFixture(drafts, "נושא חדש", false); draft.Step = 1; drafts.Save(draft);
            Assert(store.Topics().Count == 0 && drafts.List().Count == 1, "A new draft is persisted separately and is invisible in the student topic list");
            var restored = new ExamDrafts(store).Load(draft.Id);
            Assert(restored.Name == draft.Name && restored.Description == draft.Description && restored.Step == 1 && File.ReadAllText(Path.Combine(restored.Folder, "_describe.txt")) == draft.Description, "Draft resume restores the step, Hebrew name, and exactly the general description without extra required fields");
            Reject(() => drafts.Load("../unsafe"), "Draft identifiers cannot escape the draft directory");
            foreach (string bad in new[] { "../unsafe", "CON.txt", "NUL", "COM1", "x.", "a:b", "" }) Reject(() => ExamDrafts.CleanTopicName(bad), "Unsafe Windows topic name is rejected: " + bad);

            string inputs = Path.Combine(store.Root, "input"); Directory.CreateDirectory(inputs);
            string first = Path.Combine(inputs, "חומר.txt"); File.WriteAllText(first, "חומר מקור ראשון בעברית", Encoding.UTF8);
            var imported = drafts.Import(draft, new[] { first }, CancellationToken.None).GetAwaiter().GetResult();
            Assert(imported.Added.Count == 1 && File.ReadAllText(first) == File.ReadAllText(Path.Combine(draft.Folder, "חומר.txt")), "Import copies Hebrew-named material and leaves the original intact");
            var duplicate = drafts.Import(draft, new[] { first, Path.Combine(draft.Folder, "חומר.txt") }, CancellationToken.None).GetAwaiter().GetResult();
            Assert(duplicate.Existing.Count == 2 && drafts.Files(draft).Length == 1, "Repeated imports and files already inside the draft do not create duplicates");
            File.WriteAllText(first, "חומר מקור אחר בעל אותו שם", Encoding.UTF8);
            var collision = drafts.Import(draft, new[] { first }, CancellationToken.None).GetAwaiter().GetResult();
            Assert(collision.Added.Single() == "חומר (2).txt" && File.ReadAllText(Path.Combine(draft.Folder, "חומר.txt")) != File.ReadAllText(first), "Different files with the same name receive a suffix without overwriting existing material");
            Assert(drafts.Import(draft, new[] { first }, CancellationToken.None).GetAwaiter().GetResult().Existing.Single() == "חומר (2).txt", "Reimporting a renamed collision reuses the identical copy rather than adding more duplicates");
            string systemFile = Path.Combine(inputs, "_describe.txt"); File.WriteAllText(systemFile, "must not replace description");
            string valid = Path.Combine(inputs, "נוסף.txt"); File.WriteAllText(valid, "חומר נוסף");
            var errors = drafts.Import(draft, new[] { inputs, systemFile, Path.Combine(inputs, "missing.txt"), valid }, CancellationToken.None).GetAwaiter().GetResult();
            Assert(errors.Errors.Count == 3 && errors.Added.Count == 1 && File.ReadAllText(Path.Combine(draft.Folder, "_describe.txt")) == draft.Description, "Invalid imports report per-file errors while valid material is retained and the description is protected");
            File.WriteAllText(Path.Combine(draft.Folder, "תוספת חיצונית.txt"), "חומר שנגרר ישירות לתיקייה");
            Assert(drafts.Files(draft).Any(p => Path.GetFileName(p) == "תוספת חיצונית.txt") && drafts.Files(draft).All(p => !Path.GetFileName(p).StartsWith("_")), "Files added through Explorer are discovered while draft metadata stays out of learning material");
            drafts.Remove(draft, "חומר.txt");
            Assert(!drafts.Files(draft).Any(p => Path.GetFileName(p) == "חומר.txt") && Directory.GetFiles(Path.Combine(draft.Folder, "_removed")).Length == 1 && File.Exists(first), "Removing an imported copy excludes it from learning, preserves it privately, and never deletes the source");
            var cancelledImport = new CancellationTokenSource(); cancelledImport.Cancel(); bool cancelled = false;
            try { drafts.Import(draft, new[] { first }, cancelledImport.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { cancelled = true; }
            Assert(cancelled && !Directory.GetFiles(draft.Folder, "_import_*").Any(), "Cancelled import leaves no partial files presented as learning material");

            var empty = DraftFixture(drafts, "ללא חומר", false); var noCalls = DraftModel(empty);
            Reject(() => drafts.Complete(empty, new ExamService(store, noCalls), CancellationToken.None, r => MaterialDecision.Continue).GetAwaiter().GetResult(), "Finishing without material is blocked before generation");
            Assert(noCalls.Calls.Count == 0 && store.Topics().Count == 0, "An empty draft never invokes the model or becomes a visible topic");
            var warning = DraftFixture(drafts, "החלטה על חומר"); var warningModel = new FakeModel { Reply = schema => schema == "materials" ? (object)TestReview(MaterialReader.Inspect(Store.ReadTopic(warning.Folder)), true) : Fixture() };
            Assert(Declines(() => drafts.Complete(warning, new ExamService(store, warningModel), CancellationToken.None, r => MaterialDecision.Cancel).GetAwaiter().GetResult()) && !warningModel.Calls.Contains("exam") && Directory.Exists(warning.Folder) && store.Topics().Count == 0, "Declining the source-warning screen keeps the draft and never creates or publishes an exam");

            var failure = DraftFixture(drafts, "ניסיון חוזר"); var broken = new FakeModel { Reply = schema => { if (schema == "materials") return TestReview(MaterialReader.Inspect(Store.ReadTopic(failure.Folder))); throw new InvalidOperationException("Synthetic generation failure"); } };
            Reject(() => drafts.Complete(failure, new ExamService(store, broken), CancellationToken.None, r => MaterialDecision.Continue).GetAwaiter().GetResult(), "Generation failure is propagated for review");
            Assert(Directory.Exists(failure.Folder) && drafts.Files(failure).Length == 1 && store.Topics().Count == 0, "Failed generation retains the materials and description without exposing an unfinished topic");
            var retry = DraftModel(failure); string oldFolder = failure.Folder;
            var ready = drafts.Complete(failure, new ExamService(store, retry), CancellationToken.None, r => MaterialDecision.Continue).GetAwaiter().GetResult();
            Assert(ready.Name == failure.Name && !Directory.Exists(oldFolder) && store.LoadExam(ready, 1).Number == 1 && retry.Calls.Count(c => c == "exam") == 1 && store.Topics().Count == 1, "Successful retry saves shared exam 1 and publishes the complete topic in one directory move");
            Assert(!drafts.List().Any(d => d.Id == failure.Id) && Directory.GetDirectories(Path.Combine(store.Root, "תלמידים")).Length == 0, "Publishing removes the private draft from the chooser and creates no student profile or attempt");
            bool movedSave = false; try { drafts.Save(failure); } catch (DirectoryNotFoundException) { movedSave = true; }
            Assert(movedSave && !Directory.Exists(oldFolder), "A delayed autosave cannot recreate a phantom draft after publication");
            var repeatedName = DraftFixture(drafts, ready.Name.ToUpperInvariant());
            Reject(() => drafts.ValidateDetails(repeatedName), "Existing topics cannot be overwritten by a draft with the same Windows folder name");

            var duringCancel = DraftFixture(drafts, "ביטול יצירה"); var stop = new CancellationTokenSource();
            var stopModel = new FakeModel { Reply = schema => { if (schema == "materials") return TestReview(MaterialReader.Inspect(Store.ReadTopic(duringCancel.Folder))); stop.Cancel(); return Fixture(); } };
            cancelled = false;
            try { drafts.Complete(duringCancel, new ExamService(store, stopModel), stop.Token, r => MaterialDecision.Continue).GetAwaiter().GetResult(); } catch (OperationCanceledException) { cancelled = true; }
            Assert(cancelled && Directory.Exists(duringCancel.Folder) && !File.Exists(store.ExamPath(Store.ReadTopic(duringCancel.Folder), 1)) && store.Topics().Count == 1, "Cancelling during generation preserves the draft and prevents saving or publishing questions");

            var changed = DraftFixture(drafts, "חומר משתנה"); var changeModel = new FakeModel { Reply = schema => { if (schema == "materials") return TestReview(MaterialReader.Inspect(Store.ReadTopic(changed.Folder))); File.AppendAllText(Path.Combine(changed.Folder, "סיכום.txt"), " שינוי בזמן יצירה"); return Fixture(); } };
            Reject(() => drafts.Complete(changed, new ExamService(store, changeModel), CancellationToken.None, r => MaterialDecision.Continue).GetAwaiter().GetResult(), "Material changes during generation prevent publishing a mismatched exam");
            Assert(Directory.Exists(changed.Folder) && store.Topics().Count == 1, "A changed-source failure leaves a resumable draft rather than a visible topic");

            var stale = DraftFixture(drafts, "טיוטה מוכנה שהתעדכנה"); var staleTopic = Store.ReadTopic(stale.Folder); var staleExam = Fixture(); staleExam.MaterialHash = MaterialReader.Hash(staleTopic); Store.Write(store.ExamPath(staleTopic, 1), staleExam);
            File.AppendAllText(Path.Combine(stale.Folder, "סיכום.txt"), " תוספת לחומר"); var newer = DraftModel(stale);
            var updated = drafts.Complete(stale, new ExamService(store, newer), CancellationToken.None, r => MaterialDecision.Continue).GetAwaiter().GetResult();
            Assert(newer.Calls.Contains("exam") && Directory.GetFiles(Path.Combine(updated.Folder, "_previous")).Length == 1 && store.LoadExam(updated, 1).MaterialHash == MaterialReader.Hash(updated), "Retry after changing a ready draft preserves the old private questions and generates exam 1 from the updated material");

            var illustrated = DraftFixture(drafts, "מבחן עם תמונה"); var illustratedTopic = Store.ReadTopic(illustrated.Folder); var withImage = Fixture();
            string imageFolder = Path.Combine(illustrated.Folder, "_materials", "fixture"); Directory.CreateDirectory(imageFolder); string png = Path.Combine(imageFolder, "figure.png");
            using (var source = typeof(Program).Assembly.GetManifestResourceStream("Education2k.AppLogo.png")) using (var target = File.Create(png)) source.CopyTo(target);
            withImage.Images.Add(new ExamImage { Id = "figure", RelativePath = "_materials/fixture/figure.png", ImageHash = SourceImages.Digest(png), Caption = "תרשים בדיקה", Source = "חומר בדיקה" }); withImage.Questions[0].ImageIds.Add("figure");
            withImage.MaterialHash = MaterialReader.Hash(illustratedTopic); Store.Write(store.ExamPath(illustratedTopic, 1), withImage); var cachedModel = DraftModel(illustrated);
            var publishedImage = drafts.Complete(illustrated, new ExamService(store, cachedModel), CancellationToken.None, r => MaterialDecision.Continue).GetAwaiter().GetResult();
            Assert(store.LoadExam(publishedImage, 1).Images.Count == 1 && File.Exists(SourceImages.Resolve(publishedImage, withImage.Images[0].RelativePath)) && cachedModel.Calls.Count == 0, "Publishing a ready draft moves its relative image cache intact and reuses exam 1 without another model request");

            var race = DraftFixture(drafts, "שם שנתפס בזמן יצירה"); var racingModel = new FakeModel { Reply = schema => { if (schema == "materials") return TestReview(MaterialReader.Inspect(Store.ReadTopic(race.Folder))); Directory.CreateDirectory(Path.Combine(store.Root, "מבחנים", race.Name)); return Fixture(); } };
            Reject(() => drafts.Complete(race, new ExamService(store, racingModel), CancellationToken.None, r => MaterialDecision.Continue).GetAwaiter().GetResult(), "A destination created during generation is rechecked before publishing and is never overwritten");
            Assert(Directory.Exists(race.Folder) && File.Exists(store.ExamPath(Store.ReadTopic(race.Folder), 1)), "A publication conflict preserves the completed draft exam for retry with another name");
        }

        public static string PrepareNewExamPreview(string root)
        {
            string previewRoot = Path.Combine(root, "Build", "UiNewExam-" + Guid.NewGuid().ToString("N")); new Store(previewRoot);
            string schemas = Path.Combine(previewRoot, "Schemas"); Directory.CreateDirectory(schemas);
            foreach (string schema in Directory.GetFiles(Path.Combine(root, "Schemas"), "*.json")) File.Copy(schema, Path.Combine(schemas, Path.GetFileName(schema)));
            File.WriteAllText(Path.Combine(root, "Build", "wizard-demo-material.txt"), "חומר בדיקה סינתטי בלבד: מים מופיעים בטבע במצבי צבירה שונים. קרח הוא מוצק. מים זורמים הם נוזל. אדי מים הם גז. בחימום קרח ניתך למים נוזליים.", Encoding.UTF8);
            return previewRoot;
        }
        public static IModelClient WizardDemoModel(Topic topic) { return new WizardPreviewModel(topic); }
        sealed class WizardPreviewModel : IModelClient
        {
            readonly Topic topic;
            public WizardPreviewModel(Topic topic) { this.topic = topic; }
            public async Task<T> Ask<T>(string prompt, string schema, bool search, int timeoutSeconds, CancellationToken cancellation)
            {
                WorkFlow.Activity("תצוגת בדיקה · מודל מדומה, ללא פנייה ל־Codex.", schema == "materials" ? "מדמים בדיקת קריאה של חומרי הלימוד." : "מדמים הכנת שאלות למבחן הראשון.");
                await Task.Delay(2200, cancellation);
                return (T)(object)(schema == "materials" ? (object)TestReview(MaterialReader.Inspect(Store.ReadTopic(topic.Folder))) : Fixture());
            }
        }
    }
}
