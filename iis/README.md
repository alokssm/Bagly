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

Edit `backend\Bagly.Api\appsettings.Development.json` (gitignored), then re-run `Publish-IIS.ps1` or copy the file into `publish\iis\api\`.

## Order confirmation email (Gmail SMTP)

Local IIS uses `appsettings.Development.json`. Gmail SMTP is preconfigured except for your account — see **[GMAIL-SMTP.md](./GMAIL-SMTP.md)** for App Password setup and health-check steps.

## Google sign-in setup ("Continue with Google")

Customer login/register support Google sign-in via Google Identity Services (GIS). To enable it:

1. Open the [Google Cloud Console credentials page](https://console.cloud.google.com/apis/credentials) (create/select a project first).
2. **Create credentials → OAuth client ID → Application type: Web application**.
3. Under **Authorized JavaScript origins**, add:
   - `http://localhost:8080` (local IIS storefront)
   - `http://localhost:5173` (Vite dev server)
   - `https://www.bagly.co.in` (production custom domain)
   - `https://bagly.co.in` (apex, if used)
   - `https://bagly-one.vercel.app` (Vercel preview/production URL)
4. **Authorized redirect URIs** are not needed — GIS uses the One Tap / credential button flow (returns an ID token directly, no redirect).
5. Copy the generated **Client ID** (looks like `xxxxxxxx-xxxx.apps.googleusercontent.com`).
6. Set it in **both**:
   - Backend: `GoogleAuth__ClientId` (env var, or `GoogleAuth:ClientId` in `appsettings.Development.json` for local IIS/dev)
   - Frontend: `VITE_GOOGLE_CLIENT_ID` in `frontend/.env` (dev) or `frontend/.env.iis` (IIS build)
7. Re-run `Publish-IIS.ps1` (frontend) and restart the API app pool so both sides pick up the new value.

Until a Client ID is set, the "Continue with Google" button is simply hidden and email/password login still works. `/api/health` reports `customerAuth.googleConfigured` so you can confirm the backend picked up the value.

## Cloudinary image uploads (admin product images)

Admin → Products → Add/Edit product has an **Upload image** / **Add gallery image** button that uploads to Cloudinary's free tier and fills in the URL field automatically. To enable it locally:

1. Sign up free at https://cloudinary.com/users/register/free and copy **Cloud name**, **API Key**, **API Secret** from the Dashboard.
2. Add to `backend\Bagly.Api\appsettings.Development.json` (gitignored — never commit real secrets):
   ```json
   {
     "Cloudinary": {
       "CloudName": "your-cloud-name",
       "ApiKey": "123456789012345",
       "ApiSecret": "your-api-secret"
     }
   }
   ```
3. Restart the API (or `Restart-WebAppPool BaglyApiAppPool` for IIS). `/api/health` → `uploads.cloudinaryConfigured` should report `true`.

Without Cloudinary configured, the upload buttons show an error but the **URL-paste workflow still works** for both the main image and gallery fields.

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
