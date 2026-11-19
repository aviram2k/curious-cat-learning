using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Education2k
{
    public enum MaterialDecision { Cancel, Continue, ExcludeProblematic }
    public class SourceUnit
    {
        public string Id, FileName, Label, Text;
    }
    public class MaterialSelection
    {
        public bool Filtered;
        public MaterialSnapshot Snapshot;
        public List<SourceUnit> Units = new List<SourceUnit>();
        public List<string> Excluded = new List<string>();
        public string ScopeNote { get { return "המבחן נוצר רק מהחומר שנקרא במלואו. החומר הבא לא נכלל: " + String.Join("; ", Excluded) + ". הציון והמשוב מתייחסים לחומר שנכלל בלבד."; } }
        public void Apply(Exam exam)
        {
            if (!Filtered) return;
            var allowed = Units.ToDictionary(u => u.Id);
            foreach (var question in exam.Questions)
            {
                if (question.SourceUnitIds == null || question.SourceUnitIds.Count == 0 || question.SourceUnitIds.Any(id => !allowed.ContainsKey(id))) throw new InvalidOperationException("אחת השאלות אינה משויכת לחומר המאושר בלבד. המבחן לא נשמר; אפשר לנסות שוב.");
                question.SourceReferences = question.SourceUnitIds.Distinct().Select(id => allowed[id].FileName + " · " + allowed[id].Label).ToList();
            }
            exam.Sources = exam.Questions.SelectMany(q => q.SourceReferences).Distinct().ToList();
            exam.ExcludedMaterials = Excluded.ToList(); exam.FilteredSources = true;
        }
    }
    public static class MaterialScope
    {
        public static MaterialSelection Select(Topic topic, MaterialSnapshot source, MaterialReview review)
        {
            var selection = new MaterialSelection { Filtered = true, Snapshot = new MaterialSnapshot { Hash = source.Hash } };
            var problems = new HashSet<string>(review.Files.Where(f => f.Status != "read").Select(f => f.FileName), StringComparer.OrdinalIgnoreCase);
            foreach (string unknown in problems.Where(name => !source.Files.Any(f => String.Equals(f.FileName, name, StringComparison.OrdinalIgnoreCase)))) selection.Excluded.Add(unknown);
            int fileNumber = 0;
            foreach (var file in source.Files)
            {
                fileNumber++;
                var roots = file.Visuals.Where(v => String.IsNullOrEmpty(v.ParentId)).ToList();
                bool problematic = problems.Contains(file.FileName) || file.MustWarn || file.Status != "read";
                // Page-level filtering is safe only when the complete document was rendered,
                // and the reading status identifies the problematic pages unambiguously.
                bool unread = file.Status == "unread" || review.Files.Any(f => f.Status == "unread" && String.Equals(f.FileName, file.FileName, StringComparison.OrdinalIgnoreCase));
                bool granular = problematic && !unread && file.CanResolveVisuals && roots.Count > 0 && roots.Any(v => v.Reading == null || v.Reading.Status != "read");
                if (file.FileName == "_describe.txt" || (problematic && !granular)) { selection.Excluded.Add(file.FileName); continue; }
                var kept = new MaterialFileContent { FileName = file.FileName, Path = file.Path, Status = "read", Detail = "חומר שאושר לקריאה מלאה לאחר החרגת מקורות בעייתיים.", Text = "" };
                if (roots.Count > 0)
                {
                    foreach (var page in roots)
                    {
                        if (page.Reading == null || page.Reading.Status != "read") { selection.Excluded.Add(file.FileName + " · " + page.Label + " (כולל תמונות המקור שלה)"); continue; }
                        var content = page.Reading.Text + "\nתוכן חזותי: " + page.Reading.Description;
                        selection.Units.Add(new SourceUnit { Id = page.Id, FileName = file.FileName, Label = page.Label, Text = content });
                        kept.Visuals.Add(page);
                        kept.Visuals.AddRange(file.Visuals.Where(v => v.ParentId == page.Id && v.Reading != null && v.Reading.Status == "read"));
                    }
                }
                else if (!String.IsNullOrWhiteSpace(file.Text)) selection.Units.Add(new SourceUnit { Id = "source-file-" + fileNumber, FileName = file.FileName, Label = "תוכן הקובץ", Text = file.Text });
                var units = selection.Units.Where(u => u.FileName == file.FileName).ToList();
                if (units.Count > 0) { kept.Text = String.Join("\n\n", units.Select(u => "יחידת מקור " + u.Id + " · " + u.Label + "\n" + u.Text)); selection.Snapshot.Files.Add(kept); }
            }
            selection.Excluded = selection.Excluded.Distinct().ToList();
            MaterialReader.RebuildContext(new Topic { Description = "" }, selection.Snapshot);
            // RebuildContext must not silently remove part of an approved source unit.
            if (selection.Snapshot.Files.Any(f => f.MustWarn)) throw new InvalidOperationException("החומר שנשאר גדול מדי לבקשה אחת. יש לפצל את הנושא לפני יצירת מבחן ללא החומר הבעייתי.");
            return selection;
        }
        public static void Preview(Topic topic, MaterialSnapshot snapshot, MaterialReview review)
        {
            var selection = Select(topic, snapshot, review); review.ExclusionPreview = selection.Excluded;
            review.HasUsableMaterial = selection.Units.Count > 0;
        }
        public static MaterialSelection Exclude(Topic topic, MaterialSnapshot snapshot, MaterialReview review)
        {
            var selection = Select(topic, snapshot, review);
            if (selection.Units.Count == 0) throw new InvalidOperationException("לא נשאר חומר לימוד שנקרא במלואו לאחר ההחרגה. לא נוצר מבחן. יש לצרף חומר קריא או לתקן את הקבצים ולנסות שוב.");
            WorkFlow.Activity("החומר הבעייתי הוחרג. מכינים שאלות רק מתוך " + selection.Units.Count + " יחידות מקור שנקראו במלואן.");
            return selection;
        }
    }
}
