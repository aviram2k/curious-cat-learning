$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $frameworkDir 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework 4.x compiler is required on Windows.' }
if (!(Test-Path -LiteralPath (Join-Path $projectRoot 'Native\pdfium.dll'))) { throw 'The bundled Windows x64 PDFium library in Native/pdfium.dll is required.' }
$outputDir = Join-Path $projectRoot 'Build'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$releaseVersion = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'VERSION')).Trim()
if ($releaseVersion -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw 'VERSION must contain MAJOR.MINOR.PATCH.' }
$versionParts = $releaseVersion.Split('.')
foreach ($versionPart in $versionParts) { if ([long]::Parse($versionPart) -gt 65534) { throw 'Version components must fit the Windows file version format (0-65534).' } }
$versionDate = [System.IO.File]::ReadAllText((Join-Path $projectRoot 'VERSION_DATE')).Trim()
[datetimeoffset]::ParseExact($versionDate, "yyyy-MM-dd'T'HH:mm:sszzz", [System.Globalization.CultureInfo]::InvariantCulture) | Out-Null
$assemblyInfoPath = Join-Path $outputDir 'AssemblyInfo.g.cs'
$assemblyInfo = @"
using System.Reflection;
[assembly: AssemblyTitle(Education2k.AppInfo.Name)]
[assembly: AssemblyProduct(Education2k.AppInfo.Name)]
[assembly: AssemblyDescription(Education2k.AppInfo.Slogan)]
[assembly: AssemblyVersion("$releaseVersion.0")]
[assembly: AssemblyFileVersion("$releaseVersion.0")]
[assembly: AssemblyInformationalVersion("$releaseVersion")]
[assembly: AssemblyMetadata("VersionDate", "$versionDate")]
"@
[System.IO.File]::WriteAllText($assemblyInfoPath, $assemblyInfo, [System.Text.UTF8Encoding]::new($false))
& (Join-Path $projectRoot 'Tools\build-icon.ps1')
$iconPath = Join-Path $projectRoot 'Assets\app-icon.ico'
$references = @('System.dll','System.Core.dll','System.Xml.dll','System.Xml.Linq.dll','System.Web.Extensions.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','System.Xaml.dll','Microsoft.CSharp.dll')
$compilerArgs = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/codepage:65001',('/out:' + (Join-Path $projectRoot 'Education2k.exe')),('/resource:' + (Join-Path $projectRoot 'Source\Theme.xaml') + ',Education2k.Theme.xaml'))
$compilerArgs += '/win32icon:' + $iconPath
$compilerArgs += '/resource:' + $iconPath + ',Education2k.AppIcon.ico'
$compilerArgs += '/resource:' + (Join-Path $projectRoot 'Assets\app-icon.png') + ',Education2k.AppLogo.png'
foreach ($logoAnimationFile in Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Assets\LogoAnimation') -File | Where-Object { $_.Extension -eq '.png' -or $_.Name -eq 'frames.json' }) {
    $compilerArgs += '/resource:' + $logoAnimationFile.FullName + ',Education2k.LogoAnimation.' + $logoAnimationFile.Name
}
foreach ($reference in $references) { $compilerArgs += '/reference:' + (Join-Path $frameworkDir $reference) }
$compilerArgs += '/reference:' + (Join-Path $frameworkDir 'WPF\PresentationCore.dll')
$compilerArgs += '/reference:' + (Join-Path $frameworkDir 'WPF\WindowsBase.dll')
$compilerArgs += '/reference:' + (Join-Path $frameworkDir 'WPF\PresentationFramework.dll')
$compilerArgs += '/reference:' + (Join-Path $frameworkDir 'WPF\ReachFramework.dll')
$compilerArgs += '/reference:' + (Join-Path $frameworkDir 'WPF\System.Printing.dll')
$compilerArgs += (Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Source') -Filter '*.cs').FullName
$compilerArgs += $assemblyInfoPath
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Write-Output ('Built: ' + (Join-Path $projectRoot 'Education2k.exe'))
$codexCommand = Get-Command codex.exe -ErrorAction SilentlyContinue
if ($codexCommand) { [System.IO.File]::WriteAllText((Join-Path $projectRoot 'codex-location.txt'), $codexCommand.Source) }
if ($args -contains '-Test') {
    $testProcess = Start-Process -FilePath (Join-Path $projectRoot 'Education2k.exe') -ArgumentList '--test' -PassThru -Wait -WindowStyle Hidden
    Get-Content -LiteralPath (Join-Path $outputDir 'tests.txt')
    if ($testProcess.ExitCode -ne 0) { throw 'Tests failed.' }
}
