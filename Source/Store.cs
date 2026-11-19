using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Education2k
{
    public class Store
    {
        public readonly string Root;
        public readonly List<string> Warnings = new List<string>();
        public static JavaScriptSerializer Json { get { return new JavaScriptSerializer { MaxJsonLength = 16000000, RecursionLimit = 100 }; } }
        public Store(string root)
        {
            Root = Path.GetFullPath(root);
            Directory.CreateDirectory(Path.Combine(Root, "מבחנים"));
            Directory.CreateDirectory(Path.Combine(Root, "תלמידים"));
        }
        public static T Read<T>(string path) { return Json.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8)); }
        public static void Write(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, Json.Serialize(value), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public List<Topic> Topics()
        {
            return Directory.GetDirectories(Path.Combine(Root, "מבחנים"))
                .Where(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0)
                .OrderBy(Path.GetFileName, StringComparer.CurrentCulture).Select(ReadTopic).ToList();
        }
        internal static Topic ReadTopic(string folder)
        {
            var topic = new Topic { Name = Path.GetFileName(folder), Folder = folder };
            string description = Path.Combine(folder, "_describe.txt");
            try
            {
                topic.Description = File.Exists(description) ? MaterialReader.ReadText(description) : "";
                if (String.IsNullOrWhiteSpace(topic.Description)) { topic.DescriptionIssue = "קובץ התיאור _describe.txt חסר או ריק, ולכן לא ידועים הכיתה, המקורות והיקף הנושאים המבוקש."; topic.Description = topic.DescriptionIssue; }
            }
            catch (Exception e)
            {
                if (!(e is IOException) && !(e is UnauthorizedAccessException)) throw;
                topic.DescriptionIssue = "לא ניתן לקרוא את קובץ התיאור _describe.txt: " + e.Message; topic.Description = topic.DescriptionIssue;
            }
            topic.Materials = Directory.GetFiles(folder).Where(f => !Path.GetFileName(f).StartsWith("_") && !Path.GetFileName(f).StartsWith("~$") && (File.GetAttributes(f) & FileAttributes.ReparsePoint) == 0).OrderBy(f => f).ToArray();
            return topic;
        }
        public static string CleanName(string name)
        {
            name = (name ?? "").Trim().Normalize(NormalizationForm.FormC);
            if (name.Length < 1 || name.Length > 45 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith(".") || name.Any(Char.IsControl) || name == "." || name == "..")
                throw new InvalidOperationException("יש להזין שם תקין, ללא סימנים המשמשים בשמות תיקיות.");
            return name;
        }
        public string StudentFolder(Student student) { return Path.Combine(Root, "תלמידים", CleanName(student.FirstName) + " " + CleanName(student.LastName)); }
        public Student Login(string first, string last)
        {
            var student = new Student { FirstName = CleanName(first), LastName = CleanName(last), CreatedUtc = DateTime.UtcNow.ToString("o") };
            var profile = Path.Combine(StudentFolder(student), "_student.json");
            if (File.Exists(profile)) return Read<Student>(profile);
            Write(profile, student);
            return student;
        }
        public List<Attempt> History(Student student)
        {
            var folder = Path.Combine(StudentFolder(student), "נסיונות");
            if (!Directory.Exists(folder)) return new List<Attempt>();
            var list = new List<Attempt>();
            foreach (var file in Directory.GetFiles(folder, "*.json"))
            {
                try { list.Add(Read<Attempt>(file)); }
                catch (Exception e) { Warnings.Add("לא ניתן לקרוא ניסיון שמור: " + Path.GetFileName(file) + " — " + e.Message); }
            }
            return list.OrderByDescending(a => a.StartedUtc).ToList();
        }
        public void Save(Student student, Attempt attempt) { Write(Path.Combine(StudentFolder(student), "נסיונות", attempt.Id + ".json"), attempt); }
        public string ExamPath(Topic topic, int number, bool quiz = false) { return Path.Combine(topic.Folder, String.Format(quiz ? "_quiz_{0:D4}.json" : "_exam_{0:D4}.json", number)); }
        public IEnumerable<string> ExamFiles(Topic topic)
        {
            return Directory.GetFiles(topic.Folder, "_exam_*.json").Where(p => Regex.IsMatch(Path.GetFileName(p), @"^_exam_[0-9]+\.json$"));
        }
        public Exam LoadExam(Topic topic, int number, bool quiz = false)
        {
            var exam = Read<Exam>(ExamPath(topic, number, quiz));
            Rules.Validate(exam);
            SourceImages.Check(topic, exam);
            if (exam.Number != number || exam.IsQuiz != quiz) throw new InvalidOperationException("מספר הפעילות או סוגה אינם תואמים לשם הקובץ.");
            return exam;
        }
        public Attempt CreateAttempt(Topic topic, Exam exam, bool practice)
        {
            var attempt = new Attempt {
                Id = Guid.NewGuid().ToString("N"), TopicName = topic.Name, ExamNumber = exam.Number, Practice = practice,
                Quiz = exam.IsQuiz, DurationMinutes = exam.DurationMinutes,
                FilteredSources = exam.FilteredSources,
                StartedUtc = DateTime.UtcNow.ToString("o"), Answers = exam.Questions.Select(q => new Answer {
                    QuestionId = q.Id, Cells = q.Rows.Select(r => q.Columns.Select(c => "").ToList()).ToList()
                }).ToList()
            };
            foreach (var q in exam.Questions)
            {
                var answer = attempt.Answers.Single(a => a.QuestionId == q.Id);
                Rules.InitializeParts(q, answer); Rules.OpenPracticeHint(attempt, answer);
            }
            return attempt;
        }
    }
}
