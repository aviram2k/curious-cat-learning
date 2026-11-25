using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Education2k
{
    public partial class MainWindow
    {
        StackPanel SummaryQuestion(int number, Question question, Answer answer)
        {
            var panel = Panel(); bool correct = FeedbackReport.Correct(answer);
            panel.Children.Add(Text("שאלה " + number + " · " + (correct ? attempt.Practice ? "נענה בהצלחה בתרגול" : "הבנה טובה" : "נושא שכדאי לחזק"), 20, correct ? "#176F65" : "#7C623D"));
            if (!attempt.Practice) panel.Children.Add(Text(Rules.Earned(question, answer).ToString("0.##") + " מתוך " + question.Points.ToString("0.##") + " נקודות", 14, "#596F76"));
            if (correct)
            {
                panel.Children.Add(Text(FeedbackReport.Success(answer, attempt.Practice), 17, "#176F65")); return panel;
            }
            foreach (var note in FeedbackReport.Notes(question, answer))
            {
                if (note.PartId != "")
                {
                    int index = question.Parts.FindIndex(p => p.Id == note.PartId); var part = question.Parts[index]; var partAnswer = answer.Parts.Single(a => a.PartId == part.Id);
                    Gap(panel, 6); panel.Children.Add(Text(Rules.PartLabel(index), 18));
                    SummaryResponse(panel, "התשובה שלך", part.Type, part.Rows, part.Columns, partAnswer.Text, partAnswer.Cells, partAnswer.HasContent, false);
                    SummaryResponse(panel, "התשובה המלאה וההסבר", part.Type, part.Rows, part.Columns, part.ExpectedAnswer, part.CorrectCells, true, true);
                }
                else
                {
                    SummaryResponse(panel, "התשובה שלך", question.Type, question.Rows, question.Columns, AnswerText(question, answer), answer.Cells, answer.HasContent, false);
                    SummaryResponse(panel, "התשובה המלאה וההסבר", question.Type, question.Rows, question.Columns, question.ExpectedAnswer, question.CorrectCells, true, true);
                    if (question.Type == "multiple_choice" && question.ExpectedAnswer.Trim() != question.Options[question.CorrectOption].Trim())
                        panel.Children.Add(Text("האפשרות הנכונה: " + question.Options[question.CorrectOption], 16, "#176F65"));
                }
                panel.Children.Add(Text("מה צריך לדייק: " + note.Note, 16, "#7C623D"));
            }
            if (!attempt.Practice && answer.HintUsed) panel.Children.Add(Text("בנוסף לתיקונים, השימוש ברמז הפחית מחצית מהניקוד לשאלה.", 14, "#596F76"));
            var details = Panel(); details.Children.Add(Text(FeedbackReport.Structured(question, answer) ? question.Context : question.Prompt, 17));
            foreach (var part in question.Parts) details.Children.Add(Text(Rules.PartLabel(question.Parts.IndexOf(part)) + " · " + part.Prompt, 16));
            SourceImages.Add(details, this, topic, exam, question);
            details.Children.Add(Text("מקורות: " + String.Join(" · ", question.SourceReferences), 13, "#596F76"));
            panel.Children.Add(new Expander { Header = "השאלה והמקורות", IsExpanded = false, FontSize = 14, Content = details, Margin = new Thickness(0, 8, 0, 0) }); return panel;
        }
        static void SummaryResponse(StackPanel panel, string label, string type, List<string> rows, List<string> columns, string text, List<List<string>> cells, bool hasContent, bool reference)
        {
            panel.Children.Add(Text(label, 16, reference ? "#176F65" : "#596F76"));
            if (!hasContent) { panel.Children.Add(Text("לא נכתבה תשובה", 17)); return; }
            if (type == "table")
            {
                var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
                for (int i = 0; i <= columns.Count; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (int c = 0; c < columns.Count; c++) AddCell(grid, Text(columns[c], 14), 0, c + 1, true);
                for (int r = 0; r < rows.Count; r++)
                {
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); AddCell(grid, Text(rows[r], 14), r + 1, 0, true);
                    for (int c = 0; c < columns.Count; c++)
                    {
                        string value = r < cells.Count && c < cells[r].Count ? cells[r][c] : "";
                        AddCell(grid, Text(String.IsNullOrWhiteSpace(value) ? "—" : value, 15), r + 1, c + 1, false);
                    }
                }
                panel.Children.Add(TableViewport(grid, columns.Count));
                if (!reference) return;
            }
            panel.Children.Add(Text(String.IsNullOrWhiteSpace(text) ? "לא נכתבה תשובה" : text, 17, reference ? "#176F65" : null));
        }
    }
}
