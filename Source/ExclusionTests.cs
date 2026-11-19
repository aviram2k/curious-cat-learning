using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Education2k
{
    public static partial class Tests
    {
        public static MaterialReview HistoryExclusionPreview(string root)
        {
            var store = new Store(root); var topic = store.Topics().First(t => t.Name.Contains("היסטוריה"));
            var snapshot = MaterialReader.Inspect(topic);
            var review = new MaterialReviewService(new CodexClient(root)).Inspect(topic, snapshot, CancellationToken.None).GetAwaiter().GetResult();
            MaterialScope.Preview(topic, snapshot, review); var selection = MaterialScope.Select(topic, snapshot, review);
            foreach (var file in snapshot.Files)
                foreach (var visual in file.Visuals.Where(v => v.Reading == null || v.Reading.Status != "read"))
                    if (selection.Snapshot.Files.SelectMany(f => f.Visuals).Any(v => v.Id == visual.Id)) throw new InvalidOperationException("An unread history illustration survived source filtering.");
            File.WriteAllText(Path.Combine(root, "Build", "history-exclusion-test.txt"), "PASS: History source filtering retains " + selection.Units.Count + " fully read source units and excludes:\n" + String.Join("\n", selection.Excluded) + "\nNo exam or student record was created or changed.", Encoding.UTF8);
            return review;
        }
        sealed class ScopeModel : IModelClient
        {
            public readonly List<string> Prompts = new List<string>();
            public readonly List<bool> Searches = new List<bool>();
            public Func<string, string, object> Reply;
            public Task<T> Ask<T>(string prompt, string schema, bool search, int seconds, CancellationToken token)
            {
                token.ThrowIfCancellationRequested(); if (schema == "exam") { Prompts.Add(prompt); Searches.Add(search); }
                return Task.FromResult((T)Reply(prompt, schema));
            }
        }
        static Exam ScopeExam(string id)
        {
            var exam = Fixture(); foreach (var q in exam.Questions) q.SourceUnitIds.Add(id); return exam;
        }
        static void ExclusionChecks(string temp)
        {
            var store = new Store(Path.Combine(temp, "exclusion")); var topic = TestTopic(store, "בדיקה כללית של החרגת חומר", false);
            File.WriteAllText(topic.Materials[0], "SAFE_SOURCE_TEXT", Encoding.UTF8);
            File.WriteAllText(Path.Combine(topic.Folder, "מקור בעייתי.txt"), "EXCLUDED_SOURCE_TEXT", Encoding.UTF8); topic = Store.ReadTopic(topic.Folder);
            var snapshot = MaterialReader.Inspect(topic); var review = TestReview(snapshot);
            review.Summary = "EXCLUDED_REVIEW_SUMMARY";
            review.Files.Single(f => f.FileName == "מקור בעייתי.txt").Status = "partial";
            review.Files.Single(f => f.FileName == "מקור בעייתי.txt").Explanation = "EXCLUDED_REVIEW_FACTS";
            var selection = MaterialScope.Exclude(topic, snapshot, review); string id = selection.Units.Single().Id;
            Assert(selection.Excluded.Single() == "מקור בעייתי.txt" && selection.Snapshot.Context.Contains("SAFE_SOURCE_TEXT") && !selection.Snapshot.Context.Contains("EXCLUDED_SOURCE_TEXT"), "Exclusion removes an entire ambiguous source from the generation context while keeping a clean source");
            MaterialScope.Preview(topic, snapshot, review);
            Assert(review.HasUsableMaterial && review.ExclusionPreview.SequenceEqual(selection.Excluded), "The warning dialog previews exactly which material the exclusion option will remove");
            var old = Fixture(); old.Number = 9; old.Questions[0].Prompt = "EXCLUDED_PRIOR_EXAM"; Store.Write(store.ExamPath(topic, 9), old);
            var model = new ScopeModel { Reply = (prompt, schema) => schema == "materials" ? (object)review : ScopeExam(id) };
            var service = new ExamService(store, model); var result = service.GetExam(topic, 1, CancellationToken.None, r => MaterialDecision.ExcludeProblematic).GetAwaiter().GetResult();
            Assert(model.Searches.SequenceEqual(new[] { false }) && model.Prompts.Single().Contains("SAFE_SOURCE_TEXT") && !new[] { "EXCLUDED_SOURCE_TEXT", "EXCLUDED_REVIEW_FACTS", "EXCLUDED_REVIEW_SUMMARY", "EXCLUDED_PRIOR_EXAM" }.Any(model.Prompts.Single().Contains), "Filtered generation receives no excluded text through source content, reviews or previous exams and disables web search");
            Assert(result.FilteredSources && result.ExcludedMaterials.SequenceEqual(selection.Excluded) && result.Questions.All(q => q.SourceReferences.All(r => r.Contains("סיכום.txt"))), "Filtered shared exams persist their reduced scope and only approved source citations");
            var cached = service.GetExam(topic, 1, CancellationToken.None, r => { throw new Exception("Already excluded material must not require approval again."); }).GetAwaiter().GetResult();
            Assert(cached.FilteredSources && model.Prompts.Count == 1, "A filtered cached exam is reused with its original scope without repeated source approval or regeneration");
            var quiz = service.GetQuiz(topic, 1, CancellationToken.None).GetAwaiter().GetResult();
            Assert(quiz.FilteredSources && quiz.ExcludedMaterials.SequenceEqual(result.ExcludedMaterials) && store.CreateAttempt(topic, quiz, false).FilteredSources, "Short quizzes and student attempts retain the reduced material scope");
            var bad = ScopeExam("not-an-approved-unit"); Reject(() => selection.Apply(bad), "Filtered questions cannot cite an excluded or invented source unit");
            var noIds = Fixture(); Reject(() => selection.Apply(noIds), "Every question in a filtered exam must identify an approved source unit");
            var allBad = TestReview(snapshot, true); Reject(() => MaterialScope.Exclude(topic, snapshot, allBad), "No exam is created when exclusion leaves no fully read material");
            var cachedReview = Store.Json.Deserialize<MaterialReview>(Store.Json.Serialize(review)); cachedReview.ExistingExam = true;
            Reject(() => MaterialReviewService.Confirm(cachedReview, r => MaterialDecision.ExcludeProblematic, CancellationToken.None), "A third-party caller cannot change the scope of an already saved shared exam");
            Assert(Store.Read<Exam>(store.ExamPath(topic, 9)).Questions[0].Prompt == "EXCLUDED_PRIOR_EXAM", "Exclusion never rewrites other shared exams or their question sets");

            var visual = new MaterialFileContent { FileName = "מצגת בנושא אחר.pptx", Status = "partial", MustWarn = true, CanResolveVisuals = true, Text = "UNSAFE_WHOLE_DOCUMENT" };
            visual.Visuals.Add(new SourceVisual { Id = "safe-page", FileName = visual.FileName, Label = "שקופית 3", Reading = new VisualReading { Status = "read", Text = "SAFE_PAGE_TEXT", Description = "תרשים קריא" } });
            visual.Visuals.Add(new SourceVisual { Id = "bad-page", FileName = visual.FileName, Label = "שקופית 7", Reading = new VisualReading { Status = "partial", Text = "EXCLUDED_PAGE_TEXT", Description = "מפה מטושטשת" } });
            visual.Visuals.Add(new SourceVisual { Id = "bad-page-child", ParentId = "bad-page", FileName = visual.FileName, Label = "תמונה מתוך שקופית 7", Reading = new VisualReading { Status = "read", Text = "EXCLUDED_CHILD_TEXT", Description = "תמונת מקור" } });
            var pageSnapshot = new MaterialSnapshot { Files = new List<MaterialFileContent> { visual } };
            var pageSelection = MaterialScope.Select(topic, pageSnapshot, TestReview(pageSnapshot));
            Assert(pageSelection.Units.Single().Id == "safe-page" && pageSelection.Excluded.Single().Contains("שקופית 7") && pageSelection.Snapshot.Files.Single().Visuals.All(v => v.Id == "safe-page") && !new[] { "UNSAFE_WHOLE_DOCUMENT", "EXCLUDED_PAGE_TEXT", "EXCLUDED_CHILD_TEXT" }.Any(pageSelection.Snapshot.Context.Contains), "General page-level exclusion retains clean slides and removes all text and pictures belonging to a problematic slide");
            var globallyUnread = TestReview(pageSnapshot); globallyUnread.Files[0].Status = "unread";
            Assert(MaterialScope.Select(topic, pageSnapshot, globallyUnread).Units.Count == 0, "A globally unread file cannot be partially admitted using local page data");

            var lateTopic = TestTopic(store, "בעיה שהתגלתה ביצירה", false);
            File.WriteAllText(Path.Combine(lateTopic.Folder, "בעייתי.txt"), "LATE_BAD_SOURCE", Encoding.UTF8); lateTopic = Store.ReadTopic(lateTopic.Folder);
            var lateSnapshot = MaterialReader.Inspect(lateTopic); var lateReview = TestReview(lateSnapshot); int generation = 0;
            var lateRestricted = Store.Json.Deserialize<MaterialReview>(Store.Json.Serialize(lateReview)); lateRestricted.Files.Single(f => f.FileName == "בעייתי.txt").Status = "partial";
            string lateId = MaterialScope.Select(lateTopic, lateSnapshot, lateRestricted).Units.Single().Id;
            var lateModel = new ScopeModel { Reply = (prompt, schema) => {
                if (schema == "materials") return lateReview;
                generation++; var draft = ScopeExam(lateId);
                if (generation == 1) draft.SourceIssues.Add(new MaterialIssue { FileName = "בעייתי.txt", Explanation = "בעיה חדשה", Impact = "חומר חלקי", Recommendation = "להוציא את המקור" });
                return draft;
            } };
            var regenerated = new ExamService(store, lateModel).GetExam(lateTopic, 1, CancellationToken.None, r => MaterialDecision.ExcludeProblematic).GetAwaiter().GetResult();
            Assert(generation == 2 && lateModel.Searches.SequenceEqual(new[] { true, false }) && !lateModel.Prompts.Last().Contains("LATE_BAD_SOURCE") && regenerated.FilteredSources, "Choosing exclusion for a newly discovered issue discards the draft and regenerates from the remaining sources before saving");

            var blockedTopic = TestTopic(store, "אין חומר לאחר החרגה", false); var blockedSnapshot = MaterialReader.Inspect(blockedTopic); var blockedReview = TestReview(blockedSnapshot, true);
            var blockedModel = new ScopeModel { Reply = (prompt, schema) => blockedReview };
            Reject(() => new ExamService(store, blockedModel).GetExam(blockedTopic, 1, CancellationToken.None, r => MaterialDecision.ExcludeProblematic).GetAwaiter().GetResult(), "Excluding all sources stops before a model generation request");
            Assert(blockedModel.Prompts.Count == 0 && !File.Exists(store.ExamPath(blockedTopic, 1)), "A source-less exclusion produces neither questions nor a saved exam");

            var warningTopic = TestTopic(store, "החרגה עם בעיה נוספת", false);
            File.WriteAllText(Path.Combine(warningTopic.Folder, "נוסף.txt"), "חומר חלקי", Encoding.UTF8); warningTopic = Store.ReadTopic(warningTopic.Folder);
            var warningSnapshot = MaterialReader.Inspect(warningTopic); var warningReview = TestReview(warningSnapshot); warningReview.Files.Single(f => f.FileName == "נוסף.txt").Status = "partial";
            string warningId = MaterialScope.Select(warningTopic, warningSnapshot, warningReview).Units.Single().Id;
            var warningModel = new ScopeModel { Reply = (prompt, schema) => {
                if (schema == "materials") return warningReview;
                var draft = ScopeExam(warningId); draft.SourceIssues.Add(new MaterialIssue { FileName = "מקור נוסף שלא צורף", Explanation = "בעיה חדשה", Impact = "כיסוי מוגבל נוסף", Recommendation = "להוסיף את החומר" }); return draft;
            } };
            int choices = 0; var warningService = new ExamService(store, warningModel);
            var warningExam = warningService.GetExam(warningTopic, 1, CancellationToken.None, r => ++choices == 1 ? MaterialDecision.ExcludeProblematic : MaterialDecision.Continue).GetAwaiter().GetResult();
            Assert(warningExam.SourceWarningsRemain && Declines(() => warningService.GetExam(warningTopic, 1, CancellationToken.None, r => MaterialDecision.Cancel).GetAwaiter().GetResult()), "Excluding an initial issue never hides a different unresolved issue approved later in generation");
        }
    }
}
