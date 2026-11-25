using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Education2k
{
    public class WorkUpdate
    {
        public string Kind, Step, Message, WaitingFor, FileName, Role;
        public DateTime Utc = DateTime.UtcNow;
    }
    public static class WorkFlow
    {
        static readonly AsyncLocal<IProgress<WorkUpdate>> current = new AsyncLocal<IProgress<WorkUpdate>>();
        sealed class Scope : IDisposable
        {
            readonly IProgress<WorkUpdate> previous;
            public Scope(IProgress<WorkUpdate> progress) { previous = current.Value; current.Value = progress; }
            public void Dispose() { current.Value = previous; }
        }
        public static IDisposable Use(IProgress<WorkUpdate> progress) { return new Scope(progress); }
        public static void Send(WorkUpdate update) { var target = current.Value; if (target != null) target.Report(update); }
        public static void Start(string step, string message, string waiting = null, string role = null) { Send(new WorkUpdate { Kind = "start", Step = step, Message = message, WaitingFor = waiting ?? message, Role = role }); }
        public static void Complete(string step, string message, string role = null) { Send(new WorkUpdate { Kind = "complete", Step = step, Message = message, Role = role }); }
        public static void Skip(string step, string message) { Send(new WorkUpdate { Kind = "skip", Step = step, Message = message }); }
        public static void Activity(string message, string waiting = null) { Send(new WorkUpdate { Kind = "activity", Message = message, WaitingFor = waiting }); }
        public static void Signal() { Send(new WorkUpdate { Kind = "signal" }); }
        public static void Saved(string file, string role, bool existing = false)
        {
            Send(new WorkUpdate { Kind = existing ? "existing" : "saved", FileName = Path.GetFileName(file), Role = role,
                Message = (existing ? "קובץ שמור זמין: " : "נשמר קובץ: ") + "\u200e" + Path.GetFileName(file) + "\u200e" });
        }
        public static string SafeLabel(string text)
        {
            string value = String.Concat((text ?? "").Where(c => !Char.IsControl(c)));
            return value.Length > 250 ? value.Substring(0, 250) + "…" : value;
        }
    }
    public class WorkState : IDisposable
    {
        public static readonly string[] Steps = { "read", "review", "create", "check", "save" };
        public static readonly string[] Labels = { "קריאת החומרים", "בדיקת המקורות", "הכנת השאלות", "בדיקת המבחן", "שמירה" };
        public readonly Dictionary<string, string> States = Steps.ToDictionary(s => s, s => "pending");
        public readonly List<string> Log = new List<string>();
        public readonly DateTime StartedUtc;
        public DateTime LastUpdateUtc;
        DateTime lastStatusUtc;
        public readonly string Goal;
        public string Heading, WaitingFor, FileStatus, ResultFile;
        public bool HasResult;
        public bool IsPreparation { get { return Goal == "exam" || Goal == "quiz" || Goal == "pdf"; } }
        public WorkState(string heading, string goal, string root, DateTime now)
        {
            Goal = goal; Heading = heading; StartedUtc = LastUpdateUtc = lastStatusUtc = now;
            WaitingFor = "בודקים מה נדרש כדי להתחיל.";
            FileStatus = IsPreparation ? (goal == "pdf" ? "קובץ ה־PDF עדיין לא נשמר." : goal == "quiz" ? "הבוחן עדיין לא נשמר." : "המבחן עדיין לא נשמר.") : "טרם נשמר קובץ חדש בפעולה הזאת.";
        }
        public void Apply(WorkUpdate update)
        {
            if (update.Utc > LastUpdateUtc) LastUpdateUtc = update.Utc;
            if (update.Kind == "signal") return;
            bool primaryStep = !(update.Step == "save" && IsPreparation && update.Role != null && update.Role != Goal);
            if (update.Step != null && States.ContainsKey(update.Step) && primaryStep)
            {
                if (update.Kind == "start") States[update.Step] = "active";
                if (update.Kind == "complete") States[update.Step] = "done";
                if (update.Kind == "skip") States[update.Step] = "skipped";
            }
            if (update.Utc >= lastStatusUtc)
            {
                if (update.Kind == "start") Heading = update.Message;
                if (update.WaitingFor != null) WaitingFor = update.WaitingFor;
                lastStatusUtc = update.Utc;
            }
            if ((update.Kind == "saved" || update.Kind == "existing") && update.Role == Goal)
            {
                HasResult = true; ResultFile = update.FileName;
                FileStatus = (update.Kind == "existing" ? "משתמשים בקובץ שכבר נשמר: " : "נשמר בהצלחה: ") + "\u200e" + update.FileName + "\u200e";
            }
            else if (update.Kind == "saved" && update.Role == "report" && IsPreparation && !HasResult)
                FileStatus = "דוח בדיקת המקורות נשמר. " + (Goal == "pdf" ? "קובץ ה־PDF עדיין לא נשמר." : Goal == "quiz" ? "הבוחן עדיין לא נשמר." : "המבחן עדיין לא נשמר.");
            else if (update.Kind == "saved" && update.Role == "exam" && Goal == "quiz" && !HasResult)
                FileStatus = "המבחן המלא נשמר. הבוחן עדיין בהכנה.";
            else if (update.Kind == "saved" && update.Role == "exam" && Goal == "pdf" && !HasResult)
                FileStatus = "השאלות נשמרו. קובץ ה־PDF עדיין בהכנה.";
            if (String.IsNullOrEmpty(update.Message)) return;
            string line = Elapsed(update.Utc) + " · " + WorkFlow.SafeLabel(update.Message);
            Log.Add(line); if (Log.Count > 120) Log.RemoveAt(0);
        }
        public string Elapsed(DateTime now)
        {
            var elapsed = now > StartedUtc ? now - StartedUtc : TimeSpan.Zero;
            return ((int)elapsed.TotalMinutes).ToString("00") + ":" + elapsed.Seconds.ToString("00");
        }
        public string Freshness(DateTime now)
        {
            int seconds = Math.Max(0, (int)(now - LastUpdateUtc).TotalSeconds);
            return seconds < 5 ? "התקבל עדכון פעילות כעת" : seconds < 60 ? "עדכון פעילות אחרון לפני " + seconds + " שניות" : seconds < 120 ? "לא התקבל עדכון חדש כבר דקה. עדיין ממתינים; אין מידע חדש על ההתקדמות." : "לא התקבל עדכון חדש כבר " + (seconds / 60) + " דקות. עדיין ממתינים; אין מידע חדש על ההתקדמות.";
        }
        public void Dispose() { Log.Clear(); }
    }
    public class CodexEventStream
    {
        public string FinalText, ErrorMessage;
        public bool Failed;
        DateTime lastSignal = DateTime.MinValue;
        static string Text(Dictionary<string, object> data, string key)
        {
            object value; return data.TryGetValue(key, out value) && value is string ? (string)value : "";
        }
        public static void Diagnostic(string line)
        {
            string value = (line ?? "").ToLowerInvariant();
            if (value.Contains("tools::router") && value.Contains("blocked by policy"))
                WorkFlow.Activity("Codex דיווח שבקשת כלי נחסמה לפי מדיניות ההרשאות.", "ממתינים להמשך הבקשה לאחר חסימת כלי. Codex עשוי לדווח שחלק מהחומר אינו זמין.");
            else if (value.Contains("reconnecting") || value.Contains("stream disconnected") || value.Contains("retrying request"))
                WorkFlow.Activity("Codex הודיע על ניסיון לחידוש החיבור.", "ממתינים לחידוש החיבור של Codex.");
            else if (value.Contains("tools::router") && value.Contains("failed"))
                WorkFlow.Activity("Codex דיווח שפעולת כלי נכשלה.", "ממתינים להמשך עיבוד הבקשה לאחר כשל בכלי עזר.");
        }
        public void Accept(string line)
        {
            Dictionary<string, object> data;
            try { data = Store.Json.DeserializeObject(line) as Dictionary<string, object>; }
            catch (ArgumentException) { return; }
            catch (InvalidOperationException) { return; }
            if (data == null) return;
            string type = Text(data, "type");
            if (DateTime.UtcNow - lastSignal > TimeSpan.FromSeconds(1)) { WorkFlow.Signal(); lastSignal = DateTime.UtcNow; }
            if (type == "thread.started") WorkFlow.Activity("נוצר חיבור לבקשת Codex.", "ממתינים לתחילת העיבוד ב־Codex.");
            else if (type == "turn.started") WorkFlow.Activity("Codex התחיל לעבד את הבקשה.", "Codex מעבד את הבקשה ומכין תשובה.");
            else if (type == "turn.completed") WorkFlow.Activity("Codex סיים את התשובה; התוכנה בודקת אותה.", "בודקים את התוצאה שהתקבלה מ־Codex.");
            else if (type == "error" || type == "turn.failed")
            {
                if (type == "turn.failed") Failed = true;
                ErrorMessage = Text(data, "message"); object error;
                if (data.TryGetValue("error", out error) && error is Dictionary<string, object>) ErrorMessage = Text((Dictionary<string, object>)error, "message");
                WorkFlow.Activity("Codex דיווח על תקלה בבקשה.", "ממתינים לסיום הבקשה ולפרטי התקלה.");
            }
            else if (type == "item.started" || type == "item.updated" || type == "item.completed")
            {
                object value; if (!data.TryGetValue("item", out value)) return;
                var item = value as Dictionary<string, object>; if (item == null) return;
                string itemType = Text(item, "type"); bool done = type == "item.completed";
                // Deliberately whitelist metadata. Raw commands, outputs, model
                // reasoning and final exam JSON must never enter the preview.
                if (itemType == "agent_message" && done)
                {
                    FinalText = Text(item, "text");
                    WorkFlow.Activity("התקבלה הודעה מ־Codex.", "ממתינים לסיום בקשת Codex ולבדיקת התוצאה.");
                }
                else if (itemType == "command_execution" && (done || type == "item.started"))
                    WorkFlow.Activity(done ? "פעולת כלי העזר של Codex הסתיימה." : "Codex מפעיל כלי עזר.", done ? "ממתינים להמשך העיבוד ב־Codex." : "ממתינים לסיום פעולת כלי העזר.");
                else if (itemType == "web_search" && (done || type == "item.started"))
                    WorkFlow.Activity(done ? "התקבל עדכון על חיפוש במקורות ברשת." : "Codex מחפש מקורות ברשת.", done ? "Codex מעבד את המקורות לקראת התשובה." : "ממתינים לתוצאות החיפוש ברשת.");
                else if (itemType == "mcp_tool_call" && (done || type == "item.started"))
                    WorkFlow.Activity(done ? "פעולת הכלי המחובר הסתיימה." : "Codex פונה לכלי מחובר.", done ? "ממתינים להמשך העיבוד ב־Codex." : "ממתינים לתשובת הכלי המחובר.");
            }
        }
    }
}
