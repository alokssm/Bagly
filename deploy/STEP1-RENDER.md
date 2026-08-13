# Step 1 — Deploy Bagly API to Render (free)

This guide deploys only the **backend** (`backend/Bagly.Api`).

> A [Neon](https://neon.tech) Postgres database (Step 3) is required for a fully working API.  
> You can still create the Render service now and set the DB connection string in Step 3.

---

## Prerequisites

1. GitHub account: https://github.com  
2. Render account: https://render.com (sign up with GitHub)  
3. **Git for Windows** installed: https://git-scm.com/download/win  
   - Restart Cursor/terminal after install so `git` works.

---

## A) Push this project to GitHub

In **PowerShell** (from `D:\Projects\Bagly`):

```powershell
cd D:\Projects\Bagly
git init
git add .
git commit -m "Prepare Bagly for Render backend deploy"
```

Then on GitHub: **New repository** → name `Bagly` → **do not** add README.

```powershell
git branch -M main
git remote add origin https://github.com/YOUR_USERNAME/Bagly.git
git push -u origin main
```

Replace `YOUR_USERNAME` with your GitHub username.

---

## B) Create the Render web service

### Option 1 — Blueprint (recommended)

1. Open https://dashboard.render.com  
2. **New** → **Blueprint**  
3. Connect the `Bagly` GitHub repo  
4. Render reads `render.yaml` and creates **bagly-api**

### Option 2 — Manual Docker service

1. **New** → **Web Service**  
2. Connect `Bagly` repo  
3. Settings:
   - **Runtime:** Docker  
   - **Root Directory:** `backend/Bagly.Api`  
   - **Dockerfile Path:** `./Dockerfile`  
   - **Instance type:** Free  
   - **Health Check Path:** `/api/health`

---

## C) Environment variables (Render → Environment)

Set these **before** or right after first deploy:

| Key | Example / notes |
|-----|-----------------|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `EnableSwagger` | `true` (useful while testing) |
| `EnableHttpsRedirection` | `false` |
| `ConnectionStrings__DefaultConnection` | *(leave empty until Step 3 — Neon Postgres)* |
| `Jwt__Key` | long random string (32+ chars) |
| `Admin__Email` | `admin@bagly.store` |
| `Admin__Password` | your strong password |
| `Admin__Name` | `Bagly Admin` |
| `Razorpay__KeyId` | your `rzp_test_...` |
| `Razorpay__KeySecret` | your test secret |
| `Razorpay__Currency` | `INR` |
| `Razorpay__UsdToInrRate` | `83` |
| `Cors__AllowedOrigins__0` | primary storefront origin, e.g. `https://www.bagly.co.in` |
| `Cors__AllowedOrigins__1` | apex domain if used, e.g. `https://bagly.co.in` |
| `Cors__AllowedOrigins__2` | legacy/preview host, e.g. `https://bagly-one.vercel.app` |
| `Storefront__BaseUrl` | storefront URL for product links in restock alert emails, e.g. `https://www.bagly.co.in`. Falls back to `Cors__AllowedOrigins__0` if unset. |
| `Email__Enabled` | `true` |
| `Email__Provider` | `Resend` on Render **free** tier (SMTP ports blocked); `SendGrid` or `Smtp` also supported |
| `Email__ResendApiKey` | Resend API key (`re_xxx`) — **mark Secret**; uses HTTPS so it works on Render free |
| `Email__SendGridApiKey` | SendGrid API key (`SG.xxx`) — alternative HTTPS provider; **mark Secret** |
| `Email__Host` | SMTP host (only if `Email__Provider=Smtp`), e.g. `smtp.gmail.com` |
| `Email__Port` | `587` (typical STARTTLS) |
| `Email__Username` | SMTP login (SendGrid SMTP: `apikey`) |
| `Email__Password` | SMTP password or app password (**mark Secret**) |
| `Email__FromAddress` | verified sender/domain in Resend (see below) or Gmail for SMTP |
| `Email__FromName` | `Bagly` |
| `Email__UseSsl` | `true` |
| `Email__AdminOrderNotify` | Mailbox that gets a copy of every successfully placed order (order number, customer, shipping address, items, totals, payment status). Defaults to `alok73772@gmail.com` in `appsettings.json` if unset. `Admin__OrderNotifyEmail` is also read as a fallback if `Email__AdminOrderNotify` is not set. Sending this copy never fails the customer's order — a failure is only logged. |
| `Shiprocket__Enabled` | `true` to push confirmed India orders to Shiprocket after checkout (default in appsettings is `false`) |
| `Shiprocket__Email` | Shiprocket **API user** email (Settings → API → API User) — not necessarily your panel login email |
| `Shiprocket__Password` | Shiprocket API user password — **mark Secret** |
| `Shiprocket__PickupLocation` | Exact **pickup nickname** from Shiprocket → Settings → Pickup Addresses (must match character-for-character) |
| `GoogleAuth__ClientId` | Google OAuth Web Client ID for "Continue with Google" (optional — leave unset to hide the button). See `iis/README.md` → "Google sign-in setup" for how to create one. Must match frontend `VITE_GOOGLE_CLIENT_ID`. |
| `Cloudinary__CloudName` | Your Cloudinary cloud name (optional — leave unset to hide admin image uploads; URL-paste still works). See "Cloudinary setup" below. |
| `Cloudinary__ApiKey` | Cloudinary API Key |
| `Cloudinary__ApiSecret` | Cloudinary API Secret — **mark Secret** |

Order confirmation emails are sent after Razorpay payment verify succeeds (India) and after non-India checkout orders. An admin copy (`Email__AdminOrderNotify`, see above) is sent alongside each of these — never on out-of-stock/refund emails.

> **CORS on Render:** Environment variables **override** `appsettings.json`. If you only set `Cors__AllowedOrigins__0`, that becomes the sole allowed origin — you must set `__0`, `__1`, `__2` explicitly for each host. After changing CORS env vars, **Manual Deploy** (or push to `main`) and verify with:
>
> ```powershell
> curl.exe -s -i "https://bagly.onrender.com/api/health" -H "Origin: https://www.bagly.co.in"
> ```
>
> Response must include `access-control-allow-origin: https://www.bagly.co.in`.

> **Render free tier blocks outbound SMTP** on ports 25, 465, and 587. Use an HTTPS email API instead: **Resend** (recommended) or SendGrid.

### Resend setup (recommended for Render free)

1. Sign up at https://resend.com (free tier includes 100 emails/day).
2. **API Keys** → **Create API Key** → copy the key (`re_...`).
3. In Render → **Environment**, set:
   ```
   Email__Enabled=true
   Email__Provider=Resend
   Email__ResendApiKey=re_xxxx
   Email__FromName=Bagly
   Email__AdminOrderNotify=alok73772@gmail.com
   ```
4. **From address — pick one:**
   - **Quick test (no domain):** `Email__FromAddress=onboarding@resend.dev`  
     Resend only delivers to the email address you signed up with (typically `alok73772@gmail.com`). A test order as that customer correctly yields **two** emails (customer "Bagly order confirmed" + admin "New Bagly order"). Any other customer address is rejected until a domain is verified.
   - **Production (required for real customers):** Resend → **Domains** → add `bagly.co.in` → add DNS records → wait until **Verified** → set `Email__FromAddress=noreply@bagly.co.in`. Do **not** remove the admin notify email — both templates are intentional.
5. Save env vars → **Manual Deploy** → check `/api/health` shows `"provider":"Resend"`, `"resendApiKeySet":true`, `"willSend":true`, and `fromAddress` is your verified sender.

If email is not configured, checkout still succeeds but no email is sent — check Render logs for `Order confirmation email skipped` / Resend HTTP errors.

### Shiprocket setup (India fulfilment)

1. In Shiprocket: create/enable an **API user** and note the email + password.
2. Add a pickup address and copy its **nickname** exactly (e.g. `Primary` or `Warehouse`).
3. In Render → **Environment**, set:
   ```
   Shiprocket__Enabled=true
   Shiprocket__Email=your-api-user@email.com
   Shiprocket__Password=your-api-password
   Shiprocket__PickupLocation=ExactNicknameFromPanel
   ```
4. Save → **Manual Deploy** → `/api/health` should show `"shiprocket": { "enabled": true, "configured": true, ... }`.
5. Place a confirmed India order with a 10-digit phone → Admin → Orders should show a Shiprocket id, and the order should appear in the Shiprocket panel. If not, Admin → Orders shows `shiprocketLastError` (also in `/api/health` hint when pickup is literally `test`), and Render logs include `Shiprocket create failed` plus the API response body (wrong pickup nickname, auth failure, etc.).

**Do not** set `Shiprocket__PickupLocation=test` unless that is literally the nickname in the Shiprocket panel — a mismatch causes every create to fail with no orders on the dashboard.

Without `Shiprocket__Enabled=true` (and credentials), **no Shiprocket API call is made** — checkout still succeeds.

### Cloudinary setup (admin product image uploads)

Cloudinary's free tier (25 GB storage/bandwidth credit) lets admins upload product photos instead of only pasting URLs.

1. Sign up at https://cloudinary.com/users/register/free (no credit card required).
2. After signup you land on the **Dashboard** — copy the three values shown under **Product Environment Credentials**:
   - **Cloud name**
   - **API Key**
   - **API Secret** (click "Show" to reveal it)
3. In Render → **Environment**, set:
   ```
   Cloudinary__CloudName=your-cloud-name
   Cloudinary__ApiKey=123456789012345
   Cloudinary__ApiSecret=your-api-secret
   ```
   Mark `Cloudinary__ApiSecret` (and ideally `Cloudinary__ApiKey`) as **Secret**.
4. Save env vars → **Manual Deploy** → check `/api/health` shows `"uploads": { "cloudinaryConfigured": true }`.
5. In the admin panel, editing a product now shows an **Upload image** / **Add gallery image** button next to the URL fields. Uploaded files are stored under the `bagly/products` folder in your Cloudinary media library and the returned `secure_url` (`https://res.cloudinary.com/...`) is written into the Image URL / Gallery fields automatically.

If `Cloudinary__*` env vars are not set, the upload buttons will show an error when clicked, but the existing **URL-paste workflow keeps working** — nothing else breaks.

---

## D) Verify

After deploy, open:

```
https://YOUR-SERVICE.onrender.com/api/health
```

You should see JSON `"status":"healthy"`.

Swagger (if enabled):

```
https://YOUR-SERVICE.onrender.com/swagger
```

> Free tier **sleeps** after idle time. First request can take 30–60 seconds.

---

## Files added for Step 1

| File | Purpose |
|------|---------|
| `backend/Bagly.Api/Dockerfile` | Container build for Render |
| `render.yaml` | Render Blueprint |
| `.gitignore` | Keeps secrets / build folders out of Git |

Local secrets stay in `appsettings.Development.json` (gitignored). Cloud secrets must be Render env vars.

---

## Next

- **Step 2:** Deploy frontend on Vercel (`VITE_API_URL=https://YOUR-SERVICE.onrender.com/api`)
- **Step 3:** Create a free [Neon](https://neon.tech) Postgres project → copy the connection string → paste into Render as `ConnectionStrings__DefaultConnection` (Npgsql format, see below) → Manual Deploy

### Step 3 details — Neon Postgres

1. Sign up at https://neon.tech (free tier) → **New Project** (choose a region close to your Render region).
2. On the project dashboard, click **Connect** and copy the connection details, or build the Npgsql-format string yourself:
   ```
   Host=ep-xxxx.region.aws.neon.tech;Database=neondb;Username=neondb_owner;Password=YOUR_PASSWORD;SSL Mode=Require;Trust Server Certificate=true
   ```
   (Neon's dashboard shows a `postgresql://user:password@host/db?sslmode=require` URI — convert it to the `Key=Value;...` format above; Npgsql does not accept the `postgresql://` URI form directly in this app.)
3. In Render → **Environment**, set `ConnectionStrings__DefaultConnection` to that string (mark it **Secret**), or use `BAGLY_CONNECTION_STRING` instead (checked first).
4. **Manual Deploy**. On startup the API runs EF Core migrations automatically (`db.Database.MigrateAsync()`), creating all tables on the empty Neon database.
5. Seed demo data + the admin account by calling:
   ```powershell
   curl.exe -s -X POST "https://YOUR-SERVICE.onrender.com/api/setup/seed"
   ```
6. Check `https://YOUR-SERVICE.onrender.com/api/health` → `database.status` should be `"connected"`.

> Neon's free tier auto-suspends idle databases; the first request after idle can take a few seconds while it wakes up.
