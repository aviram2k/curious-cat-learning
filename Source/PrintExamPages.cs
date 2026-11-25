using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace Education2k
{
    public partial class MainWindow
    {
        readonly PrintExamService printService;
        readonly bool printDemo;
        ExamDraft printDraft;
        bool printVisible, printDirty;
        StackPanel printFiles;
        TextBlock printCount, printMessage;
        string printSignature;
        void FlushPrintDraft()
        {
            if (!printDirty || printDraft == null) return;
            if (!Directory.Exists(printDraft.Folder)) { printDirty = false; return; }
            printService.Save(printDraft); printDirty = false;
        }
        void PrintTick()
        {
            if (operation != null) return;
            try { FlushPrintDraft(); if (printVisible) RefreshPrintFiles(); }
            catch (Exception e)
            {
                if (!(e is IOException) && !(e is UnauthorizedAccessException) && !(e is InvalidOperationException)) throw;
                if (printVisible && printMessage != null) printMessage.Text = "לא ניתן לעדכן את הטיוטה: " + e.Message;
            }
        }
        void ShowPrintExam()
        {
            if (operation != null) return;
            try
            {
                Save();
                if (printDraft != null && Directory.Exists(printDraft.Folder)) { PrintForm(); return; }
                var drafts = printService.Drafts.List(); if (drafts.Count == 0) { BeginPrint(null); return; }
                Page("צור מבחן PDF", "אפשר ליצור כמה מבחנים שרוצים, או להמשיך מטיוטה שנשמרה.", ShowPrintExam);
                body.Children.Add(Button("יצירת מבחן חדש להדפסה", delegate { BeginPrint(null); }, true));
                foreach (var draft in drafts)
                {
                    var selected = draft; var panel = Panel(); panel.Children.Add(Text(String.IsNullOrWhiteSpace(draft.Name) ? "טיוטת מבחן PDF" : draft.Name, 22));
                    panel.Children.Add(Text("כיתה " + draft.Classroom + " | " + draft.School + " | " + WizardMaterialCount(printService.Drafts.Files(draft).Length), 15));
                    panel.Children.Add(Button("המשך מהטיוטה", delegate { BeginPrint(selected.Id); })); body.Children.Add(Card(panel));
                }
                body.Children.Add(Button("פתיחת תיקיית המבחנים להדפסה", delegate { Directory.CreateDirectory(printService.Root); OpenWizardFolder(printService.Root); }));
            }
            catch (Exception e) { Error(e); }
        }
        void BeginPrint(string id)
        {
            try { printDraft = id == null ? printService.Drafts.Create() : printService.Drafts.Load(id); printDirty = false; PrintForm(); } catch (Exception e) { Error(e); }
        }
        void PrintForm()
        {
            Page("צור מבחן PDF", "מוסיפים חומרים ומקבלים מבחן עם מקום לתשובות, ואחריו פתרונות בעמוד חדש.", PrintForm);
            printVisible = true; printSignature = null; var panel = Panel(); panel.Children.Add(Text("פרטי המבחן", 23));
            panel.Children.Add(Text("שם המבחן", 16)); var name = Inputs.Hebrew(); name.MaxLength = 80; name.Text = printDraft.Name; AutomationProperties.SetName(name, "שם מבחן PDF"); panel.Children.Add(name); Gap(panel);
            var fields = new Grid(); fields.ColumnDefinitions.Add(new ColumnDefinition()); fields.ColumnDefinitions.Add(new ColumnDefinition());
            var classPanel = Panel(); classPanel.Margin = new Thickness(0, 0, 0, 0); classPanel.Children.Add(Text("כיתה", 16)); var classroom = Inputs.Hebrew(); classroom.MaxLength = 60; classroom.Text = printDraft.Classroom; AutomationProperties.SetName(classroom, "כיתה למבחן PDF"); classPanel.Children.Add(classroom); fields.Children.Add(classPanel);
            var schoolPanel = Panel(); schoolPanel.Margin = new Thickness(12, 0, 0, 0); schoolPanel.Children.Add(Text("בית ספר", 16)); var school = Inputs.Hebrew(); school.MaxLength = 120; school.Text = printDraft.School; AutomationProperties.SetName(school, "בית ספר למבחן PDF"); schoolPanel.Children.Add(school); Grid.SetColumn(schoolPanel, 1); fields.Children.Add(schoolPanel); panel.Children.Add(fields); Gap(panel, 20);
            name.TextChanged += delegate { printDraft.Name = name.Text; printDirty = true; }; classroom.TextChanged += delegate { printDraft.Classroom = classroom.Text; printDirty = true; }; school.TextChanged += delegate { printDraft.School = school.Text; printDirty = true; };
            var drop = Panel(); drop.Children.Add(Text("גררו לכאן את חומרי הלימוד", 21, "#176F65")); drop.Children.Add(Text("PDF, מסמכי Word, מצגות, תמונות או קובצי טקסט. המקור נשאר במקומו; נשמור עותק בתיקיית המבחן להדפסה.", 15));
            panel.Children.Add(new Border { Background = Brush("#E7F1EB"), BorderBrush = Brush("#9BC6B6"), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(14), Padding = new Thickness(20), Child = drop, Margin = new Thickness(0, 0, 0, 14) });
            var actions = new WrapPanel(); actions.Children.Add(Button("בחירת קבצים מהמחשב", delegate {
                var picker = new Microsoft.Win32.OpenFileDialog { Title = "חומרי לימוד למבחן PDF", Multiselect = true, CheckFileExists = true, Filter = "חומרי לימוד|*.pdf;*.docx;*.pptx;*.txt;*.md;*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.gif|כל הקבצים|*.*" };
                if (picker.ShowDialog(this) == true) ImportPrintFiles(picker.FileNames);
            })); actions.Children.Add(Button("פתיחת תיקיית החומרים", delegate { OpenWizardFolder(printDraft.Folder); })); panel.Children.Add(actions);
            printCount = Text("", 16); panel.Children.Add(printCount); printFiles = Panel(); panel.Children.Add(printFiles); printMessage = Text("", 15, "#98543E"); panel.Children.Add(printMessage);
            panel.Children.Add(Text("במהלך היצירה תוצג התקדמות. אם יש בעיה בקריאת החומר, תבחרו כיצד להמשיך. כל הקבצים נשמרים בתוך „מבחנים / מבחנים להדפסה”.", 14, "#596F76"));
            panel.Children.Add(Button("צור מבחן", CreatePrintExam, true)); body.Children.Add(Card(panel)); RefreshPrintFiles();
            var footer = new WrapPanel(); footer.Children.Add(Button("שמירת טיוטה וחזרה", delegate { try { FlushPrintDraft(); printDraft = null; if (student == null) ShowLogin(); else ShowModes(); } catch (Exception e) { Error(e); } }));
            footer.Children.Add(Button("כל המבחנים להדפסה", delegate { Directory.CreateDirectory(printService.Root); OpenWizardFolder(printService.Root); })); body.Children.Add(footer);
            Dispatcher.BeginInvoke(new Action(delegate { if (printVisible) name.Focus(); }), DispatcherPriority.Input);
        }
        void RefreshPrintFiles()
        {
            if (printDraft == null || printFiles == null) return;
            var files = printService.Drafts.Files(printDraft).Select(p => new FileInfo(p)).ToList(); string signature = String.Join("|", files.Select(f => f.Name + ":" + f.Length + ":" + f.LastWriteTimeUtc.Ticks));
            if (signature == printSignature) return; printSignature = signature; printCount.Text = files.Count == 0 ? "עדיין לא נוספו חומרי לימוד." : WizardMaterialCount(files.Count); printFiles.Children.Clear();
            foreach (var file in files)
            {
                string selected = file.Name; var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(Text(file.Name, 15)); var remove = Button("הסרה מהרשימה", delegate { try { printService.Drafts.Remove(printDraft, selected); printSignature = null; RefreshPrintFiles(); } catch (Exception e) { printMessage.Text = e.Message; } });
                remove.FontSize = 13; remove.MinHeight = 32; remove.Padding = new Thickness(12, 5, 12, 5); Grid.SetColumn(remove, 1); row.Children.Add(remove); printFiles.Children.Add(row);
            }
        }
        void ImportPrintFiles(string[] paths)
        {
            if (!printVisible || printDraft == null || operation != null) return;
            var selected = printDraft;
            Run("מעתיקים את החומרים למבחן PDF…", async cancellation => {
                var imported = await Task.Run(() => printService.Drafts.Import(selected, paths, cancellation), cancellation); printSignature = null; RefreshPrintFiles();
                printMessage.Text = (imported.Added.Count == 1 ? "נוסף קובץ אחד." : "נוספו " + imported.Added.Count + " קבצים.") + (imported.Existing.Count > 0 ? " קבצים שכבר הועתקו לא נוספו שוב." : "") + (imported.Errors.Count > 0 ? "\nלא נוספו:\n" + String.Join("\n", imported.Errors) : "");
            });
        }
        void CreatePrintExam()
        {
            try { FlushPrintDraft(); PrintExamDetails.From(printDraft).Validate(); if (printService.Drafts.Files(printDraft).Length == 0) throw new InvalidOperationException("יש להוסיף לפחות קובץ אחד של חומר לימוד."); }
            catch (Exception e) { printMessage.Text = e.Message; return; }
            var selected = printDraft;
            Run("מכינים מבחן PDF…", async cancellation => {
                var builder = printDemo ? new ExamService(store, Tests.WizardDemoModel(Store.ReadTopic(selected.Folder))) : service;
                var result = await printService.Create(selected, builder, cancellation, review => Dispatcher.CheckAccess() ? Dialogs.ConfirmMaterials(this, review) : Dispatcher.Invoke(() => Dialogs.ConfirmMaterials(this, review)));
                printDraft = null; printDirty = false; ShowPrintReady(result);
            }, "pdf");
        }
        void ShowPrintReady(PrintExamResult result)
        {
            Page("המבחן להדפסה מוכן", result.Paper.Details.Name, delegate { ShowPrintReady(result); }); var panel = Panel();
            panel.Children.Add(Text("קובץ ה־PDF נוצר ונשמר בהצלחה.", 24, "#176F65"));
            panel.Children.Add(Text(result.Paper.Pages.Count + " עמודים | השאלות בעמודים 1 עד " + (result.Paper.AnswerStartPage - 1) + " | הפתרונות מתחילים בעמוד " + result.Paper.AnswerStartPage, 17));
            panel.Children.Add(Text("כל חומרי היצירה והקובץ המקורי שמורים בתיקייה נפרדת בתוך „מבחנים / מבחנים להדפסה”."));
            panel.Children.Add(Button("פתיחת PDF", delegate { OpenWizardFolder(result.PdfPath); }, true));
            panel.Children.Add(Button("שמירת עותק / הורדה", delegate {
                var picker = new Microsoft.Win32.SaveFileDialog { Title = "שמירת עותק של המבחן", Filter = "קובץ PDF|*.pdf", AddExtension = true, DefaultExt = ".pdf", FileName = Path.GetFileName(result.PdfPath), InitialDirectory = Path.GetDirectoryName(result.PdfPath), OverwritePrompt = true };
                if (picker.ShowDialog(this) == true && !String.Equals(Path.GetFullPath(picker.FileName), Path.GetFullPath(result.PdfPath), StringComparison.OrdinalIgnoreCase)) { try { File.Copy(result.PdfPath, picker.FileName, true); } catch (Exception e) { Error(e); } }
            }));
            panel.Children.Add(Button("הדפסה", delegate { PrintPaper(result.Paper); }));
            panel.Children.Add(Button("פתיחת התיקייה", delegate { OpenWizardFolder(Path.GetDirectoryName(result.PdfPath)); }));
            panel.Children.Add(Button("יצירת מבחן PDF נוסף", ShowPrintExam)); body.Children.Add(Card(panel));
        }
        void PrintPaper(ExamPaper paper)
        {
            try
            {
                var dialog = new PrintDialog { UserPageRangeEnabled = true, MinPage = 1, MaxPage = (uint)paper.Pages.Count, PageRangeSelection = PageRangeSelection.UserPages, PageRange = new PageRange(1, paper.AnswerStartPage - 1) };
                if (dialog.ShowDialog() != true) return;
                int first = dialog.PageRangeSelection == PageRangeSelection.UserPages ? Math.Max(1, dialog.PageRange.PageFrom) : 1;
                int last = dialog.PageRangeSelection == PageRangeSelection.UserPages ? Math.Min(paper.Pages.Count, dialog.PageRange.PageTo) : paper.Pages.Count;
                var document = ExamPrintDocument.Create(paper, first, last, dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
                dialog.PrintDocument(document.DocumentPaginator, paper.Details.Name);
            }
            catch (Exception e) { Error(e); }
        }
    }
}
