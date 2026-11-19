using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Education2k
{
    public static partial class Tests
    {
        sealed class RecordingProgress : IProgress<WorkUpdate>
        {
            public readonly List<WorkUpdate> Events = new List<WorkUpdate>();
            readonly object gate = new object();
            public WorkState State;
            public void Report(WorkUpdate update) { lock (gate) { Events.Add(update); if (State != null) State.Apply(update); } }
        }
        static void ProgressChecks(string temp)
        {
            var recorded = new RecordingProgress(); var stream = new CodexEventStream();
            string final = "{\"ExpectedAnswer\":\"ANSWER_SECRET\",\"Questions\":[]}";
            using (WorkFlow.Use(recorded))
            {
                stream.Accept("not a JSON event");
                stream.Accept("{\"type\":\"thread.started\"}"); stream.Accept("{\"type\":\"turn.started\"}");
                stream.Accept("{\"type\":\"item.started\",\"item\":{\"type\":\"command_execution\",\"command\":\"COMMAND_SECRET\"}}");
                stream.Accept("{\"type\":\"item.completed\",\"item\":{\"type\":\"command_execution\",\"aggregated_output\":\"OUTPUT_SECRET\"}}");
                stream.Accept("{\"type\":\"item.completed\",\"item\":{\"type\":\"reasoning\",\"text\":\"REASONING_SECRET\"}}");
                stream.Accept("{\"type\":\"item.started\",\"item\":{\"type\":\"web_search\",\"query\":\"QUERY_SECRET\"}}");
                stream.Accept(Store.Json.Serialize(new { type = "item.completed", item = new { type = "agent_message", text = final } }));
                stream.Accept("{\"type\":\"turn.completed\"}");
                CodexEventStream.Diagnostic("ERROR codex_core::tools::router COMMAND_SECRET rejected: blocked by policy");
                CodexEventStream.Diagnostic("stream disconnected; reconnecting PRIVATE_CONNECTION_DATA");
                Task.Run(() => WorkFlow.Activity("פעולה בתהליך רקע")).GetAwaiter().GetResult();
            }
            int count = recorded.Events.Count; WorkFlow.Activity("מחוץ לפעולה");
            Assert(stream.FinalText == final && !stream.Failed, "Streaming events retain the exact structured final answer for validation");
            Assert(recorded.Events.Any(e => (e.Message ?? "").Contains("כלי עזר")) && recorded.Events.Any(e => (e.Message ?? "").Contains("מחפש מקורות")), "Command and web search events become meaningful Hebrew preview updates");
            string visible = Store.Json.Serialize(recorded.Events);
            Assert(!new[] { "ANSWER_SECRET", "COMMAND_SECRET", "OUTPUT_SECRET", "REASONING_SECRET", "QUERY_SECRET", "PRIVATE_CONNECTION_DATA" }.Any(visible.Contains), "Activity previews never expose answers, commands, tool output, reasoning or search queries");
            Assert(recorded.Events.Any(e => (e.Message ?? "").Contains("מדיניות ההרשאות")) && recorded.Events.Any(e => (e.WaitingFor ?? "").Contains("לחידוש החיבור")), "Tool policy blocks and connection retries are explained without exposing raw diagnostics");
            Assert(count == recorded.Events.Count && recorded.Events.Any(e => e.Message == "פעולה בתהליך רקע"), "Operation scopes flow into background tasks and restore isolation after disposal");
            stream.Accept("{\"type\":\"turn.failed\",\"error\":{\"message\":\"request failed\"}}");
            Assert(stream.Failed && stream.ErrorMessage == "request failed", "A failed streamed turn cannot be mistaken for a successful result");

            var now = new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);
            using (var state = new WorkState("הכנה", "quiz", null, now))
            {
                state.Apply(new WorkUpdate { Kind = "saved", Role = "report", FileName = "_material_review.json", Utc = now });
                Assert(!state.HasResult && state.FileStatus.Contains("עדיין לא נשמר"), "Saving a source report never claims the requested activity has been saved");
                state.Apply(new WorkUpdate { Kind = "saved", Role = "exam", FileName = "_exam_0001.json", Utc = now });
                Assert(!state.HasResult && state.FileStatus.Contains("הבוחן עדיין"), "Saving a quiz's full source exam does not claim the quiz itself has been saved");
                state.Apply(new WorkUpdate { Kind = "start", Step = "save", Role = "exam", Message = "שמירה", Utc = now });
                Assert(state.States["save"] == "pending", "The saving step tracks the requested activity rather than an intermediate file");
                state.Apply(new WorkUpdate { Kind = "saved", Role = "quiz", FileName = "_quiz_0001.json", Utc = now });
                Assert(state.HasResult && state.FileStatus.Contains("_quiz_0001.json"), "The saved indicator names the actual requested result file");
                string before = state.States["create"];
                Assert(state.Freshness(now.AddSeconds(75)).Contains("לא התקבל עדכון חדש") && state.Elapsed(now.AddSeconds(75)) == "01:15" && state.States["create"] == before, "Idle feedback reports stale information without inventing stage progress");
            }
            var store = new Store(Path.Combine(temp, "progress-service")); var topic = TestTopic(store, "בדיקת התקדמות", false);
            var snapshot = MaterialReader.Inspect(topic); var fake = new FakeModel { Reply = schema => schema == "materials" ? (object)TestReview(snapshot) : Fixture() };
            var events = new RecordingProgress();
            using (WorkFlow.Use(events)) new ExamService(store, fake).GetExam(topic, 1, CancellationToken.None).GetAwaiter().GetResult();
            Assert(events.Events.Where(e => e.Kind == "start").Select(e => e.Step).SequenceEqual(WorkState.Steps), "New exam progress follows actual read, review, create, check and save operations");
            int reportSaved = events.Events.FindIndex(e => e.Kind == "saved" && e.Role == "report"), examSaved = events.Events.FindIndex(e => e.Kind == "saved" && e.Role == "exam");
            Assert(reportSaved >= 0 && examSaved > reportSaved && File.Exists(store.ExamPath(topic, 1)), "File save updates distinguish the source report from the final persisted exam");
            events.Events.Clear(); int modelCalls = fake.Calls.Count;
            using (WorkFlow.Use(events)) new ExamService(store, fake).GetExam(topic, 1, CancellationToken.None).GetAwaiter().GetResult();
            Assert(fake.Calls.Count == modelCalls && events.Events.Any(e => e.Kind == "skip" && e.Step == "create") && events.Events.Any(e => e.Kind == "existing" && e.Role == "exam"), "Cached exams report skipped creation and existing files without faking a model request");
            events.Events.Clear();
            using (var held = new FileStream(Path.Combine(topic.Folder, "_generation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            using (var cancelled = new CancellationTokenSource())
            using (WorkFlow.Use(events))
            {
                var pending = new ExamService(store, fake).GetExam(topic, 2, cancelled.Token);
                Assert(events.Events.Any(e => (e.WaitingFor ?? "").Contains("תהליך ההכנה האחר")) && fake.Calls.Count == modelCalls, "A real exclusive lock is reported as waiting for another process before any model request");
                cancelled.Cancel(); bool stopped = false;
                try { pending.GetAwaiter().GetResult(); } catch (OperationCanceledException) { stopped = true; }
                Assert(stopped && !File.Exists(store.ExamPath(topic, 2)), "Cancellation interrupts a lock wait without creating an exam");
            }
            string logRoot = Path.Combine(temp, "progress-log");
            using (var state = new WorkState("הכנה", "exam", logRoot, now))
            {
                state.Apply(new WorkUpdate { Kind = "activity", Message = "נקרא קובץ בדיקה", Utc = now });
                Assert(state.Log.Any(l => l.Contains("נקרא קובץ בדיקה")) && !Directory.Exists(logRoot), "Activity preview stays in memory and creates no files or folders");
            }
        }
        public static int LiveProgress(string root)
        {
            var events = new RecordingProgress();
            using (var state = new WorkState("בדיקת זרם עדכונים", "activity", root, DateTime.UtcNow))
            using (WorkFlow.Use(events))
            {
                events.State = state;
                var reply = new CodexClient(root).Ask<CodexClient.Ping>("Return Message exactly as מוכן. Do not use tools, read files, modify files or search the web.", "ping", false, 90, CancellationToken.None).GetAwaiter().GetResult();
                if (reply == null || reply.Message != "מוכן" || !events.Events.Any(e => (e.Message ?? "").Contains("התחיל לעבד")) || !events.Events.Any(e => (e.Message ?? "").Contains("סיים את התשובה"))) throw new InvalidOperationException("The live streamed reply or lifecycle events were not observed.");
                File.WriteAllText(Path.Combine(root, "Build", "progress-live-test.txt"), "PASS: A real Codex request emitted live Hebrew lifecycle updates and returned a valid structured final response.\n" + String.Join("\n", state.Log), Encoding.UTF8); return 0;
            }
        }
        sealed class DemoProgressModel : IModelClient
        {
            readonly MaterialSnapshot snapshot;
            public DemoProgressModel(MaterialSnapshot snapshot) { this.snapshot = snapshot; }
            public async Task<T> Ask<T>(string prompt, string schema, bool search, int timeoutSeconds, CancellationToken cancellation)
            {
                WorkFlow.Activity("תצוגת בדיקה: מדגימים המתנה לתשובת המודל.", "זו תצוגת בדיקה עם נתוני הדגמה, ללא פנייה אמיתית ל־Codex.");
                await Task.Delay(2200, cancellation);
                if (schema == "materials") return (T)(object)TestReview(snapshot);
                await Task.Delay(1800, cancellation); return (T)(object)Fixture();
            }
        }
        public static async Task ProgressPreview(Store store, CancellationToken cancellation)
        {
            var topic = TestTopic(store, "הדגמת מסך המתנה", false); var snapshot = MaterialReader.Inspect(topic);
            int number = store.ExamFiles(topic).Count() + 1;
            await new ExamService(store, new DemoProgressModel(snapshot)).GetExam(topic, number, cancellation);
            WorkFlow.Start("", "תצוגת בדיקה — המבחן לדוגמה נשמר", "מסך ההמתנה נשאר פתוח לזמן קצר לצורך בדיקת הפריוויו.");
            await Task.Delay(120000, cancellation);
        }
    }
}
