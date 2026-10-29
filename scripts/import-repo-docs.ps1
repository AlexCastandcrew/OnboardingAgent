<#
.SYNOPSIS
    Copies the htg-server documents listed in corpus/sources/repo-docs.json into corpus/repo/ at one git commit,
    scans them for personal data, and lists them in corpus/manifest.pending.json (NOT in manifest.json).

.DESCRIPTION
    Files are read from git at -Ref (never from the working copy), so each source has a fixed commit and a GitHub
    URL to that commit. Nothing is indexed or sent to Bedrock: ingestion only reads corpus/manifest.json. Once the
    documents are approved and reviewed, run ./scripts/promote-corpus.ps1.

    The personal-data scan flags emails, @mentions, phone and SSN-like numbers, personal SharePoint/OneDrive links
    and local user paths. The findings go to .data/import-report.md (gitignored, because they quote what was
    found). Review every finding by hand, plus person names, which the scan cannot detect.

.EXAMPLE
    ./scripts/import-repo-docs.ps1 -RepoPath "C:\Projects\htg-server"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$RepoPath,
    [string]$Ref = 'origin/majorfeature_2.0'
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/common.ps1"

$corpus = Join-Path $Root 'corpus'
# ReadAllText reads UTF-8; Get-Content in Windows PowerShell 5.1 would read these BOM-less files as ANSI.
$sources = [IO.File]::ReadAllText((Join-Path $corpus 'sources/repo-docs.json')) | ConvertFrom-Json
$commit = (& git -C $RepoPath rev-parse --verify "$Ref^{commit}").Trim()
if ($LASTEXITCODE -ne 0) { throw "Ref '$Ref' not found in $RepoPath. Run git fetch first." }
$commitDate = (& git -C $RepoPath log -1 --format=%cs $commit).Trim()
$shortCommit = $commit.Substring(0, 10)

# Raw bytes from git, so UTF-8 survives Windows PowerShell 5.1.
function Get-GitFile([string]$Path) {
    $psi = [Diagnostics.ProcessStartInfo]::new('git', "-C `"$RepoPath`" show `"${commit}:$Path`"")
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $process = [Diagnostics.Process]::Start($psi)
    $buffer = [IO.MemoryStream]::new()
    $process.StandardOutput.BaseStream.CopyTo($buffer)
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "git show ${commit}:$Path failed: $($process.StandardError.ReadToEnd())" }
    return [Text.UTF8Encoding]::new($false).GetString($buffer.ToArray()).TrimStart([char]0xFEFF)
}

$patterns = [ordered]@{
    'email'            = '[\w.+-]+@[\w-]+\.[\w.-]+'
    '@mention'         = '(?<![\w.])@[A-Za-z][\w-]{2,}'
    'phone'            = '\+?\d{1,2}[\s.-]?\(?\d{3}\)?[\s.-]\d{3}[\s.-]\d{4}\b'
    'ssn-like'         = '\b\d{3}-\d{2}-\d{4}\b'
    'personal link'    = 'https?://[\w.-]*(sharepoint\.com/personal|-my\.sharepoint\.com|onedrive\.live\.com)[^\s)]*'
    'local user path'  = '[A-Za-z]:\\Users\\[^\\\s]+'
}

$outFolder = Join-Path $corpus 'repo'
New-Item -ItemType Directory -Force $outFolder | Out-Null
$entries = @()
$report = [Text.StringBuilder]::new()
[void]$report.AppendLine("# Personal-data scan, $($sources.repoUrl) at $shortCommit ($Ref, $commitDate)").AppendLine()
$flagged = 0

$redactions = @(([IO.File]::ReadAllText((Join-Path $corpus 'sources/redactions.json')) | ConvertFrom-Json).redactions)
$applied = 0

foreach ($doc in $sources.documents) {
    $text = Get-GitFile $doc.path
    foreach ($redaction in $redactions | Where-Object { $_.documentId -eq $doc.documentId }) {
        if (-not $text.Contains($redaction.find)) {
            throw "Redaction for $($doc.documentId) no longer matches the source at $shortCommit ('$($redaction.find)'). Review the document and update corpus/sources/redactions.json."
        }

        $text = $text.Replace($redaction.find, $redaction.replace)
        $applied++
    }

    $title = ([regex]::Match($text, '(?m)^#\s+(.+?)\s*$')).Groups[1].Value
    if (-not $title) { $title = [IO.Path]::GetFileNameWithoutExtension($doc.path) }

    # Design documents name their ticket only in the file name (H2G-7165-...); put it in the title, the most
    # heavily weighted search field, so a question about H2G-7165 finds its design.
    $ticket = ([regex]::Match([IO.Path]::GetFileName($doc.path), '^H2G-\d+')).Value
    if ($ticket -and -not $title.Contains($ticket)) { $title = "$ticket $([char]0x2014) $title" }

    $findings = @()
    foreach ($name in $patterns.Keys) {
        foreach ($match in [regex]::Matches($text, $patterns[$name])) {
            $line = ($text.Substring(0, $match.Index) -split "`n").Count
            $findings += "- line ${line}, ${name}: ``$($match.Value)``"
        }
    }

    if ($findings.Count -gt 0) {
        $flagged++
        [void]$report.AppendLine("## $($doc.documentId) ($($doc.path))").AppendLine()
        $findings | ForEach-Object { [void]$report.AppendLine($_) }
        [void]$report.AppendLine()
    }

    [IO.File]::WriteAllText((Join-Path $outFolder "$($doc.documentId).md"), $text, [Text.UTF8Encoding]::new($false))
    $entries += [ordered]@{
        documentId      = $doc.documentId
        path            = "repo/$($doc.documentId).md"
        title           = $title
        sourceType      = 'repo'
        sourceUrl       = "$($sources.repoUrl)/blob/$commit/$($doc.path)"
        sourceVersion   = $shortCommit
        sourceUpdatedAt = $commitDate
        domains         = @($doc.domains)
        initiatives     = @(if ($doc.initiatives) { $doc.initiatives } else { @() })
    }
    Write-Host ("{0,-36} {1,3} finding(s)  {2}" -f $doc.documentId, $findings.Count, $title)
}

$pending = [ordered]@{
    '_comment' = "Imported from $Ref at $shortCommit. Not indexed: run ./scripts/promote-corpus.ps1 after approval and review."
    projectId  = 'htg'
    documents  = $entries
}
[IO.File]::WriteAllText((Join-Path $corpus 'manifest.pending.json'), ($pending | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))

$reportPath = Join-Path $Root '.data/import-report.md'
New-Item -ItemType Directory -Force (Split-Path $reportPath) | Out-Null
if ($flagged -eq 0) { [void]$report.AppendLine('No findings. Still review person names by hand.') }
[IO.File]::WriteAllText($reportPath, $report.ToString(), [Text.UTF8Encoding]::new($false))

Write-Host "$($entries.Count) documents in corpus/manifest.pending.json; $applied redactions applied; $flagged with findings after them. Report: $reportPath"
