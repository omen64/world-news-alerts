param([string]$OutputDirectory = 'artifacts/publish')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    Push-Location (Join-Path $projectRoot 'frontend')
    try { npm ci; if ($LASTEXITCODE -ne 0) { throw 'npm ci failed' }; npm run build; if ($LASTEXITCODE -ne 0) { throw 'Angular build failed' } }
    finally { Pop-Location }
    $webRoot = Join-Path $projectRoot 'backend/src/WorldNewsAlerts.Api/wwwroot'
    New-Item -ItemType Directory -Path $webRoot -Force | Out-Null
    Copy-Item -Path (Join-Path $projectRoot 'frontend/dist/frontend/browser/*') -Destination $webRoot -Recurse -Force
    dotnet publish backend/src/WorldNewsAlerts.Api -c Release -o $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Backend publish failed' }
}
finally { Pop-Location }
