[CmdletBinding()]
param(
    [string]$SourceRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$SourceRoot = [System.IO.Path]::GetFullPath($SourceRoot)
if (!$OutputDirectory) { $OutputDirectory = Join-Path $SourceRoot 'Build\Releases' }
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$releaseVersion = [System.IO.File]::ReadAllText((Join-Path $SourceRoot 'VERSION')).Trim()
if ($releaseVersion -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw 'Invalid release version.' }
$executable = Join-Path $SourceRoot 'Education2k.exe'
if ([System.Diagnostics.FileVersionInfo]::GetVersionInfo($executable).ProductVersion -ne $releaseVersion) { throw 'Compile the selected version before packaging.' }

$packageName = 'CuriousCatLearning-' + $releaseVersion + '-windows-x64'
$payloadRoot = Join-Path $OutputDirectory $packageName
$zipPath = Join-Path $OutputDirectory ($packageName + '.zip')
if ((Test-Path -LiteralPath $payloadRoot) -or (Test-Path -LiteralPath $zipPath)) { throw 'The package destination already exists. Use a fresh output directory to avoid mixing release files.' }
New-Item -ItemType Directory -Path $payloadRoot -Force | Out-Null

# Explicit runtime allowlist: never copy the working directory or data folders.
foreach ($name in @('Education2k.exe','Education2k.exe.config','VERSION','VERSION_DATE','README.md','LICENSE','CHANGELOG.md')) {
    Copy-Item -LiteralPath (Join-Path $SourceRoot $name) -Destination $payloadRoot
}
$schemaFolder = Join-Path $payloadRoot 'Schemas'
New-Item -ItemType Directory -Path $schemaFolder | Out-Null
foreach ($schema in @('exam.json','grade.json','materials.json','ping.json','presentation.json','visuals.json')) {
    Copy-Item -LiteralPath (Join-Path $SourceRoot ('Schemas\' + $schema)) -Destination $schemaFolder
}
$nativeFolder = Join-Path $payloadRoot 'Native'
New-Item -ItemType Directory -Path $nativeFolder | Out-Null
foreach ($name in @('pdfium.dll','pdfium-version.json','README.md')) {
    Copy-Item -LiteralPath (Join-Path $SourceRoot ('Native\' + $name)) -Destination $nativeFolder
}
Copy-Item -LiteralPath (Join-Path $SourceRoot 'Native\Licenses') -Destination $nativeFolder -Recurse

Compress-Archive -LiteralPath $payloadRoot -DestinationPath $zipPath -CompressionLevel Optimal
$digest = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = Join-Path $OutputDirectory 'SHA256SUMS.txt'
[System.IO.File]::WriteAllText($checksumPath, $digest + '  ' + [System.IO.Path]::GetFileName($zipPath) + "`n", [System.Text.UTF8Encoding]::new($false))
[pscustomobject]@{ Version = $releaseVersion; ZipPath = $zipPath; ChecksumPath = $checksumPath; SHA256 = $digest }
