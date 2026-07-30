# Local IIS hosting for Bagly

## Sites

| Site | URL | Content |
|------|-----|---------|
| **Bagly.Web** | http://localhost:8080 | React storefront + admin |
| **Bagly.Api** | http://localhost:8081 | .NET 8 Web API |

Frontend is built to call `http://localhost:8081/api` via `frontend/.env.iis`:

```
VITE_API_URL=http://localhost:8081/api
```

(Do not use JSON in that file — Vite needs `KEY=value` lines.)

## Sites

1. **Enable IIS** (Windows Features)
   - Internet Information Services
   - IIS → World Wide Web Services → Application Development Features → **ASP.NET 4.8** (optional)
   - Common HTTP Features (Static Content, Default Document)

2. **Install ASP.NET Core 8 Hosting Bundle**  
   https://dotnet.microsoft.com/download/dotnet/8.0  
   Choose **Hosting Bundle**, then **restart IIS**:
   ```powershell
   iisreset
   ```

3. **Install URL Rewrite** (SPA routes like `/shop`, `/admin`)  
   https://www.iis.net/downloads/microsoft/url-rewrite

4. SQL Server Express running with database **BaglyDb**.

## Publish + create sites

```powershell
# 1) Publish (no admin needed)
powershell -ExecutionPolicy Bypass -File D:\Projects\Bagly\scripts\Publish-IIS.ps1

# 2) Create IIS sites (Administrator PowerShell)
powershell -ExecutionPolicy Bypass -File D:\Projects\Bagly\scripts\Setup-IIS.ps1
```

Open: **http://localhost:8080**

## After code changes

Re-run `Publish-IIS.ps1`, then in Admin PowerShell:

```powershell
Restart-WebAppPool BaglyApiAppPool
Restart-WebAppPool BaglyWebAppPool
```

## Razorpay keys

Edit either:
- `D:\Projects\Bagly\backend\Bagly.Api\appsettings.json` then re-publish, or
- `D:\Projects\Bagly\publish\iis\api\appsettings.json` directly on the published copy.

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| **500.30 app failed to start** / login failed for `IIS APPPOOL\BaglyApiAppPool` | Run `backend\scripts\Grant-IisAppPoolSqlAccess.sql` (Setup-IIS.ps1 does this automatically) |
| 500.19 / 500.30 module missing | Install Hosting Bundle + `iisreset` |
| Blank page on refresh of `/shop` | Install URL Rewrite |
| CORS / API failed | Confirm API at http://localhost:8081/api/health |
| DB errors | Start SQL Server (`SQLEXPRESS`) and check connection string |
| Port in use | Change ports in `Setup-IIS.ps1` and update `frontend/.env.iis` |

API stdout logs (when enabled): `publish\iis\api\logs\`
