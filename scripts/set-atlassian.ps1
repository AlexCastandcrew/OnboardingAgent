<#
.SYNOPSIS
    Stores the Atlassian API token (encrypted) and the account email for the Confluence sync.

.DESCRIPTION
    - The API token is read with hidden input and saved encrypted with your Windows account (DPAPI) in
      %LOCALAPPDATA%\OnboardingAgent\atlassian-api-token.txt. Only you, on this machine, can decrypt it.
    - The account email is not secret; it goes in the API's user secrets (Atlassian:Email).
    - An admin must have enabled API token authentication for the Atlassian Rovo MCP server.
    Leave a prompt empty to keep the current value. ./scripts/start.ps1 decrypts the token for the API process only.

.EXAMPLE
    ./scripts/set-atlassian.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/common.ps1"

$secretsId = ([xml](Get-Content (Join-Path $ApiProject 'OnboardingAgent.Api.csproj') -Raw)).Project.PropertyGroup.UserSecretsId |
    Where-Object { $_ } | Select-Object -First 1
if (-not $secretsId) { throw 'The API project has no UserSecretsId.' }

$folder = Join-Path $env:APPDATA "Microsoft\UserSecrets\$secretsId"
$file = Join-Path $folder 'secrets.json'
$secrets = [ordered]@{}
if (Test-Path $file) {
    $existing = [IO.File]::ReadAllText($file) | ConvertFrom-Json
    if ($existing) { $existing.PSObject.Properties | ForEach-Object { $secrets[$_.Name] = $_.Value } }
}

$current = $secrets['Atlassian:Email']
$shown = if ($current) { " [$current]" } else { '' }
$email = Read-Host "Atlassian account email$shown"
if ($email) { $secrets['Atlassian:Email'] = $email.Trim() }

$hasToken = [bool](Test-Path $AtlassianTokenFile)
$prompt = if ($hasToken) { 'Atlassian API token (hidden; empty keeps the saved one)' } else { 'Atlassian API token (hidden)' }
$secure = Read-Host $prompt -AsSecureString
if ($secure.Length -gt 0) {
    Save-AtlassianToken $secure
    $hasToken = $true
}
$secure.Dispose()

# Never keep the token in plain text.
$secrets.Remove('Atlassian:ApiToken')

New-Item -ItemType Directory -Force $folder | Out-Null
# Depth 10: the JWT signing keys from dotnet user-jwts are a nested array that the default depth would flatten.
[IO.File]::WriteAllText($file, ($secrets | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))

Write-Host "API token saved encrypted: $hasToken ($AtlassianTokenFile). Email: $($secrets['Atlassian:Email'])."
Write-Host 'Start the API with ./scripts/start.ps1, then run: dotnet run --project src/OnboardingAgent.Api --no-launch-profile -- sync-confluence'
