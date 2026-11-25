using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Education2k
{
    // WPF shapes Hebrew and mixed-direction text. Export its glyph outlines as
    // vector paths so printed pages do not depend on fonts or a PDF printer driver.
    public sealed class ExamPdfWriter
    {
        readonly List<byte[]> objects = new List<byte[]> { null };
        readonly Dictionary<BitmapSource, int> images = new Dictionary<BitmapSource, int>();
        static string N(double value) { return value.ToString("0.####", CultureInfo.InvariantCulture); }
        static byte[] Bytes(string value) { return Encoding.ASCII.GetBytes(value); }
        static string Unicode(string value) { return "<FEFF" + BitConverter.ToString(Encoding.BigEndianUnicode.GetBytes(value ?? "")).Replace("-", "") + ">"; }
        int Add(byte[] value) { objects.Add(value); return objects.Count - 1; }
        static byte[] Compress(byte[] value)
        {
            using (var output = new MemoryStream())
            {
                output.WriteByte(0x78); output.WriteByte(0x9c);
                using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, true)) deflate.Write(value, 0, value.Length);
                uint a = 1, b = 0;
                foreach (byte c in value) { a = (a + c) % 65521; b = (b + a) % 65521; }
                uint checksum = (b << 16) | a;
                for (int shift = 24; shift >= 0; shift -= 8) output.WriteByte((byte)(checksum >> shift));
                return output.ToArray();
            }
        }
        static byte[] Stream(byte[] content, string properties = "")
        {
            byte[] compressed = Compress(content);
            using (var output = new MemoryStream())
            {
                byte[] header = Bytes("<< /Length " + compressed.Length + " /Filter /FlateDecode " + properties + ">>\nstream\n"); output.Write(header, 0, header.Length);
                output.Write(compressed, 0, compressed.Length); byte[] tail = Bytes("\nendstream"); output.Write(tail, 0, tail.Length); return output.ToArray();
            }
        }
        int Image(BitmapSource source)
        {
            int existing; if (images.TryGetValue(source, out existing)) return existing;
            var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0); int width = bitmap.PixelWidth, height = bitmap.PixelHeight;
            byte[] bgra = new byte[checked(width * height * 4)], rgb = new byte[checked(width * height * 3)]; bitmap.CopyPixels(bgra, width * 4, 0);
            for (int i = 0, j = 0; i < bgra.Length; i += 4, j += 3)
            {
                int alpha = bgra[i + 3]; rgb[j] = (byte)((bgra[i + 2] * alpha + 255 * (255 - alpha) + 127) / 255);
                rgb[j + 1] = (byte)((bgra[i + 1] * alpha + 255 * (255 - alpha) + 127) / 255); rgb[j + 2] = (byte)((bgra[i] * alpha + 255 * (255 - alpha) + 127) / 255);
            }
            existing = Add(Stream(rgb, "/Type /XObject /Subtype /Image /Width " + width + " /Height " + height + " /ColorSpace /DeviceRGB /BitsPerComponent 8 ")); images.Add(source, existing); return existing;
        }
        static string Color(Brush brush)
        {
            var solid = brush as SolidColorBrush; if (solid == null) throw new InvalidOperationException("צבע שאינו נתמך בעימוד המבחן.");
            return N(solid.Color.R / 255.0) + " " + N(solid.Color.G / 255.0) + " " + N(solid.Color.B / 255.0);
        }
        static void Point(StringBuilder output, Point point, string operation) { output.Append(N(point.X)).Append(' ').Append(N(point.Y)).Append(' ').Append(operation).Append('\n'); }
        static void Shape(StringBuilder output, PaperMark mark)
        {
            var path = mark.Shape.GetFlattenedPathGeometry(0.08, ToleranceType.Absolute); var transform = path.Transform == null ? Matrix.Identity : path.Transform.Value;
            if (mark.Fill != null) output.Append(Color(mark.Fill)).Append(" rg\n");
            if (mark.Pen != null) output.Append(Color(mark.Pen.Brush)).Append(" RG\n").Append(N(mark.Pen.Thickness)).Append(" w\n");
            foreach (var figure in path.Figures)
            {
                Point(output, transform.Transform(figure.StartPoint), "m");
                foreach (var segment in figure.Segments)
                {
                    var line = segment as LineSegment; var poly = segment as PolyLineSegment;
                    if (line != null) Point(output, transform.Transform(line.Point), "l");
                    else if (poly != null) foreach (var point in poly.Points) Point(output, transform.Transform(point), "l");
                    else throw new InvalidOperationException("נתיב עימוד אינו נתמך. קובץ ה־PDF לא נשמר.");
                }
                if (figure.IsClosed) output.Append("h\n");
            }
            output.Append(mark.Fill != null ? (mark.Pen != null ? path.FillRule == FillRule.EvenOdd ? "B*" : "B" : path.FillRule == FillRule.EvenOdd ? "f*" : "f") : "S").Append('\n');
        }
        public static void Write(ExamPaper paper, string path, CancellationToken cancellation) { new ExamPdfWriter().WriteDocument(paper, path, cancellation); }
        void WriteDocument(ExamPaper paper, string path, CancellationToken cancellation)
        {
            int catalog = Add(null), pages = Add(null), info = Add(Bytes("<< /Title " + Unicode(paper.Details.Name) + " /Creator (Codex) /Producer (Curious Cat Learning) >>"));
            var pageIds = new List<int>();
            for (int index = 0; index < paper.Pages.Count; index++)
            {
                cancellation.ThrowIfCancellationRequested(); WorkFlow.Activity("שומרים עמוד PDF " + (index + 1) + " מתוך " + paper.Pages.Count, "ממתינים להשלמת כתיבת קובץ ה־PDF.");
                var content = new StringBuilder("q\n0.75 0 0 -0.75 0 " + N(ExamPaper.Height * 0.75) + " cm\n"); var resources = new StringBuilder(); int imageNumber = 0;
                foreach (var mark in paper.Pages[index].Marks)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (mark.Image == null) Shape(content, mark);
                    else
                    {
                        string name = "Im" + imageNumber++; int image = Image(mark.Image); resources.Append('/').Append(name).Append(' ').Append(image).Append(" 0 R "); var r = mark.ImageRect;
                        content.Append("q ").Append(N(r.Width)).Append(" 0 0 ").Append(N(-r.Height)).Append(' ').Append(N(r.X)).Append(' ').Append(N(r.Y + r.Height)).Append(" cm /").Append(name).Append(" Do Q\n");
                    }
                }
                content.Append("Q\n"); int stream = Add(Stream(Bytes(content.ToString())));
                string rect = N(ExamPaper.Margin * 0.75) + " " + N((ExamPaper.Height - 1110) * 0.75) + " " + N((ExamPaper.Width - ExamPaper.Margin) * 0.75) + " " + N((ExamPaper.Height - 1090) * 0.75);
                int link = Add(Bytes("<< /Type /Annot /Subtype /Link /Rect [" + rect + "] /Border [0 0 0] /A << /S /URI /URI (" + ExamPaper.ProjectUrl + ") >> >>"));
                pageIds.Add(Add(Bytes("<< /Type /Page /Parent " + pages + " 0 R /MediaBox [0 0 " + N(ExamPaper.Width * 0.75) + " " + N(ExamPaper.Height * 0.75) + "] /Resources << /XObject << " + resources + ">> >> /Contents " + stream + " 0 R /Annots [" + link + " 0 R] >>")));
            }
            int outlines = Add(null), questions = Add(null), answers = Add(null);
            objects[outlines] = Bytes("<< /Type /Outlines /First " + questions + " 0 R /Last " + answers + " 0 R /Count 2 >>");
            objects[questions] = Bytes("<< /Title " + Unicode("שאלות") + " /Parent " + outlines + " 0 R /Next " + answers + " 0 R /Dest [" + pageIds[0] + " 0 R /Fit] >>");
            objects[answers] = Bytes("<< /Title " + Unicode("שאלות ופתרונות") + " /Parent " + outlines + " 0 R /Prev " + questions + " 0 R /Dest [" + pageIds[paper.AnswerStartPage - 1] + " 0 R /Fit] >>");
            objects[pages] = Bytes("<< /Type /Pages /Count " + pageIds.Count + " /Kids [" + String.Join(" ", pageIds.Select(i => i + " 0 R")) + "] >>");
            objects[catalog] = Bytes("<< /Type /Catalog /Pages " + pages + " 0 R /Outlines " + outlines + " 0 R /Lang (he-IL) >>");
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                Action<string> write = s => { byte[] data = Bytes(s); output.Write(data, 0, data.Length); };
                write("%PDF-1.4\n"); output.Write(new byte[] { 37, 226, 227, 207, 211, 10 }, 0, 6); var offsets = new List<long> { 0 };
                WorkFlow.Activity("קובץ ה־PDF נוצר; משלימים כתיבה לדיסק.", "כותבים את קובץ ה־PDF. עדיין ממתינים להשלמת השמירה.");
                for (int i = 1; i < objects.Count; i++) { cancellation.ThrowIfCancellationRequested(); if (objects[i] == null) throw new InvalidOperationException("מסמך PDF אינו שלם."); offsets.Add(output.Position); write(i + " 0 obj\n"); output.Write(objects[i], 0, objects[i].Length); write("\nendobj\n"); }
                long xref = output.Position; write("xref\n0 " + objects.Count + "\n0000000000 65535 f \n");
                for (int i = 1; i < offsets.Count; i++) write(offsets[i].ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \n");
                write("trailer\n<< /Size " + objects.Count + " /Root " + catalog + " 0 R /Info " + info + " 0 R >>\nstartxref\n" + xref + "\n%%EOF\n"); output.Flush(true);
            }
        }
    }
}
