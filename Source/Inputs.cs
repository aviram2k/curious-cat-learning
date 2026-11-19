using System.Windows;
using System.Windows.Controls;

namespace Education2k
{
    public static class Inputs
    {
        public static TextBox Hebrew()
        {
            return new HebrewTextBox();
        }
    }
    public class HebrewTextBox : TextBox
    {
        public HebrewTextBox()
        {
            SetResourceReference(StyleProperty, typeof(TextBox));
            FlowDirection = FlowDirection.RightToLeft;
            TextAlignment = TextAlignment.Left;
        }
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            // Keep the view and the control consistent after Windows changes keyboard
            // direction; inherited styles alone can be overridden by that change.
            if (e.Property == FlowDirectionProperty && (FlowDirection)e.NewValue != FlowDirection.RightToLeft) FlowDirection = FlowDirection.RightToLeft;
            if (e.Property == TextAlignmentProperty && (TextAlignment)e.NewValue != TextAlignment.Left) TextAlignment = TextAlignment.Left;
        }
    }
}
