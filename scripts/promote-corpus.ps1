<#
.SYNOPSIS
    Moves the reviewed documents from corpus/manifest.pending.json into corpus/manifest.json, so the next start
    (or POST /api/admin/reindex) indexes them and they can be sent to Bedrock.

.DESCRIPTION
    Run it only after the documents are approved for Bedrock and the personal-data findings are resolved.
    Entries replace any with the same documentId; the pending file is removed.

.EXAMPLE
    ./scripts/promote-corpus.ps1
#>
[CmdletBinding(SupportsShouldProcess)]
param()

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/common.ps1"

$corpus = Join-Path $Root 'corpus'
$pendingPath = Join-Path $corpus 'manifest.pending.json'
if (-not (Test-Path $pendingPath)) { throw 'Nothing to promote: corpus/manifest.pending.json does not exist.' }

$manifestPath = Join-Path $corpus 'manifest.json'
# ReadAllText reads UTF-8; Get-Content in Windows PowerShell 5.1 would mangle titles such as with an em dash.
$manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
$pending = [IO.File]::ReadAllText($pendingPath) | ConvertFrom-Json
$ids = @($pending.documents | ForEach-Object { $_.documentId })

$documents = @($manifest.documents | Where-Object { $ids -notcontains $_.documentId }) + @($pending.documents)
$result = [ordered]@{ projectId = $manifest.projectId; documents = $documents }

if ($PSCmdlet.ShouldProcess($manifestPath, "add $($ids.Count) documents")) {
    [IO.File]::WriteAllText($manifestPath, ($result | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
    Remove-Item $pendingPath
    Write-Host "Promoted $($ids.Count) documents. Restart the API or call POST /api/admin/reindex to index them."
}
