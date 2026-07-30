# Enable Bagly IIS sites for LAN + Internet access from this PC.
# MUST run as Administrator.
#
# What this does:
#  1) Binds Bagly.Web / Bagly.Api to all interfaces (*:8080 / *:8081)
#  2) Opens Windows Firewall for inbound TCP 8080 and 8081
#  3) Rebuilds frontend to call http://<public-ip>:8081/api
#  4) Republishes API + web and recycles app pools
#
# You still must configure ROUTER port forwarding:
#   External 8080 -> 192.168.x.x:8080
#   External 8081 -> 192.168.x.x:8081

#Requires -RunAsAdministrator
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$publishScript = Join-Path $root "scripts\Publish-IIS.ps1"
$envIis = Join-Path $root "frontend\.env.iis"
$apiPort = 8081
$webPort = 8080
$apiSite = "Bagly.Api"
$webSite = "Bagly.Web"
$apiPool = "BaglyApiAppPool"
$webPool = "BaglyWebAppPool"

Write-Host "==> Detecting addresses"
$lanIp = Get-NetIPAddress -AddressFamily IPv4 |
  Where-Object {
    $_.IPAddress -notlike '127.*' -and
    $_.PrefixOrigin -ne 'WellKnown' -and
    ($_.IPAddress -like '192.168.*' -or $_.IPAddress -like '10.*' -or $_.IPAddress -like '172.*')
  } |
  Select-Object -First 1 -ExpandProperty IPAddress

if (-not $lanIp) {
  throw "Could not detect LAN IP. Connect to Wi-Fi/Ethernet and retry."
}

$publicIp = $null
try {
  $publicIp = (Invoke-RestMethod -Uri "https://api.ipify.org?format=json" -TimeoutSec 10).ip
} catch {
  Write-Host "WARNING: Could not detect public IP automatically."
}

Write-Host "  LAN IP    : $lanIp"
Write-Host "  Public IP : $(if ($publicIp) { $publicIp } else { '(unknown)' })"

# Prefer public IP for internet users; fall back to LAN.
$apiHost = if ($publicIp) { $publicIp } else { $lanIp }
$apiBase = "http://${apiHost}:${apiPort}/api"
Write-Host "  Frontend API URL will be: $apiBase"

Write-Host "==> Updating frontend\.env.iis"
@"
VITE_API_URL=$apiBase
"@ | Set-Content -Path $envIis -Encoding UTF8

Write-Host "==> Publishing app (API + frontend)"
powershell -ExecutionPolicy Bypass -File $publishScript
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

Import-Module WebAdministration -ErrorAction Stop

function Set-SiteBindingAllInterfaces([string]$siteName, [int]$port) {
  $site = Get-Website -Name $siteName -ErrorAction SilentlyContinue
  if (-not $site) {
    throw "IIS site '$siteName' not found. Run scripts\Setup-IIS.ps1 first."
  }

  # Remove existing http bindings for this site, then add *:port:
  Get-WebBinding -Name $siteName -Protocol "http" | ForEach-Object {
    Remove-WebBinding -Name $siteName -BindingInformation $_.bindingInformation -Protocol "http"
  }
  New-WebBinding -Name $siteName -Protocol "http" -Port $port -IPAddress "*"
  Write-Host "  $siteName bound to *: ${port}"
}

Write-Host "==> Updating IIS bindings to all network interfaces"
Set-SiteBindingAllInterfaces $webSite $webPort
Set-SiteBindingAllInterfaces $apiSite $apiPort

Write-Host "==> Creating Windows Firewall rules"
$rules = @(
  @{ Name = "Bagly Web (TCP $webPort)"; Port = $webPort },
  @{ Name = "Bagly API (TCP $apiPort)"; Port = $apiPort }
)
foreach ($rule in $rules) {
  $existing = Get-NetFirewallRule -DisplayName $rule.Name -ErrorAction SilentlyContinue
  if ($existing) {
    Remove-NetFirewallRule -DisplayName $rule.Name -ErrorAction SilentlyContinue
  }
  New-NetFirewallRule `
    -DisplayName $rule.Name `
    -Direction Inbound `
    -Action Allow `
    -Protocol TCP `
    -LocalPort $rule.Port `
    -Profile Any |
    Out-Null
  Write-Host "  Allowed inbound TCP $($rule.Port)"
}

Write-Host "==> Restarting IIS app pools"
foreach ($pool in @($apiPool, $webPool)) {
  $state = (Get-WebAppPoolState -Name $pool).Value
  if ($state -eq "Stopped") {
    Start-WebAppPool -Name $pool
  } else {
    Restart-WebAppPool -Name $pool
  }
}
Start-Website -Name $apiSite -ErrorAction SilentlyContinue
Start-Website -Name $webSite -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "PC-side setup complete."
Write-Host ""
Write-Host "Test on this PC / LAN first:"
Write-Host "  http://${lanIp}:${webPort}/"
Write-Host "  http://${lanIp}:${apiPort}/api/health"
if ($publicIp) {
  Write-Host ""
  Write-Host "Internet URL (after router port forwarding):"
  Write-Host "  http://${publicIp}:${webPort}/"
  Write-Host "  http://${publicIp}:${apiPort}/api/health"
}
Write-Host ""
Write-Host "REQUIRED router setup (manual):"
Write-Host "  1) Open router admin (usually http://192.168.1.1 )"
Write-Host "  2) Port Forwarding / Virtual Server:"
Write-Host "       WAN/External port $webPort  ->  ${lanIp}:$webPort   (TCP)"
Write-Host "       WAN/External port $apiPort  ->  ${lanIp}:$apiPort   (TCP)"
Write-Host "  3) Save, then test from mobile data (not Wi-Fi)."
Write-Host ""
Write-Host "Security note: this exposes your Bagly admin + API to the internet."
Write-Host "Use only for testing; change admin password; prefer HTTPS later."
