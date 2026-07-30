# Create / update local IIS sites for Bagly.
# MUST run in elevated PowerShell (Run as Administrator).
#
# Sites created:
#   Bagly.Api  -> http://localhost:8081  (ASP.NET Core)
#   Bagly.Web  -> http://localhost:8080  (React SPA)
#
# Prerequisites:
#   1) IIS enabled (Internet Information Services)
#   2) ASP.NET Core 8 Hosting Bundle
#      https://dotnet.microsoft.com/download/dotnet/8.0  (Hosting Bundle)
#   3) URL Rewrite Module (for SPA routes)
#      https://www.iis.net/downloads/microsoft/url-rewrite
#   4) Run Publish-IIS.ps1 first

#Requires -RunAsAdministrator
$ErrorActionPreference = "Stop"

Import-Module WebAdministration -ErrorAction Stop

$root = Split-Path -Parent $PSScriptRoot
$apiPath = Join-Path $root "publish\iis\api"
$webPath = Join-Path $root "publish\iis\web"
$apiSite = "Bagly.Api"
$webSite = "Bagly.Web"
$apiPool = "BaglyApiAppPool"
$webPool = "BaglyWebAppPool"
$apiPort = 8081
$webPort = 8080

if (-not (Test-Path $apiPath)) {
  throw "API publish folder missing: $apiPath`nRun scripts\Publish-IIS.ps1 first."
}
if (-not (Test-Path $webPath)) {
  throw "Web publish folder missing: $webPath`nRun scripts\Publish-IIS.ps1 first."
}

function Ensure-AppPool([string]$name, [string]$runtime) {
  if (Test-Path "IIS:\AppPools\$name") {
    Write-Host "App pool exists: $name"
  } else {
    Write-Host "Creating app pool: $name"
    New-WebAppPool -Name $name | Out-Null
  }

  Set-ItemProperty "IIS:\AppPools\$name" -Name managedRuntimeVersion -Value $runtime
  Set-ItemProperty "IIS:\AppPools\$name" -Name startMode -Value "AlwaysRunning"
  # No Managed Code for ASP.NET Core / static
  if ($runtime -eq "") {
    Set-ItemProperty "IIS:\AppPools\$name" -Name managedRuntimeVersion -Value ""
  }
}

function Ensure-Site([string]$name, [string]$path, [string]$pool, [int]$port) {
  $existing = Get-Website -Name $name -ErrorAction SilentlyContinue
  if ($existing) {
    Write-Host "Updating site: $name"
    Set-ItemProperty "IIS:\Sites\$name" -Name physicalPath -Value $path
    Set-ItemProperty "IIS:\Sites\$name" -Name applicationPool -Value $pool
  } else {
    Write-Host "Creating site: $name on port $port"
    New-Website -Name $name -Port $port -PhysicalPath $path -ApplicationPool $pool | Out-Null
  }
}

Write-Host "==> Ensuring application pools"
Ensure-AppPool $apiPool ""
Ensure-AppPool $webPool ""

Write-Host "==> Ensuring websites"
Ensure-Site $apiSite $apiPath $apiPool $apiPort
Ensure-Site $webSite $webPath $webPool $webPort

Write-Host "==> Granting SQL access to IIS app pool identity"
$sqlScript = Join-Path $root "backend\scripts\Grant-IisAppPoolSqlAccess.sql"
if (Test-Path $sqlScript) {
  & sqlcmd -S "localhost\SQLEXPRESS" -E -C -i $sqlScript
  if ($LASTEXITCODE -ne 0) {
    Write-Warning "SQL grant failed. Run backend\scripts\Grant-IisAppPoolSqlAccess.sql manually if API returns 500.30."
  }
} else {
  Write-Warning "Missing $sqlScript"
}

# Grant IIS_IUSRS modify on publish folders (logs / temp)
Write-Host "==> Granting IIS_IUSRS modify rights"
icacls $apiPath /grant "IIS_IUSRS:(OI)(CI)M" /T | Out-Null
New-Item -ItemType Directory -Path (Join-Path $apiPath "logs") -Force | Out-Null
icacls (Join-Path $apiPath "logs") /grant "IIS APPPOOL\BaglyApiAppPool:(OI)(CI)M" /T | Out-Null
icacls $webPath /grant "IIS_IUSRS:(OI)(CI)RX" /T | Out-Null

Write-Host "==> Restarting sites"
Start-Website -Name $apiSite -ErrorAction SilentlyContinue
Start-Website -Name $webSite -ErrorAction SilentlyContinue
Restart-WebAppPool -Name $apiPool
Restart-WebAppPool -Name $webPool

Write-Host ""
Write-Host "IIS ready:"
Write-Host "  Storefront : http://localhost:$webPort"
Write-Host "  API        : http://localhost:$apiPort"
Write-Host "  Health     : http://localhost:$apiPort/api/health"
Write-Host ""
Write-Host "SQL Server must be running (localhost\SQLEXPRESS / BaglyDb)."
