using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Education2k
{
    public static class Dialogs
    {
        static TextBlock ReviewText(string value, int size = 16)
        {
            return new TextBlock { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.55, Foreground = new SolidColorBrush(Color.FromRgb(33, 59, 66)), Margin = new Thickness(0, 0, 0, 12) };
        }
        public static MaterialDecision ConfirmMaterials(Window owner, MaterialReview report)
        {
            MaterialDecision decision = MaterialDecision.Cancel;
            var window = new Window { Title = "בעיה בקריאת חומרי הלימוד", Width = 800, Height = Math.Min(760, SystemParameters.WorkArea.Height - 60), MinHeight = 400,
                FlowDirection = FlowDirection.RightToLeft, FontFamily = new FontFamily("Segoe UI"), WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(Color.FromRgb(244, 247, 243)), ShowInTaskbar = false };
            if (owner != null && owner.IsVisible) { window.Owner = owner; window.Icon = owner.Icon; }
            var grid = new Grid { Margin = new Thickness(26) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var heading = new StackPanel();
            heading.Children.Add(ReviewText(report.ExistingExam ? "המבחן השמור נוצר עם חומר חסר" : "רגע לפני שיוצרים את המבחן", 27));
            heading.Children.Add(ReviewText("יש בעיה מהותית בקריאת המקורות, ולכן ייתכן שהמבחן לא יכסה את כל חומר הלימוד. הבחירה אם להמשיך היא שלכם.", 17)); grid.Children.Add(heading);
            var details = new StackPanel(); details.Children.Add(ReviewText("הסבר הצ׳אט", 21)); details.Children.Add(ReviewText(report.Summary));
            foreach (var file in report.Files)
            {
                if (file.Status == "read") continue;
                var card = new StackPanel(); card.Children.Add(ReviewText(file.FileName + " · " + (file.Status == "unread" ? "לא נקרא" : "נקרא חלקית"), 19));
                card.Children.Add(ReviewText(file.Explanation)); card.Children.Add(ReviewText("איך זה משפיע על המבחן?", 17)); card.Children.Add(ReviewText(file.Impact));
                card.Children.Add(ReviewText("מה אפשר לעשות?", 17)); card.Children.Add(ReviewText(file.Recommendation));
                details.Children.Add(new Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(200, 143, 106)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(18), Margin = new Thickness(0, 0, 0, 16), Child = card });
            }
            var scroll = new ScrollViewer { Content = details, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 0, 8) }; Grid.SetRow(scroll, 1); grid.Children.Add(scroll);
            var footer = new StackPanel();
            footer.Children.Add(ReviewText(report.ExistingExam ? "המשך למבחן השמור אינו מתקן את החומר שהיה חסר בזמן יצירתו." : "בחירה בחזרה תעצור כאן. לא ייווצר ולא יישמר מבחן חדש.", 14));
            if (!report.ExistingExam)
            {
                footer.Children.Add(ReviewText("אפשר גם ליצור מבחן רק מהחומר שנקרא במלואו. כשאפשר לזהות עמוד או שקופית בעייתיים, יוחרגו רק הם; אחרת יוחרג הקובץ כולו. החיפוש ברשת לא ישלים חומר שהוחרג.", 14));
                var excluded = report.ExclusionPreview ?? report.Files.Where(f => f.Status != "read").Select(f => f.FileName).Distinct().ToList();
                if (excluded.Count > 0) footer.Children.Add(new Expander { Header = "מה לא ייכלל אם אבחר להחריג?", FontSize = 14, Content = new ScrollViewer { MaxHeight = 100, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = ReviewText(String.Join("\n", excluded), 14) }, Margin = new Thickness(0, 0, 0, 8) });
                if (!report.HasUsableMaterial) footer.Children.Add(ReviewText("לא נשאר חומר קריא מספיק לאחר ההחרגה. יש להוסיף או לתקן חומר לפני יצירת מבחן באפשרות הזאת.", 14));
            }
            var buttons = new WrapPanel();
            var cancel = new Button { Content = "חזור למסך הראשי", IsCancel = true, IsDefault = true, FontSize = 16, Margin = new Thickness(0, 0, 0, 8) };
            cancel.Click += delegate { window.DialogResult = false; }; buttons.Children.Add(cancel);
            var proceed = new Button { Content = report.ExistingExam ? "המשך למבחן השמור בכל זאת" : "צור את המבחן בכל זאת", FontSize = 16, Margin = new Thickness(12, 0, 0, 8) };
            proceed.Click += delegate { decision = MaterialDecision.Continue; window.DialogResult = true; }; buttons.Children.Add(proceed);
            if (!report.ExistingExam)
            {
                var exclude = new Button { Content = "צור את המבחן ללא החומר הבעייתי", IsEnabled = report.HasUsableMaterial, FontSize = 16, Margin = new Thickness(12, 0, 0, 8), Background = new SolidColorBrush(Color.FromRgb(23, 111, 101)), Foreground = Brushes.White };
                exclude.Click += delegate { decision = MaterialDecision.ExcludeProblematic; window.DialogResult = true; }; buttons.Children.Add(exclude);
            }
            footer.Children.Add(buttons); Grid.SetRow(footer, 2); grid.Children.Add(footer);
            window.Content = grid; window.ShowDialog(); return decision;
        }
        public static bool Show(Window owner, string title, string message, bool confirm)
        {
            var window = new Window { Title = title, Width = 540, MaxHeight = 650, SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize, FlowDirection = FlowDirection.RightToLeft, FontFamily = new FontFamily("Segoe UI"),
                WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(Color.FromRgb(244, 247, 243)), ShowInTaskbar = false };
            if (owner != null && owner.IsVisible) window.Owner = owner;
            var p = new StackPanel { Margin = new Thickness(26) };
            p.Children.Add(new TextBlock { Text = title, FontSize = 24, Margin = new Thickness(0, 0, 0, 14) });
            int diagnostic = message.IndexOf("פרטים טכניים:", StringComparison.Ordinal);
            string main = diagnostic < 0 ? message : message.Substring(0, diagnostic).Trim();
            p.Children.Add(new TextBlock { Text = main, FontSize = 17, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) });
            if (diagnostic >= 0) p.Children.Add(new Expander { Header = "פרטים טכניים", Content = new TextBlock { Text = message.Substring(diagnostic), FontSize = 13, TextWrapping = TextWrapping.Wrap }, Margin = new Thickness(0, 0, 0, 14) });
            var accept = new Button { Content = confirm ? "לסיום המבחן" : "חזרה", IsDefault = !confirm };
            accept.Click += delegate { window.DialogResult = true; }; p.Children.Add(accept);
            if (confirm) { var cancel = new Button { Content = "להמשיך לענות", IsDefault = true, IsCancel = true }; cancel.Click += delegate { window.DialogResult = false; }; p.Children.Add(cancel); }
            window.Content = new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            return window.ShowDialog() == true;
        }
    }
}
