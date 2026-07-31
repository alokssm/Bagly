# Step 1 — Deploy Bagly API to Render (free)

This guide deploys only the **backend** (`backend/Bagly.Api`).

> Azure SQL (Step 3) is required for a fully working API.  
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
| `ConnectionStrings__DefaultConnection` | *(leave empty until Step 3 — Azure SQL)* |
| `Jwt__Key` | long random string (32+ chars) |
| `Admin__Email` | `admin@bagly.store` |
| `Admin__Password` | your strong password |
| `Admin__Name` | `Bagly Admin` |
| `Razorpay__KeyId` | your `rzp_test_...` |
| `Razorpay__KeySecret` | your test secret |
| `Razorpay__Currency` | `INR` |
| `Razorpay__UsdToInrRate` | `83` |
| `Cors__AllowedOrigins__0` | your future Vercel URL (Step 2), e.g. `https://bagly.vercel.app` |
| `Email__Enabled` | `true` |
| `Email__Provider` | `SendGrid` on Render **free** tier (SMTP ports blocked); `Smtp` on paid Render or local dev |
| `Email__SendGridApiKey` | SendGrid API key (`SG.xxx`) — **mark Secret**; uses HTTPS so it works on Render free |
| `Email__Host` | SMTP host (only if `Email__Provider=Smtp`), e.g. `smtp.gmail.com` |
| `Email__Port` | `587` (typical STARTTLS) |
| `Email__Username` | SMTP login (SendGrid SMTP: `apikey`) |
| `Email__Password` | SMTP password or app password (**mark Secret**) |
| `Email__FromAddress` | verified sender in SendGrid (or Gmail account / alias for SMTP) |
| `Email__FromName` | `Bagly` |
| `Email__UseSsl` | `true` |
| `GoogleAuth__ClientId` | Google OAuth Web Client ID for "Continue with Google" (optional — leave unset to hide the button). See `iis/README.md` → "Google sign-in setup" for how to create one. Must match frontend `VITE_GOOGLE_CLIENT_ID`. |

Order confirmation emails are sent after Razorpay payment verify succeeds (India) and after non-India checkout orders.

> **Render free tier blocks outbound SMTP** on ports 25, 465, and 587. If `/api/health` shows `willSend: true` with `provider: Smtp` but emails never arrive, check Render logs for SMTP timeout — switch to `Email__Provider=SendGrid` + `Email__SendGridApiKey`, or upgrade to a paid Render instance.

If email is not configured, checkout still succeeds but no email is sent — check Render logs for `Order confirmation email skipped`.

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
- **Step 3:** Create Azure SQL free DB → paste connection string into Render → redeploy / restart
