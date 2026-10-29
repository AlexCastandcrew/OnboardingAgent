<#
.SYNOPSIS
    Stores the Bedrock API key (encrypted) and the region and model ids for local development.

.DESCRIPTION
    - The API key is read with hidden input and saved encrypted with your Windows account (DPAPI) in
      %LOCALAPPDATA%\OnboardingAgent\bedrock-api-key.txt. Only you, on this machine, can decrypt it. It never
      appears on screen, in the shell history, on a command line or in plain text on disk.
    - The region and model ids are not secret; they go in the API's user secrets.
    - A plain-text Bedrock:ApiKey left in user secrets by an earlier version of this script is removed.
    Leave a prompt empty to keep the current value. ./scripts/start.ps1 decrypts the key for the API process only.

.EXAMPLE
    ./scripts/set-bedrock.ps1
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

function Read-Setting([string]$Key, [string]$Prompt) {
    $current = $secrets[$Key]
    $shown = if ($current) { " [$current]" } else { '' }
    $value = Read-Host "$Prompt$shown"
    if ($value) { $secrets[$Key] = $value.Trim() }
}

$hasKey = [bool](Test-Path $BedrockKeyFile)
$prompt = if ($hasKey) { 'Bedrock API key (hidden; empty keeps the saved one)' } else { 'Bedrock API key (hidden)' }
$secure = Read-Host $prompt -AsSecureString
if ($secure.Length -gt 0) {
    Save-BedrockKey $secure
    $hasKey = $true
}
$secure.Dispose()

Read-Setting 'Bedrock:Region' 'Region, for example us-east-1'
Read-Setting 'Bedrock:AnswerModelId' 'Answer model or inference profile id (Sonnet)'
Read-Setting 'Bedrock:AuxModelId' 'Rewrite/rerank model or inference profile id (Haiku)'

# Never keep the key in plain text.
$secrets.Remove('Bedrock:ApiKey')

New-Item -ItemType Directory -Force $folder | Out-Null
# Depth 10: the JWT signing keys from dotnet user-jwts are a nested array that the default depth would flatten.
[IO.File]::WriteAllText($file, ($secrets | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))

Write-Host "API key saved encrypted: $hasKey ($BedrockKeyFile)."
Write-Host "Region: $($secrets['Bedrock:Region']). Answer model: $($secrets['Bedrock:AnswerModelId']). Aux model: $($secrets['Bedrock:AuxModelId'])."
Write-Host 'Start the API with ./scripts/start.ps1, then check /api/health: models.auth should be apiKey and configured true.'
