using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Education2k
{
    public class WaitingView : Grid
    {
        readonly TextBlock heading = Label(27), reason = Label(17), elapsed = Label(14), freshness = Label(14), saved = Label(15), activity = Label(14);
        readonly UniformGrid steps = new UniformGrid { Columns = 5, Margin = new Thickness(0, 12, 0, 12) };
        readonly Border[] badges = new Border[5];
        readonly TextBlock[] stepLabels = new TextBlock[5];
        readonly Expander preview;
        readonly ScrollViewer logScroll;
        readonly Button cancel;
        WorkState state;
        string previousLog;
        static SolidColorBrush Color(string hex) { return (SolidColorBrush)new BrushConverter().ConvertFromString(hex); }
        static TextBlock Label(int size)
        {
            return new TextBlock { FontSize = size, Foreground = Color("#213B42"), TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.45, Margin = new Thickness(0, 0, 0, 6) };
        }
        public WaitingView(Action onCancel)
        {
            Margin = new Thickness(28); MaxWidth = 850; HorizontalAlignment = HorizontalAlignment.Stretch; VerticalAlignment = VerticalAlignment.Stretch;
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition()); RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var top = new StackPanel();
            var cat = new Image { Width = 104, Height = 104, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, FlowDirection = FlowDirection.LeftToRight, Margin = new Thickness(0, 0, 0, 8), IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(cat, BitmapScalingMode.HighQuality); LogoAnimation.Attach(cat);
            AutomationProperties.SetName(cat, "החתול קורא וממצמץ בזמן ההכנה"); top.Children.Add(cat);
            heading.TextAlignment = TextAlignment.Center; reason.TextAlignment = TextAlignment.Center; top.Children.Add(heading); top.Children.Add(reason); Children.Add(top);
            for (int index = 0; index < 5; index++)
            {
                stepLabels[index] = Label(14); stepLabels[index].TextAlignment = TextAlignment.Center; stepLabels[index].Margin = new Thickness(0);
                badges[index] = new Border { Child = stepLabels[index], Background = Brushes.White, CornerRadius = new CornerRadius(11), Padding = new Thickness(8), Margin = new Thickness(4) };
                steps.Children.Add(badges[index]);
            }
            SetRow(steps, 1); Children.Add(steps);
            var times = new StackPanel(); elapsed.TextAlignment = freshness.TextAlignment = TextAlignment.Center;
            times.Children.Add(elapsed); times.Children.Add(freshness); SetRow(times, 2); Children.Add(times);
            logScroll = new ScrollViewer { Content = activity, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(14), MaxHeight = 190 };
            preview = new Expander { Header = "מה קורה עכשיו? — פריוויו הפעילות", FontSize = 16, Margin = new Thickness(0, 8, 0, 8), VerticalAlignment = VerticalAlignment.Top,
                Content = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(10), Child = logScroll } };
            preview.Expanded += delegate { logScroll.ScrollToEnd(); }; SetRow(preview, 3); Children.Add(preview);
            var footer = new StackPanel();
            footer.Children.Add(new Border { Child = saved, Background = Color("#E7F1EB"), CornerRadius = new CornerRadius(11), Padding = new Thickness(14, 10, 14, 6), Margin = new Thickness(0, 6, 0, 8) });
            var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
            cancel = new Button { Content = "ביטול וחזרה", FontSize = 16, Margin = new Thickness(7, 0, 7, 0) };
            cancel.Click += delegate { cancel.IsEnabled = false; cancel.Content = "מבטלים את הפעולה…"; onCancel(); }; buttons.Children.Add(cancel);
            footer.Children.Add(buttons); SetRow(footer, 4); Children.Add(footer);
        }
        public void End() { state = null; previousLog = null; activity.Text = ""; preview.IsExpanded = false; }
        public void Begin(WorkState value)
        {
            state = value; previousLog = null; activity.Text = ""; preview.IsExpanded = false;
            cancel.IsEnabled = true; cancel.Content = "ביטול וחזרה"; Refresh();
        }
        public void Refresh()
        {
            if (state == null) return;
            heading.Text = state.Heading; reason.Text = state.WaitingFor; saved.Text = state.FileStatus;
            steps.Visibility = state.IsPreparation ? Visibility.Visible : Visibility.Collapsed;
            for (int index = 0; index < 5; index++)
            {
                string status = state.States[WorkState.Steps[index]];
                string symbol = status == "done" ? "✓" : status == "active" ? "●" : status == "skipped" ? "—" : "○";
                stepLabels[index].Text = symbol + " " + WorkState.Labels[index] + "\n" + (status == "done" ? "הושלם" : status == "active" ? "כעת" : status == "skipped" ? "לא נדרש" : "טרם התחיל");
                stepLabels[index].Foreground = Color(status == "pending" || status == "skipped" ? "#647A7D" : "#176F65");
                badges[index].Background = Color(status == "active" ? "#DDEFE7" : "#FFFFFF");
            }
            string lines = String.Join("\n", state.Log);
            if (lines != previousLog)
            {
                bool atEnd = logScroll.VerticalOffset >= logScroll.ScrollableHeight - 12;
                activity.Text = lines; previousLog = lines;
                if (atEnd && preview.IsExpanded) logScroll.ScrollToEnd();
            }
            RefreshTimes();
        }
        public void RefreshTimes()
        {
            if (state == null) return;
            var now = DateTime.UtcNow; elapsed.Text = "זמן שחלף: " + state.Elapsed(now); freshness.Text = state.Freshness(now);
            freshness.Foreground = Color((now - state.LastUpdateUtc).TotalSeconds >= 60 ? "#866337" : "#596F76");
        }
    }
}
