using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace Education2k
{
    public static partial class Tests
    {
        sealed class VisualTestModel : IVisualModelClient
        {
            public int Calls;
            public bool Partial;
            public Task<T> Ask<T>(string prompt, string schema, bool search, int seconds, CancellationToken token) { throw new InvalidOperationException("Unexpected non-visual model request."); }
            public Task<T> AskImages<T>(string prompt, string schema, IList<string> images, int seconds, CancellationToken token)
            {
                Calls++; token.ThrowIfCancellationRequested();
                var response = new VisualResponse();
                foreach (string image in images)
                {
                    string key = new DirectoryInfo(Path.GetDirectoryName(image)).Name;
                    int page = Int32.Parse(Path.GetFileNameWithoutExtension(image).Substring(5));
                    response.Items.Add(new VisualReading { Id = key + "-p" + page, Status = Partial ? "partial" : "read", Text = "תוכן לימודי שנקרא מהעמוד. שאלה מבית הספר: השוו בין הנתונים בטבלה.", Description = "תרשים מקור וטבלת נתונים בעברית מימין לשמאל.", Caption = "תרשים מתוך חומר הלימוד", ExamSuitable = !Partial, Issue = Partial ? "כיתוב חשוב קטן מכדי לקרוא." : "" });
                }
                return Task.FromResult((T)(object)response);
            }
        }
        static void PdfFixture(string path)
        {
            // Two-page PDF: searchable text followed by a page with no text layer.
            Func<string, string> stream = data => "<< /Length " + Encoding.ASCII.GetByteCount(data) + " >>\nstream\n" + data + "endstream";
            var objects = new[] {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Resources << /Font << /F1 5 0 R >> >> /Contents 6 0 R >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Resources << >> /Contents 7 0 R >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
                stream("BT /F1 20 Tf 20 100 Td (School reference) Tj ET\n"),
                stream("0.1 0.5 0.4 rg 20 20 180 120 re f\n")
            };
            using (var output = new MemoryStream())
            {
                Action<string> write = text => { var bytes = Encoding.ASCII.GetBytes(text); output.Write(bytes, 0, bytes.Length); };
                write("%PDF-1.4\n"); var offsets = new List<long>();
                for (int i = 0; i < objects.Length; i++) { offsets.Add(output.Position); write((i + 1) + " 0 obj\n" + objects[i] + "\nendobj\n"); }
                long xref = output.Position; write("xref\n0 8\n0000000000 65535 f \n");
                foreach (long offset in offsets) write(offset.ToString("0000000000") + " 00000 n \n");
                write("trailer\n<< /Size 8 /Root 1 0 R >>\nstartxref\n" + xref + "\n%%EOF\n"); File.WriteAllBytes(path, output.ToArray());
            }
        }
        static void VisualChecks(string temp, string sampleRoot)
        {
            string folder = Path.Combine(temp, "visual-source"); Directory.CreateDirectory(folder);
            string pdf = Path.Combine(folder, "חומר בעברית.pdf"); PdfFixture(pdf);
            File.WriteAllText(Path.Combine(folder, "_describe.txt"), "חומר בית ספר", Encoding.UTF8);
            var topic = Store.ReadTopic(folder);
            string text = MaterialReader.Extract(pdf);
            Assert(text.Contains("School reference") && text.Contains("עמוד 2"), "Native PDF reader extracts text and preserves page coverage including pages without text");
            var snapshot = MaterialReader.Inspect(topic); MaterialAssets.PrepareSnapshot(topic, snapshot, CancellationToken.None);
            var visuals = snapshot.Files.Single().Visuals;
            Assert(visuals.Count == 2 && visuals.All(v => SourceImages.Valid(topic, v)), "PDF pages with vector diagrams and no text layer are rendered as verified source images");
            using (var input = File.OpenRead(SourceImages.Resolve(topic, visuals[1].RelativePath))) Assert(BitmapFrame.Create(input).PixelWidth == 2000, "Source rendering produces a legible high-resolution image of a non-text page");
            var model = new VisualTestModel(); MaterialVision.Enrich(topic, snapshot, model, CancellationToken.None).GetAwaiter().GetResult();
            Assert(model.Calls == 1 && snapshot.Files.Single().Status == "read" && !snapshot.Files.Single().MustWarn && snapshot.Context.Contains("שאלה מבית הספר"), "Visual interpretation resolves source warnings only after every page is read and retains existing school questions");
            var next = MaterialReader.Inspect(topic); MaterialAssets.PrepareSnapshot(topic, next, CancellationToken.None); MaterialVision.Enrich(topic, next, model, CancellationToken.None).GetAwaiter().GetResult();
            Assert(model.Calls == 1 && next.Files.Single().Visuals.All(v => v.Reading != null), "Unchanged verified source images reuse their interpretation without another Codex request");
            var exam = Fixture(); exam.Questions[0].ImageIds.Add(visuals[0].Id); SourceImages.Bind(exam, topic, snapshot); Rules.Validate(exam); SourceImages.Check(topic, exam);
            Assert(exam.Images.Count == 1 && QuizBuilder.Create(exam).Questions[0].ImageIds.Single() == visuals[0].Id, "Full exams and short quizzes retain their selected source images");
            var wrong = Fixture(); wrong.Questions[0].ImageIds.Add("not-from-the-source"); Reject(() => SourceImages.Bind(wrong, topic, snapshot), "A model cannot reference an invented image or arbitrary file path");
            Reject(() => SourceImages.Resolve(topic, "_materials/../../private.png"), "Source image paths cannot escape the material cache");
            var partial = new VisualResponse { Items = new List<VisualReading> { new VisualReading { Id = visuals[0].Id, Status = "partial", Text = "", Description = "תמונה מטושטשת", Issue = "כיתוב לא קריא", ExamSuitable = true, Caption = "תרשים" } } };
            Reject(() => MaterialVision.Validate(partial, new List<SourceVisual> { visuals[0] }), "An incompletely read image cannot be offered as an exam illustration");
            var missing = new VisualResponse(); Reject(() => MaterialVision.Validate(missing, visuals), "Every submitted page must be accounted for in the visual response");
            string firstPath = SourceImages.Resolve(topic, visuals[0].RelativePath); File.AppendAllText(firstPath, "changed");
            Reject(() => SourceImages.Check(topic, exam), "A changed source image cannot silently alter an existing exam");
            var repaired = MaterialAssets.Prepare(topic, pdf, CancellationToken.None);
            Assert(repaired.Visuals.All(v => SourceImages.Valid(topic, v)), "A damaged image cache is rebuilt from the unchanged original PDF");
            string pptx = Path.Combine(temp, "embedded-fixture.pptx"); PresentationFixture(pptx);
            using (var zip = ZipFile.Open(pptx, ZipArchiveMode.Update))
            {
                zip.GetEntry("ppt/slides/slide1.xml").Delete();
                ZipText(zip, "ppt/slides/slide1.xml", "<p:sld xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><p:pic><a:blip r:embed=\"picture\"/></p:pic></p:sld>");
                ZipText(zip, "ppt/slides/_rels/slide1.xml.rels", "<Relationships><Relationship Id=\"picture\" Target=\"../media/diagram.png\"/></Relationships>");
                using (var input = File.OpenRead(SourceImages.Resolve(topic, repaired.Visuals[1].RelativePath))) using (var output = zip.CreateEntry("ppt/media/diagram.png").Open()) input.CopyTo(output);
            }
            var originals = MaterialAssets.OriginalPictures(topic, pptx, repaired.Visuals[1]);
            using (var input = File.OpenRead(SourceImages.Resolve(topic, originals.Single().RelativePath))) Assert(BitmapFrame.Create(input).PixelWidth == 2000, "Embedded pictures are fully decoded from compressed Office streams without losing pixels");
            // Force a fresh interpretation with an explicit unresolved visual issue.
            foreach (var v in repaired.Visuals) File.Delete(SourceImages.Resolve(topic, v.RelativePath) + ".reading.json");
            var incomplete = MaterialReader.Inspect(topic); MaterialAssets.PrepareSnapshot(topic, incomplete, CancellationToken.None);
            MaterialVision.Enrich(topic, incomplete, new VisualTestModel { Partial = true }, CancellationToken.None).GetAwaiter().GetResult();
            Assert(incomplete.Files.Single().MustWarn && incomplete.Files.Single().Status == "partial" && incomplete.Context.Contains("כיתוב חשוב קטן"), "Unread visual details remain an explicit source warning with the model's explanation");
            var reportStore = new Store(Path.Combine(temp, "coverage-cache")); var reportTopic = TestTopic(reportStore, "בדיקת כיסוי", false);
            var firstCoverage = MaterialReader.Inspect(reportTopic); var reportModel = new FakeModel { Reply = schema => TestReview(firstCoverage) }; var reviews = new MaterialReviewService(reportModel);
            reviews.Inspect(reportTopic, firstCoverage, CancellationToken.None).GetAwaiter().GetResult(); int calls = reportModel.Calls.Count;
            var updatedCoverage = MaterialReader.Inspect(reportTopic); updatedCoverage.Files[0].Text += "\nמידע נוסף שהושלם בפענוח.";
            reviews.Inspect(reportTopic, updatedCoverage, CancellationToken.None).GetAwaiter().GetResult();
            Assert(reportModel.Calls.Count == calls + 1, "Improved interpretation invalidates an old source report even when the original source bytes are unchanged");
            var actualPdf = new Store(sampleRoot).Topics().Where(t => t.Name.Contains("היסטוריה")).SelectMany(t => t.Materials).FirstOrDefault(f => Path.GetExtension(f).Equals(".pdf", StringComparison.OrdinalIgnoreCase));
            if (actualPdf != null)
            {
                string actual = MaterialReader.Extract(actualPdf);
                Assert(actual.Any(c => c >= '\u0590' && c <= '\u05ff') && actual.Length > 1000 && actual.Contains("24"), "The actual history PDF yields Hebrew text and its textbook page references through the application's native reader");
            }
        }
        public static Topic PrepareImagePreview(Store store)
        {
            var topic = TestTopic(store, "הדגמת תמונה במבחן", false);
            PdfFixture(Path.Combine(topic.Folder, "דוגמת תרשים.pdf")); topic = Store.ReadTopic(topic.Folder);
            var snapshot = MaterialReader.Inspect(topic); MaterialAssets.PrepareSnapshot(topic, snapshot, CancellationToken.None);
            MaterialVision.Enrich(topic, snapshot, new VisualTestModel(), CancellationToken.None).GetAwaiter().GetResult();
            var exam = Fixture(); exam.Title = "תצוגת בדיקה — שאלה עם תמונת מקור";
            exam.Questions[0].Prompt = "התבוננו בתרשים המצורף ובחרו את צורתו.";
            exam.Questions[0].Options = new List<string> { "מלבן", "עיגול", "משולש", "כוכב" };
            exam.Questions[0].ImageIds.Add(snapshot.Files.SelectMany(f => f.Visuals).First(v => v.PageNumber == 2).Id);
            SourceImages.Bind(exam, topic, snapshot); Store.Write(store.ExamPath(topic, 1), exam); return topic;
        }
    }
}
