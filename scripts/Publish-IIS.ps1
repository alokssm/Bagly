# Publish Bagly for local IIS (frontend + API).
# Run from anywhere:  powershell -ExecutionPolicy Bypass -File D:\Projects\Bagly\scripts\Publish-IIS.ps1

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$apiProject = Join-Path $root "backend\Bagly.Api\Bagly.Api.csproj"
$frontendDir = Join-Path $root "frontend"
$publishRoot = Join-Path $root "publish\iis"
$apiOut = Join-Path $publishRoot "api"
$webOut = Join-Path $publishRoot "web"

Write-Host "==> Publishing .NET API to $apiOut"

# Stop IIS app pools if present so publish can overwrite locked files.
$hasIis = $false
try {
  Import-Module WebAdministration -ErrorAction Stop
  $hasIis = $true
} catch {}

if ($hasIis) {
  foreach ($pool in @("BaglyApiAppPool", "BaglyWebAppPool")) {
    if (Test-Path "IIS:\AppPools\$pool") {
      Write-Host "Stopping app pool $pool"
      Stop-WebAppPool -Name $pool -ErrorAction SilentlyContinue
    }
  }
  Start-Sleep -Seconds 2
}

if (Test-Path $apiOut) {
  Get-ChildItem $apiOut -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object {
    try { $_.Attributes = 'Normal' } catch {}
  }
  Remove-Item $apiOut -Recurse -Force -ErrorAction SilentlyContinue
  if (Test-Path $apiOut) {
    # Fallback: clear contents when some log files stay locked
    Get-ChildItem $apiOut -Force | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
  }
}
New-Item -ItemType Directory -Force -Path $apiOut | Out-Null
dotnet publish $apiProject -c Release -o $apiOut
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

# Ensure ASP.NET Core module env + stdout logs in published web.config
$apiWebConfig = Join-Path $apiOut "web.config"
@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <handlers>
        <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      </handlers>
      <aspNetCore processPath="dotnet"
                  arguments=".\Bagly.Api.dll"
                  stdoutLogEnabled="true"
                  stdoutLogFile=".\logs\stdout"
                  hostingModel="inprocess">
        <environmentVariables>
          <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Development" />
        </environmentVariables>
      </aspNetCore>
    </system.webServer>
  </location>
</configuration>
"@ | Set-Content -Path $apiWebConfig -Encoding UTF8
New-Item -ItemType Directory -Path (Join-Path $apiOut "logs") -Force | Out-Null

# Keep local secrets available for IIS (file is gitignored from source control commits).
$devSettings = Join-Path $root "backend\Bagly.Api\appsettings.Development.json"
if (Test-Path $devSettings) {
  Copy-Item $devSettings (Join-Path $apiOut "appsettings.Development.json") -Force
}

Write-Host "==> Building React frontend for IIS"
Push-Location $frontendDir
try {
  $envFile = Join-Path $frontendDir ".env.iis"
  if (Test-Path $envFile) {
    Get-Content $envFile | ForEach-Object {
      if ($_ -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)\s*$') {
        $key = $Matches[1]
        $val = $Matches[2].Trim().Trim('"')
        if ($val) {
          Set-Item -Path "Env:$key" -Value $val
        }
      }
    }
  }
  if (-not $env:VITE_API_URL) {
    $env:VITE_API_URL = "http://localhost:8081/api"
  }
  Write-Host "  VITE_API_URL=$($env:VITE_API_URL)"
  if ($env:VITE_GOOGLE_CLIENT_ID) {
    Write-Host "  VITE_GOOGLE_CLIENT_ID=$($env:VITE_GOOGLE_CLIENT_ID)"
  } else {
    Write-Host "  VITE_GOOGLE_CLIENT_ID not set - 'Continue with Google' button will be hidden until configured (see iis/README.md)."
  }
  npm run build
  if ($LASTEXITCODE -ne 0) { throw "npm run build failed." }

  $builtJs = Get-ChildItem (Join-Path $frontendDir "dist\assets\*.js") | Select-Object -First 1
  if (-not $builtJs -or -not (Select-String -Path $builtJs.FullName -Pattern ([regex]::Escape($env:VITE_API_URL)) -SimpleMatch -Quiet)) {
    throw "Frontend build is missing API URL '$($env:VITE_API_URL)'. Aborting publish."
  }
}
finally {
  Pop-Location
}

Write-Host "==> Copying frontend dist to $webOut"
if (Test-Path $webOut) { Remove-Item $webOut -Recurse -Force }
New-Item -ItemType Directory -Path $webOut | Out-Null
Copy-Item (Join-Path $frontendDir "dist\*") $webOut -Recurse -Force
Copy-Item (Join-Path $root "iis\web\web.config") (Join-Path $webOut "web.config") -Force

Write-Host ""
Write-Host "Publish complete."
Write-Host "  API : $apiOut"
Write-Host "  Web : $webOut"
Write-Host ""
Write-Host "Next (Run as Administrator):"
Write-Host "  powershell -ExecutionPolicy Bypass -File `"$root\scripts\Setup-IIS.ps1`""
