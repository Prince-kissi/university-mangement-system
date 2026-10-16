[CmdletBinding()]
param(
    [switch]$NoBrowser,
    [switch]$SkipBuild,
    [string]$ApiBaseUrl = "http://localhost:8080"
)

$ErrorActionPreference = "Stop"
$projectRoot = $PSScriptRoot
$backendPath = Join-Path $projectRoot "backend"
$composePath = Join-Path $backendPath "docker-compose.yml"
$dockerDesktopPath = "C:\Program Files\Docker\Docker\Docker Desktop.exe"
$applicationUrl = "http://localhost:8080/homepage.html"
$apiHealthUrl = "$ApiBaseUrl/api/health"

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Docker Desktop is required. Install Docker Desktop, then run this launcher again."
}

if (-not (Test-Path (Join-Path $backendPath ".env"))) {
    throw "backend\.env is missing. Copy backend\.env.example to backend\.env and configure its values."
}

$runtimeConfig = @{ apiBaseUrl = $ApiBaseUrl } | ConvertTo-Json
[System.IO.File]::WriteAllText((Join-Path $projectRoot "runtime-config.json"), "$runtimeConfig`n")

try {
    docker info *> $null
} catch {
    if (-not (Test-Path $dockerDesktopPath)) {
        throw "Docker is installed but its service is not running, and Docker Desktop could not be found."
    }

    Write-Host "Starting Docker Desktop..."
    Start-Process -FilePath $dockerDesktopPath -WindowStyle Hidden
    $dockerReady = $false
    for ($attempt = 1; $attempt -le 60; $attempt++) {
        Start-Sleep -Seconds 2
        docker info *> $null
        if ($LASTEXITCODE -eq 0) {
            $dockerReady = $true
            break
        }
    }
    if (-not $dockerReady) {
        throw "Docker Desktop did not become ready within two minutes. Open Docker Desktop and check its status."
    }
}

if (-not $SkipBuild) {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw ".NET 10 SDK is required to build the backend API."
    }
    Write-Host "Building the backend API..."
    & dotnet publish (Join-Path $backendPath "src\ComplaintSystem.Api\ComplaintSystem.Api.csproj") `
        --configuration Release --output (Join-Path $backendPath "docker-publish")
    if ($LASTEXITCODE -ne 0) {
        throw "The backend API build failed. Review the build errors above."
    }
}

Write-Host "Starting PostgreSQL, Redis, API, frontend, and database viewer..."
$composeArguments = @("compose", "--file", $composePath, "up", "--detach")
if (-not $SkipBuild) {
    $composeArguments += "--build"
}
& docker @composeArguments
if ($LASTEXITCODE -ne 0) {
    throw "The UMaT backend could not be started. Run 'docker compose --file backend/docker-compose.yml logs' for details."
}

Write-Host "Waiting for the secure service to become ready..."
$serviceReady = $false
for ($attempt = 1; $attempt -le 90; $attempt++) {
    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri $apiHealthUrl -TimeoutSec 3
        if ($response.StatusCode -eq 200) {
            $serviceReady = $true
            break
        }
    } catch {
        Start-Sleep -Seconds 2
    }
}

if (-not $serviceReady) {
    docker compose --file $composePath ps
    throw "The containers started, but the backend did not become healthy within three minutes."
}

Write-Host "UMaT Complaint Management is ready at $applicationUrl"
if (-not $NoBrowser) {
    Start-Process $applicationUrl
}
