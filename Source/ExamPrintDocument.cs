using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Education2k
{
    public static class ExamPrintDocument
    {
        public static FixedDocument Create(ExamPaper paper, int first, int last, double width, double height)
        {
            double scale = Math.Min(width / ExamPaper.Width, height / ExamPaper.Height);
            var document = new FixedDocument(); document.DocumentPaginator.PageSize = new Size(width, height);
            for (int i = first - 1; i < last; i++)
            {
                var page = new FixedPage { Width = width, Height = height, FlowDirection = FlowDirection.LeftToRight };
                var source = new DrawingImage(paper.Pages[i].Drawing); source.Freeze();
                var picture = new Image { Source = source, Width = ExamPaper.Width * scale, Height = ExamPaper.Height * scale, Stretch = Stretch.Fill, FlowDirection = FlowDirection.LeftToRight };
                FixedPage.SetLeft(picture, (width - picture.Width) / 2); FixedPage.SetTop(picture, (height - picture.Height) / 2); page.Children.Add(picture); document.Pages.Add(new PageContent { Child = page });
            }
            return document;
        }
    }
}
