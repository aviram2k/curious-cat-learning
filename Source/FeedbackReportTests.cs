using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Education2k
{
    public static partial class Tests
    {
        static Attempt FeedbackFixture(Store store, Topic topic, out Exam exam)
        {
            exam = Fixture(); var question = exam.Questions[5]; question.Context = "לשני אטומים יש שמונה פרוטונים בכל אחד. בדיקת משוב סינתטית.";
            question.Parts = new List<QuestionPart> {
                new QuestionPart { Id = "a", Type = "text", Prompt = "מה המספר האטומי?", Points = 3, ExpectedAnswer = "8." },
                new QuestionPart { Id = "b", Type = "text", Prompt = "כמה פרוטונים יש באטום השני?", Points = 3, ExpectedAnswer = "8 פרוטונים." },
                new QuestionPart { Id = "c", Type = "text", Prompt = "האם שני האטומים שייכים לאותו יסוד? נמקו.", Points = 4, ExpectedAnswer = "שניהם שייכים לאותו יסוד, כי בכל אחד מהם יש שמונה פרוטונים. מספר הפרוטונים קובע את זהות היסוד." }
            };
            var attempt = Answered(store, topic, exam, false); attempt.Answers[0].HintUsed = true;
            attempt.Answers[2].SelectedOption = 1; attempt.Answers[2].Result.Fraction = 0;
            attempt.Answers[3].SelectedOption = -1; attempt.Answers[3].Result.Fraction = 0;
            var table = attempt.Answers[4]; table.Cells[0][0] = "תשובה לא מדויקת"; table.Result.Fraction = 0.5;
            table.Result.ReportCorrections.Add(new FeedbackCorrection { PartId = "", Note = "בתא נכתבה תכונה אחרת מזו שהתבקשה." }); table.Result.ReportVersion = FeedbackReport.Version;
            var answer = attempt.Answers[5]; answer.Parts[0].Text = "8"; answer.Parts[1].Text = "8"; answer.Parts[2].Text = "לפי מספר הפרוטונים"; Rules.SyncParts(question, answer);
            answer.Result.Fraction = 0.8; answer.Result.Comment = "שני המספרים נכונים. חסרה ההשוואה בין האטומים והמסקנה ממנה.";
            answer.Result.ReportCorrections.Add(new FeedbackCorrection { PartId = "c", Note = "לא השווית בין שני האטומים ולא כתבת אם הם שייכים לאותו יסוד." }); answer.Result.ReportVersion = FeedbackReport.Version;
            foreach (var a in attempt.Answers) a.Result.AnswerSnapshot = Rules.Snapshot(a);
            ExamService.Complete(exam, attempt); return attempt;
        }
        static string FeedbackGradeState(Attempt attempt)
        {
            return Store.Json.Serialize(new { attempt.Id, attempt.TopicName, attempt.ExamNumber, attempt.Practice, attempt.Quiz, attempt.StartedUtc, attempt.FinishedUtc, attempt.Score, attempt.Passed,
                Answers = attempt.Answers.Select(a => new { a.QuestionId, a.Text, a.SelectedOption, a.Cells, a.Parts, a.HintUsed, a.HintShownAutomatically,
                    Result = new { a.Result.Fraction, a.Result.Comment, a.Result.AnswerSnapshot, a.Result.CheckedUtc }, Tries = a.Tries.Select(t => new { t.QuestionId, t.Fraction, t.Comment, t.AnswerSnapshot, t.CheckedUtc }) }) });
        }
        static void FeedbackChecks(string temp)
        {
            var store = new Store(Path.Combine(temp, "feedback")); var topic = TestTopic(store, "משוב סינתטי", false); Exam exam; var attempt = FeedbackFixture(store, topic, out exam);
            var hint = attempt.Answers[0];
            Assert(FeedbackReport.Correct(hint) && FeedbackReport.Notes(exam.Questions[0], hint).Count == 0 && FeedbackReport.Success(hint, false).Contains("רק בגלל השימוש ברמז") && Rules.Earned(exam.Questions[0], hint) == 10, "A fully correct hinted answer has good understanding, no corrections, and an explicit hint-only deduction without altering points");
            Assert(FeedbackReport.Success(attempt.Answers[1], false) == "כל הכבוד!" && FeedbackReport.Success(hint, true) == "כל הכבוד!", "Correct answers receive only congratulations and practice never claims that hint points were deducted");
            Assert(!FeedbackReport.Correct(attempt.Answers[5]) && FeedbackReport.Notes(exam.Questions[5], attempt.Answers[5]).Single().PartId == "c", "A partially correct structured answer shows only the section flagged by the grading feedback, without guessing from reference wording");
            Assert(!FeedbackReport.NeedsModel(exam.Questions[3], attempt.Answers[3]) && FeedbackReport.Notes(exam.Questions[3], attempt.Answers[3]).Single().Note.Contains("לא נכתבה") && !FeedbackReport.NeedsModel(exam.Questions[2], attempt.Answers[2]), "Blank and incorrect multiple-choice answers have local report explanations without a model request");
            Reject(() => {
                if (!FeedbackReport.Valid(exam.Questions[5], attempt.Answers[5], new List<FeedbackCorrection> { new FeedbackCorrection { PartId = "unknown", Note = "טעות" } })) throw new InvalidOperationException();
            }, "Unknown section identifiers cannot be used to hide or mislabel a correction");
            Assert(!FeedbackReport.Valid(exam.Questions[5], attempt.Answers[5], new List<FeedbackCorrection>()) && !FeedbackReport.Valid(exam.Questions[0], hint, new List<FeedbackCorrection> { new FeedbackCorrection { PartId = "", Note = "טעות שהומצאה" } }), "Partial results require an explanation and correct results cannot contain invented corrections");

            var legacy = Store.Json.Deserialize<Attempt>(Store.Json.Serialize(attempt));
            foreach (var a in legacy.Answers) { a.Result.ReportVersion = 0; a.Result.ReportCorrections.Clear(); }
            string protectedState = FeedbackGradeState(legacy);
            var fake = new FakeModel { Reply = schema => { if (schema != "report") throw new Exception("Report editing must not request a grading response."); return new FeedbackReportResponse { Results = new[] { 4, 5 }.Select(i => new QuestionCorrections {
                QuestionId = legacy.Answers[i].QuestionId,
                ReportCorrections = new List<FeedbackCorrection> { new FeedbackCorrection { PartId = i == 5 ? "c" : "", Note = "חסר הפירוט שהתבקש בסעיף הזה." } }
            }).ToList() }; } };
            var service = new FeedbackReportService(fake);
            Assert(service.Simplify(exam, legacy, CancellationToken.None).GetAwaiter().GetResult() && FeedbackGradeState(legacy) == protectedState, "Simplifying an existing report adds presentation notes without changing its grade, answers, original comments, snapshots, timestamps or attempt history");
            Assert(FeedbackReport.Notes(exam.Questions[5], legacy.Answers[5]).Single().PartId == "c" && !service.Simplify(exam, legacy, CancellationToken.None).GetAwaiter().GetResult() && fake.Calls.Count == 1, "Existing report section corrections are cached and reopening the report requires no repeated model call");
            string file = Path.Combine(store.Root, "report-roundtrip.json"); Store.Write(file, legacy); var loaded = Store.Read<Attempt>(file);
            Assert(FeedbackGradeState(loaded) == protectedState && loaded.Answers[5].Result.ReportCorrections.Single().PartId == "c", "Compact report details survive persistence while all original grading information remains intact");

            var unsafeReply = Store.Json.Deserialize<Attempt>(Store.Json.Serialize(attempt)); foreach (var a in unsafeReply.Answers) a.Result.ReportVersion = 0;
            string beforeRejected = Store.Json.Serialize(unsafeReply);
            var wrongTarget = new FakeModel { Reply = schema => new FeedbackReportResponse { Results = new List<QuestionCorrections> {
                new QuestionCorrections { QuestionId = "q5", ReportCorrections = new List<FeedbackCorrection> { new FeedbackCorrection { PartId = "", Note = "חסר פרט" } } },
                new QuestionCorrections { QuestionId = "q6", ReportCorrections = new List<FeedbackCorrection> { new FeedbackCorrection { PartId = "unknown", Note = "שיוך שגוי" } } }
            } } };
            Reject(() => new FeedbackReportService(wrongTarget).Simplify(exam, unsafeReply, CancellationToken.None).GetAwaiter().GetResult(), "A report-editing response that targets an unknown section is rejected");
            Assert(Store.Json.Serialize(unsafeReply) == beforeRejected, "An invalid report edit applies no partial changes to any question");
            var cancelled = new CancellationTokenSource(); cancelled.Cancel(); bool stopped = false;
            try { new FeedbackReportService(fake).Simplify(exam, unsafeReply, cancelled.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { stopped = true; }
            Assert(stopped && Store.Json.Serialize(unsafeReply) == beforeRejected, "Cancelling a report update retains all original information");

            var newAnswers = new List<Answer> { attempt.Answers[4], attempt.Answers[5] };
            var grading = new FakeModel { Reply = schema => new GradeResponse { Results = newAnswers.Select(a => new Feedback { QuestionId = a.QuestionId, Fraction = a.Result.Fraction, Comment = "יש להשלים את הפרט החסר.", ReportCorrections = a.Result.ReportCorrections }).ToList() } };
            var graded = new ExamService(store, grading).Grade(exam, newAnswers, CancellationToken.None).GetAwaiter().GetResult();
            Assert(graded.All(r => r.ReportVersion == FeedbackReport.Version) && graded.Single(r => r.QuestionId == "q6").ReportCorrections.Single().PartId == "c", "Newly graded exams retain concise section-specific corrections directly from the grading response");
            var badGrading = new FakeModel { Reply = schema => new GradeResponse { Results = new List<Feedback> { new Feedback { QuestionId = "q6", Fraction = 0.8, Comment = "חסר פרט", ReportCorrections = new List<FeedbackCorrection>() } } } };
            Reject(() => new ExamService(store, badGrading).Grade(exam, new List<Answer> { attempt.Answers[5] }, CancellationToken.None).GetAwaiter().GetResult(), "New partial grading results without report corrections are rejected before completion");
        }
        public static string PrepareFeedbackPreview(string root)
        {
            string previewRoot = Path.Combine(root, "Build", "UiFeedback-" + Guid.NewGuid().ToString("N")); var store = new Store(previewRoot); var topic = TestTopic(store, "בדיקת משוב", false); Exam exam;
            var attempt = FeedbackFixture(store, topic, out exam); Store.Write(store.ExamPath(topic, 1), exam); store.Save(store.Login("בדיקת", "משוב"), attempt); return previewRoot;
        }
    }
}
