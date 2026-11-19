using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace Education2k
{
    public static class MaterialReader
    {
        public static string ReadText(string file)
        {
            var bytes = File.ReadAllBytes(file);
            if (bytes.Length >= 2 && bytes[0] == 255 && bytes[1] == 254) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 254 && bytes[1] == 255) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            try { return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\ufeff'); }
            catch (DecoderFallbackException) { return Encoding.GetEncoding(1255).GetString(bytes); }
        }
        public static string Extract(string file)
        {
            string extension = Path.GetExtension(file).ToLowerInvariant();
            if (extension == ".txt" || extension == ".md" || extension == ".csv" || extension == ".tsv") return ReadText(file);
            if (extension == ".pdf") return String.Join("\n\n", PdfDocument.Read(file, null, null, Path.GetFileName(file), "", false, System.Threading.CancellationToken.None).Select(p => "עמוד " + p.PageNumber + "\n" + p.Text));
            if (extension == ".docx")
            {
                using (var zip = ZipFile.OpenRead(file))
                {
                    var entry = zip.GetEntry("word/document.xml");
                    if (entry == null) throw new InvalidDataException("קובץ Word ללא תוכן תקין.");
                    using (var stream = entry.Open())
                    {
                        var doc = XDocument.Load(stream);
                        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                        return String.Join("\n", doc.Descendants(w + "p").Select(p => String.Concat(p.Descendants(w + "t").Select(t => t.Value))));
                    }
                }
            }
            if (extension == ".pptx")
            {
                using (var zip = ZipFile.OpenRead(file))
                {
                    var text = new StringBuilder(); int slideNumber = 0;
                    foreach (var slidePath in SlidePaths(zip))
                    {
                        var slide = ReadXml(zip, slidePath);
                        text.AppendLine("שקופית " + (++slideNumber)); text.AppendLine(DrawingText(slide));
                        var relsEntry = zip.GetEntry(RelationshipPath(slidePath));
                        if (relsEntry == null) continue;
                        var rels = ReadXml(zip, relsEntry.FullName);
                        foreach (var relation in rels.Root.Elements().Where(e => ((string)e.Attribute("Type") ?? "").EndsWith("/notesSlide", StringComparison.Ordinal) && (string)e.Attribute("TargetMode") != "External"))
                        {
                            var notes = ReadXml(zip, ResolvePart(slidePath, (string)relation.Attribute("Target")));
                            XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
                            foreach (var shape in notes.Descendants(p + "sp"))
                            {
                                var placeholder = shape.Descendants(p + "ph").FirstOrDefault();
                                if (placeholder != null && (string)placeholder.Attribute("type") != "body") continue;
                                string note = DrawingText(shape);
                                if (!String.IsNullOrWhiteSpace(note)) text.AppendLine("הערות לשקופית: " + note);
                            }
                        }
                    }
                    return text.ToString();
                }
            }
            return null;
        }
        internal static XDocument ReadXml(ZipArchive zip, string part)
        {
            var entry = zip.GetEntry(part);
            if (entry == null) throw new InvalidDataException("חסר רכיב במסמך: " + part);
            using (var stream = entry.Open()) return XDocument.Load(stream);
        }
        static string DrawingText(XContainer xml)
        {
            XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
            return String.Join("\n", xml.Descendants(a + "p").Select(p => String.Concat(p.Descendants().Where(e => e.Name == a + "t" || e.Name == a + "br").Select(e => e.Name == a + "br" ? "\n" : e.Value))));
        }
        internal static string RelationshipPath(string part)
        {
            int split = part.LastIndexOf('/'); return part.Substring(0, split + 1) + "_rels/" + part.Substring(split + 1) + ".rels";
        }
        internal static string ResolvePart(string origin, string target)
        {
            return Uri.UnescapeDataString(new Uri(new Uri("http://package/" + origin), target).AbsolutePath.TrimStart('/'));
        }
        internal static List<string> SlidePaths(ZipArchive zip)
        {
            XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
            XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            var presentation = ReadXml(zip, "ppt/presentation.xml");
            var relations = ReadXml(zip, "ppt/_rels/presentation.xml.rels").Root.Elements().ToDictionary(e => (string)e.Attribute("Id"));
            var paths = new List<string>();
            foreach (var id in presentation.Descendants(p + "sldId"))
            {
                XElement relation;
                if (!relations.TryGetValue((string)id.Attribute(r + "id"), out relation) || (string)relation.Attribute("TargetMode") == "External") throw new InvalidDataException("קישור לשקופית אינו תקין במצגת.");
                paths.Add(ResolvePart("ppt/presentation.xml", (string)relation.Attribute("Target")));
            }
            if (paths.Count == 0) throw new InvalidDataException("לא נמצאו שקופיות במצגת.");
            return paths;
        }
        static string UnreadVisuals(string file)
        {
            string extension = Path.GetExtension(file).ToLowerInvariant();
            if (extension == ".pdf") return "יש לבדוק גם את תצוגת העמודים, כולל סדר העברית, טבלאות, תמונות ותרשימים.";
            if (extension != ".docx" && extension != ".pptx") return "";
            using (var zip = ZipFile.OpenRead(file))
            {
                if (extension == ".docx")
                {
                    int media = zip.Entries.Count(e => e.FullName.StartsWith("word/media/", StringComparison.Ordinal) || e.FullName.StartsWith("word/embeddings/", StringComparison.Ordinal) || e.FullName.StartsWith("word/charts/", StringComparison.Ordinal));
                    if (ReadXml(zip, "word/document.xml").Descendants().Any(e => e.Name.LocalName == "drawing" || e.Name.LocalName == "pict" || e.Name.LocalName == "object")) media = Math.Max(1, media);
                    return media == 0 ? "" : "הטקסט נקרא, אך " + media + " קובצי תמונה או אובייקטים משובצים אינם כלולים בחילוץ הטקסט. תוכנם החזותי לא נבדק.";
                }
                var details = new List<string>(); int number = 0;
                XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main";
                XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
                foreach (var path in SlidePaths(zip))
                {
                    number++; var slide = ReadXml(zip, path);
                    int count = slide.Descendants(p + "pic").Count() + slide.Descendants(p + "graphicFrame").Count(g => !g.Descendants(a + "tbl").Any());
                    if (count > 0) details.Add("שקופית " + number + ": " + count + " תמונות, טבלאות או תרשימים; " + DrawingText(slide).Length + " תווי טקסט שחולצו");
                }
                return details.Count == 0 ? "" : "הטקסט והערות המרצה נקראו לפי סדר השקופיות. תוכן חזותי אינו מפוענח על ידי חילוץ הטקסט; טקסט מטבלאות נקרא, אך תמונות, מפות, גרפים ואובייקטים אחרים עלולים להכיל חומר שלא נמסר. " + String.Join("; ", details);
            }
        }
        public static string Hash(Topic topic)
        {
            using (var sha = SHA256.Create())
            using (var memory = new MemoryStream())
            {
                var heading = Encoding.UTF8.GetBytes(topic.Description);
                memory.Write(heading, 0, heading.Length);
                foreach (var file in topic.Materials)
                {
                    var name = Encoding.UTF8.GetBytes(Path.GetFileName(file));
                    memory.Write(name, 0, name.Length);
                    try { using (var input = File.OpenRead(file)) { var digest = sha.ComputeHash(input); memory.Write(digest, 0, digest.Length); } }
                    catch (Exception e)
                    {
                        if (!(e is IOException) && !(e is UnauthorizedAccessException)) throw;
                        var failure = Encoding.UTF8.GetBytes("UNREADABLE|" + e.GetType().Name + "|" + File.GetLastWriteTimeUtc(file).Ticks);
                        memory.Write(failure, 0, failure.Length);
                    }
                }
                memory.Position = 0;
                return BitConverter.ToString(sha.ComputeHash(memory)).Replace("-", "");
            }
        }
        public static string CurrentHash(Topic topic) { return Hash(Store.ReadTopic(topic.Folder)); }
        public static string Context(Topic topic)
        {
            return Inspect(topic).Context;
        }
        public static MaterialSnapshot Inspect(Topic topic)
        {
            var snapshot = new MaterialSnapshot { Hash = Hash(topic) };
            var context = new StringBuilder();
            context.AppendLine("תיאור הנושא: " + topic.Description);
            context.AppendLine("תיקיית המקורות: " + topic.Folder);
            if (!String.IsNullOrEmpty(topic.DescriptionIssue)) snapshot.Files.Add(new MaterialFileContent { FileName = "_describe.txt", Path = Path.Combine(topic.Folder, "_describe.txt"), Status = "unread", MustWarn = true, Detail = topic.DescriptionIssue, Text = "" });
            foreach (var file in topic.Materials)
            {
                WorkFlow.Activity("קוראים קובץ: " + WorkFlow.SafeLabel(Path.GetFileName(file)), "קוראים חומר לימוד מהמחשב.");
                var item = new MaterialFileContent { FileName = Path.GetFileName(file), Path = file, Status = "read", Detail = "", Text = "" };
                try
                {
                    string text = Extract(file);
                    if (text == null) { item.Status = "unread"; item.Detail = "אין חילוץ טקסט מקומי לפורמט " + Path.GetExtension(file) + ". נדרשת בדיקת כלי הקריאה של הצ׳אט."; }
                    else if (String.IsNullOrWhiteSpace(text)) { item.Status = "unread"; item.MustWarn = true; item.Detail = "לא חולץ טקסט קריא מהקובץ."; }
                    else
                    {
                        item.Text = text;
                        string visuals = UnreadVisuals(file);
                        item.Detail = "חולצו " + text.Length + " תווי טקסט. " + visuals;
                        if (visuals.Length > 0) { item.Status = "partial"; item.MustWarn = true; }
                        int budget = Math.Max(0, 400000 - context.Length);
                        if (item.Text.Length > budget) { item.Text = item.Text.Substring(0, budget); item.Status = "partial"; item.MustWarn = true; item.Detail += " הטקסט גדול מדי לבקשה אחת ונמסרו רק " + budget + " תווים. יש לפצל את החומר לתיקיות קטנות יותר."; }
                    }
                }
                catch (Exception e)
                {
                    if (!(e is IOException) && !(e is UnauthorizedAccessException) && !(e is System.Xml.XmlException) && !(e is InvalidDataException) && !(e is ArgumentException)) throw;
                    item.Status = "unread"; item.MustWarn = true; item.Detail = "הקריאה המקומית נכשלה: " + e.GetType().Name + ": " + e.Message;
                }
                snapshot.Files.Add(item);
                WorkFlow.Activity(WorkFlow.SafeLabel(item.FileName) + (item.Status == "read" ? " — הסתיים חילוץ הטקסט." : " — נמצאה מגבלה בקריאה; תיכלל בדוח הצ׳אט."));
                context.AppendLine("\n--- מקור: " + item.FileName + " ---\nמצב חילוץ: " + item.Status + "\nפרטי הקריאה: " + item.Detail + "\n" + item.Text);
            }
            if (snapshot.Files.Count == 0) snapshot.Files.Add(new MaterialFileContent { FileName = "תיקיית הנושא", Path = topic.Folder, Status = "unread", MustWarn = true, Detail = "אין בתיקייה קובצי חומר לימוד. יש רק תיאור נושא, אם הוזן.", Text = "" });
            snapshot.Context = context.ToString(); return snapshot;
        }
        public static void RebuildContext(Topic topic, MaterialSnapshot snapshot)
        {
            var context = new StringBuilder("תיאור הנושא: " + topic.Description + "\n");
            foreach (var file in snapshot.Files)
            {
                int budget = Math.Max(0, 400000 - context.Length);
                if (file.Text.Length > budget) { file.Text = file.Text.Substring(0, budget); file.Status = "partial"; file.MustWarn = true; file.Detail += " חלק מהתוכן לא נמסר בגלל גודלו. יש לפצל את החומר."; }
                context.AppendLine("\nמקור: " + file.FileName + "\nמצב קריאה: " + file.Status + "\nפרטי קריאה: " + file.Detail + "\n" + file.Text);
            }
            snapshot.Context = context.ToString();
        }
    }
}
