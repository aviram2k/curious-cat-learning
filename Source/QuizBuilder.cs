using System;
using System.Collections.Generic;
using System.Linq;

namespace Education2k
{
    public static class QuizBuilder
    {
        // Largest-remainder allocation preserves exact totals, including uneven weights.
        static int[] Allocate(int total, IList<double> weights, int minimum)
        {
            int remaining = total - minimum * weights.Count;
            double sum = weights.Sum();
            var exact = weights.Select(w => remaining * w / sum).ToArray();
            var values = exact.Select(v => minimum + (int)Math.Floor(v)).ToArray();
            int extra = total - values.Sum();
            foreach (int i in Enumerable.Range(0, weights.Count).OrderByDescending(i => exact[i] - Math.Floor(exact[i])).ThenBy(i => i).Take(extra)) values[i]++;
            return values;
        }
        public static Exam Create(Exam full)
        {
            if (full.IsQuiz) throw new InvalidOperationException("בוחן נבנה מתוך מבחן מלא בלבד.");
            Rules.Validate(full);
            var quiz = Store.Json.Deserialize<Exam>(Store.Json.Serialize(full));
            int count = (full.Questions.Count + 1) / 2;
            var selected = Enumerable.Range(0, count).Select(i => i * full.Questions.Count / count).ToList();
            foreach (var type in full.Questions.Select(q => q.Type).Distinct())
            {
                if (selected.Any(i => full.Questions[i].Type == type)) continue;
                int replacement = selected.GroupBy(i => full.Questions[i].Type).OrderByDescending(g => g.Count()).First(g => g.Count() > 1).Last();
                selected.Remove(replacement); selected.Add(full.Questions.FindIndex(q => q.Type == type));
            }
            quiz.Questions = selected.OrderBy(i => i).Select(i => quiz.Questions[i]).ToList();
            quiz.IsQuiz = true; quiz.SourceQuestionCount = full.Questions.Count; quiz.DurationMinutes = 35;
            quiz.Introduction = "בוחן קצר כדי להתחיל ללמוד בנחת. הזמן המומלץ הוא 35 דקות. אפשר להמשיך גם אחריו, ואין הגשה אוטומטית. עונים על חצי משאלות המבחן ובסיום מקבלים משוב וציון.";
            var points = Allocate(10000, quiz.Questions.Select(q => q.Points).ToList(), 1);
            var minutes = Allocate(35, quiz.Questions.Select(q => (double)q.EstimatedMinutes).ToList(), 1);
            for (int i = 0; i < quiz.Questions.Count; i++)
            {
                var q = quiz.Questions[i]; q.Points = points[i] / 100.0; q.EstimatedMinutes = minutes[i];
                if (q.Parts.Count > 0)
                {
                    var partPoints = Allocate(points[i], q.Parts.Select(p => p.Points).ToList(), 1);
                    for (int j = 0; j < q.Parts.Count; j++) q.Parts[j].Points = partPoints[j] / 100.0;
                }
                // The original rubric describes full-exam point allocations. Preserve
                // relative weights while grading the short exam on its own 100-point scale.
                q.Rubric = "יש לשמור על המשקל היחסי של רכיבי המחוון ולהחזיר Fraction בין 0 ל־1; הניקוד לשאלה בבוחן מנורמל בנפרד. " + q.Rubric;
            }
            Rules.Validate(quiz); return quiz;
        }
    }
}
