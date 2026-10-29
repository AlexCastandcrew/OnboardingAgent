# Starts the Angular app against the running API (scripts/start.ps1). The dev token goes only into this process's
# environment, where the dev-server proxy adds it to /api calls; it is never printed.
# -Mock runs the app on fixtures, without the API.
param(
    [switch]$Mock
)

. "$PSScriptRoot/common.ps1"

$web = Join-Path $script:Root 'web'
if (-not (Test-Path (Join-Path $web 'node_modules'))) {
    Write-Host 'Installing web dependencies (npm ci)...'
    Push-Location $web
    try { & npm ci } finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' }
}

Push-Location $web
try {
    if ($Mock) {
        & npx ng serve --configuration mock
    }
    else {
        $health = Test-ApiHealth "http://localhost:$script:ApiPort"
        if (-not $health) {
            throw "The API is not answering on port $script:ApiPort. Start it with scripts/start.ps1 or use -Mock."
        }

        $env:ONBOARDING_TOKEN = (Get-DevToken).Token
        $env:ONBOARDING_API_URL = "http://localhost:$script:ApiPort"
        & npx ng serve
    }
}
finally {
    Remove-Item Env:ONBOARDING_TOKEN -ErrorAction SilentlyContinue
    Pop-Location
}
