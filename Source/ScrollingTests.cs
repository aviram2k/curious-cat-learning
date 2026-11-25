using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Education2k
{
    public static partial class Tests
    {
        static void ScrollLayout(ScrollViewer page)
        {
            page.Measure(new Size(700, 300)); page.Arrange(new Rect(0, 0, 700, 300)); page.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(delegate { })); page.UpdateLayout();
        }
        static void Wheel(UIElement source, ScrollViewer page, int delta)
        {
            var preview = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
            source.RaiseEvent(preview);
            if (!preview.Handled) source.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = Mouse.MouseWheelEvent });
            ScrollLayout(page);
        }
        static T ScrollDescendant<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i); var found = child as T ?? ScrollDescendant<T>(child); if (found != null) return found;
            }
            return null;
        }
        static void ScrollChecks()
        {
            var answer = Inputs.Hebrew(); answer.Height = 90; answer.AcceptsReturn = true; answer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            var table = new Grid { Width = 1100 }; table.Children.Add(answer);
            var horizontal = new ScrollViewer { Content = table, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
            var content = new StackPanel(); content.Children.Add(horizontal); content.Children.Add(new Border { Height = 1000 });
            var page = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; ScrollWheelRouting.Enable(page); ScrollLayout(page);
            var innerAnswer = ScrollDescendant<ScrollViewer>(answer);
            var answerSurface = ScrollDescendant<ScrollContentPresenter>(answer);
            Assert(innerAnswer != null && answerSurface != null && horizontal.ScrollableWidth > 0 && page.ScrollableHeight > 0, "Scrolling regression fixture contains a real answer-field viewer inside a wide table and a short page viewport");
            Wheel(answerSurface, page, -120);
            Assert(page.VerticalOffset > 0 && horizontal.HorizontalOffset == 0 && answer.Text == "", "Wheel over an empty table answer scrolls the exam page without moving the table sideways or changing the answer");
            double lower = page.VerticalOffset; Wheel(answerSurface, page, 120);
            Assert(page.VerticalOffset < lower, "Wheel over a table answer also scrolls the exam page upward");
            Wheel(table, page, -120);
            Assert(page.VerticalOffset > 0, "Wheel over table content bypasses a horizontal-only viewer and scrolls the page");
            page.ScrollToVerticalOffset(0); answer.Text = String.Join("\n", Enumerable.Repeat("תשובה ארוכה בעברית", 45)); ScrollLayout(page);
            Wheel(answerSurface, page, -120);
            Assert(innerAnswer.ScrollableHeight > 0 && innerAnswer.VerticalOffset > 0 && page.VerticalOffset == 0, "A long answer retains its own wheel scrolling while it has more text in that direction");
            innerAnswer.ScrollToBottom(); ScrollLayout(page); Wheel(answerSurface, page, -120);
            Assert(page.VerticalOffset > 0 && innerAnswer.VerticalOffset == innerAnswer.ScrollableHeight, "At the bottom of a long answer, wheel scrolling continues on the surrounding exam page");
            double pageOffset = page.VerticalOffset; Wheel(answerSurface, page, 120);
            Assert(innerAnswer.VerticalOffset < innerAnswer.ScrollableHeight && page.VerticalOffset == pageOffset, "Changing wheel direction first scrolls back through the long answer rather than skipping it");
            innerAnswer.ScrollToTop(); ScrollLayout(page); Wheel(answerSurface, page, 120);
            Assert(page.VerticalOffset < pageOffset && innerAnswer.VerticalOffset == 0, "At the top of a long answer, upward wheel scrolling continues on the exam page");
            double sideways = horizontal.ScrollableWidth / 2; horizontal.ScrollToHorizontalOffset(sideways); ScrollLayout(page);
            Assert(Math.Abs(horizontal.HorizontalOffset - sideways) < 1, "The table's horizontal scrollbar remains usable independently of page-wheel routing");

            var text = new TextBlock(); var run = new Run("תוכן בתוך אזור גלילה פנימי"); text.Inlines.Add(run);
            var inner = new ScrollViewer { Height = 70, Content = text, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            content.Children.Clear(); content.Children.Add(inner); content.Children.Add(new Border { Height = 1000 }); page.ScrollToTop(); ScrollLayout(page);
            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent }; run.RaiseEvent(wheel); ScrollLayout(page);
            Assert(wheel.Handled && page.VerticalOffset > 0, "Inline text in a nested viewer routes wheel scrolling to the page through content-element parents");
        }
        public static string PrepareScrollPreview(string root)
        {
            string previewRoot = Path.Combine(root, "Build", "UiScrolling-" + Guid.NewGuid().ToString("N")); var previewStore = new Store(previewRoot);
            var topic = TestTopic(previewStore, "בדיקת גלילה", false); var exam = Fixture(); exam.Title = "בדיקת גלילה בחלון קטן · חומר סינתטי";
            var table = exam.Questions[4]; table.Prompt = "מלאו את הטבלה. זהו תוכן בדיקה בלבד.";
            table.Columns = Enumerable.Range(1, 6).Select(i => "תכונה " + i).ToList(); table.Rows = Enumerable.Range(1, 4).Select(i => "חומר " + i).ToList();
            table.CorrectCells = table.Rows.Select(r => table.Columns.Select(c => "תשובה").ToList()).ToList();
            exam.MaterialHash = MaterialReader.Hash(topic); Rules.Validate(exam); Store.Write(previewStore.ExamPath(topic, 1), exam); return previewRoot;
        }
    }
}
