using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Markup;

namespace Education2k
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            if (args.Length == 3 && args[0] == "--render-office") return OfficeRender.Worker(args[1], args[2]);
            try
            {
                if (args.Contains("--test")) return Tests.Run(root);
                if (args.Contains("--check-connection"))
                {
                    new CodexClient(root).Check(CancellationToken.None).GetAwaiter().GetResult();
                    File.WriteAllText(Path.Combine(root, "Build", "connection-test.txt"), "PASS: Codex authentication and a live model response verified.");
                    return 0;
                }
                if (args.Contains("--generate-example"))
                {
                    var store = new Store(root); var topic = store.Topics().First();
                    var exam = new ExamService(store, new CodexClient(root)).GetExam(topic, 1, CancellationToken.None).GetAwaiter().GetResult();
                    File.WriteAllText(Path.Combine(root, "Build", "generation-test.txt"), "PASS: " + exam.Title + ", " + exam.Questions.Count + " questions, " + exam.Questions.Sum(q => q.Points) + " points.");
                    return 0;
                }
                if (args.Contains("--grade-example")) return Tests.LiveGrade(root);
                if (args.Contains("--test-updates")) return Tests.LiveUpdates(root);
                if (args.Contains("--review-materials")) return Tests.LiveMaterials(root);
                if (args.Contains("--test-live-progress")) return Tests.LiveProgress(root);
                if (args.Contains("--refresh-exams"))
                {
                    var store = new Store(root); var service = new ExamService(store, new CodexClient(root)); int count = 0;
                    foreach (var topic in store.Topics()) foreach (var file in store.ExamFiles(topic))
                    {
                        var old = Store.Read<Exam>(file); service.GetExam(topic, old.Number, CancellationToken.None).GetAwaiter().GetResult(); count++;
                        service.GetQuiz(topic, old.Number, CancellationToken.None).GetAwaiter().GetResult();
                    }
                    File.WriteAllText(Path.Combine(root, "Build", "presentation-update.txt"), "PASS: Updated " + count + " shared exams with educational hints and clear sections; created their short quizzes."); return 0;
                }
                var app = new Application();
                using (var stream = typeof(Program).Assembly.GetManifestResourceStream("Education2k.Theme.xaml")) app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(stream));
                app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) {
                    Dialogs.Show(app.MainWindow, "אירעה תקלה", e.Exception.Message, false); e.Handled = true;
                };
                string previewMode = args.Contains("--preview-new-exam") ? "new-exam" : args.Contains("--preview-images") ? "images" : args.Contains("--preview-waiting") ? "waiting" : args.Contains("--preview-materials") ? "materials" : args.Contains("--preview-updates") ? "sections" : args.Contains("--preview-quiz") ? "quiz" : args.Contains("--preview-modes") ? "modes" : null;
                string dataRoot = previewMode == null ? root : previewMode == "new-exam" ? Tests.PrepareNewExamPreview(root) : Tests.PreparePreview(root);
                app.Run(new MainWindow(new Store(dataRoot), previewMode)); return 0;
            }
            catch (Exception e)
            {
                if (args.Length > 0)
                {
                    Directory.CreateDirectory(Path.Combine(root, "Build"));
                    File.WriteAllText(Path.Combine(root, "Build", "last-error.txt"), e.ToString()); return 1;
                }
                Dialogs.Show(null, "לא ניתן לפתוח את התוכנה", e.Message, false); return 1;
            }
        }
    }
}
