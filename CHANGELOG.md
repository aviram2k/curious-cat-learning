# Changelog

## 0.1.4 - 2026-11-25

- A Hebrew PDF exam workflow with exam name, class, school, source-file drop,
  progress preview, and repeatable creation without an application quantity cap.
- Printable questions with writing areas and blank tables, followed by a separate
  solution section. Every A4 page includes the reading-cat logo, a clickable
  project link and Codex attribution.
- Self-contained PDF export and native print, save-copy, open-file and open-folder
  actions. All managed print data stays under `מבחנים/מבחנים להדפסה`, outside
  student topic lists and progression. Interrupted work remains resumable.
- Native printing supports the default question-only range or all pages with
  solutions. Background-decoded images are detached from their original decoder
  before printing; a regression test covers XPS serialization across threads.

## 0.1.3 - 2026-11-25

- Shorter end-of-exam feedback: correct answers receive congratulations, hint-only
  deductions are explained separately, and partial answers show only the sections
  needing correction with the student's answer and the complete reference answer.
- Question wording and sources are available in collapsed details. Existing report
  comments can be simplified once through Codex without regrading or changing answers.
- Mouse-wheel scrolling continues through the exam page when the pointer is
  over a table or an answer field that cannot scroll further in that direction.
  Long answers retain their own scrolling, and horizontal table scrolling is preserved.

## Repository documentation - 2026-11-19

- Display the promotional flyer at the top of the README, linked to the Windows
  downloads page.
- Documentation-only publication; the application version and release timestamp
  remain unchanged.

## 0.1.2 - 2026-11-19

- A Hebrew wizard for adding a topic with only a name and general description,
  file drop, a Windows file picker, or direct copies into its draft folder.
- Automatic description creation, safe file copies, and resumable local drafts.
- The finish action prepares shared exam 1 through the existing source review
  and visual reading pipeline before making the topic available to students.
- Synthetic tests and an isolated wizard preview without Codex requests.

## 0.1.1 - 2026-11-19

- Name and branding: **החתול הסקרן** (Curious Cat), with the slogan
  **למידה מתוך סקרנות** and the existing reading cat artwork.
- A larger header logo beside the stacked application name and slogan.
- Consistent header button sizes and a connection indicator in the fixed footer.
- A fixed version strip with the version's update date and time in Israel.
- Version and timestamp metadata embedded in the executable and Windows file
  properties, rather than inferred from launch time or file modification time.
- Explicit owner approval for releases, PATCH-only version updates, and a clean
  portable Windows package that excludes school materials and student data.

## 0.1.0 - 2026-11-19

- Initial source publication of the Hebrew Windows learning application.
- Codex-based exams, short quizzes, guided practice, source reading and visual
  interpretation, material exclusion, feedback and student progress tracking.
- Application artwork and the licensed PDFium runtime included; school materials,
  generated exams, interpreted-source caches and student records excluded.
