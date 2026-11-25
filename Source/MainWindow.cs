using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Education2k
{
    public partial class MainWindow : Window
    {
        readonly Store store;
        readonly CodexClient codex;
        readonly ExamService service;
        readonly ImageSource appLogo;
        readonly StackPanel body = new StackPanel();
        readonly TextBlock connection = new TextBlock();
        readonly TextBlock identity = new TextBlock();
        readonly TextBlock clock = new TextBlock();
        readonly WaitingView waitingView;
        WorkState workState;
        Guid workSession;
        readonly Border busy = new Border();
        readonly Border notice = new Border();
        readonly ScrollViewer scroller;
        readonly DispatcherTimer timer = new DispatcherTimer();
        readonly DispatcherTimer autosave = new DispatcherTimer();
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        CancellationTokenSource operation;
        Student student;
        Topic topic;
        Exam exam;
        Attempt attempt;
        bool rendering;
        bool dirty;
        bool checking;
        bool ready;
        string connectionError;
        bool inQuestion;
        bool scrollToHint;
        Button historyButton;
        Action currentPage;

        static SolidColorBrush Brush(string hex) { return (SolidColorBrush)new BrushConverter().ConvertFromString(hex); }
        static TextBlock Text(string value, double size = 17, string color = null)
        {
            return new TextBlock { Text = value, FontSize = size, Foreground = Brush(color ?? "#213B42"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), LineHeight = size * 1.55 };
        }
        static Button Button(string label, Action action, bool primary = false)
        {
            var b = new Button { Content = label };
            AutomationProperties.SetName(b, label);
            if (primary) { b.Background = Brush("#176F65"); b.Foreground = Brushes.White; b.BorderBrush = b.Background; }
            b.Click += delegate { action(); };
            return b;
        }
        static Button HeaderButton(string label, Action action, int fontSize = 16)
        {
            var button = Button(label, action);
            button.Width = 160; button.Height = button.MinHeight = 60;
            button.Padding = new Thickness(12, 8, 12, 8); button.Margin = new Thickness(5, 0, 5, 8);
            button.Content = new TextBlock { Text = label, FontSize = fontSize, LineHeight = 20, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
            return button;
        }
        static Border Card(UIElement content)
        {
            return new Border { Child = content, Background = Brushes.White, BorderBrush = Brush("#DFE8E4"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(17), Padding = new Thickness(28), Margin = new Thickness(0, 0, 0, 18) };
        }
        static StackPanel Panel() { return new StackPanel(); }
        static void Gap(Panel panel, int height = 14) { panel.Children.Add(new Border { Height = height }); }
        public MainWindow(Store store, string previewMode = null)
        {
            this.store = store; codex = new CodexClient(store.Root); service = new ExamService(store, codex); drafts = new ExamDrafts(store); wizardDemo = previewMode == "new-exam";
            printService = new PrintExamService(store); printDemo = previewMode == "print" || previewMode == "print-ready";
            Title = AppInfo.Name + " | " + AppInfo.Slogan; Width = 1180; Height = 840; MinWidth = 860; MinHeight = 620;
            using (var iconStream = typeof(MainWindow).Assembly.GetManifestResourceStream("Education2k.AppIcon.ico"))
            {
                Icon = BitmapFrame.Create(iconStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                Icon.Freeze();
            }
            using (var logoStream = typeof(MainWindow).Assembly.GetManifestResourceStream("Education2k.AppLogo.png"))
            {
                appLogo = BitmapFrame.Create(logoStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                appLogo.Freeze();
            }
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Brush("#F4F7F3"); FontFamily = new FontFamily("Segoe UI"); FlowDirection = FlowDirection.RightToLeft;
            Language = System.Windows.Markup.XmlLanguage.GetLanguage("he-IL");
            var shell = new Grid();
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); shell.RowDefinitions.Add(new RowDefinition()); shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var version = Text(AppInfo.VersionLabel, 12, "#596F76");
            version.Margin = new Thickness(34, 7, 34, 0); version.HorizontalAlignment = HorizontalAlignment.Left;
            version.ToolTip = "תאריך ושעת העדכון לפי שעון ישראל";
            AutomationProperties.SetName(version, AppInfo.VersionLabel); shell.Children.Add(version);
            var layout = new Grid(); layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); layout.RowDefinitions.Add(new RowDefinition());
            var header = new Grid { Margin = new Thickness(34, 18, 34, 14) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            var brand = new Grid { VerticalAlignment = VerticalAlignment.Center };
            brand.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            brand.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            brand.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var brandLogo = new Image { Source = appLogo, Width = 72, Height = 72, Stretch = Stretch.Uniform, FlowDirection = FlowDirection.LeftToRight };
            AutomationProperties.SetName(brandLogo, "לוגו " + AppInfo.Name + " — חתול עם משקפיים קורא ספר");
            brand.Children.Add(brandLogo);
            var brandWords = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var brandName = Text(AppInfo.Name, 28); brandName.Margin = new Thickness(0, 0, 0, 5); brandName.LineHeight = 36; brandName.TextWrapping = TextWrapping.NoWrap;
            var brandSlogan = Text(AppInfo.Slogan, 14, "#596F76"); brandSlogan.Margin = new Thickness(0); brandSlogan.LineHeight = 22;
            brandWords.Children.Add(brandName); brandWords.Children.Add(brandSlogan); Grid.SetColumn(brandWords, 2); brand.Children.Add(brandWords);
            header.Children.Add(brand);
            var tools = new WrapPanel { Orientation = Orientation.Horizontal, ItemWidth = 170, ItemHeight = 68, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            historyButton = HeaderButton("ההישגים שלי", ShowHistory); historyButton.IsEnabled = false;
            tools.Children.Add(historyButton);
            var home = HeaderButton("המסך הראשי", delegate { if (student != null) { Save(); ShowModes(); } else ShowLogin(); }); tools.Children.Add(home);
            var addExamHelp = HeaderButton("הוסף מבחן חדש", ShowAddExamWizard, 14); tools.Children.Add(addExamHelp);
            tools.Children.Add(HeaderButton("צור מבחן PDF", ShowPrintExam));
            var connectButton = HeaderButton("פרטי החיבור", ShowConnection); tools.Children.Add(connectButton);
            Grid.SetColumn(tools, 2); header.Children.Add(tools); layout.Children.Add(header);
            var scroll = new ScrollViewer { Content = body, Padding = new Thickness(32, 12, 32, 30) }; scroller = scroll;
            ScrollWheelRouting.Enable(scroll);
            body.MaxWidth = 960; body.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetRow(scroll, 1); layout.Children.Add(scroll);
            var footer = new DockPanel { Margin = new Thickness(34, 10, 34, 14) };
            identity.FontSize = 13; identity.Foreground = Brush("#596F76"); DockPanel.SetDock(identity, Dock.Right); footer.Children.Add(identity);
            connection.Text = "● בודק חיבור ל־Codex…"; connection.FontSize = 13; connection.Foreground = Brush("#6B6D45"); connection.Margin = new Thickness(0); connection.VerticalAlignment = VerticalAlignment.Center;
            footer.Children.Add(connection); Grid.SetRow(footer, 2); shell.Children.Add(footer);
            Grid.SetRow(layout, 1); shell.Children.Add(layout);
            notice.Background = Brush("#E6F2E9"); notice.BorderBrush = Brush("#BCD9C2"); notice.BorderThickness = new Thickness(1); notice.CornerRadius = new CornerRadius(14);
            notice.Padding = new Thickness(20); notice.Margin = new Thickness(26); notice.HorizontalAlignment = HorizontalAlignment.Left; notice.VerticalAlignment = VerticalAlignment.Bottom; notice.MaxWidth = 340; notice.Visibility = Visibility.Collapsed;
            notice.Child = Text("נותרו כ־10 דקות מהזמן המתוכנן. אפשר להמשיך לעבוד בנחת.", 16); Grid.SetRow(notice, 1); shell.Children.Add(notice);
            busy.Background = Brush("#F4F7F3"); busy.Visibility = Visibility.Collapsed;
            waitingView = new WaitingView(delegate { if (operation != null) operation.Cancel(); });
            busy.Child = waitingView; Grid.SetRow(busy, 1); shell.Children.Add(busy); Content = shell;
            AllowDrop = true;
            PreviewDragOver += delegate(object sender, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = (printVisible || wizardVisible && wizardStep == 1) && operation == null ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; } };
            PreviewDrop += delegate(object sender, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Handled = true; if (printVisible) ImportPrintFiles((string[])e.Data.GetData(DataFormats.FileDrop)); else ImportWizardFiles((string[])e.Data.GetData(DataFormats.FileDrop)); } };
            timer.Interval = TimeSpan.FromSeconds(1); timer.Tick += delegate { Tick(); }; timer.Start();
            autosave.Interval = TimeSpan.FromMilliseconds(900); autosave.Tick += delegate { if (dirty) { try { Save(); } catch (Exception e) { autosave.Stop(); Error(e); } } }; autosave.Start();
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs closing) { try { Save(); } catch (Exception e) { closing.Cancel = true; Error(new InvalidOperationException("לא ניתן לשמור את התשובות: " + e.Message)); return; } lifetime.Cancel(); if (operation != null) operation.Cancel(); };
            ShowLogin();
            Loaded += async delegate {
                if (previewMode != null)
                {
                    if (previewMode == "print-ready") { identity.Text = "בדיקת PDF · חומר סינתטי"; connection.Text = "● תצוגת בדיקה · ללא פנייה ל־Codex"; Run("מכינים קובץ PDF לבדיקה…", async ct => { ShowPrintReady(await Tests.PrintReadyPreview(store, ct)); }, "pdf"); return; }
                    if (printDemo) { identity.Text = "בדיקת מבחן PDF · מודל מדומה"; connection.Text = "● תצוגת בדיקה · ללא פנייה ל־Codex"; ShowPrintExam(); return; }
                    if (previewMode == "feedback")
                    {
                        student = store.Login("בדיקת", "משוב"); identity.Text = "תצוגת משוב · נתונים סינתטיים"; connection.Text = "● תצוגת בדיקה · ללא פנייה ל־Codex"; historyButton.IsEnabled = true;
                        topic = store.Topics().Single(); exam = store.LoadExam(topic, 1); attempt = store.History(student).Single(); ShowSummary(); return;
                    }
                    if (previewMode == "scroll")
                    {
                        Width = MinWidth; Height = MinHeight;
                        student = store.Login("בדיקת", "גלילה"); identity.Text = "בדיקת גלילה · נתונים סינתטיים"; historyButton.IsEnabled = true;
                        connection.Text = "● תצוגת בדיקה · ללא פנייה ל־Codex";
                        topic = store.Topics().Single(); exam = store.LoadExam(topic, 1); attempt = store.CreateAttempt(topic, exam, false); attempt.CurrentQuestion = 4; ShowQuestion(); return;
                    }
                    if (wizardDemo) { identity.Text = "תצוגת בדיקה · יצירת מבחן עם מודל מדומה"; connection.Text = "● תצוגת בדיקה · ללא פנייה ל־Codex"; ShowAddExamWizard(); return; }
                    student = store.Login("בדיקת", "ממשק"); identity.Text = "תצוגת בדיקה · " + student.FullName; historyButton.IsEnabled = true; topic = store.Topics().First();
                    if (previewMode == "waiting")
                    {
                        ShowModes(); Run("תצוגת בדיקה של הכנת מבחן", ct => Tests.ProgressPreview(store, ct), "exam");
                    }
                    else if (previewMode == "materials")
                    {
                        ShowModes();
                        var sourceReport = await Task.Run(() => Tests.HistoryExclusionPreview(AppDomain.CurrentDomain.BaseDirectory));
                        Dialogs.ConfirmMaterials(this, sourceReport);
                    }
                    else if (previewMode == "modes") ShowExamModes();
                    else if (previewMode == "images")
                    {
                        topic = Tests.PrepareImagePreview(store); exam = store.LoadExam(topic, 1); attempt = store.CreateAttempt(topic, exam, false); ShowQuestion();
                    }
                    else
                    {
                        exam = store.LoadExam(topic, 1, previewMode == "quiz"); attempt = store.CreateAttempt(topic, exam, false);
                        if (previewMode == "sections") attempt.CurrentQuestion = exam.Questions.FindIndex(q => q.Id == "q13");
                        ShowQuestion();
                    }
                }
                await CheckConnection();
            };
        }
        void Page(string title, string subtitle, Action render)
        {
            wizardVisible = false;
            printVisible = false;
            currentPage = render; inQuestion = false; notice.Visibility = Visibility.Collapsed; body.Children.Clear();
            scroller.ScrollToTop();
            body.Children.Add(Text(title, 32)); body.Children.Add(Text(subtitle, 17, "#596F76")); Gap(body);
        }
        void Error(Exception e)
        {
            if (e is OperationCanceledException || lifetime.IsCancellationRequested) return;
            string message = e.Message;
            if (message.Length > 2400) message = message.Substring(0, 2400);
            Dialogs.Show(this, "כדאי לבדוק רגע", message, false);
        }
        sealed class WorkSink : IProgress<WorkUpdate>
        {
            readonly Action<WorkUpdate> report;
            public WorkSink(Action<WorkUpdate> report) { this.report = report; }
            public void Report(WorkUpdate update) { report(update); }
        }
        void ReportWork(Guid session, WorkUpdate update)
        {
            if (lifetime.IsCancellationRequested || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(delegate { ReportWork(session, update); })); return; }
            if (session != workSession || operation == null || lifetime.IsCancellationRequested) return;
            workState.Apply(update); waitingView.Refresh();
        }
        async void Run(string label, Func<CancellationToken, Task> action, string goal = "activity")
        {
            if (operation != null) return;
            operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            Guid session = workSession = Guid.NewGuid(); workState = new WorkState(label, goal, store.Root, DateTime.UtcNow);
            waitingView.Begin(workState); busy.Visibility = Visibility.Visible;
            using (WorkFlow.Use(new WorkSink(update => ReportWork(session, update))))
            {
                try { WorkFlow.Activity("הפעולה התחילה."); Save(); await action(operation.Token); WorkFlow.Activity("הפעולה הסתיימה.", "הפעולה הושלמה."); }
                catch (MaterialDecisionDeclinedException) { WorkFlow.Activity("נבחרה חזרה ללא יצירה חדשה."); if (student != null) ShowModes(); else ShowLogin(); }
                catch (OperationCanceledException) { WorkFlow.Activity("הפעולה בוטלה. קבצים שנשמרו לפני הביטול נשארים במחשב."); }
                catch (Exception e) { WorkFlow.Activity("הפעולה נעצרה עקב תקלה. מוצגים פרטי התקלה."); Error(e); }
                finally { busy.Visibility = Visibility.Collapsed; waitingView.End(); workState.Dispose(); workState = null; operation.Dispose(); operation = null; }
            }
        }
        void Save()
        {
            FlushWizardDraft();
            FlushPrintDraft();
            if (student != null && attempt != null) store.Save(student, attempt);
            dirty = false;
        }
        async Task CheckConnection()
        {
            if (checking) return;
            checking = true; ready = false; connection.Text = "● בודק חיבור ל־Codex…"; connection.Foreground = Brush("#6B6D45");
            try { await codex.Check(lifetime.Token); ready = true; connection.Text = "● מחובר ל־Codex"; connection.Foreground = Brush("#176F65"); }
            catch (Exception e) { connectionError = e.Message; connection.Text = "● החיבור אינו זמין"; connection.Foreground = Brush("#98543E"); }
            finally { checking = false; }
        }
        void ShowConnection()
        {
            Save(); Action back = currentPage;
            Page("החיבור למורה החכם", "יצירת מבחנים ובדיקת תשובות נעשות באמצעות Codex וחשבון ChatGPT במחשב הזה.", ShowConnection);
            var p = Panel(); p.Children.Add(Text(ready ? "החיבור נבדק בהצלחה." : "אם עדיין לא התחברת, יש ללחוץ על התחברות ולהשלים את התהליך בחלון הדפדפן שייפתח.", 21));
            p.Children.Add(Text("אין צורך לכתוב הודעה בצ׳אט או להשאיר את חלון השיחה פתוח. נדרשים Codex מותקן, חשבון בעל גישה מתאימה וחיבור לאינטרנט. השימוש כפוף למכסה בחשבון.", 17));
            p.Children.Add(Text("בעת יצירת מבחן נשלחים חומרי הלימוד למודל. בבדיקת תשובות נשלחים תוכן השאלה והתשובה, ללא שם התלמיד. המידע המקומי נשמר בתיקיות הפרויקט.", 15, "#596F76"));
            if (!ready && !String.IsNullOrEmpty(connectionError)) p.Children.Add(new Expander { Header = "פרטי התקלה בחיבור", Content = Text(connectionError, 14), Margin = new Thickness(0, 10, 0, 16) });
            p.Children.Add(Button("התחברות לחשבון ChatGPT", delegate { Run("ממתינים להשלמת ההתחברות בדפדפן…", async ct => { await codex.Login(ct); await CheckConnection(); }); }, true));
            p.Children.Add(Button("בדיקת החיבור מחדש", async delegate { await CheckConnection(); }));
            body.Children.Add(Card(p)); body.Children.Add(Button("חזרה", delegate { if (back != null) back(); else ShowLogin(); }));
        }
        void ShowLogin()
        {
            Page("נעים ללמוד יחד", "לפני שמתחילים, איך קוראים לך?", ShowLogin);
            var p = Panel(); p.MaxWidth = 560; p.HorizontalAlignment = HorizontalAlignment.Stretch;
            var logo = new Image { Source = appLogo, Width = 144, Height = 144, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center,
                FlowDirection = FlowDirection.LeftToRight, Margin = new Thickness(0, 0, 0, 12), IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
            LogoAnimation.Attach(logo);
            AutomationProperties.SetName(logo, "לוגו " + AppInfo.Name + " — חתול עם משקפיים קורא ספר"); p.Children.Add(logo);
            var first = Inputs.Hebrew(); first.Margin = new Thickness(0, 0, 0, 10); first.MaxLength = 45;
            var last = Inputs.Hebrew(); last.Margin = new Thickness(0, 0, 0, 14); last.MaxLength = 45;
            AutomationProperties.SetName(first, "שם פרטי"); AutomationProperties.SetName(last, "שם משפחה");
            p.Children.Add(Text("שם פרטי", 16)); p.Children.Add(first); p.Children.Add(Text("שם משפחה", 16)); p.Children.Add(last);
            Action login = delegate { try { student = store.Login(first.Text, last.Text); identity.Text = "שלום, " + student.FullName; historyButton.IsEnabled = true; attempt = null; ShowModes(); } catch (Exception e) { Error(e); } };
            var next = Button("המשך", login, true); next.IsDefault = true; p.Children.Add(next);
            p.Children.Add(Text("ההתקדמות תישמר בשם הזה גם בפעם הבאה.", 14, "#596F76"));
            var loginCard = Card(p); loginCard.Padding = new Thickness(18); body.Children.Add(loginCard);
            Dispatcher.BeginInvoke(new Action(delegate { first.Focus(); }), DispatcherPriority.Input);
        }
        void ShowModes()
        {
            Page("שלום " + student.FirstName + ", מה נלמד היום?", "יש מקום לחשוב, לנסות ולגלות. נתקדם צעד אחר צעד.", ShowModes);
            var examPanel = Panel(); examPanel.Children.Add(Text("מבחנים ותרגול", 25)); examPanel.Children.Add(Text("בוחרים נושא ומתנסים במבחן עצמאי או בתרגול עם עזרה ומשוב."));
            examPanel.Children.Add(Button("לבחירת נושא", ShowTopics, true)); body.Children.Add(Card(examPanel));
            var learning = Panel(); learning.Children.Add(Text("למידה", 25)); learning.Children.Add(Text("כאן יהיה מרחב הלמידה שלך.")); learning.Children.Add(Button("למצב למידה", ShowLearning)); body.Children.Add(Card(learning));
            body.Children.Add(Button("החלפת תלמיד", delegate { Save(); attempt = null; student = null; historyButton.IsEnabled = false; identity.Text = ""; ShowLogin(); }));
        }
        void ShowLearning()
        {
            Page("למידה", "מרחב הלמידה ייפתח בהמשך.", ShowLearning);
            body.Children.Add(Card(Text("התוכנה בבניה", 28))); body.Children.Add(Button("חזרה למסך הראשי", ShowModes));
        }
        void ShowTopics()
        {
            Page("מה מתרגלים היום?", "הנושאים מופיעים מתוך התיקיות שבתיקיית „מבחנים”.", ShowTopics);
            try
            {
                var topics = store.Topics(); var history = store.History(student);
                if (topics.Count == 0) body.Children.Add(Card(Text("עדיין אין נושאים. לוחצים על „הוסף מבחן חדש” למעלה ומוסיפים את חומרי הלימוד בכמה צעדים פשוטים.")));
                foreach (var t in topics)
                {
                    var p = Panel(); p.Children.Add(Text(t.Name, 24)); p.Children.Add(Text(t.Description));
                    int number = Rules.NextNumber(history, t.Name);
                    p.Children.Add(Text(t.Materials.Length + " קובצי חומר לימוד · המבחן הבא שלך: מספר " + number, 14, "#596F76"));
                    var target = t; p.Children.Add(Button("בחירת הנושא", delegate { topic = target; ShowExamModes(); }, true)); body.Children.Add(Card(p));
                }
                body.Children.Add(Button("רענון הנושאים", ShowTopics));
            }
            catch (Exception e) { Error(e); }
        }
        void ShowExamModes()
        {
            Page(topic.Name, topic.Description, ShowExamModes);
            int number = Rules.NextNumber(store.History(student), topic.Name);
            int quizNumber = Rules.NextNumber(store.History(student), topic.Name, true);
            body.Children.Add(Text("המבחן הבא שלך: מספר " + number + " · הבוחן הבא: מספר " + quizNumber, 20));
            var shortExam = Panel(); shortExam.Children.Add(Text("בוחן", 25));
            shortExam.Children.Add(Text("מתחילים בקטן ובנחת: חצי מהשאלות ו־35 דקות מומלצות. מקבלים ציון ומשוב בסיום, וניתן להשתמש ברמז לימודי. בדקה ה־25 מופיעה תזכורת שקטה למשך דקה; אפשר להמשיך לענות גם אחרי הזמן המומלץ."));
            shortExam.Children.Add(Text("לבחנים יש מספור והתקדמות משלהם. מעבר בבוחן אינו מדלג על המבחן המלא.", 15, "#596F76"));
            shortExam.Children.Add(Button("מתחילים בוחן", delegate { Start(false, true); }, true)); body.Children.Add(Card(shortExam));
            var p = Panel(); p.Children.Add(Text("מבחן אמיתי", 25));
            p.Children.Add(Text("עונים באופן עצמאי ומקבלים בסיום ציון ודוח עם הסברים. השעה היא זמן עבודה מומלץ: אין הגשה אוטומטית ואין ספירה לאחור. תופיע תזכורת שקטה אחת כ־10 דקות לפני סיום השעה.", 17));
            p.Children.Add(Text("אפשר לבקש רמז בכל שאלה. חשיפת הרמז מפחיתה בחצי את הניקוד שניתן לקבל באותה שאלה, פעם אחת בלבד.", 15, "#596F76"));
            p.Children.Add(Button("מתחילים מבחן אמיתי", delegate { Start(false); }, true)); body.Children.Add(Card(p));
            var help = Panel(); help.Children.Add(Text("מבחן עם עזרה", 25));
            help.Children.Add(Text("בלי מגבלת זמן ובלי ציון. אחרי כל תשובה מקבלים משוב, ואפשר לנסות שוב עד שמצליחים. בסיום נראה מה כבר ברור ומה כדאי לחזק."));
            help.Children.Add(Button("מתחילים עם עזרה", delegate { Start(true); }, true)); body.Children.Add(Card(help)); body.Children.Add(Button("בחירת נושא אחר", ShowTopics));
        }
        void Start(bool practice, bool quiz = false)
        {
            Run("מכינים עבורך את המבחן…", async ct => {
                var history = store.History(student); int number = Rules.NextNumber(history, topic.Name, quiz);
                var pending = history.FirstOrDefault(a => a.TopicName == topic.Name && a.ExamNumber == number && a.Practice == practice && a.Quiz == quiz && a.FinishedUtc == null);
                Func<MaterialReview, MaterialDecision> confirm = review => Dialogs.ConfirmMaterials(this, review);
                exam = quiz ? await service.GetQuiz(topic, number, ct, confirm) : await service.GetExam(topic, number, ct, confirm);
                attempt = pending ?? store.CreateAttempt(topic, exam, practice);
                if (attempt.Answers.Count != exam.Questions.Count || attempt.Answers.Any(a => !exam.Questions.Any(q => q.Id == a.QuestionId))) throw new InvalidOperationException("מבנה המבחן השתנה מאז שמירת התשובות. יש לשחזר את קובץ המבחן המקורי לפני ההמשך.");
                Save(); ShowIntroduction(pending != null);
            }, quiz ? "quiz" : "exam");
        }
        void ShowIntroduction(bool resumed)
        {
            Page((attempt.Quiz ? "בוחן מספר " : "מבחן מספר ") + exam.Number + " · " + Rules.Mode(attempt), exam.Title, delegate { ShowIntroduction(true); });
            var p = Panel(); p.Children.Add(Text(resumed ? "טוב שחזרת. התשובות שלך מחכות כאן." : "אפשר להתחיל בנחת.", 24));
            p.Children.Add(Text(exam.Introduction)); p.Children.Add(Text(exam.Questions.Count + " שאלות · " + (attempt.Practice ? "ללא ציון וללא מגבלת זמן" : "100 נקודות · " + Rules.TimeBudget(attempt) + " דקות מומלצות"), 17));
            if (exam.FilteredSources) p.Children.Add(Text(new MaterialSelection { Excluded = exam.ExcludedMaterials }.ScopeNote, 16, "#866337"));
            if (MaterialReader.CurrentHash(topic) != exam.MaterialHash) p.Children.Add(Text("חומרי המקור עודכנו מאז יצירת המבחן הזה. הגרסה השמורה מבוססת על החומר שהיה בזמן יצירתה.", 15, "#98543E"));
            p.Children.Add(Button(resumed ? "ממשיכים מהמקום שבו עצרנו" : "לשאלה הראשונה", ShowQuestion, true)); body.Children.Add(Card(p));
        }
        void Changed(Answer answer)
        {
            if (rendering) return;
            Rules.SyncParts(exam.Questions.Single(q => q.Id == answer.QuestionId), answer);
            if (answer.Result != null && answer.Result.AnswerSnapshot != Rules.Snapshot(answer)) answer.Result = null;
            dirty = true;
        }
        void ShowQuestion()
        {
            rendering = true;
            attempt.CurrentQuestion = Math.Max(0, Math.Min(exam.Questions.Count - 1, attempt.CurrentQuestion));
            var q = exam.Questions[attempt.CurrentQuestion]; var answer = attempt.Answers.Single(a => a.QuestionId == q.Id);
            Rules.InitializeParts(q, answer);
            if (Rules.OpenPracticeHint(attempt, answer)) dirty = true;
            bool structured = answer.Parts.Count > 0;
            Page((attempt.Quiz ? "בוחן מספר " : "מבחן מספר ") + exam.Number, exam.Title + " · " + Rules.Mode(attempt), ShowQuestion); inQuestion = true;
            var line = new Grid(); line.ColumnDefinitions.Add(new ColumnDefinition()); line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var progress = Text("שאלה " + (attempt.CurrentQuestion + 1) + " מתוך " + exam.Questions.Count + " · " + q.Topic, 16, "#596F76");
            line.Children.Add(progress);
            clock.Margin = new Thickness(0, 0, 0, 10); clock.FontSize = 14; clock.Foreground = Brush("#596F76");
            if (clock.Parent is Panel) ((Panel)clock.Parent).Children.Remove(clock);
            Grid.SetColumn(clock, 1); clock.Margin = new Thickness(20, 0, 0, 10); line.Children.Add(clock); body.Children.Add(line); Tick();
            var p = Panel(); p.Children.Add(Text(structured ? q.Context : q.Prompt, 23));
            SourceImages.Add(p, this, topic, exam, q);
            if (!attempt.Practice) p.Children.Add(Text(q.Points.ToString("0.##") + " נקודות" + (answer.HintUsed ? " · לאחר רמז: עד " + (q.Points / 2).ToString("0.##") + " נקודות" : ""), 14, "#596F76"));
            if (attempt.Practice)
            {
                p.Children.Add(Text("קטע לימוד שיעזור בפתרון", 20, "#176F65"));
                p.Children.Add(Card(Text(q.Hint, 17)));
            }
            Gap(p, 6);
            if (q.Type == "multiple_choice")
            {
                for (int i = 0; i < q.Options.Count; i++)
                {
                    int option = i;
                    var radio = new RadioButton { Content = Text(q.Options[i], 18), IsChecked = answer.SelectedOption == i, GroupName = "answer" };
                    radio.Checked += delegate { answer.SelectedOption = option; Changed(answer); }; p.Children.Add(radio);
                }
            }
            else if (structured)
            {
                for (int i = 0; i < q.Parts.Count; i++)
                {
                    var part = q.Parts[i]; var partAnswer = answer.Parts.Single(a => a.PartId == part.Id);
                    Gap(p, 10); p.Children.Add(Text(Rules.PartLabel(i) + " · " + part.Prompt, 19));
                    AddEditor(p, part.Type, part.Columns, part.Rows, partAnswer.Text, partAnswer.Cells, delegate(string value) { partAnswer.Text = value; Changed(answer); }, delegate { Changed(answer); }, Rules.PartLabel(i));
                }
            }
            else if (q.Type == "text")
            {
                var input = Inputs.Hebrew(); input.Text = answer.Text; input.AcceptsReturn = true; input.TextWrapping = TextWrapping.Wrap; input.MinHeight = 160; input.MaxLength = 12000; input.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; input.VerticalContentAlignment = VerticalAlignment.Top;
                AutomationProperties.SetName(input, "התשובה שלך"); input.TextChanged += delegate { answer.Text = input.Text; Changed(answer); }; p.Children.Add(input);
            }
            else
            {
                var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
                foreach (var col in q.Columns) grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (int c = 0; c < q.Columns.Count; c++) AddCell(grid, Text(q.Columns[c], 15), 0, c + 1, true);
                for (int r = 0; r < q.Rows.Count; r++)
                {
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); AddCell(grid, Text(q.Rows[r], 16), r + 1, 0, true);
                    for (int c = 0; c < q.Columns.Count; c++)
                    {
                        int row = r, col = c; var input = Inputs.Hebrew(); input.Text = answer.Cells[r][c]; input.AcceptsReturn = true; input.TextWrapping = TextWrapping.Wrap; input.MinHeight = 70; input.MaxLength = 2500; input.BorderThickness = new Thickness(0);
                        AutomationProperties.SetName(input, q.Rows[r] + " — " + q.Columns[c]); input.TextChanged += delegate { answer.Cells[row][col] = input.Text; Changed(answer); }; AddCell(grid, input, r + 1, c + 1, false);
                    }
                }
                p.Children.Add(TableViewport(grid, q.Columns.Count));
            }
            Gap(p);
            if (!attempt.Practice && answer.HintUsed)
            {
                var hintTitle = Text("קטע לימוד שיעזור בפתרון", 20, "#176F65"); var hintCard = Card(Text(q.Hint, 17)); p.Children.Add(hintTitle); p.Children.Add(hintCard);
                if (scrollToHint) { scrollToHint = false; Dispatcher.BeginInvoke(new Action(delegate { hintCard.BringIntoView(); }), DispatcherPriority.Loaded); }
            }
            else if (!attempt.Practice) p.Children.Add(Button("רמז לימודי · מפחית חצי מהניקוד לשאלה", delegate { answer.HintUsed = true; scrollToHint = true; Save(); ShowQuestion(); }));
            if (attempt.Practice)
            {
                if (answer.Result != null && answer.Result.AnswerSnapshot == Rules.Snapshot(answer)) p.Children.Add(Text(answer.Result.Comment, 18, answer.Result.Fraction >= 0.9999 ? "#176F65" : "#7C623D"));
                p.Children.Add(Button(answer.Result != null && answer.Result.Fraction >= 0.9999 ? "בדיקת התשובה שוב" : "בדיקת התשובה", delegate {
                    Run("בודקים את דרך החשיבה שלך…", async ct => {
                        var result = (await service.Grade(exam, new List<Answer> { answer }, ct)).Single();
                        answer.Result = result; answer.Tries.Add(result); Save(); ShowQuestion();
                    });
                }, true));
            }
            body.Children.Add(Card(p));
            var navigation = new WrapPanel();
            if (attempt.CurrentQuestion > 0) navigation.Children.Add(Button("השאלה הקודמת", delegate { Save(); attempt.CurrentQuestion--; ShowQuestion(); Save(); }));
            if (attempt.CurrentQuestion < exam.Questions.Count - 1)
            {
                var next = Button("השאלה הבאה", delegate { Save(); attempt.CurrentQuestion++; ShowQuestion(); Save(); }, true); next.Margin = new Thickness(10, 0, 0, 8); navigation.Children.Add(next);
            }
            var finish = Button(attempt.Practice ? "סיום התרגול" : attempt.Quiz ? "סיום ובדיקת הבוחן" : "סיום ובדיקת המבחן", Finish, attempt.CurrentQuestion == exam.Questions.Count - 1); finish.Margin = new Thickness(10, 0, 0, 8); navigation.Children.Add(finish); body.Children.Add(navigation);
            var jumps = new WrapPanel { Margin = new Thickness(0, 12, 0, 12) };
            for (int i = 0; i < exam.Questions.Count; i++)
            {
                int target = i; var a = attempt.Answers[i];
                var b = Button((i + 1).ToString() + (a.HasContent ? " ✓" : ""), delegate { Save(); attempt.CurrentQuestion = target; ShowQuestion(); Save(); }, i == attempt.CurrentQuestion);
                b.Padding = new Thickness(12, 7, 12, 7); b.MinHeight = 34; b.Margin = new Thickness(0, 0, 6, 6);
                AutomationProperties.SetName(b, "מעבר לשאלה " + (i + 1)); jumps.Children.Add(b);
            }
            body.Children.Add(jumps); body.Children.Add(Text("התשובות נשמרות אוטומטית. אפשר לצאת ולחזור מאוחר יותר.", 13, "#596F76")); rendering = false;
        }
        void AddEditor(StackPanel panel, string type, List<string> columns, List<string> rows, string text, List<List<string>> cells, Action<string> setText, Action changed, string name)
        {
            if (type == "text")
            {
                var input = Inputs.Hebrew(); input.Text = text; input.AcceptsReturn = true; input.TextWrapping = TextWrapping.Wrap; input.MinHeight = 105; input.MaxLength = 12000; input.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; input.VerticalContentAlignment = VerticalAlignment.Top;
                AutomationProperties.SetName(input, name + " — התשובה שלך"); input.TextChanged += delegate { setText(input.Text); }; panel.Children.Add(input); return;
            }
            var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
            foreach (var column in columns) grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < columns.Count; c++) AddCell(grid, Text(columns[c], 15), 0, c + 1, true);
            for (int r = 0; r < rows.Count; r++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); AddCell(grid, Text(rows[r], 16), r + 1, 0, true);
                for (int c = 0; c < columns.Count; c++)
                {
                    int row = r, col = c; var input = Inputs.Hebrew(); input.Text = cells[r][c]; input.AcceptsReturn = true; input.TextWrapping = TextWrapping.Wrap; input.MinHeight = 70; input.MaxLength = 2500; input.BorderThickness = new Thickness(0);
                    AutomationProperties.SetName(input, name + " — " + rows[r] + " — " + columns[c]); input.TextChanged += delegate { cells[row][col] = input.Text; changed(); }; AddCell(grid, input, r + 1, c + 1, false);
                }
            }
            panel.Children.Add(TableViewport(grid, columns.Count));
        }
        static ScrollViewer TableViewport(Grid grid, int columns)
        {
            var viewport = new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
            grid.MinWidth = 110 * (columns + 1);
            grid.SetBinding(FrameworkElement.WidthProperty, new Binding("ActualWidth") { Source = viewport });
            return viewport;
        }
        static void AddCell(Grid grid, UIElement child, int row, int column, bool label)
        {
            var border = new Border { Child = child, Background = label ? Brush("#EDF3F0") : Brushes.White, BorderBrush = Brush("#CFDDD7"), BorderThickness = new Thickness(0.5), Padding = new Thickness(label ? 10 : 2), MinWidth = 100 };
            Grid.SetRow(border, row); Grid.SetColumn(border, column); grid.Children.Add(border);
        }
        void Tick()
        {
            if (operation != null) waitingView.RefreshTimes();
            WizardTick();
            PrintTick();
            if (attempt == null || attempt.FinishedUtc != null) { notice.Visibility = Visibility.Collapsed; return; }
            var started = DateTime.Parse(attempt.StartedUtc, null, DateTimeStyles.RoundtripKind);
            var elapsed = DateTime.UtcNow - started;
            if (inQuestion) clock.Text = attempt.Practice ? "בקצב שלך · ללא מגבלת זמן" : "זמן שחלף: " + ((int)elapsed.TotalHours).ToString("00") + ":" + elapsed.Minutes.ToString("00") + ":" + elapsed.Seconds.ToString("00");
            if (inQuestion && Rules.ShowWarning(attempt, DateTime.UtcNow)) { attempt.WarningShown = true; attempt.WarningShownUtc = DateTime.UtcNow.ToString("o"); dirty = true; }
            notice.Visibility = inQuestion && Rules.NoticeVisible(attempt, DateTime.UtcNow) ? Visibility.Visible : Visibility.Collapsed;
        }
        void Finish()
        {
            Save();
            if (attempt.Practice)
            {
                var missing = attempt.Answers.FindIndex(a => a.Result == null || a.Result.Fraction < 0.9999 || a.Result.AnswerSnapshot != Rules.Snapshot(a));
                if (missing >= 0) { attempt.CurrentQuestion = missing; ShowQuestion(); Error(new InvalidOperationException("נשארה שאלה שאפשר לעבוד עליה עוד. יש לבדוק את התשובה ולהמשיך לנסות עד שמצליחים.")); return; }
                try { ExamService.Complete(exam, attempt); Save(); ShowSummary(); } catch (Exception e) { Error(e); }
            }
            else
            {
                int blank = attempt.Answers.Count(a => !a.HasContent);
                if (blank > 0 && !Dialogs.Show(this, "לפני שמסיימים", "יש עוד " + blank + " שאלות ללא תשובה. לסיים בכל זאת?", true)) return;
                Run("קוראים את התשובות ומכינים סיכום…", async ct => {
                    var results = await service.Grade(exam, attempt.Answers, ct);
                    foreach (var result in results) { var a = attempt.Answers.Single(x => x.QuestionId == result.QuestionId); a.Result = result; a.Tries.Add(result); }
                    ExamService.Complete(exam, attempt); Save(); ShowSummary();
                });
            }
        }
        string AnswerText(Question q, Answer a)
        {
            if (a.Parts.Count > 0) return String.Join("\n\n", q.Parts.Select((p, i) => Rules.PartLabel(i) + ": " + Rules.PartText(p, a.Parts.Single(part => part.PartId == p.Id))));
            if (q.Type == "multiple_choice") return a.SelectedOption >= 0 && a.SelectedOption < q.Options.Count ? q.Options[a.SelectedOption] : "לא נבחרה תשובה";
            if (q.Type == "table") return String.Join("\n", q.Rows.Select((r, i) => r + ": " + String.Join(" | ", q.Columns.Select((c, j) => c + " — " + a.Cells[i][j]))));
            return String.IsNullOrWhiteSpace(a.Text) ? "לא נכתבה תשובה" : a.Text;
        }
        void ShowSummary()
        {
            ShowSummaryPage();
            if (operation != null || !exam.Questions.Any(q => FeedbackReport.NeedsModel(q, attempt.Answers.Single(a => a.QuestionId == q.Id)))) return;
            var savedExam = exam; var savedAttempt = attempt; var savedStudent = student;
            Run("מכינים משוב קצר וברור…", async cancellation => {
                if (await new FeedbackReportService(codex).Simplify(savedExam, savedAttempt, cancellation)) store.Save(savedStudent, savedAttempt);
                ShowSummaryPage();
            });
        }
        void ShowSummaryPage()
        {
            Page(attempt.Practice ? "עוד צעד בדרך להבנה" : "סיימת. עכשיו לומדים מהדרך", exam.Title + " · " + (attempt.Quiz ? "בוחן מספר " : "מבחן מספר ") + exam.Number, ShowSummary);
            var overview = Panel();
            if (exam.FilteredSources) overview.Children.Add(Text(new MaterialSelection { Excluded = exam.ExcludedMaterials }.ScopeNote, 16, "#866337"));
            if (attempt.Practice) overview.Children.Add(Text("תרגול שהושלם · ללא ציון", 28, "#176F65"));
            else { overview.Children.Add(Text("הציון שלך: " + attempt.Score.Value.ToString("0.##") + " מתוך 100", 29)); overview.Children.Add(Text(attempt.Passed ? (attempt.Quiz ? "הבוחן הושלם בהצלחה. בפעם הבאה ייפתח הבוחן הבא. ההתקדמות במבחנים המלאים נשמרת בנפרד." : "המבחן הושלם בהצלחה. בפעם הבאה ייפתח המבחן הבא בנושא.") : "כדאי לחזק את הנושאים שבהמשך, ולחזור כשמרגישים מוכנים. ההתקדמות היא בציון גבוה מ־80.")); }
            var weak = exam.Questions.Where(q => !FeedbackReport.Correct(attempt.Answers.Single(x => x.QuestionId == q.Id))).Select(q => q.Topic).Distinct().ToList();
            overview.Children.Add(Text(weak.Count == 0 ? (attempt.Practice ? "ענית בהצלחה בעזרת ההסברים. כדאי להתנסות בהמשך גם בשאלות באופן עצמאי." : "התשובות מראות הבנה טובה של הנושאים במבחן הזה. כדאי לבדוק את ההבנה שוב בהמשך, בשאלות נוספות.") : "כדאי לחזור במיוחד על: " + String.Join(" · ", weak), 18));
            overview.Children.Add(Text("המשוב והמוכנות מתארים את הביצועים בתרגולים שנעשו כאן. הם אינם אבחון של יכולת הלמידה.", 14, "#596F76")); body.Children.Add(Card(overview));
            body.Children.Add(Text("המשוב על השאלות", 24));
            for (int i = 0; i < exam.Questions.Count; i++)
            {
                var q = exam.Questions[i]; var a = attempt.Answers.Single(x => x.QuestionId == q.Id);
                body.Children.Add(Card(SummaryQuestion(i + 1, q, a)));
            }
            body.Children.Add(Button("המשך", ShowAfterSummary, true));
        }
        void ShowAfterSummary()
        {
            Page("איך ממשיכים מכאן?", "אפשר לתת לחומר לשקוע, לחזור לתרגול או לבחור נושא נוסף.", ShowAfterSummary);
            var p = Panel(); p.Children.Add(Button("חזרה למסך הראשי", ShowModes, true)); p.Children.Add(Button(attempt.Quiz ? "בוחן חדש" : "מבחן חדש", ShowExamModes));
            if (!attempt.Passed) p.Children.Add(Text("בנושא הזה ממשיכים " + (attempt.Quiz ? "בבוחן" : "במבחן") + " מספר " + Rules.NextNumber(store.History(student), topic.Name, attempt.Quiz) + " עד למעבר בציון גבוה מ־80.", 15, "#596F76"));
            p.Children.Add(Button("צפייה חוזרת בסיכום", ShowSummary)); p.Children.Add(Button("יציאה מהתוכנה", Close)); body.Children.Add(Card(p));
        }
        void ShowHistory()
        {
            if (student == null) return;
            Save(); var back = currentPage; var activeTopic = topic; var activeExam = exam; var activeAttempt = attempt;
            Page("הדרך שלך בלמידה", "הישגים, נושאים לחיזוק והתקדמות לאורך זמן — " + student.FullName, ShowHistory);
            var history = store.History(student); var finished = history.Where(a => a.FinishedUtc != null).ToList();
            var top = Panel(); top.Children.Add(Text(finished.Count + " פעילויות שהושלמו · " + finished.Count(a => a.Passed && !a.Quiz) + " מבחנים מלאים שעברת · " + finished.Count(a => a.Passed && a.Quiz) + " בחנים שעברת", 25));
            top.Children.Add(Text("המוכנות מתבססת על המבחן העצמאי האחרון. בתרגול עם הסבר פתוח עוקבים אחר הניסיונות וההתקדמות, ובהמשך כדאי לבדוק את ההבנה גם באופן עצמאי.", 16)); body.Children.Add(Card(top));
            foreach (var t in store.Topics())
            {
                var p = Panel(); var h = finished.Where(a => a.TopicName == t.Name).ToList(); var lastReal = h.FirstOrDefault(a => !a.Practice && !a.Quiz); var practice = h.FirstOrDefault(a => a.Practice); var lastQuiz = h.FirstOrDefault(a => a.Quiz);
                p.Children.Add(Text(t.Name, 23));
                string readiness = "עדיין אין מספיק מידע להערכת מוכנות";
                if (lastReal != null) readiness = lastReal.Passed ? "מוכנות טובה לפי המבחן העצמאי האחרון" : "כדאי לחזק את החומר לפני מבחן נוסף";
                else if (practice != null) readiness = "הושלם תרגול עם עזרה. כדאי להתנסות במבחן עצמאי להערכת המוכנות";
                else if (lastQuiz != null) readiness = "הושלם בוחן קצר. כדאי להשלים מבחן מלא כדי להעריך מוכנות בכל החומר";
                p.Children.Add(Text(readiness, 18)); p.Children.Add(Text("המבחן הבא: מספר " + Rules.NextNumber(history, t.Name), 15));
                if ((lastReal != null && lastReal.FilteredSources) || (lastQuiz != null && lastQuiz.FilteredSources)) p.Children.Add(Text("חלק מהחומר הוחרג מהפעילות האחרונה. הציון והערכת המוכנות מתייחסים לחומר שנכלל בלבד; כדאי להשלים גם את החומר שהוחרג.", 16, "#866337"));
                p.Children.Add(Text("הבוחן הבא: מספר " + Rules.NextNumber(history, t.Name, true), 15));
                if (lastQuiz != null) p.Children.Add(Text("הבוחן האחרון: " + lastQuiz.Score.Value.ToString("0.##") + " מתוך 100 · בחנים מכסים חלק מהחומר", 16));
                if (lastReal != null) p.Children.Add(Text("המבחן העצמאי האחרון: " + lastReal.Score.Value.ToString("0.##") + " מתוך 100", 16));
                if (practice != null)
                {
                    bool automaticHints = practice.Answers.Any(a => a.HintShownAutomatically);
                    int firstTryCorrect = practice.Answers.Count(a => (automaticHints || !a.HintUsed) && a.Tries.Count > 0 && a.Tries[0].Fraction >= 0.9999);
                    p.Children.Add(Text((automaticHints ? "בתרגול האחרון עם הסבר פתוח: " : "בתרגול האחרון: ") + firstTryCorrect + " מתוך " + practice.Answers.Count + " שאלות נענו נכון בניסיון הראשון" + (automaticHints ? "." : " וללא רמז."), 16));
                }
                var activity = h;
                foreach (var a in activity)
                {
                    var row = Panel(); string date = DateTime.Parse(a.FinishedUtc, null, DateTimeStyles.RoundtripKind).ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
                    row.Children.Add(Text(date + " · " + (a.Quiz ? "בוחן " : "מבחן ") + a.ExamNumber + " · " + (a.Practice ? "עם עזרה · ללא ציון" : a.Score.Value.ToString("0.##") + "/100"), 15));
                    if (a.FilteredSources) row.Children.Add(Text("נוצר ללא חומר שהוחרג · כיסוי חלקי של חומר הלימוד", 14, "#866337"));
                    var saved = a; var target = t;
                    row.Children.Add(Button("לסיכום הפעילות", delegate { try { topic = target; exam = store.LoadExam(target, saved.ExamNumber, saved.Quiz); attempt = saved; ShowSummary(); } catch (Exception e) { Error(e); } })); p.Children.Add(row);
                }
                body.Children.Add(Card(p));
            }
            if (store.Warnings.Count > 0) body.Children.Add(Text(String.Join("\n", store.Warnings.Distinct()), 14, "#98543E"));
            body.Children.Add(Button("חזרה", delegate { topic = activeTopic; exam = activeExam; attempt = activeAttempt; if (back != null) back(); else ShowModes(); }));
        }
    }
}
