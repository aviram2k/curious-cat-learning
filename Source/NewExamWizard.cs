using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Education2k
{
    public partial class MainWindow
    {
        readonly ExamDrafts drafts;
        readonly bool wizardDemo;
        ExamDraft wizardDraft;
        bool wizardVisible, wizardDirty;
        int wizardStep;
        StackPanel wizardFiles;
        TextBlock wizardCount, wizardMessage;
        string wizardFileSignature;
        static string WizardMaterialCount(int count) { return count == 1 ? "קובץ חומר לימוד אחד" : count + " קובצי חומר לימוד"; }

        void FlushWizardDraft()
        {
            if (!wizardDirty || wizardDraft == null) return;
            if (!Directory.Exists(wizardDraft.Folder)) { wizardDirty = false; return; }
            wizardDraft.Step = wizardStep; drafts.Save(wizardDraft); wizardDirty = false;
        }
        void WizardTick()
        {
            if (operation != null) return;
            try
            {
                FlushWizardDraft();
                if (wizardVisible && wizardStep == 1) RefreshWizardFiles();
            }
            catch (Exception e)
            {
                if (!(e is IOException) && !(e is UnauthorizedAccessException) && !(e is InvalidOperationException)) throw;
                if (wizardMessage != null && wizardVisible) wizardMessage.Text = "כדאי לבדוק את שמירת הטיוטה: " + e.Message;
            }
        }
        void ShowAddExamWizard()
        {
            if (operation != null) return;
            try
            {
                Save();
                if (wizardDraft != null && Directory.Exists(wizardDraft.Folder)) { ShowWizardStep(); return; }
                var saved = drafts.List();
                if (saved.Count == 0) { BeginExamDraft(null); return; }
                Page("הוסף מבחן חדש", "אפשר להתחיל נושא חדש או להמשיך מטיוטה שנשמרה.", ShowAddExamWizard);
                body.Children.Add(Button("מתחילים מבחן חדש", delegate { BeginExamDraft(null); }, true));
                foreach (var draft in saved)
                {
                    var selected = draft; var card = Panel(); card.Children.Add(Text(String.IsNullOrWhiteSpace(draft.Name) ? "מבחן שעדיין לא קיבל שם" : draft.Name, 23));
                    card.Children.Add(Text("טיוטה · " + WizardMaterialCount(drafts.Files(draft).Length) + " · עדיין אינה מופיעה לתלמידים", 15, "#596F76"));
                    card.Children.Add(Button("ממשיכים בטיוטה", delegate { BeginExamDraft(selected.Id); })); body.Children.Add(Card(card));
                }
                body.Children.Add(Button("חזרה", delegate { if (student == null) ShowLogin(); else ShowModes(); }));
            }
            catch (Exception e) { Error(e); }
        }
        void BeginExamDraft(string id)
        {
            try
            {
                wizardDraft = id == null ? drafts.Create() : drafts.Load(id); wizardStep = Math.Max(0, Math.Min(2, wizardDraft.Step));
                wizardDirty = false; ShowWizardStep();
            }
            catch (Exception e) { Error(e); }
        }
        void ShowWizardStep()
        {
            if (wizardDraft == null) { ShowAddExamWizard(); return; }
            Page("הוסף מבחן חדש", "נכין את החומרים ואת המבחן הראשון, צעד אחר צעד.", ShowWizardStep);
            wizardVisible = true; wizardFiles = null; wizardCount = null; wizardFileSignature = null;
            var steps = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 18) };
            string[] labels = { "1 · שם ותיאור", "2 · חומרי הלימוד", "3 · סיכום ויצירה" };
            for (int i = 0; i < labels.Length; i++) steps.Children.Add(new Border { Background = Brush(i == wizardStep ? "#DDEFE7" : "#EAF0ED"), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 12, 3), Margin = new Thickness(4), Child = Text(labels[i], 16, i == wizardStep ? "#176F65" : "#596F76") });
            body.Children.Add(steps); wizardMessage = Text("", 15, "#98543E");
            if (wizardStep == 0) WizardDetails();
            else if (wizardStep == 1) WizardMaterials();
            else WizardSummary();
            body.Children.Add(wizardMessage);
            var footer = new WrapPanel();
            footer.Children.Add(Button("שמירת הטיוטה ויציאה", delegate {
                try { FlushWizardDraft(); wizardDraft = null; if (student == null) ShowLogin(); else ShowModes(); } catch (Exception e) { Error(e); }
            }));
            footer.Children.Add(Button("עזרה בהוספת חומרים", ShowAddExamGuide)); body.Children.Add(footer);
        }
        void WizardDetails()
        {
            var panel = Panel(); panel.Children.Add(Text("איך נקרא למבחן?", 24));
            panel.Children.Add(Text("שם המבחן", 16)); var name = Inputs.Hebrew(); name.MaxLength = 80; name.Text = wizardDraft.Name;
            System.Windows.Automation.AutomationProperties.SetName(name, "שם המבחן"); panel.Children.Add(name); Gap(panel);
            panel.Children.Add(Text("תיאור כללי של נושא המבחן והחומר", 16));
            var description = Inputs.Hebrew(); description.AcceptsReturn = true; description.TextWrapping = TextWrapping.Wrap; description.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; description.Height = 125; description.MaxLength = 8000; description.Text = wizardDraft.Description;
            System.Windows.Automation.AutomationProperties.SetName(description, "תיאור כללי של נושא המבחן והחומר"); panel.Children.Add(description);
            panel.Children.Add(Text("מספיק תיאור כללי קצר. את המיקוד והפירוט נזהה מתוך החומרים שתצרפו; אין צורך להזין עמודים או פרקים בנפרד.", 15, "#596F76"));
            name.TextChanged += delegate { wizardDraft.Name = name.Text; wizardDirty = true; };
            description.TextChanged += delegate { wizardDraft.Description = description.Text; wizardDirty = true; };
            panel.Children.Add(Button("הבא · הוספת חומרי לימוד", delegate { AdvanceWizard(1); }, true)); body.Children.Add(Card(panel));
            Dispatcher.BeginInvoke(new Action(delegate { if (wizardVisible && wizardStep == 0) name.Focus(); }), System.Windows.Threading.DispatcherPriority.Input);
        }
        void AdvanceWizard(int step)
        {
            try
            {
                FlushWizardDraft(); drafts.ValidateDetails(wizardDraft);
                if (step == 2 && drafts.Files(wizardDraft).Length == 0) throw new InvalidOperationException("יש להוסיף לפחות קובץ אחד של חומר לימוד.");
                wizardStep = step; wizardDraft.Step = step; drafts.Save(wizardDraft); ShowWizardStep();
            }
            catch (Exception e) { wizardMessage.Text = e.Message; }
        }
        void WizardMaterials()
        {
            var panel = Panel(); panel.Children.Add(Text("מוסיפים את חומרי הלימוד", 24));
            var drop = new StackPanel(); drop.Children.Add(Text("גררו לכאן קבצים מהמחשב", 23, "#176F65"));
            drop.Children.Add(Text("אפשר לצרף PDF, מסמכי Word, מצגות, תמונות וקובצי טקסט. הקבצים יועתקו לתיקייה של המבחן.", 16));
            panel.Children.Add(new Border { Background = Brush("#E7F1EB"), BorderBrush = Brush("#9BC6B6"), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(14), Padding = new Thickness(22), Margin = new Thickness(0, 0, 0, 14), Child = drop });
            var buttons = new WrapPanel();
            buttons.Children.Add(Button("בחירת קבצים מהמחשב", delegate {
                var picker = new Microsoft.Win32.OpenFileDialog { Title = "בחירת חומרי לימוד למבחן", Multiselect = true, CheckFileExists = true,
                    Filter = "חומרי לימוד|*.pdf;*.docx;*.pptx;*.txt;*.md;*.csv;*.tsv;*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.gif|כל הקבצים|*.*" };
                if (picker.ShowDialog(this) == true) ImportWizardFiles(picker.FileNames);
            }, true));
            buttons.Children.Add(Button("פתיחת התיקייה לגרירת קבצים", delegate { OpenWizardFolder(wizardDraft.Folder); }));
            buttons.Children.Add(Button("רענון הקבצים", delegate { wizardFileSignature = null; RefreshWizardFiles(); })); panel.Children.Add(buttons);
            panel.Children.Add(Text("גם קבצים שתעתיקו ישירות לתיקייה יופיעו כאן. אפשר להסיר מהרשימה קובץ שלא רוצים לכלול.", 14, "#596F76"));
            wizardCount = Text("", 17); panel.Children.Add(wizardCount); wizardFiles = Panel(); panel.Children.Add(wizardFiles);
            var navigation = new WrapPanel(); navigation.Children.Add(Button("חזרה לשם ולתיאור", delegate { AdvanceWizard(0); })); navigation.Children.Add(Button("הבא · סיכום", delegate { AdvanceWizard(2); }, true)); panel.Children.Add(navigation);
            body.Children.Add(Card(panel)); RefreshWizardFiles();
        }
        void OpenWizardFolder(string folder)
        {
            try { Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true }); } catch (Exception e) { Error(e); }
        }
        void RefreshWizardFiles()
        {
            if (wizardDraft == null || wizardFiles == null) return;
            var files = drafts.Files(wizardDraft).Select(f => new FileInfo(f)).ToList();
            string signature = String.Join("|", files.Select(f => f.Name + ":" + f.Length + ":" + f.LastWriteTimeUtc.Ticks));
            if (signature == wizardFileSignature) return; wizardFileSignature = signature;
            wizardCount.Text = files.Count == 0 ? "עדיין לא נוספו קובצי חומר לימוד." : (files.Count == 1 ? "נוסף " : "נוספו ") + WizardMaterialCount(files.Count) + ".";
            wizardFiles.Children.Clear();
            foreach (var file in files)
            {
                string selected = file.Name; var row = new Grid { Margin = new Thickness(0, 0, 0, 8) }; row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var title = Panel(); title.Children.Add(Text(file.Name, 16)); title.Children.Add(Text((file.Length / 1024.0).ToString("0.#") + " KB", 13, "#596F76")); row.Children.Add(title);
                var remove = Button("הסרה מהרשימה", delegate { try { drafts.Remove(wizardDraft, selected); wizardFileSignature = null; RefreshWizardFiles(); } catch (Exception e) { wizardMessage.Text = e.Message; } });
                remove.FontSize = 13; remove.MinHeight = 32; remove.Padding = new Thickness(12, 5, 12, 5); remove.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(remove, 1); row.Children.Add(remove); wizardFiles.Children.Add(row);
            }
        }
        void ImportWizardFiles(string[] paths)
        {
            if (!wizardVisible || wizardStep != 1 || operation != null || wizardDraft == null) return;
            var selected = wizardDraft;
            Run("מוסיפים את חומרי הלימוד…", async cancellation => {
                var result = await Task.Run(() => drafts.Import(selected, paths, cancellation), cancellation);
                wizardFileSignature = null; RefreshWizardFiles();
                wizardMessage.Text = (result.Added.Count == 1 ? "נוסף קובץ אחד." : "נוספו " + result.Added.Count + " קבצים.") + (result.Existing.Count == 1 ? " קובץ אחד כבר היה בטיוטה." : result.Existing.Count > 1 ? " " + result.Existing.Count + " קבצים כבר היו בטיוטה." : "") + (result.Errors.Count > 0 ? "\nלא נוספו:\n" + String.Join("\n", result.Errors) : "");
                wizardMessage.Foreground = Brush(result.Errors.Count > 0 ? "#98543E" : "#176F65");
            });
        }
        void WizardSummary()
        {
            var panel = Panel(); panel.Children.Add(Text("רגע לפני שמכינים את המבחן הראשון", 24));
            panel.Children.Add(Text(wizardDraft.Name, 22)); panel.Children.Add(Text(wizardDraft.Description));
            var files = drafts.Files(wizardDraft); panel.Children.Add(Text(WizardMaterialCount(files.Length), 17));
            panel.Children.Add(new ScrollViewer { MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = Text(String.Join("\n", files.Select(Path.GetFileName)), 15, "#596F76") });
            panel.Children.Add(Text("בלחיצה על סיום נקרא את החומרים ונפענח תמונות ותרשימים באמצעות Codex, ואז נכין ונשמור את מבחן מספר 1. אם יש בעיה בחומר, תבחרו כיצד להמשיך. הנושא יופיע לתלמידים רק כשהמבחן מוכן.", 16));
            var buttons = new WrapPanel(); buttons.Children.Add(Button("חזרה לחומרי הלימוד", delegate { AdvanceWizard(1); }));
            buttons.Children.Add(Button("סיום והוספת המבחן", FinishWizard, true)); panel.Children.Add(buttons); body.Children.Add(Card(panel));
        }
        void FinishWizard()
        {
            var selected = wizardDraft;
            Run("מכינים את המבחן הראשון…", async cancellation => {
                FlushWizardDraft();
                var builder = wizardDemo ? new ExamService(store, Tests.WizardDemoModel(Store.ReadTopic(selected.Folder))) : service;
                var completed = await drafts.Complete(selected, builder, cancellation, review => {
                    if (Dispatcher.CheckAccess()) return Dialogs.ConfirmMaterials(this, review);
                    return Dispatcher.Invoke(() => Dialogs.ConfirmMaterials(this, review));
                });
                wizardDraft = null; wizardDirty = false;
                ShowCreatedExam(completed);
            }, "exam");
        }
        void ShowCreatedExam(Topic completed)
        {
            Page("המבחן הראשון מוכן", completed.Name, delegate { ShowCreatedExam(completed); });
            var panel = Panel(); panel.Children.Add(Text("מבחן מספר 1 נוצר ונשמר בהצלחה.", 25, "#176F65"));
            panel.Children.Add(Text("הנושא נוסף לרשימת המבחנים. כשהתלמיד יבחר בו, המבחן הראשון כבר יחכה לו."));
            panel.Children.Add(Button("פתיחת תיקיית המבחן", delegate { OpenWizardFolder(completed.Folder); }));
            panel.Children.Add(Button("הוספת מבחן נוסף", ShowAddExamWizard));
            panel.Children.Add(Button(student == null ? "חזרה למסך הכניסה" : "לרשימת המבחנים", delegate { if (student == null) ShowLogin(); else ShowTopics(); }, true)); body.Children.Add(Card(panel));
        }
    }
}
