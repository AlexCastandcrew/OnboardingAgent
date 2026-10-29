# Shared helpers for start.ps1 and eval.ps1. Dot-source it: . "$PSScriptRoot/common.ps1"

$script:Root = Split-Path -Parent $PSScriptRoot
$script:ApiProject = Join-Path $script:Root 'src/OnboardingAgent.Api'
$script:ApiPort = 5006
$script:TokenName = 'onboarding-dev'

function Assert-DotnetSdk {
    $required = (Get-Content (Join-Path $script:Root 'global.json') -Raw | ConvertFrom-Json).sdk.version
    $major = $required.Split('.')[0]
    $installed = & dotnet --list-sdks 2>$null
    if (-not ($installed | Where-Object { $_ -match "^$major\." })) {
        throw ".NET SDK $required (or a later $major.x) is required. Install it from https://dotnet.microsoft.com/download."
    }
}

# Returns a local dev JWT for the API, creating one with dotnet user-jwts when none is valid for at least an hour.
# The token is a credential: callers copy it or pass it through the environment, never print it.
function Get-DevToken {
    $tokens = @()
    try {
        $listed = (& dotnet user-jwts list --project $script:ApiProject --output json 2>$null) -join "`n" | ConvertFrom-Json
        if ($listed) { $tokens = @($listed.PSObject.Properties | ForEach-Object { $_.Value }) }
    }
    catch {
        # No tokens yet: the command prints a message instead of JSON.
    }

    $valid = $tokens |
        Where-Object { $_.Name -eq $script:TokenName -and [datetimeoffset]$_.Expires -gt [datetimeoffset]::Now.AddHours(1) } |
        Select-Object -First 1
    if ($valid) { return $valid }

    Write-Host "Creating a local dev token ($script:TokenName) with dotnet user-jwts..."
    $created = (& dotnet user-jwts create --project $script:ApiProject --name $script:TokenName --output json) -join "`n" | ConvertFrom-Json
    if (-not $created.Token) { throw "dotnet user-jwts did not return a token." }
    return $created
}

# The Bedrock API key is stored encrypted with Windows DPAPI (ConvertFrom-SecureString without -Key): only the same
# Windows user on the same machine can decrypt it. Nothing is stored in plain text.
$script:BedrockKeyFile = Join-Path $env:LOCALAPPDATA 'OnboardingAgent\bedrock-api-key.txt'

function Save-BedrockKey([securestring]$Key, [string]$Path = $script:BedrockKeyFile) {
    New-Item -ItemType Directory -Force (Split-Path $Path) | Out-Null
    [IO.File]::WriteAllText($Path, (ConvertFrom-SecureString $Key))
}

# Returns the decrypted key, or $null when none is stored. Callers pass it only to the API process.
function Get-BedrockKey([string]$Path = $script:BedrockKeyFile) {
    if (-not (Test-Path $Path)) { return $null }
    try {
        $secure = ConvertTo-SecureString ([IO.File]::ReadAllText($Path).Trim()) -ErrorAction Stop
    }
    catch {
        throw "Cannot decrypt '$Path'. It was saved by another Windows user or machine; run ./scripts/set-bedrock.ps1 again."
    }

    return [System.Net.NetworkCredential]::new('', $secure).Password
}

function Test-ApiHealth([string]$BaseUrl) {
    try {
        Invoke-RestMethod "$BaseUrl/api/health" -TimeoutSec 3
    }
    catch {
        $null
    }
}

# The Atlassian API token is stored like the Bedrock key: encrypted with Windows DPAPI, never in plain text.
$script:AtlassianTokenFile = Join-Path $env:LOCALAPPDATA 'OnboardingAgent\atlassian-api-token.txt'

function Save-AtlassianToken([securestring]$Token, [string]$Path = $script:AtlassianTokenFile) {
    New-Item -ItemType Directory -Force (Split-Path $Path) | Out-Null
    [IO.File]::WriteAllText($Path, (ConvertFrom-SecureString $Token))
}

# Returns the decrypted token, or $null when none is stored. Callers pass it only to the API process.
function Get-AtlassianToken([string]$Path = $script:AtlassianTokenFile) {
    if (-not (Test-Path $Path)) { return $null }
    try {
        $secure = ConvertTo-SecureString ([IO.File]::ReadAllText($Path).Trim()) -ErrorAction Stop
    }
    catch {
        throw "Cannot decrypt '$Path'. It was saved by another Windows user or machine; run ./scripts/set-atlassian.ps1 again."
    }

    return [System.Net.NetworkCredential]::new('', $secure).Password
}
