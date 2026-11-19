[CmdletBinding()]
param([switch]$ApprovedUpload)

$ErrorActionPreference = 'Stop'
if (!$ApprovedUpload) {
    throw 'Explicit owner approval for this upload is required. After obtaining it, run with -ApprovedUpload. This script does not upload anything.'
}

$versionPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'VERSION'
$currentVersion = [System.IO.File]::ReadAllText($versionPath).Trim()
if ($currentVersion -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw 'VERSION must contain exactly MAJOR.MINOR.PATCH, for example 0.1.0.'
}

$majorPart = $Matches[1]
$minorPart = $Matches[2]
$patchPart = [long]::Parse($Matches[3], [System.Globalization.CultureInfo]::InvariantCulture)
if ($patchPart -eq [long]::MaxValue) { throw 'The patch version cannot be incremented further.' }
$nextVersion = $majorPart + '.' + $minorPart + '.' + ($patchPart + 1).ToString([System.Globalization.CultureInfo]::InvariantCulture)
$versionDatePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'VERSION_DATE'
$releaseTimeZone = [System.TimeZoneInfo]::FindSystemTimeZoneById('Israel Standard Time')
$nextDate = [System.TimeZoneInfo]::ConvertTime([datetimeoffset]::UtcNow, $releaseTimeZone).ToString("yyyy-MM-dd'T'HH:mm:sszzz", [System.Globalization.CultureInfo]::InvariantCulture)
[System.IO.File]::WriteAllText($versionPath, $nextVersion + "`n", [System.Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText($versionDatePath, $nextDate + "`n", [System.Text.UTF8Encoding]::new($false))
Write-Output ('Version: ' + $currentVersion + ' -> ' + $nextVersion + '. No files were committed or uploaded.')
