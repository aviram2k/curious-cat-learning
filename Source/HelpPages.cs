using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Education2k
{
    public partial class MainWindow
    {
        static StackPanel GuideStep(int number, string title, string instructions)
        {
            var panel = Panel();
            var heading = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            heading.Children.Add(new Border { Background = Brush("#E5F1EB"), CornerRadius = new CornerRadius(20), Width = 38, Height = 38, Margin = new Thickness(12, 0, 0, 0),
                Child = new TextBlock { Text = number.ToString(), FontSize = 21, Foreground = Brush("#176F65"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
            heading.Children.Add(Text(title, 23)); panel.Children.Add(heading); panel.Children.Add(Text(instructions)); return panel;
        }
        static object GuideTreeLabel(string label, bool folder)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 5) };
            row.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse(folder ? "M1,6 L1,20 L23,20 L23,6 L12,6 L9,2 L1,2 Z" : "M4,1 L16,1 L22,7 L22,25 L4,25 Z M16,1 L16,7 L22,7"),
                Fill = Brush(folder ? "#E8B85F" : "#EDF5F1"), Stroke = Brush(folder ? "#A77D31" : "#176F65"), StrokeThickness = 1.3, Width = 25, Height = 26, Stretch = Stretch.Uniform, Margin = new Thickness(8, 0, 0, 0) });
            row.Children.Add(new TextBlock { Text = label, FlowDirection = label == "_describe.txt" ? FlowDirection.LeftToRight : FlowDirection.RightToLeft, FontSize = 17, Foreground = Brush("#213B42"), VerticalAlignment = VerticalAlignment.Center }); return row;
        }
        void ShowAddExamGuide()
        {
            if (currentPage == ShowAddExamGuide) return;
            Save(); Action back = currentPage;
            Page("כיצד מוסיפים מבחן חדש?", "מכינים תיקייה עם חומרי הלימוד, והמערכת מכינה ממנה את השאלות. אפשר לעשות זאת בנחת, צעד אחר צעד.", ShowAddExamGuide);
            var first = GuideStep(1, "פותחים את תיקיית המבחנים", "כל נושא מקבל תיקייה משלו בתוך התיקייה „מבחנים” של התוכנה. הכפתור הבא יפתח את המקום הנכון במחשב הזה.");
            first.Children.Add(Button("פתיחת תיקיית המבחנים", delegate {
                try { Process.Start(new ProcessStartInfo { FileName = Path.Combine(store.Root, "מבחנים"), UseShellExecute = true }); }
                catch (Exception e) { Error(e); }
            }, true)); body.Children.Add(Card(first));

            var second = GuideStep(2, "יוצרים תיקייה לנושא החדש", "בתוך „מבחנים” יוצרים תיקייה חדשה ונותנים לה שם ברור, למשל „מבחן בהיסטוריה כיתה ח”. שם התיקייה הוא השם שיופיע ברשימת הנושאים.");
            var root = new TreeViewItem { Header = GuideTreeLabel("מבחנים", true), IsExpanded = true };
            var topicFolder = new TreeViewItem { Header = GuideTreeLabel("מבחן בהיסטוריה כיתה ח", true), IsExpanded = true };
            topicFolder.Items.Add(new TreeViewItem { Header = GuideTreeLabel("_describe.txt", false) });
            topicFolder.Items.Add(new TreeViewItem { Header = GuideTreeLabel("סיכום שיעור.docx", false) });
            topicFolder.Items.Add(new TreeViewItem { Header = GuideTreeLabel("מצגת לימוד.pptx", false) });
            topicFolder.Items.Add(new TreeViewItem { Header = GuideTreeLabel("דף מיקוד.pdf", false) });
            root.Items.Add(topicFolder);
            var tree = new TreeView { Background = Brushes.Transparent, BorderThickness = new Thickness(0), IsHitTestVisible = false, Focusable = false, Padding = new Thickness(10) };
            tree.Items.Add(root); second.Children.Add(new Border { Background = Brush("#F4F7F3"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14), Child = tree });
            second.Children.Add(Text("כך נראית תיקייה לדוגמה. שמות קובצי החומר יכולים להיות בעברית.", 14, "#596F76")); body.Children.Add(Card(second));

            var third = GuideStep(3, "כותבים תיאור קצר של המבחן", "בתוך תיקיית הנושא שומרים קובץ טקסט בשם המדויק שמופיע כאן. אפשר ליצור אותו בפנקס הרשימות. כותבים בו את הכיתה, הנושאים, המקורות ומה צריך לדעת למבחן.");
            third.Children.Add(new TextBlock { Text = "_describe.txt", FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Right, FontFamily = new FontFamily("Consolas"), FontSize = 21, Foreground = Brush("#176F65"), Margin = new Thickness(0, 0, 0, 12) });
            third.Children.Add(new Border { Background = Brush("#F4F7F3"), CornerRadius = new CornerRadius(12), Padding = new Thickness(18), Child = Text("דוגמה לתוכן התיאור:\nכיתה ח, היסטוריה — לידתה של אירופה.\nהחומר נמצא במצגת ובסיכום המצורפים.\nיש לכלול את נפילת רומא, ממלכת הפרנקים והתפשטות הנצרות.\nאין לכלול נושאים שלא מופיעים בחומר.", 16) });
            third.Children.Add(Text("שימו לב שהשם לא נשמר בטעות כ־_describe.txt.txt. בסייר הקבצים אפשר להציג את סיומות הקבצים כדי לבדוק זאת.", 15, "#596F76")); body.Children.Add(Card(third));

            var fourth = GuideStep(4, "מוסיפים את חומרי הלימוד עצמם", "מעתיקים לתיקיית הנושא את דפי הסיכום, דף המטרים והמצגות. אפשר לצרף PDF, קובצי Word מסוג DOCX, מצגות PowerPoint מסוג PPTX, תמונות וקובצי טקסט. אין צורך לתת לקובצי החומר שמות מיוחדים.");
            fourth.Children.Add(Text("כדאי לצרף את התוכן המלא שרוצים ללמוד. כתיבת שם של ספר או מספרי עמודים בלבד אינה נותנת למערכת את העמודים עצמם.", 16));
            fourth.Children.Add(Text("המערכת מפענחת דרך Codex גם טקסט בתמונות, סריקות, מפות ותרשימים, ושומרת את הפענוח לשימוש חוזר. כדאי לצרף עמודים שלמים ותמונות חדות. לקריאת התצוגה המלאה של מצגות נדרש PowerPoint, ולתצוגה מלאה של Word עם תמונות נדרש Word. אם אינם מותקנים, שמרו את החומר כ־PDF וצרפו אותו.", 16));
            fourth.Children.Add(Text("שמות שמתחילים בקו תחתון שמורים למערכת ואינם נקראים כחומר לימוד. את הקבצים שמים ישירות בתיקיית הנושא, ולא בתוך תיקיות נוספות.", 15, "#596F76")); body.Children.Add(Card(fourth));

            var fifth = GuideStep(5, "חוזרים לתוכנה ובוחרים את הנושא", "נכנסים למבחנים ולבחירת נושא. אם הרשימה כבר פתוחה, לוחצים „רענון הנושאים”. בוחרים את התיקייה החדשה ואז את סוג הפעילות.");
            fifth.Children.Add(Text("בפעם הראשונה המערכת בודקת את קריאת החומרים לפני הכנת השאלות. אם יש בעיה, יוצג הסבר מפורט מהצ׳אט. אפשר ליצור את המבחן בכל זאת, ליצור רק מהחומר שנקרא במלואו בלי החומר הבעייתי, או לחזור למסך הראשי ולתקן את החומר. לפני החרגה תוכלו לראות מה לא ייכלל.", 16));
            fifth.Children.Add(Text("המבחן הראשון נשמר אוטומטית ומשמש גם תלמידים נוספים. אין צורך להכין שאלות בעצמכם. להוספת חומר חדש אחרי שכבר נוצר מבחן, מומלץ ליצור תיקיית נושא חדשה כדי שהמבחן החדש ייבנה מהחומר המעודכן.", 16)); body.Children.Add(Card(fifth));
            body.Children.Add(Button("חזרה למסך הקודם", delegate { if (back != null) back(); else ShowLogin(); }));
        }
    }
}
