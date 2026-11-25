using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Education2k
{
    public static class ScrollWheelRouting
    {
        public static void Enable(ScrollViewer page)
        {
            page.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e) {
                if (e.Handled || e.Delta == 0 || Keyboard.Modifiers != ModifierKeys.None) return;
                bool nested = false;
                for (var node = e.OriginalSource as DependencyObject; node != null; node = Parent(node))
                {
                    var viewer = node as ScrollViewer;
                    if (viewer == null) continue;
                    if (CanScroll(viewer, e.Delta))
                    {
                        // Let a scrollable answer field handle its own wheel normally.
                        // Otherwise bypass the inner viewer that would consume the event
                        // even though it cannot move, and use the nearest scrollable parent.
                        if (nested)
                        {
                            e.Handled = true;
                            viewer.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = Mouse.MouseWheelEvent, Source = viewer });
                        }
                        return;
                    }
                    if (viewer == page) return;
                    nested = true;
                }
            };
        }
        static bool CanScroll(ScrollViewer viewer, int delta)
        {
            return viewer.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled && viewer.ScrollableHeight > 0.01 &&
                (delta > 0 ? viewer.VerticalOffset > 0.01 : viewer.VerticalOffset < viewer.ScrollableHeight - 0.01);
        }
        static DependencyObject Parent(DependencyObject element)
        {
            var content = element as ContentElement;
            if (content != null)
            {
                var parent = ContentOperations.GetParent(content);
                var frameworkContent = content as FrameworkContentElement;
                return parent ?? (frameworkContent == null ? null : frameworkContent.Parent);
            }
            return element is Visual || element is Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
    }
}
