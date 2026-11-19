using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Education2k
{
    public static partial class Tests
    {
        static readonly List<string> report = new List<string>();
        static void Assert(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); report.Add("PASS: " + label); }
        static void Reject(Action action, string label) { try { action(); } catch (InvalidOperationException) { report.Add("PASS: " + label); return; } throw new Exception("FAIL (accepted invalid data): " + label); }
        static Exam Fixture()
        {
            var exam = new Exam { Number = 1, Title = "מבחן בדיקה בעברית", DurationMinutes = 60, Introduction = "ננסה ונלמד", CreatedUtc = DateTime.UtcNow.ToString("o"), MaterialHash = "fixture", PresentationVersion = 2 };
            for (int i = 0; i < 6; i++) exam.Questions.Add(new Question {
                Id = "q" + (i + 1), Type = i == 4 ? "table" : i == 5 ? "text" : "multiple_choice", Topic = "מדעים", Prompt = "שאלה בעברית " + i,
                Points = i < 4 ? 20 : 10, EstimatedMinutes = 10, Hint = "קטע לימוד בעברית שמסביר את משמעות ההגדרה ואת הדרך לפתרון. אפשר לקרוא את הנתונים ולחבר אותם לעיקרון המדעי. למשל, תכונה של חומר עוזרת להשוות בינו לבין חומר אחר. מתוך ההסבר אפשר לבחור תשובה ולנסח נימוק מתאים.", ExpectedAnswer = "תשובה נכונה", Rubric = "לפי ההגדרה", CorrectOption = i < 4 ? 0 : -1,
                Options = i < 4 ? new List<string> { "נכון", "לא נכון" } : new List<string>(), SourceReferences = new List<string> { "מקור בדיקה" },
                Rows = i == 4 ? new List<string> { "חומר" } : new List<string>(), Columns = i == 4 ? new List<string> { "תכונה" } : new List<string>(),
                CorrectCells = i == 4 ? new List<List<string>> { new List<string> { "תכונה נכונה" } } : new List<List<string>>()
            });
            exam.Questions[5].Context = "נתונים לשאלה";
            exam.Questions[5].Parts.Add(new QuestionPart { Id = "a", Type = "text", Prompt = "הסבירו את הנתון.", Points = 10, ExpectedAnswer = "תשובה נכונה" });
            return exam;
        }
        static Attempt Answered(Store store, Topic topic, Exam exam, bool practice)
        {
            var attempt = store.CreateAttempt(topic, exam, practice);
            foreach (var a in attempt.Answers)
            {
                var q = exam.Questions.Single(x => x.Id == a.QuestionId);
                if (q.Type == "multiple_choice") a.SelectedOption = q.CorrectOption;
                else if (q.Type == "table") a.Cells = q.CorrectCells.Select(r => r.ToList()).ToList();
                else a.Text = q.ExpectedAnswer;
                foreach (var part in q.Parts)
                {
                    var pa = a.Parts.Single(p => p.PartId == part.Id); pa.Text = part.ExpectedAnswer; pa.Cells = part.CorrectCells.Select(r => r.ToList()).ToList();
                }
                Rules.SyncParts(q, a);
                a.Result = new Feedback { QuestionId = a.QuestionId, Fraction = 1, Comment = "נכון", AnswerSnapshot = Rules.Snapshot(a), CheckedUtc = DateTime.UtcNow.ToString("o") }; a.Tries.Add(a.Result);
            }
            return attempt;
        }
        public static int Run(string sampleRoot)
        {
            string temp = Path.Combine(Path.GetTempPath(), "Education2k-tests-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new Store(temp); var topicFolder = Path.Combine(temp, "מבחנים", "מדעים"); Directory.CreateDirectory(topicFolder);
                var input = Inputs.Hebrew(); input.FlowDirection = System.Windows.FlowDirection.LeftToRight; input.Text = "טקסט בעברית CO₂";
                Assert(input.FlowDirection == System.Windows.FlowDirection.RightToLeft && input.TextAlignment == System.Windows.TextAlignment.Left, "Hebrew input restores RTL and logical-start alignment after keyboard direction changes");
                File.WriteAllText(Path.Combine(topicFolder, "_describe.txt"), "תיאור בעברית", Encoding.UTF8);
                File.WriteAllText(Path.Combine(topicFolder, "סיכום.txt"), "מים וחומר", Encoding.UTF8);
                File.WriteAllText(Path.Combine(topicFolder, "_generated.tmp"), "ignored");
                var topic = store.Topics().Single(); Assert(topic.Description == "תיאור בעברית", "Hebrew topic descriptions round-trip"); Assert(topic.Materials.Length == 1, "Generated metadata is excluded from source materials");
                var alice = store.Login("תלמידה", "בדיקה"); var bob = store.Login("תלמיד", "נוסף"); Assert(store.Login("תלמידה", "בדיקה").CreatedUtc == alice.CreatedUtc, "Existing student profile is reused");
                Reject(delegate { store.Login("../unsafe", "name"); }, "Student folder traversal is rejected"); Reject(delegate { store.Login("", "name"); }, "Empty names are rejected");
                var exam = Fixture(); Rules.Validate(exam); Assert(true, "Mixed question exam passes structural validation");
                Store.Write(store.ExamPath(topic, 1), exam); Assert(store.LoadExam(topic, 1).Title == exam.Title, "Shared exam persists with Hebrew content");
                var cached = new ExamService(store, new CodexClient(temp)).GetExam(topic, 1, CancellationToken.None).GetAwaiter().GetResult(); Assert(cached.Number == 1, "Existing exam is reused without calling Codex");
                Assert(topic.Materials.Length == store.Topics().Single().Materials.Length, "Saved exam is not treated as learning material");
                var attempt = Answered(store, topic, exam, false); ExamService.Complete(exam, attempt); Assert(attempt.Score == 100 && attempt.Passed, "Correct answers earn 100 and pass"); store.Save(alice, attempt);
                Assert(Rules.NextNumber(store.History(alice), topic.Name) == 2, "Passing student advances to exam 2"); Assert(Rules.NextNumber(store.History(bob), topic.Name) == 1, "New student still starts at shared exam 1");
                var hint = Answered(store, topic, exam, false); hint.Answers[0].HintUsed = true; ExamService.Complete(exam, hint); Assert(hint.Score == 90, "Hint halves the question points"); Assert(Rules.Earned(exam.Questions[0], hint.Answers[0]) == 10, "Repeated hint state does not stack deductions");
                hint.Answers[0].Result.Fraction = 0.5; Assert(Rules.Earned(exam.Questions[0], hint.Answers[0]) == 5, "Partial credit and hint penalty combine");
                var eighty = Answered(store, topic, exam, false); eighty.Answers[0].Result.Fraction = 0; ExamService.Complete(exam, eighty); Assert(eighty.Score == 80 && !eighty.Passed, "Exactly 80 does not pass the strict threshold");
                var practice = Answered(store, topic, exam, true); ExamService.Complete(exam, practice); Assert(practice.Score == null && !practice.Passed, "Practice has no grade and never counts as passing"); store.Save(bob, practice); Assert(Rules.NextNumber(store.History(bob), topic.Name) == 1, "Practice does not advance the exam index");
                Assert(practice.Answers.All(a => a.HintUsed && a.HintShownAutomatically) && attempt.Answers.All(a => !a.HintUsed), "Practice opens every hint automatically while real exams keep hints closed");
                Assert(practice.Answers.All(a => !Rules.NeedsReview(a)), "An automatic practice hint alone does not mark correct answers as weak");
                Assert(store.History(bob).Single(a => a.Id == practice.Id).Answers.All(a => a.HintShownAutomatically), "Automatic hint exposure is retained in saved practice attempts");
                var oldPractice = new Attempt { Practice = true }; var oldAnswer = new Answer { Text = "תשובה שנשמרה" };
                Assert(Rules.OpenPracticeHint(oldPractice, oldAnswer) && oldAnswer.HintUsed && oldAnswer.HintShownAutomatically && oldAnswer.Text == "תשובה שנשמרה" && !Rules.OpenPracticeHint(oldPractice, oldAnswer), "Resumed legacy practice opens its hint once and preserves the answer");
                oldAnswer.Tries.Add(new Feedback { Fraction = 0 }); Assert(Rules.NeedsReview(oldAnswer), "Errors still identify review topics when practice hints open automatically");
                var partial = Answered(store, topic, exam, true); partial.Answers[0].Result.Fraction = 0.5; Reject(delegate { ExamService.Complete(exam, partial); }, "Practice cannot finish before all answers are correct");
                var edited = Answered(store, topic, exam, false); edited.Answers[5].Parts[0].Text += "changed"; Reject(delegate { ExamService.Complete(exam, edited); }, "Changing a checked subpart invalidates completion");
                var draft = store.CreateAttempt(topic, exam, false); draft.Answers[0].HintUsed = true; draft.Answers[5].Parts[0].Text = "תשובה שנשמרת"; Rules.SyncParts(exam.Questions[5], draft.Answers[5]); draft.CurrentQuestion = 5; store.Save(bob, draft); draft.Answers[5].Parts[0].Text += " שוב"; Rules.SyncParts(exam.Questions[5], draft.Answers[5]); store.Save(bob, draft);
                var loaded = store.History(bob).Single(a => a.Id == draft.Id); Assert(loaded.Answers[0].HintUsed && loaded.Answers[5].Parts[0].Text == "תשובה שנשמרת שוב" && loaded.CurrentQuestion == 5, "Draft subparts, hint and position survive atomic saves"); Assert(loaded.FinishedUtc == null, "Draft remains resumable");
                var start = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc); draft.StartedUtc = start.ToString("o");
                Assert(!Rules.ShowWarning(draft, start.AddMinutes(49.99)), "No early timing warning"); Assert(Rules.ShowWarning(draft, start.AddMinutes(50)), "Quiet warning begins at minute 50"); Assert(Rules.ShowWarning(draft, start.AddMinutes(50.99)), "Warning remains inside the one-minute window"); Assert(!Rules.ShowWarning(draft, start.AddMinutes(51)), "Warning disappears after one minute"); Assert(!Rules.ShowWarning(draft, start.AddMinutes(75)) && draft.FinishedUtc == null, "Exceeding one hour never auto-submits");
                draft.WarningShown = true; Assert(!Rules.ShowWarning(draft, start.AddMinutes(50.5)), "Warning is issued only once"); draft.WarningShown = false; draft.Practice = true; Assert(!Rules.ShowWarning(draft, start.AddMinutes(50)), "Practice has no timing warning");
                var quiz = QuizBuilder.Create(exam); Assert(quiz.IsQuiz && quiz.Questions.Count == 3 && quiz.DurationMinutes == 35, "Quiz contains half the questions and recommends 35 minutes");
                Assert(quiz.Questions.Sum(q => q.Points) == 100 && quiz.Questions.Sum(q => q.EstimatedMinutes) == 35, "Quiz points and times have exact totals");
                Assert(quiz.Questions.Select(q => q.Type).Distinct().Count() == 3, "Quiz retains all available question types");
                Assert(exam.Questions.Count == 6 && exam.DurationMinutes == 60 && exam.Questions[0].Points == 20, "Quiz construction does not mutate its source exam");
                var quiz2 = QuizBuilder.Create(exam); Assert(String.Join(",", quiz.Questions.Select(q => q.Id)) == String.Join(",", quiz2.Questions.Select(q => q.Id)), "Shared quiz selection is deterministic");
                Store.Write(store.ExamPath(topic, 1, true), quiz); Assert(store.LoadExam(topic, 1, true).IsQuiz && !store.LoadExam(topic, 1).IsQuiz, "Quiz and full exam have independent shared cache files");
                var quizAttempt = Answered(store, topic, quiz, false); ExamService.Complete(quiz, quizAttempt); Assert(quizAttempt.Quiz && quizAttempt.Score == 100 && quizAttempt.Passed, "Quiz is graded on its own 100-point scale"); store.Save(bob, quizAttempt);
                Assert(Rules.NextNumber(store.History(bob), topic.Name, true) == 2 && Rules.NextNumber(store.History(bob), topic.Name) == 1, "Passing a quiz advances quizzes only");
                Assert(Rules.NextNumber(store.History(alice), topic.Name, true) == 1, "Passing a full exam does not advance quizzes");
                var quizClock = store.CreateAttempt(topic, quiz, false); quizClock.StartedUtc = start.ToString("o");
                Assert(!Rules.ShowWarning(quizClock, start.AddMinutes(24.99)) && Rules.ShowWarning(quizClock, start.AddMinutes(25)), "Quiz warning starts at minute 25, ten minutes before 35");
                quizClock.WarningShown = true; quizClock.WarningShownUtc = start.AddMinutes(25.5).ToString("o");
                Assert(Rules.NoticeVisible(quizClock, start.AddMinutes(26.49)) && !Rules.NoticeVisible(quizClock, start.AddMinutes(26.5)), "Quiet warning stays visible for a full minute from display");
                Assert(!Rules.ShowWarning(quizClock, start.AddMinutes(45)) && quizClock.FinishedUtc == null, "Quiz never auto-submits after 35 minutes");
                var emptyParts = store.CreateAttempt(topic, exam, false).Answers[5]; Rules.SyncParts(exam.Questions[5], emptyParts); Assert(!emptyParts.HasContent, "Section labels alone never count as answered content");
                var legacy = Store.Json.Deserialize<Attempt>("{\"Id\":\"legacy\",\"Practice\":false,\"Answers\":[{\"QuestionId\":\"q6\",\"Text\":\"תשובה ישנה\",\"SelectedOption\":-1}]}");
                string oldSnapshot = Rules.Snapshot(legacy.Answers[0]); Rules.InitializeParts(exam.Questions[5], legacy.Answers[0]); Assert(!legacy.Quiz && Rules.TimeBudget(legacy) == 60 && legacy.Answers[0].Parts.Count == 0 && Rules.Snapshot(legacy.Answers[0]) == oldSnapshot, "Legacy answers and timing remain compatible without rewriting text");
                var invalidParts = Fixture(); invalidParts.Questions[5].Parts[0].Points = 3; Reject(delegate { Rules.Validate(invalidParts); }, "Subpart points must add up to the original question score");
                var odd = Fixture(); odd.Questions.RemoveAt(3); odd.Questions[0].Points += 20; odd.Questions[0].EstimatedMinutes += 10; odd.PresentationVersion = 0; odd.Questions.Add(new Question { Id = "q7", Type = "multiple_choice", Topic = "מדעים", Prompt = "שאלה נוספת", Points = 1, EstimatedMinutes = 1, Hint = "רמז", ExpectedAnswer = "נכון", Rubric = "נכון", CorrectOption = 0, Options = new List<string> { "נכון", "לא" }, SourceReferences = new List<string> { "מקור" } }); odd.Questions.Add(new Question { Id = "q8", Type = "multiple_choice", Topic = "מדעים", Prompt = "עוד שאלה", Points = 1, EstimatedMinutes = 1, Hint = "רמז", ExpectedAnswer = "נכון", Rubric = "נכון", CorrectOption = 0, Options = new List<string> { "נכון", "לא" }, SourceReferences = new List<string> { "מקור" } }); odd.Questions[0].Points -= 2; odd.Questions[0].EstimatedMinutes -= 2;
                Assert(QuizBuilder.Create(odd).Questions.Count == 4, "Odd source question counts round up when halved");
                var malformed = Fixture(); malformed.Questions[1].Id = malformed.Questions[0].Id; Reject(delegate { Rules.Validate(malformed); }, "Duplicate question IDs are rejected");
                malformed = Fixture(); malformed.Questions[0].Points = 21; Reject(delegate { Rules.Validate(malformed); }, "Incorrect total points are rejected");
                malformed = Fixture(); malformed.Questions[4].CorrectCells.Clear(); Reject(delegate { Rules.Validate(malformed); }, "Invalid table shapes are rejected");
                malformed = Fixture(); malformed.Questions[0].CorrectOption = 8; Reject(delegate { Rules.Validate(malformed); }, "Out-of-range answer indexes are rejected");
                var hash = MaterialReader.Hash(topic); File.AppendAllText(topic.Materials[0], "שינוי"); Assert(MaterialReader.Hash(topic) != hash, "Source changes are detected by content hash");
                var sample = new Store(sampleRoot).Topics().SelectMany(t => t.Materials).FirstOrDefault(f => Path.GetExtension(f).Equals(".docx", StringComparison.OrdinalIgnoreCase));
                if (sample != null) { var content = MaterialReader.Extract(sample); Assert(!String.IsNullOrWhiteSpace(content) && content.Any(c => c >= '\u0590' && c <= '\u05ff'), "Actual sample DOCX extracts readable Hebrew"); File.WriteAllText(Path.Combine(sampleRoot, "Build", "sample-material.txt"), content, Encoding.UTF8); }
                MaterialSafety(temp, sampleRoot);
                ProgressChecks(temp);
                VisualChecks(temp, sampleRoot);
                ExclusionChecks(temp);
                report.Add("\n" + report.Count + " checks passed. No sample student data was modified.");
                File.WriteAllLines(Path.Combine(sampleRoot, "Build", "tests.txt"), report, Encoding.UTF8); return 0;
            }
            catch (Exception e) { report.Add(e.ToString()); File.WriteAllLines(Path.Combine(sampleRoot, "Build", "tests.txt"), report, Encoding.UTF8); return 1; }
            finally { if (Path.GetFullPath(temp).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(temp).StartsWith("Education2k-tests-")) Directory.Delete(temp, true); }
        }
        public static int LiveGrade(string root)
        {
            var store = new Store(root); var topic = store.Topics().First(); var exam = store.LoadExam(topic, 1);
            var answers = exam.Questions.Where(q => q.Type == "text").Take(2).Select(q => new Answer { QuestionId = q.Id, Text = q.ExpectedAnswer, Cells = q.CorrectCells.Select(r => r.ToList()).ToList() }).ToList();
            if (answers.Count < 2) throw new InvalidOperationException("Need two open questions for a meaningful grading test.");
            answers[1].Text = "אין תנועה בשום חומר. כל חלקיקי הסוכר עומדים ללא תנועה מוחלטת גם ברמת החלקיקים.";
            var table = exam.Questions.First(q => q.Type == "table");
            var tableAnswer = new Answer { QuestionId = table.Id, Cells = table.CorrectCells.Select(r => r.ToList()).ToList() };
            tableAnswer.Cells[0][0] = "לא ידוע"; answers.Add(tableAnswer);
            var results = new ExamService(store, new CodexClient(root)).Grade(exam, answers, CancellationToken.None).GetAwaiter().GetResult();
            if (results.Single(r => r.QuestionId == answers[0].QuestionId).Fraction < 0.99 || results.Single(r => r.QuestionId == answers[1].QuestionId).Fraction > 0.01 || results.Single(r => r.QuestionId == table.Id).Fraction <= 0 || results.Single(r => r.QuestionId == table.Id).Fraction >= 1)
                throw new InvalidOperationException("Live grading failed reference/wrong/partial-table expectations: " + Store.Json.Serialize(results));
            File.WriteAllText(Path.Combine(root, "Build", "grading-test.txt"), "PASS: Live Codex accepted a correct answer, rejected a wrong answer, and awarded partial credit for a partially completed table.\n" + Store.Json.Serialize(results), Encoding.UTF8); return 0;
        }
        public static int LiveUpdates(string root)
        {
            var store = new Store(root); var topic = store.Topics().First(); var exam = store.LoadExam(topic, 1); var quiz = store.LoadExam(topic, 1, true);
            Rules.Validate(exam); Rules.Validate(quiz);
            var q = exam.Questions.Single(question => question.Id == "q13");
            if (q.Parts.Count < 3 || q.Parts[0].Type != "table" || q.Hint.Length < 160) throw new InvalidOperationException("Question 13 needs a table, independent follow-up parts and an educational hint.");
            var attempt = store.CreateAttempt(topic, exam, false); var answer = attempt.Answers.Single(a => a.QuestionId == q.Id);
            foreach (var p in q.Parts) { var a = answer.Parts.Single(pa => pa.PartId == p.Id); a.Text = p.Type == "text" ? p.ExpectedAnswer : ""; a.Cells = p.CorrectCells.Select(r => r.ToList()).ToList(); }
            var service = new ExamService(store, new CodexClient(root)); var correct = service.Grade(exam, new List<Answer> { answer }, CancellationToken.None).GetAwaiter().GetResult().Single();
            if (correct.Fraction < 0.99) throw new InvalidOperationException("Complete subpart reference answer was not accepted.");
            var last = answer.Parts.Last(); last.Text = "";
            var partial = service.Grade(exam, new List<Answer> { answer }, CancellationToken.None).GetAwaiter().GetResult().Single();
            if (partial.Fraction <= 0 || partial.Fraction >= 1) throw new InvalidOperationException("A missing subpart must receive partial credit, not full credit.");
            File.WriteAllText(Path.Combine(root, "Build", "updates-live-test.txt"), "PASS: Cached question 13 has a table and separate answer sections; complete structured answers receive full credit; a missing section receives partial credit.\nQuiz: " + quiz.Questions.Count + " questions, " + quiz.DurationMinutes + " minutes.\nCorrect: " + correct.Fraction + "; partial: " + partial.Fraction, Encoding.UTF8); return 0;
        }
        public static string PreparePreview(string root)
        {
            string previewRoot = Path.Combine(root, "Build", "UiPreview2"); var preview = new Store(previewRoot);
            foreach (var topic in new Store(root).Topics())
            {
                string folder = Path.Combine(previewRoot, "מבחנים", topic.Name); Directory.CreateDirectory(folder);
                foreach (var file in Directory.GetFiles(topic.Folder))
                {
                    if (Path.GetFileName(file) == "_generation.lock") continue;
                    File.Copy(file, Path.Combine(folder, Path.GetFileName(file)), true);
                }
            }
            Directory.CreateDirectory(Path.Combine(previewRoot, "Schemas"));
            foreach (var schema in Directory.GetFiles(Path.Combine(root, "Schemas"), "*.json")) File.Copy(schema, Path.Combine(previewRoot, "Schemas", Path.GetFileName(schema)), true);
            return previewRoot;
        }
    }
}
