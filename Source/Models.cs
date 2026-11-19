using System;
using System.Collections.Generic;
using System.Linq;

namespace Education2k
{
    public class Topic
    {
        public string Name;
        public string Folder;
        public string Description;
        public string DescriptionIssue;
        public string[] Materials;
    }
    public class Exam
    {
        public int Number;
        public string Title;
        public string Introduction;
        public int DurationMinutes;
        public bool IsQuiz;
        public int SourceQuestionCount;
        public int PresentationVersion;
        public string CreatedUtc;
        public string MaterialHash;
        public MaterialReview SourceReview;
        public List<MaterialIssue> SourceIssues = new List<MaterialIssue>();
        public List<Question> Questions = new List<Question>();
        public List<string> Sources = new List<string>();
        public List<ExamImage> Images = new List<ExamImage>();
        public bool FilteredSources;
        public bool SourceWarningsRemain;
        public List<string> ExcludedMaterials = new List<string>();
    }
    public class Question
    {
        public string Id;
        public string Topic;
        public string Type;
        public string Prompt;
        public string Context;
        public List<QuestionPart> Parts = new List<QuestionPart>();
        public double Points;
        public int EstimatedMinutes;
        public string Hint;
        public List<string> Options = new List<string>();
        public int CorrectOption;
        public string ExpectedAnswer;
        public string Rubric;
        public List<string> Columns = new List<string>();
        public List<string> Rows = new List<string>();
        public List<List<string>> CorrectCells = new List<List<string>>();
        public List<string> SourceReferences = new List<string>();
        public List<string> ImageIds = new List<string>();
        public List<string> SourceUnitIds = new List<string>();
    }
    public class QuestionPart
    {
        public string Id;
        public string Prompt;
        public string Type;
        public double Points;
        public string ExpectedAnswer;
        public List<string> Columns = new List<string>();
        public List<string> Rows = new List<string>();
        public List<List<string>> CorrectCells = new List<List<string>>();
    }
    public class QuestionPresentation
    {
        public string QuestionId;
        public string Context;
        public string Hint;
        public List<QuestionPart> Parts = new List<QuestionPart>();
    }
    public class PresentationResponse { public List<QuestionPresentation> Questions = new List<QuestionPresentation>(); }
    public class Student
    {
        public string FirstName;
        public string LastName;
        public string CreatedUtc;
        public string FullName { get { return FirstName + " " + LastName; } }
    }
    public class Answer
    {
        public string QuestionId;
        public int SelectedOption = -1;
        public string Text = "";
        public List<List<string>> Cells = new List<List<string>>();
        public List<PartAnswer> Parts = new List<PartAnswer>();
        public bool HintUsed;
        public bool HintShownAutomatically;
        public List<Feedback> Tries = new List<Feedback>();
        public Feedback Result;
        public bool HasContent { get { return Parts.Count > 0 ? Parts.Any(p => p.HasContent) : SelectedOption >= 0 || !String.IsNullOrWhiteSpace(Text) || Cells.Any(r => r.Any(c => !String.IsNullOrWhiteSpace(c))); } }
    }
    public class PartAnswer
    {
        public string PartId;
        public string Text = "";
        public List<List<string>> Cells = new List<List<string>>();
        public bool HasContent { get { return !String.IsNullOrWhiteSpace(Text) || Cells.Any(r => r.Any(c => !String.IsNullOrWhiteSpace(c))); } }
    }
    public class Feedback
    {
        public string QuestionId;
        public double Fraction;
        public string Comment;
        public string AnswerSnapshot;
        public string CheckedUtc;
    }
    public class GradeResponse { public List<Feedback> Results = new List<Feedback>(); }
    public class Attempt
    {
        public bool FilteredSources;
        public string Id;
        public string TopicName;
        public int ExamNumber;
        public bool Practice;
        public bool Quiz;
        public int DurationMinutes;
        public string StartedUtc;
        public string FinishedUtc;
        public bool WarningShown;
        public string WarningShownUtc;
        public int CurrentQuestion;
        public List<Answer> Answers = new List<Answer>();
        public double? Score;
        public bool Passed;
    }
    public static class Rules
    {
        public static bool OpenPracticeHint(Attempt attempt, Answer answer)
        {
            if (!attempt.Practice || answer.HintUsed) return false;
            answer.HintUsed = true; answer.HintShownAutomatically = true;
            return true;
        }
        public static bool NeedsReview(Answer answer)
        {
            return (answer.HintUsed && !answer.HintShownAutomatically) || answer.Tries.Any(t => t.Fraction < 0.9999) || (answer.Result != null && answer.Result.Fraction < 0.9999);
        }
        public static double Earned(Question q, Answer a)
        {
            if (a.Result == null) return 0;
            return q.Points * Math.Max(0, Math.Min(1, a.Result.Fraction)) * (a.HintUsed ? 0.5 : 1.0);
        }
        public static int NextNumber(IEnumerable<Attempt> history, string topic, bool quiz = false)
        {
            var passed = new HashSet<int>(history.Where(a => a.TopicName == topic && !a.Practice && a.Quiz == quiz && a.FinishedUtc != null && a.Passed && a.Score > 80).Select(a => a.ExamNumber));
            int next = 1;
            while (passed.Contains(next)) next++;
            return next;
        }
        public static bool ShowWarning(Attempt attempt, DateTime utcNow)
        {
            var elapsed = (utcNow - DateTime.Parse(attempt.StartedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind)).TotalMinutes;
            double warningMinute = TimeBudget(attempt) - 10;
            return !attempt.Practice && !attempt.WarningShown && elapsed >= warningMinute && elapsed < warningMinute + 1;
        }
        public static int TimeBudget(Attempt attempt) { return attempt.DurationMinutes > 0 ? attempt.DurationMinutes : attempt.Quiz ? 35 : 60; }
        public static bool NoticeVisible(Attempt attempt, DateTime utcNow)
        {
            if (attempt.Practice || !attempt.WarningShown || attempt.FinishedUtc != null) return false;
            DateTime shown = String.IsNullOrEmpty(attempt.WarningShownUtc) ? DateTime.Parse(attempt.StartedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind).AddMinutes(TimeBudget(attempt) - 10) : DateTime.Parse(attempt.WarningShownUtc, null, System.Globalization.DateTimeStyles.RoundtripKind);
            return utcNow >= shown && utcNow < shown.AddMinutes(1);
        }
        public static string Mode(Attempt attempt) { return attempt.Practice ? "מבחן עם עזרה" : attempt.Quiz ? "בוחן" : "מבחן אמיתי"; }
        public static string PartLabel(int index) { return "סעיף " + "אבגדהוזחטיכלמנסעפצקרשת"[index]; }
        public static void InitializeParts(Question q, Answer answer)
        {
            if (answer.Parts.Count > 0 || answer.HasContent || q.Parts.Count == 0) return;
            answer.Parts = q.Parts.Select(p => new PartAnswer { PartId = p.Id, Cells = p.Rows.Select(r => p.Columns.Select(c => "").ToList()).ToList() }).ToList();
        }
        public static string PartText(QuestionPart part, PartAnswer answer)
        {
            if (part.Type == "table") return String.Join("\n", part.Rows.Select((r, i) => r + ": " + String.Join("; ", part.Columns.Select((c, j) => c + " — " + answer.Cells[i][j]))));
            return answer.Text;
        }
        public static void SyncParts(Question q, Answer answer)
        {
            if (answer.Parts.Count == 0) return;
            if (answer.Parts.Count != q.Parts.Count || answer.Parts.Any(a => !q.Parts.Any(p => p.Id == a.PartId))) throw new InvalidOperationException("מבנה הסעיפים השתנה מאז שמירת התשובה.");
            answer.Text = String.Join("\n\n", q.Parts.Select((p, i) => PartLabel(i) + ": " + p.Prompt + "\n" + PartText(p, answer.Parts.Single(a => a.PartId == p.Id))));
        }
        public static string Snapshot(Answer a)
        {
            return a.SelectedOption + "\n" + (a.Text ?? "") + "\n" + String.Join("\n", a.Cells.Select(r => String.Join("\t", r)));
        }
        public static void Validate(Exam exam)
        {
            if (exam != null)
            {
                if (exam.ExcludedMaterials == null) exam.ExcludedMaterials = new List<string>();
                if (exam.FilteredSources && (exam.ExcludedMaterials.Count == 0 || exam.Questions == null || exam.Questions.Any(q => q == null || q.SourceUnitIds == null || q.SourceUnitIds.Count == 0))) throw new InvalidOperationException("מבחן עם חומר מוחרג חייב לשמור את היקף החומר ואת שיוך כל השאלות למקורות.");
                if (exam.Images == null) exam.Images = new List<ExamImage>();
                if (exam.Images.Any(i => i == null || String.IsNullOrEmpty(i.Id) || String.IsNullOrEmpty(i.RelativePath) || String.IsNullOrEmpty(i.ImageHash)) || exam.Images.Select(i => i.Id).Distinct().Count() != exam.Images.Count) throw new InvalidOperationException("תמונות המבחן אינן תקינות.");
                if (exam.Questions != null) foreach (var question in exam.Questions.Where(q => q != null))
                {
                    if (question.ImageIds == null) question.ImageIds = new List<string>();
                    if (question.ImageIds.Count > 3 || question.ImageIds.Distinct().Count() != question.ImageIds.Count || question.ImageIds.Any(id => !exam.Images.Any(i => i.Id == id))) throw new InvalidOperationException("אחת השאלות מפנה לתמונה חסרה או כפולה.");
                }
            }
            if (exam == null || String.IsNullOrWhiteSpace(exam.Title) || exam.DurationMinutes != (exam.IsQuiz ? 35 : 60) || exam.Questions == null || exam.Questions.Count < (exam.IsQuiz ? 3 : 6) || exam.Questions.Count > 24)
                throw new InvalidOperationException("המבחן שנוצר אינו תקין: יש לבדוק את הכותרת, משך הפעילות ומספר השאלות.");
            if (exam.IsQuiz && exam.SourceQuestionCount > 0 && exam.Questions.Count != (exam.SourceQuestionCount + 1) / 2) throw new InvalidOperationException("הבוחן חייב לכלול מחצית משאלות המבחן, בעיגול כלפי מעלה.");
            if (Math.Abs(exam.Questions.Sum(q => q.Points) - 100) > 0.001 || exam.Questions.Sum(q => q.EstimatedMinutes) != exam.DurationMinutes)
                throw new InvalidOperationException("סך הנקודות חייב להיות 100 וסכום הזמנים חייב להתאים למשך הפעילות.");
            var ids = new HashSet<string>();
            foreach (var q in exam.Questions)
            {
                if (String.IsNullOrWhiteSpace(q.Id) || !ids.Add(q.Id) || String.IsNullOrWhiteSpace(q.Prompt) || String.IsNullOrWhiteSpace(q.Topic) || String.IsNullOrWhiteSpace(q.Hint) || String.IsNullOrWhiteSpace(q.ExpectedAnswer) || String.IsNullOrWhiteSpace(q.Rubric) || q.Points <= 0 || q.EstimatedMinutes <= 0 || q.SourceReferences == null || q.SourceReferences.Count == 0)
                    throw new InvalidOperationException("אחת השאלות חסרה תוכן, רמז, מחוון או מקור.");
                if (q.Type != "multiple_choice" && q.Type != "text" && q.Type != "table") throw new InvalidOperationException("סוג שאלה לא נתמך.");
                if (q.Type == "multiple_choice" && (q.Options == null || q.Options.Count < 2 || q.Options.Count > 6 || q.Options.Any(String.IsNullOrWhiteSpace) || q.Options.Distinct().Count() != q.Options.Count || q.CorrectOption < 0 || q.CorrectOption >= q.Options.Count)) throw new InvalidOperationException("אפשרויות הבחירה בשאלה אינן תקינות.");
                if (q.Type == "table" && (q.Rows == null || q.Columns == null || q.CorrectCells == null || q.Rows.Count == 0 || q.Columns.Count == 0 || q.Rows.Count > 10 || q.Columns.Count > 6 || q.CorrectCells.Count != q.Rows.Count || q.CorrectCells.Any(r => r == null || r.Count != q.Columns.Count))) throw new InvalidOperationException("מבנה הטבלה בשאלה אינו תקין.");
                if (q.Parts == null) q.Parts = new List<QuestionPart>();
                if (q.Parts.Count > 0)
                {
                    if (q.Type != "text" || String.IsNullOrWhiteSpace(q.Context) || q.Parts.Count > 8 || q.Parts.Select(p => p.Id).Distinct().Count() != q.Parts.Count || Math.Abs(q.Parts.Sum(p => p.Points) - q.Points) > 0.001) throw new InvalidOperationException("סעיפי השאלה או הניקוד שלהם אינם תקינים.");
                    foreach (var part in q.Parts)
                    {
                        if (String.IsNullOrWhiteSpace(part.Id) || String.IsNullOrWhiteSpace(part.Prompt) || String.IsNullOrWhiteSpace(part.ExpectedAnswer) || part.Points <= 0 || (part.Type != "text" && part.Type != "table")) throw new InvalidOperationException("אחד הסעיפים חסר או אינו תקין.");
                        if (part.Type == "table" && (part.Rows.Count == 0 || part.Rows.Count > 10 || part.Columns.Count == 0 || part.Columns.Count > 6 || part.CorrectCells.Count != part.Rows.Count || part.CorrectCells.Any(r => r == null || r.Count != part.Columns.Count))) throw new InvalidOperationException("טבלה באחד הסעיפים אינה תקינה.");
                    }
                }
                if (exam.PresentationVersion >= 2 && (q.Hint.Length < 160 || (q.Type == "text" && q.Parts.Count == 0))) throw new InvalidOperationException("נדרשים קטע לימוד מפורט וסעיפים ברורים לשאלות הפתוחות.");
            }
        }
    }
}
