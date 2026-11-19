# PDF reader runtime

`pdfium.dll` is the Windows x64 PDFium library redistributed from the
`pypdfium2-5.15.2-py3-none-win_amd64.whl` distribution on PyPI.
PDFium version: 159.0.8149.0. The C# application calls the native API directly;
Python and the pypdfium2 Python wrapper are not required at runtime.

SHA-256: `42D87F68957D745CE9377DFB1D8D63691E19082A7EC29FF421D22779D3142B29`.

Upstream: https://pdfium.googlesource.com/pdfium/
Binary distribution: https://pypi.org/project/pypdfium2/5.15.2/
All redistributed license notices are included in `Licenses`.
Keep this directory alongside `Education2k.exe` when copying the application.

PDFium access is serialized because its API is not thread safe. The application
opens documents from pinned memory and always releases page, text and bitmap
handles. It does not execute PDF JavaScript or embedded attachments.
