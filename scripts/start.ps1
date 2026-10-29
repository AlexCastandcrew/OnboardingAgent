<#
.SYNOPSIS
    Starts the Onboarding Agent API on http://localhost:5006 for development or the demo.

.DESCRIPTION
    1. Checks the .NET SDK pinned in global.json and that port 5006 is free.
    2. Builds the API.
    3. Makes sure a local dev token exists and copies it to the clipboard (it is never printed).
    4. Warns when the Bedrock model ids or AWS credentials are missing (the API still starts; chat returns 502).
    5. Runs the API with the http launch profile. Ctrl+C stops it.

.EXAMPLE
    ./scripts/start.ps1 -AwsProfile hackathon
#>
[CmdletBinding()]
param(
    # AWS profile for Bedrock (from `aws configure sso`). Defaults to the current AWS_PROFILE.
    [string]$AwsProfile,

    # Skip the build, for a quick restart.
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/common.ps1"

Assert-DotnetSdk

$listener = Get-NetTCPConnection -State Listen -LocalPort $ApiPort -ErrorAction SilentlyContinue | Select-Object -First 1
if ($listener) {
    $owner = Get-Process -Id $listener.OwningProcess -ErrorAction SilentlyContinue
    throw "Port $ApiPort is already used by $($owner.ProcessName) (PID $($listener.OwningProcess)). Stop it (for example the Visual Studio session) and run this again."
}

if (-not $NoBuild) {
    & dotnet build $ApiProject -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}

$token = Get-DevToken
Set-Clipboard -Value $token.Token
Write-Host "Dev token '$($token.Name)' (id $($token.Id), expires $($token.Expires)) is on the clipboard. In Swagger: Authorize, then paste."

$secrets = (& dotnet user-secrets list --project $ApiProject 2>$null) -join "`n"
foreach ($key in 'Bedrock:AnswerModelId', 'Bedrock:AuxModelId') {
    $envName = $key.Replace(':', '__')
    if ($secrets -notmatch [regex]::Escape($key) -and -not [Environment]::GetEnvironmentVariable($envName)) {
        Write-Warning "$key is not set; /api/chat will return 502. See README > Run > Bedrock."
    }
}

$apiKey = Get-BedrockKey
$hasApiKey = $apiKey -or $env:Bedrock__ApiKey -or $env:AWS_BEARER_TOKEN_BEDROCK
if ($AwsProfile) {
    $env:AWS_PROFILE = $AwsProfile
}
elseif (-not $hasApiKey -and -not $env:AWS_PROFILE -and -not $env:AWS_ACCESS_KEY_ID) {
    Write-Warning 'No Bedrock API key and no AWS credentials; Bedrock calls will fail. Run ./scripts/set-bedrock.ps1 or pass -AwsProfile <name>.'
}

Write-Host "Swagger: http://localhost:$ApiPort/swagger"
# The decrypted key lives only in this process's environment, which the API inherits, and is removed on exit.
$previousKey = $env:Bedrock__ApiKey
try {
    if ($apiKey) { $env:Bedrock__ApiKey = $apiKey }
    $apiKey = $null
    & dotnet run --project $ApiProject --launch-profile http --no-build
}
finally {
    $env:Bedrock__ApiKey = $previousKey
}
