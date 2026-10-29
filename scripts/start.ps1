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
# The clipboard is only a convenience for Swagger. Another program can hold it (Set-Clipboard then throws), and that must
# not stop the API from starting; the web app (start-web.ps1) gets its own token and does not need the clipboard.
try {
    Set-Clipboard -Value $token.Token
    Write-Host "Dev token '$($token.Name)' (id $($token.Id), expires $($token.Expires)) is on the clipboard. In Swagger: Authorize, then paste."
}
catch {
    Write-Warning "Could not copy the dev token to the clipboard ($($_.Exception.Message)). The API starts anyway. For Swagger, get a token with: dotnet user-jwts create --project src/OnboardingAgent.Api"
}

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
# It goes in the AWS SDK's own AWS_BEARER_TOKEN_BEDROCK variable, not in Bedrock__ApiKey: on one machine (2026-10-29) the
# explicit token provider that Bedrock__ApiKey enables got 403 UnrecognizedClientException with a valid key, while this
# variable worked. /api/health then reports "auth": "awsCredentials" even though the key is in use.
$previousKey = $env:AWS_BEARER_TOKEN_BEDROCK
# The optional Atlassian token (Confluence sync) is passed the same way; without it the API serves the markdown corpus.
$atlassianToken = Get-AtlassianToken
$previousAtlassian = $env:Atlassian__ApiToken
try {
    if ($apiKey) { $env:AWS_BEARER_TOKEN_BEDROCK = $apiKey.Trim() }
    $apiKey = $null
    if ($atlassianToken) { $env:Atlassian__ApiToken = $atlassianToken.Trim() }
    $atlassianToken = $null
    & dotnet run --project $ApiProject --launch-profile http --no-build
}
finally {
    $env:AWS_BEARER_TOKEN_BEDROCK = $previousKey
    $env:Atlassian__ApiToken = $previousAtlassian
}
