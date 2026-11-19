using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Education2k
{
    public class ExamImage
    {
        public string Id, RelativePath, ImageHash, Caption, Source;
    }
    public static class SourceImages
    {
        public static string Digest(string path) { using (var sha = SHA256.Create()) using (var input = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", ""); }
        public static string Resolve(Topic topic, string relative)
        {
            if (String.IsNullOrEmpty(relative) || Path.IsPathRooted(relative) || relative.Contains(":") || relative.Contains("\\")) throw new InvalidOperationException("נתיב תמונת המקור אינו תקין.");
            string basePath = Path.GetFullPath(Path.Combine(topic.Folder, "_materials")) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(topic.Folder, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(basePath, StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path) != ".png") throw new InvalidOperationException("התמונה חייבת להיות חלק מחומרי המבחן המקומיים.");
            var current = new FileInfo(path) as FileSystemInfo;
            while (current != null && current.FullName.Length >= basePath.Length - 1)
            {
                if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("אין לקרוא תמונה דרך קישור לתיקייה אחרת.");
                current = current is FileInfo ? ((FileInfo)current).Directory : ((DirectoryInfo)current).Parent;
            }
            return path;
        }
        public static bool Valid(Topic topic, SourceVisual visual)
        {
            try { string path = Resolve(topic, visual.RelativePath); return File.Exists(path) && Digest(path) == visual.ImageHash; }
            catch (Exception e) { if (!(e is IOException) && !(e is InvalidOperationException) && !(e is ArgumentException) && !(e is UnauthorizedAccessException)) throw; return false; }
        }
        public static void Bind(Exam exam, Topic topic, MaterialSnapshot snapshot)
        {
            var available = snapshot.Files.SelectMany(f => f.Visuals).Where(v => v.Reading != null && v.Reading.Status == "read" && v.Reading.ExamSuitable).ToDictionary(v => v.Id);
            var selected = exam.Questions.SelectMany(q => q.ImageIds ?? new List<string>()).Distinct().ToList();
            exam.Images = new List<ExamImage>();
            foreach (string id in selected)
            {
                SourceVisual visual;
                if (!available.TryGetValue(id, out visual) || !Valid(topic, visual)) throw new InvalidOperationException("המבחן מפנה לתמונה שלא פוענחה או שאינה מתאימה להצגה: " + id);
                exam.Images.Add(new ExamImage { Id = id, RelativePath = visual.RelativePath, ImageHash = visual.ImageHash, Caption = visual.Reading.Caption, Source = visual.FileName + " · " + visual.Label });
            }
        }
        static BitmapFrame Load(Topic topic, ExamImage image)
        {
            string path = Resolve(topic, image.RelativePath);
            if (!File.Exists(path) || Digest(path) != image.ImageHash) throw new InvalidOperationException("חסרה תמונה ששייכת למבחן או שתוכנה השתנה. יש לשחזר את תיקיית _materials יחד עם קובץ המבחן.");
            using (var input = File.OpenRead(path)) { var frame = BitmapFrame.Create(input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad); frame.Freeze(); return frame; }
        }
        public static void Check(Topic topic, Exam exam)
        {
            foreach (var image in exam.Images ?? new List<ExamImage>()) Load(topic, image);
        }
        public static void Add(Panel parent, Window owner, Topic topic, Exam exam, Question question)
        {
            foreach (string id in question.ImageIds ?? new List<string>())
            {
                var asset = exam.Images.Single(a => a.Id == id); var bitmap = Load(topic, asset);
                var picture = new Image { Source = bitmap, Stretch = Stretch.Uniform, MaxHeight = 430, Margin = new Thickness(0, 10, 0, 5), FlowDirection = FlowDirection.LeftToRight };
                var stack = new StackPanel(); stack.Children.Add(picture);
                stack.Children.Add(new TextBlock { Text = asset.Caption, TextWrapping = TextWrapping.Wrap, FontSize = 14, Margin = new Thickness(8) });
                var button = new Button { Content = "הגדלת התמונה", HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(4) };
                button.Click += delegate {
                    var zoom = new Window { Owner = owner, Title = asset.Caption, Width = Math.Min(1150, SystemParameters.WorkArea.Width - 40), Height = Math.Min(850, SystemParameters.WorkArea.Height - 40), WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brushes.White, FlowDirection = FlowDirection.RightToLeft };
                    zoom.Content = new ScrollViewer { Content = new Image { Source = bitmap, Stretch = Stretch.None, FlowDirection = FlowDirection.LeftToRight }, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; zoom.ShowDialog();
                }; stack.Children.Add(button); parent.Children.Add(new Border { Child = stack, Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(211, 226, 222)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 5, 0, 14) });
            }
        }
    }
}
