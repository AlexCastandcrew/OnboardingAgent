<#
.SYNOPSIS
    Runs the 15 evaluation cases against a running API and writes the report to eval/results.

.DESCRIPTION
    Uses the local dev token (created if needed) through the ONBOARDING_EVAL_TOKEN environment variable, which
    is removed again when the run ends. Start the API first with ./scripts/start.ps1.

.EXAMPLE
    ./scripts/eval.ps1
    ./scripts/eval.ps1 -Runs 1 -Only leader-1,exact-1
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost:5006',
    [int]$Runs = 2,
    [string[]]$Only
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/common.ps1"

Assert-DotnetSdk

$health = Test-ApiHealth $BaseUrl
if (-not $health) { throw "The API does not answer at $BaseUrl/api/health. Start it with ./scripts/start.ps1." }
if (-not $health.models.configured) { Write-Warning 'The API has no Bedrock model ids; every case will fail with 502.' }

$arguments = @('--base-url', $BaseUrl, '--runs', $Runs,
    '--cases', (Join-Path $Root 'eval/cases.json'), '--out', (Join-Path $Root 'eval/results'))
if ($Only) { $arguments += @('--only', ($Only -join ',')) }

$env:ONBOARDING_EVAL_TOKEN = (Get-DevToken).Token
try {
    & dotnet run --project (Join-Path $Root 'tools/OnboardingAgent.Evaluation') -- @arguments
    exit $LASTEXITCODE
}
finally {
    Remove-Item Env:ONBOARDING_EVAL_TOKEN -ErrorAction SilentlyContinue
}
