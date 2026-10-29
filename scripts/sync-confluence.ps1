<#
.SYNOPSIS
    Runs the Confluence sync (MCP) once, then ingests, and exits. Passes the saved Atlassian token to that process only.

.EXAMPLE
    ./scripts/sync-confluence.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/common.ps1"

Assert-DotnetSdk
$token = Get-AtlassianToken
$previous = $env:Atlassian__ApiToken
try {
    if ($token) { $env:Atlassian__ApiToken = $token.Trim() }
    $token = $null
    & dotnet run --project $ApiProject --no-launch-profile -- sync-confluence
    exit $LASTEXITCODE
}
finally {
    $env:Atlassian__ApiToken = $previous
}
