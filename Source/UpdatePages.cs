using System;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Education2k
{
    public partial class MainWindow
    {
        UpdateService appUpdates;
        readonly bool updateDemo;
        AppUpdate availableUpdate;
        UpdateJob preparedUpdate, pendingInstall;
        UpdateOutcome updateOutcome;
        readonly Border updateBanner = new Border();
        CancellationTokenSource updateDownload;
        TextBlock updateStatus;
        ProgressBar updateProgress;
        Button updateInstallButton, updateRetryButton, updateCancelButton, bannerUpdate, bannerLater;
        bool updatePageVisible;
        string updateMessage;
        void InitializeUpdateBanner(Grid layout)
        {
            var line = new Grid(); line.ColumnDefinitions.Add(new ColumnDefinition()); line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = Text("", 14, "#176F65"); label.Margin = new Thickness(6, 0, 8, 0); label.VerticalAlignment = VerticalAlignment.Center; line.Children.Add(label);
            bannerUpdate = Button("עדכן והפעל מחדש", delegate { if (preparedUpdate != null) InstallApplicationUpdate(); else ShowUpdateDownload(); });
            bannerLater = Button("אחר כך", delegate {
                if (availableUpdate != null && appUpdates.ShouldNotify(availableUpdate)) appUpdates.Defer(availableUpdate);
                else { AutomaticUpdate.DismissOutcome(store.Root); updateOutcome = null; } RefreshUpdateBanner();
            });
            foreach (var action in new[] { bannerUpdate, bannerLater }) { action.FontSize = 14; action.MinHeight = 34; action.Padding = new Thickness(12, 5, 12, 5); action.Margin = new Thickness(4, 0, 0, 0); }
            Grid.SetColumn(bannerUpdate, 1); Grid.SetColumn(bannerLater, 2); line.Children.Add(bannerUpdate); line.Children.Add(bannerLater);
            updateBanner.Child = line; updateBanner.Tag = label; updateBanner.Background = Brush("#E8F2EA"); updateBanner.BorderBrush = Brush("#BCD5C6"); updateBanner.BorderThickness = new Thickness(1); updateBanner.CornerRadius = new CornerRadius(10);
            updateBanner.Padding = new Thickness(10, 7, 10, 7); updateBanner.Margin = new Thickness(34, 0, 34, 10); updateBanner.Visibility = Visibility.Collapsed;
            AutomationProperties.SetName(updateBanner, "עדכון תוכנה"); Grid.SetRow(updateBanner, 1); layout.Children.Add(updateBanner);
        }
        void RefreshUpdateBanner()
        {
            bool eligible = appUpdates != null && availableUpdate != null && appUpdates.ShouldNotify(availableUpdate);
            updateBanner.Visibility = UpdateService.NoticeVisible(homeScreen, operation != null, eligible || updateOutcome != null) ? Visibility.Visible : Visibility.Collapsed;
            bannerUpdate.Visibility = eligible ? Visibility.Visible : Visibility.Collapsed;
            bannerUpdate.Content = preparedUpdate == null ? "פרטי העדכון" : "עדכן והפעל מחדש";
            bannerLater.Content = eligible ? "אחר כך" : "סגירה";
            ((TextBlock)updateBanner.Tag).Text = eligible ? (preparedUpdate == null ? updateMessage ?? "מכינים עדכון ברקע…" : "גרסה \u200e" + availableUpdate.Version + "\u200e מוכנה. מעדכנים בזמן שנוח לכם.") :
                updateOutcome == null ? "" : updateOutcome.Status == "updated" ? "העדכון הושלם בהצלחה. קובצי העדכון הזמניים נוקו." : updateOutcome.Status == "rolled-back" ? "העדכון לא הצליח. הגרסה הקודמת שוחזרה והנתונים נשמרו." : updateOutcome.Status == "busy" ? "יש לסגור עותקים נוספים של התוכנה לפני העדכון. לא הוחלפו קבצים." : "העדכון נעצר. הגיבוי נשמר לצורך השלמת השחזור בהפעלה הבאה.";
        }
        async void CheckApplicationUpdates()
        {
            try
            {
                if (appUpdates == null) appUpdates = new UpdateService(store.Root, AppInfo.Version);
                updateOutcome = AutomaticUpdate.LastOutcome(store.Root);
                availableUpdate = await appUpdates.Check(lifetime.Token);
                if (availableUpdate != null && updateOutcome != null && updateOutcome.Version == availableUpdate.Version && updateOutcome.Status == "rolled-back") appUpdates.Defer(availableUpdate);
                if (lifetime.IsCancellationRequested) return; RefreshUpdateBanner();
                if (availableUpdate != null && appUpdates.ShouldNotify(availableUpdate)) PrepareApplicationUpdate();
            }
            catch (OperationCanceledException) { }
            catch (Exception) { /* Update availability must never interrupt learning. */ }
        }
        void ShowUpdateDownload()
        {
            if (availableUpdate == null) return;
            Page("עדכון התוכנה", "גרסה מותקנת: \u200e" + AppInfo.Version + "\u200e | גרסה חדשה: \u200e" + availableUpdate.Version + "\u200e", ShowUpdateDownload);
            updatePageVisible = true;
            var panel = Panel(); panel.Children.Add(Text("העדכון מוכן בזמן שאתם ממשיכים ללמוד", 23));
            if (updateDemo) panel.Children.Add(Text("תצוגת בדיקה בלבד: החבילה סינתטית. לחיצה על הכפתור לא תחליף קבצים ולא תסגור את התוכנה.", 16, "#98543E"));
            panel.Children.Add(Text("החבילה יורדת ונבדקת ברקע. רק כשתלחצו על „עדכן והפעל מחדש”, התוכנה תשמור את העבודה, תיסגר ותיפתח בגרסה החדשה.", 17));
            updateStatus = Text(updateMessage ?? "מכינים את העדכון…", 16);
            updateProgress = new ProgressBar { Height = 7, Minimum = 0, Maximum = 100, IsIndeterminate = true, Margin = new Thickness(0, 0, 0, 14) };
            panel.Children.Add(updateStatus); panel.Children.Add(updateProgress);
            updateInstallButton = Button("עדכן והפעל מחדש", InstallApplicationUpdate, true); panel.Children.Add(updateInstallButton);
            updateRetryButton = Button("ניסיון נוסף להכנת העדכון", PrepareApplicationUpdate); panel.Children.Add(updateRetryButton);
            updateCancelButton = Button("ביטול ההורדה", delegate { if (updateDownload != null) updateDownload.Cancel(); }); panel.Children.Add(updateCancelButton);
            body.Children.Add(Card(panel));
            var details = Panel(); details.Children.Add(Text("החומרים וההישגים נשארים במקומם", 23));
            details.Children.Add(Text("מוחלפים רק קובצי התוכנה. חומרי הלימוד, המבחנים, פרטי התלמידים, ההישגים, הטיוטות ופרטי החיבור המקומיים נשארים באותה תיקייה.", 17));
            details.Children.Add(Text("לפני ההחלפה נוצר גיבוי זמני של קובצי התוכנה. אם הגרסה החדשה אינה מצליחה להיפתח, הגרסה הקודמת משוחזרת אוטומטית.", 17));
            details.Children.Add(Text("קובץ ה־ZIP נמחק לאחר הכנת הקבצים. קובצי ההתקנה והגיבוי הזמניים נמחקים אחרי עדכון או שחזור שהושלם. אם השחזור עצמו נקטע, הגיבוי נשמר עד להשלמתו בהפעלה הבאה.", 16, "#176F65"));
            details.Children.Add(Text("אין צורך לחלץ ZIP, להעתיק קבצים או להזין סיסמה. בדיקת העדכון וההורדה פונות לריליס הציבורי בלבד ואינן שולחות מידע על תלמידים.", 16));
            body.Children.Add(Card(details)); body.Children.Add(Button("חזרה למסך הראשי", delegate { if (student == null) ShowLogin(); else ShowModes(); })); RefreshUpdateDownloadControls();
        }
        void RefreshUpdateDownloadControls()
        {
            RefreshUpdateBanner(); if (!updatePageVisible || updateInstallButton == null) return;
            updateInstallButton.IsEnabled = preparedUpdate != null && operation == null;
            updateRetryButton.Visibility = updateDownload == null && preparedUpdate == null ? Visibility.Visible : Visibility.Collapsed;
            updateCancelButton.Visibility = updateDownload == null ? Visibility.Collapsed : Visibility.Visible;
            updateProgress.Visibility = updateDownload == null ? Visibility.Collapsed : Visibility.Visible;
            if (updateMessage != null) updateStatus.Text = updateMessage;
        }
        async void PrepareApplicationUpdate()
        {
            if (updateDownload != null || availableUpdate == null || preparedUpdate != null) return;
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); updateDownload = cancellation;
            updateMessage = "מורידים ובודקים את חבילת העדכון…"; RefreshUpdateDownloadControls();
            var progress = new Progress<long>(received => {
                if (updateDownload != cancellation) return;
                updateMessage = "מורידים עדכון ברקע: " + (received / 1048576.0).ToString("0.0") + " MB"; RefreshUpdateDownloadControls();
                if (updatePageVisible) { updateProgress.IsIndeterminate = availableUpdate.UnknownSize; if (!availableUpdate.UnknownSize) updateProgress.Value = Math.Min(100, received * 100.0 / availableUpdate.Size); }
            });
            try
            {
                preparedUpdate = await AutomaticUpdate.Prepare(appUpdates, availableUpdate, store.Root, AppInfo.Version, cancellation.Token, progress, updateDemo);
                updateMessage = "העדכון נבדק ומוכן. לחצו על „עדכן והפעל מחדש” בזמן שנוח לכם.";
            }
            catch (OperationCanceledException) { updateMessage = "ההורדה בוטלה והקבצים הזמניים נמחקו. אפשר לנסות שוב."; }
            catch (Exception) { updateMessage = "העדכון לא הוכן. התוכנה והנתונים נשארו ללא שינוי. בדקו את החיבור לאינטרנט והרשאות הכתיבה ונסו שוב."; }
            finally { updateDownload = null; cancellation.Dispose(); if (!lifetime.IsCancellationRequested) RefreshUpdateDownloadControls(); }
        }
        void InstallApplicationUpdate()
        {
            if (preparedUpdate == null || operation != null) return;
            if (updateDemo) { updateMessage = "תצוגת בדיקה: זהו הכפתור שישמור, יעדכן ויפעיל מחדש. לא בוצעה התקנה בחבילת ההדגמה."; ShowUpdateDownload(); return; }
            pendingInstall = preparedUpdate; Close(); pendingInstall = null;
        }
        void StartPendingUpdate()
        {
            if (pendingInstall != null) using (AutomaticUpdate.Start(pendingInstall)) { }
        }
        public void ApplicationUpdateCompleted(UpdateOutcome outcome)
        {
            updateOutcome = outcome; if (!lifetime.IsCancellationRequested) RefreshUpdateBanner();
        }
    }
}
