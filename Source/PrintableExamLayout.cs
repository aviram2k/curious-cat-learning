using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Education2k
{
    public class PrintExamDetails
    {
        public string Name, Classroom, School;
        public static PrintExamDetails From(ExamDraft draft) { return new PrintExamDetails { Name = draft.Name, Classroom = draft.Classroom, School = draft.School }; }
        public void Validate()
        {
            Name = ExamDrafts.CleanTopicName(Name);
            Classroom = (Classroom ?? "").Trim(); School = (School ?? "").Trim();
            if (Classroom.Length == 0 || Classroom.Length > 60 || School.Length == 0 || School.Length > 120 || Classroom.Any(Char.IsControl) || School.Any(Char.IsControl)) throw new InvalidOperationException("יש להזין כיתה ובית ספר. אפשר לכתוב עד 60 תווים לכיתה ועד 120 תווים לבית הספר.");
        }
    }
    public class PaperMark
    {
        public Geometry Shape;
        public Brush Fill;
        public Pen Pen;
        public BitmapSource Image;
        public Rect ImageRect;
    }
    public class ExamPaperPage
    {
        public bool Answers;
        public readonly List<PaperMark> Marks = new List<PaperMark>();
        public readonly List<string> Text = new List<string>();
        public DrawingGroup Drawing;
    }
    public class ExamPaper
    {
        public const double Width = 793.7007874, Height = 1122.519685, Margin = 48, Bottom = 1044;
        public const string ProjectUrl = "https://github.com/aviram2k/curious-cat-learning";
        public readonly List<ExamPaperPage> Pages = new List<ExamPaperPage>();
        public int AnswerStartPage;
        public PrintExamDetails Details;
    }
    public sealed class PrintableExamLayout
    {
        static readonly CultureInfo Hebrew = CultureInfo.GetCultureInfo("he-IL");
        static readonly Brush Ink = ColorBrush("#213B42"), Green = ColorBrush("#176F65"), Muted = ColorBrush("#596F76"), Rule = ColorBrush("#B8C7C3"), Tint = ColorBrush("#ECF3F0");
        readonly ExamPaper paper = new ExamPaper();
        readonly CancellationToken cancellation;
        readonly Topic topic;
        readonly Exam exam;
        readonly Dictionary<string, BitmapSource> images = new Dictionary<string, BitmapSource>();
        readonly BitmapSource logo;
        ExamPaperPage page;
        double y;
        bool answers;
        int questionNumber;
        double ContentWidth { get { return ExamPaper.Width - 2 * ExamPaper.Margin; } }
        static Brush ColorBrush(string color) { var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color); brush.Freeze(); return brush; }
        PrintableExamLayout(Topic topic, Exam exam, PrintExamDetails details, CancellationToken cancellation)
        {
            this.topic = topic; this.exam = exam; this.cancellation = cancellation; details.Validate(); paper.Details = details;
            using (var input = typeof(Program).Assembly.GetManifestResourceStream("Education2k.AppLogo.png")) logo = ReadImage(input);
        }
        static BitmapSource ReadImage(Stream input)
        {
            var frame = BitmapFrame.Create(input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            int stride = checked(frame.PixelWidth * 4); var pixels = new byte[checked(stride * frame.PixelHeight)]; converted.CopyPixels(pixels, stride, 0);
            // A frozen BitmapFrame still retains its thread-affine decoder. The XPS
            // print serializer accesses that decoder, so share detached pixels instead.
            var bitmap = BitmapSource.Create(frame.PixelWidth, frame.PixelHeight, frame.DpiX, frame.DpiY, PixelFormats.Bgra32, null, pixels, stride);
            bitmap.Freeze(); return bitmap;
        }
        public static ExamPaper Build(Topic topic, Exam exam, PrintExamDetails details, CancellationToken cancellation)
        {
            Rules.Validate(exam); SourceImages.Check(topic, exam);
            return new PrintableExamLayout(topic, exam, details, cancellation).Build();
        }
        FormattedText Format(string text, double size, double width, bool bold = false, bool leftToRight = false)
        {
            return new FormattedText(text ?? "", Hebrew, leftToRight ? FlowDirection.LeftToRight : FlowDirection.RightToLeft,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal), size, Ink, 1.0) {
                MaxTextWidth = Math.Max(20, width), TextAlignment = leftToRight ? TextAlignment.Left : TextAlignment.Right, LineHeight = size * 1.5
            };
        }
        void TextAt(string text, double x, double top, double width, double size, Brush brush, bool bold = false, bool ltr = false, bool center = false)
        {
            if (String.IsNullOrEmpty(text)) return;
            var formatted = Format(text, size, width, bold, ltr); var shape = formatted.BuildGeometry(new Point(0, 0));
            if (!shape.Bounds.IsEmpty)
            {
                double dx = center ? x + (width - shape.Bounds.Width) / 2 - shape.Bounds.X : ltr ? x - shape.Bounds.X : x + width - shape.Bounds.Right;
                var group = new GeometryGroup(); group.Children.Add(shape); group.Transform = new TranslateTransform(dx, top); group.Freeze();
                page.Marks.Add(new PaperMark { Shape = group, Fill = brush });
            }
            page.Text.Add(text);
        }
        void Box(Rect rect, Brush fill, double thickness = 0.7)
        {
            var shape = new RectangleGeometry(rect); shape.Freeze(); page.Marks.Add(new PaperMark { Shape = shape, Fill = fill, Pen = new Pen(Rule, thickness) });
        }
        void Line(double x1, double top, double x2)
        {
            var shape = new LineGeometry(new Point(x1, top), new Point(x2, top)); shape.Freeze(); page.Marks.Add(new PaperMark { Shape = shape, Pen = new Pen(Rule, 0.65) });
        }
        void NewPage()
        {
            cancellation.ThrowIfCancellationRequested(); page = new ExamPaperPage { Answers = answers }; paper.Pages.Add(page); y = 114;
            var background = new RectangleGeometry(new Rect(0, 0, ExamPaper.Width, ExamPaper.Height)); background.Freeze(); page.Marks.Add(new PaperMark { Shape = background, Fill = Brushes.White });
            page.Marks.Add(new PaperMark { Image = logo, ImageRect = new Rect(ExamPaper.Width - ExamPaper.Margin - 62, 27, 62, 62) });
            TextAt(AppInfo.Name, ExamPaper.Margin, 30, ContentWidth - 80, 20, Green, true);
            TextAt(paper.Details.Name + " | " + (answers ? "פתרונות" : "שאלות") + (questionNumber > 0 ? " | שאלה " + questionNumber + " - המשך" : ""), ExamPaper.Margin, 64, ContentWidth - 80, 12, Muted);
            Line(ExamPaper.Margin, 100, ExamPaper.Width - ExamPaper.Margin);
        }
        void Ensure(double height) { if (y + height > ExamPaper.Bottom) NewPage(); }
        int Fit(string text, double width, double size, double height, bool bold = false)
        {
            if (text.Length == 0) return 0;
            int low = 0, high = text.Length;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                if (Format(text.Substring(0, middle), size, width, bold).Height <= height + 0.1) low = middle; else high = middle - 1;
            }
            if (low < text.Length && low > 0)
            {
                int boundary = low;
                while (boundary > 0 && !Char.IsWhiteSpace(text[boundary - 1])) boundary--;
                if (boundary > 0) low = boundary;
                if (low > 0 && Char.IsHighSurrogate(text[low - 1])) low--;
            }
            return low;
        }
        void AddText(string value, double size = 16, Brush brush = null, bool bold = false, double after = 10)
        {
            string text = (value ?? "").Trim(); if (text.Length == 0) return;
            while (text.Length > 0)
            {
                cancellation.ThrowIfCancellationRequested(); Ensure(size * 3);
                int count = Fit(text, ContentWidth, size, ExamPaper.Bottom - y, bold);
                if (count == 0) { NewPage(); continue; }
                string chunk = text.Substring(0, count).TrimEnd(); TextAt(chunk, ExamPaper.Margin, y, ContentWidth, size, brush ?? Ink, bold);
                y += Format(chunk, size, ContentWidth, bold).Height + after; text = text.Substring(count).TrimStart();
                if (text.Length > 0) NewPage();
            }
        }
        void WritingLines(double points)
        {
            int lines = Math.Max(3, Math.Min(9, (int)Math.Ceiling(points / 2) + 2)); Ensure(65);
            for (int i = 0; i < lines; i++) { Ensure(29); y += 28; Line(ExamPaper.Margin, y, ExamPaper.Width - ExamPaper.Margin); }
            y += 16;
        }
        void Table(List<string> columns, List<string> rows, List<List<string>> cells, bool solution)
        {
            double width = ContentWidth / (columns.Count + 1), headerHeight = Math.Max(40, columns.Select(c => Format(c, 13, width - 16, true).Height + 16).Max());
            if (headerHeight > 400) throw new InvalidOperationException("אחת מכותרות הטבלה ארוכה מדי לעמוד מודפס. יש לקצר את כותרות העמודות במבחן לפני ייצוא.");
            bool header = false;
            for (int row = 0; row < rows.Count; row++)
            {
                string[] remaining = new[] { rows[row] }.Concat(columns.Select((c, i) => solution ? cells[row][i] : "")).ToArray(); bool first = true;
                while (first || remaining.Any(s => !String.IsNullOrEmpty(s)))
                {
                    cancellation.ThrowIfCancellationRequested(); double minHeight = solution ? 38 : 76;
                    if (!header || y + minHeight > ExamPaper.Bottom)
                    {
                        if (y + headerHeight + minHeight > ExamPaper.Bottom) NewPage();
                        TableRow(new[] { "פריט" }.Concat(columns).ToArray(), width, headerHeight, true); header = true;
                    }
                    double wanted = Math.Max(minHeight, remaining.Select(s => Format(s, 14, width - 16).Height + 20).Max());
                    double height = Math.Min(wanted, ExamPaper.Bottom - y);
                    string[] chunks = remaining.Select(s => { int count = Fit(s, width - 16, 14, height - 18); return s.Substring(0, count).TrimEnd(); }).ToArray();
                    if (remaining.Any(s => s.Length > 0) && chunks.All(s => s.Length == 0)) { NewPage(); header = false; continue; }
                    TableRow(chunks, width, height, false);
                    for (int c = 0; c < remaining.Length; c++) remaining[c] = remaining[c].Substring(chunks[c].Length).TrimStart();
                    first = false;
                    if (remaining.Any(s => s.Length > 0)) { NewPage(); header = false; }
                }
            }
            y += 14;
        }
        void TableRow(string[] values, double width, double height, bool header)
        {
            for (int column = 0; column < values.Length; column++)
            {
                double left = ExamPaper.Width - ExamPaper.Margin - (column + 1) * width;
                Box(new Rect(left, y, width, height), header || column == 0 ? Tint : null);
                TextAt(values[column], left + 8, y + 8, width - 16, header ? 13 : 14, Ink, header);
            }
            y += height;
        }
        void Pictures(Question question)
        {
            foreach (string id in question.ImageIds)
            {
                cancellation.ThrowIfCancellationRequested(); BitmapSource bitmap;
                var asset = exam.Images.Single(i => i.Id == id);
                if (!images.TryGetValue(id, out bitmap))
                {
                    using (var input = File.OpenRead(SourceImages.Resolve(topic, asset.RelativePath))) bitmap = ReadImage(input);
                    images[id] = bitmap;
                }
                double width = Math.Min(ContentWidth, 580), height = Math.Min(340, width * bitmap.PixelHeight / bitmap.PixelWidth); width = height * bitmap.PixelWidth / bitmap.PixelHeight;
                Ensure(height + 48); page.Marks.Add(new PaperMark { Image = bitmap, ImageRect = new Rect((ExamPaper.Width - width) / 2, y, width, height) }); y += height + 6;
                AddText(asset.Caption, 12, Muted);
            }
        }
        double TextHeight(string text, double size = 16, bool bold = false, double after = 10)
        {
            return String.IsNullOrWhiteSpace(text) ? 0 : Format(text.Trim(), size, ContentWidth, bold).Height + after;
        }
        double ResponseHeight(string type, List<string> columns, List<string> rows, List<List<string>> cells, string expected, double points)
        {
            double height = 0;
            if (type == "table")
            {
                double width = ContentWidth / (columns.Count + 1); height = Math.Max(40, columns.Select(c => Format(c, 13, width - 16, true).Height + 16).Max()) + 14;
                for (int r = 0; r < rows.Count; r++) height += Math.Max(answers ? 38 : 76, new[] { rows[r] }.Concat(columns.Select((c, i) => answers ? cells[r][i] : "")).Select(t => Format(t, 14, width - 16).Height + 20).Max());
            }
            if (answers) height += TextHeight(expected); else if (type != "table") height += Math.Max(3, Math.Min(9, (int)Math.Ceiling(points / 2) + 2)) * 28 + 16;
            return height;
        }
        double QuestionHeight(Question question)
        {
            double height = 36.5 + TextHeight(question.Parts.Count > 0 ? question.Context : question.Prompt) + 18;
            foreach (string id in question.ImageIds)
            {
                var asset = exam.Images.Single(i => i.Id == id); BitmapSource bitmap;
                if (!images.TryGetValue(id, out bitmap)) { using (var input = File.OpenRead(SourceImages.Resolve(topic, asset.RelativePath))) bitmap = ReadImage(input); images[id] = bitmap; }
                height += Math.Min(340, Math.Min(ContentWidth, 580) * bitmap.PixelHeight / bitmap.PixelWidth) + 6 + TextHeight(asset.Caption, 12);
            }
            if (question.Type == "multiple_choice")
            {
                if (answers) { height += TextHeight("התשובה הנכונה: א. " + question.Options[question.CorrectOption], 16, true); if (question.ExpectedAnswer.Trim() != question.Options[question.CorrectOption].Trim()) height += TextHeight(question.ExpectedAnswer); }
                else height += question.Options.Select(o => Format("א. " + o, 16, ContentWidth - 30).Height + 12).Sum();
            }
            else if (question.Parts.Count > 0) foreach (var part in question.Parts) height += TextHeight("סעיף א: " + part.Prompt, 16, true) + ResponseHeight(part.Type, part.Columns, part.Rows, part.CorrectCells, part.ExpectedAnswer, part.Points);
            else height += ResponseHeight(question.Type, question.Columns, question.Rows, question.CorrectCells, question.ExpectedAnswer, question.Points);
            return height;
        }
        void Question(Question question, int number)
        {
            double estimated = QuestionHeight(question); questionNumber = 0;
            Ensure(estimated <= ExamPaper.Bottom - 114 && (number != 1 || y + estimated <= ExamPaper.Bottom) ? estimated : 170); questionNumber = number;
            WorkFlow.Activity((answers ? "מעצבים פתרונות לשאלה " : "מעצבים שאלה ") + number + " מתוך " + exam.Questions.Count, "מעצבים את עמודי ה־PDF ואת המקום לתשובות.");
            AddText("שאלה " + number + " | " + question.Points.ToString("0.##", Hebrew) + " נקודות", 19, Green, true, 8);
            AddText(question.Parts.Count > 0 ? question.Context : question.Prompt, 16); Pictures(question);
            if (question.Type == "multiple_choice")
            {
                if (answers)
                {
                    AddText("התשובה הנכונה: " + "אבגדהוזחט"[question.CorrectOption] + ". " + question.Options[question.CorrectOption], 16, Green, true);
                    if (question.ExpectedAnswer.Trim() != question.Options[question.CorrectOption].Trim()) AddText(question.ExpectedAnswer);
                }
                else for (int option = 0; option < question.Options.Count; option++)
                {
                    string value = "אבגדהוזחט"[option] + ". " + question.Options[option]; double height = Format(value, 16, ContentWidth - 30).Height + 12; Ensure(height);
                    var circle = new EllipseGeometry(new Point(ExamPaper.Width - ExamPaper.Margin - 7, y + 12), 5, 5); circle.Freeze(); page.Marks.Add(new PaperMark { Shape = circle, Pen = new Pen(Ink, 1) });
                    TextAt(value, ExamPaper.Margin, y, ContentWidth - 26, 16, Ink); y += height;
                }
            }
            else if (question.Parts.Count > 0)
            {
                for (int i = 0; i < question.Parts.Count; i++)
                {
                    var part = question.Parts[i]; Ensure(140); AddText(Rules.PartLabel(i) + ": " + part.Prompt, 16, Ink, true);
                    Response(part.Type, part.Columns, part.Rows, part.CorrectCells, part.ExpectedAnswer, part.Points);
                }
            }
            else Response(question.Type, question.Columns, question.Rows, question.CorrectCells, question.ExpectedAnswer, question.Points);
            y += 18;
        }
        void Response(string type, List<string> columns, List<string> rows, List<List<string>> cells, string expected, double points)
        {
            if (type == "table") Table(columns, rows, cells, answers);
            if (answers) AddText(expected, 16, Green); else if (type != "table") WritingLines(points);
        }
        ExamPaper Build()
        {
            NewPage(); AddText(paper.Details.Name, 27, Green, true);
            AddText("כיתה: " + paper.Details.Classroom + " | בית ספר: " + paper.Details.School, 15);
            AddText("שם התלמיד/ה: ____________________    תאריך: ______________", 15);
            AddText("משך מומלץ: 60 דקות | 100 נקודות. קראו את השאלות בעיון וענו במקום המיועד.", 14, Muted);
            if (exam.FilteredSources) AddText(new MaterialSelection { Excluded = exam.ExcludedMaterials }.ScopeNote, 12, Muted);
            else if (exam.SourceReview != null && exam.SourceReview.RequiresDecision) AddText("לתשומת לב: המבחן נוצר עם מגבלות בקריאת חלק מהחומר. יש לעיין בדוח המקורות שבתיקיית המבחן.", 12, Muted);
            for (int i = 0; i < exam.Questions.Count; i++) Question(exam.Questions[i], i + 1);
            answers = true; questionNumber = 0; NewPage(); paper.AnswerStartPage = paper.Pages.Count;
            AddText("שאלות ופתרונות", 27, Green, true); AddText(paper.Details.Name + " | כיתה " + paper.Details.Classroom, 15);
            for (int i = 0; i < exam.Questions.Count; i++) Question(exam.Questions[i], i + 1);
            for (int i = 0; i < paper.Pages.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested(); page = paper.Pages[i]; Line(ExamPaper.Margin, 1064, ExamPaper.Width - ExamPaper.Margin);
                TextAt("נוצר עם Codex | " + AppInfo.Name, ExamPaper.Margin, 1072, ContentWidth, 11, Muted);
                TextAt("עמוד " + (i + 1) + " מתוך " + paper.Pages.Count, ExamPaper.Margin, 1072, 130, 11, Muted);
                TextAt(ExamPaper.ProjectUrl, ExamPaper.Margin, 1092, ContentWidth, 10.5, Green, false, true, true);
                var drawing = new DrawingGroup();
                foreach (var mark in page.Marks)
                {
                    if (mark.Image != null) drawing.Children.Add(new ImageDrawing(mark.Image, mark.ImageRect));
                    else drawing.Children.Add(new GeometryDrawing(mark.Fill, mark.Pen, mark.Shape));
                }
                drawing.Freeze(); page.Drawing = drawing;
            }
            return paper;
        }
    }
}
