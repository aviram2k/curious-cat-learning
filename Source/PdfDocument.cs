using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Education2k
{
    // PDFium calls are serialized: the native library is not thread safe.
    public static class PdfDocument
    {
        const string Library = "pdfium.dll";
        static readonly object gate = new object();
        static bool initialized;
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern void FPDF_InitLibrary();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr FPDF_LoadMemDocument(IntPtr data, int length, string password);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern uint FPDF_GetLastError();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int FPDF_GetPageCount(IntPtr doc);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr FPDF_LoadPage(IntPtr doc, int index);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern double FPDF_GetPageWidth(IntPtr page);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern double FPDF_GetPageHeight(IntPtr page);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern void FPDF_ClosePage(IntPtr page);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern void FPDF_CloseDocument(IntPtr doc);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr FPDFText_LoadPage(IntPtr page);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int FPDFText_CountChars(IntPtr page);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int FPDFText_GetText(IntPtr page, int start, int count, [Out] byte[] result);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern void FPDFText_ClosePage(IntPtr page);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr FPDFBitmap_Create(int width, int height, int alpha);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern void FPDFBitmap_FillRect(IntPtr bitmap, int x, int y, int width, int height, uint color);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern int FPDFBitmap_GetStride(IntPtr bitmap);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern void FPDFBitmap_Destroy(IntPtr bitmap);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int x, int y, int width, int height, int rotation, int flags);

        public static List<SourceVisual> Read(string file, string folder, string relativeFolder, string sourceName, string key, bool slides, CancellationToken cancellation)
        {
            lock (gate)
            {
                if (!initialized)
                {
                    if (LoadLibraryEx(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Native", "pdfium.dll"), IntPtr.Zero, 0x1100) == IntPtr.Zero) throw new IOException("לא ניתן לטעון את קורא PDF. יש להעתיק גם את תיקיית Native עם התוכנה ולהפעיל ב־Windows של 64 סיביות.");
                    FPDF_InitLibrary(); initialized = true;
                }
                byte[] bytes = File.ReadAllBytes(file); var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                IntPtr doc = IntPtr.Zero;
                try
                {
                    doc = FPDF_LoadMemDocument(pinned.AddrOfPinnedObject(), bytes.Length, null);
                    if (doc == IntPtr.Zero) throw new InvalidDataException("לא ניתן לפתוח PDF. קוד קורא PDF: " + FPDF_GetLastError() + ". ייתכן שהקובץ מוגן בסיסמה או אינו תקין.");
                    int count = FPDF_GetPageCount(doc);
                    if (count <= 0 || count > 200) throw new InvalidDataException("נדרש PDF המכיל 1 עד 200 עמודים. קבצים ארוכים יש לפצל.");
                    if (folder != null) Directory.CreateDirectory(folder); var result = new List<SourceVisual>();
                    for (int i = 0; i < count; i++)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        WorkFlow.Activity("מכינים תמונה לקריאה: " + sourceName + " · " + (slides ? "שקופית " : "עמוד ") + (i + 1) + " מתוך " + count);
                        IntPtr page = FPDF_LoadPage(doc, i), textPage = IntPtr.Zero, bitmap = IntPtr.Zero;
                        if (page == IntPtr.Zero) throw new InvalidDataException("לא ניתן לקרוא עמוד " + (i + 1));
                        try
                        {
                            string text = ""; textPage = FPDFText_LoadPage(page);
                            if (textPage != IntPtr.Zero)
                            {
                                int chars = FPDFText_CountChars(textPage);
                                if (chars < 0 || chars > 2000000) throw new InvalidDataException("כמות הטקסט בעמוד אינה נתמכת.");
                                if (chars > 0) { var buffer = new byte[(chars + 1) * 2]; int copied = FPDFText_GetText(textPage, 0, chars, buffer); text = Encoding.Unicode.GetString(buffer, 0, Math.Max(0, copied - 1) * 2); }
                            }
                            if (folder == null) { result.Add(new SourceVisual { PageNumber = i + 1, Text = text }); continue; }
                            double w = FPDF_GetPageWidth(page), h = FPDF_GetPageHeight(page);
                            if (w <= 0 || h <= 0 || Double.IsNaN(w + h) || Double.IsInfinity(w + h)) throw new InvalidDataException("מידות עמוד PDF אינן תקינות.");
                            double scale = 2000.0 / Math.Max(w, h); int width = Math.Max(1, (int)Math.Round(w * scale)), height = Math.Max(1, (int)Math.Round(h * scale));
                            bitmap = FPDFBitmap_Create(width, height, 1);
                            if (bitmap == IntPtr.Zero) throw new IOException("לא ניתן להקצות זיכרון לתמונת העמוד.");
                            FPDFBitmap_FillRect(bitmap, 0, 0, width, height, 0xffffffff);
                            FPDF_RenderPageBitmap(bitmap, page, 0, 0, width, height, 0, 1);
                            var pixels = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, FPDFBitmap_GetBuffer(bitmap), FPDFBitmap_GetStride(bitmap) * height, FPDFBitmap_GetStride(bitmap));
                            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(pixels));
                            string name = String.Format("page-{0:D4}.png", i + 1), path = Path.Combine(folder, name);
                            using (var output = File.Create(path)) encoder.Save(output);
                            result.Add(new SourceVisual { Id = key + "-p" + (i + 1), FileName = sourceName, PageNumber = i + 1, Label = (slides ? "שקופית " : "עמוד ") + (i + 1), RelativePath = relativeFolder + "/" + name, Text = text, ImageHash = SourceImages.Digest(path) });
                        }
                        finally { if (bitmap != IntPtr.Zero) FPDFBitmap_Destroy(bitmap); if (textPage != IntPtr.Zero) FPDFText_ClosePage(textPage); FPDF_ClosePage(page); }
                    }
                    return result;
                }
                finally { if (doc != IntPtr.Zero) FPDF_CloseDocument(doc); pinned.Free(); }
            }
        }
    }
}
