# Step 2 — Deploy Bagly frontend to Vercel

Backend API (already live):
https://bagly.onrender.com/api

---

## A) Deploy on Vercel

1. Open https://vercel.com and sign in with **GitHub** (`alokssm`)
2. **Add New… → Project**
3. Import **`alokssm/Bagly`**
4. Configure:

| Setting | Value |
|--------|--------|
| **Framework Preset** | Vite |
| **Root Directory** | `frontend` (click Edit → select `frontend`) |
| **Build Command** | `npm run build` |
| **Output Directory** | `dist` |
| **Install Command** | `npm install` |

5. **Environment Variables** → Add:

| Key | Value |
|-----|--------|
| `VITE_API_URL` | `https://bagly.onrender.com/api` |
| `VITE_GOOGLE_CLIENT_ID` | Google OAuth Web Client ID (optional — see `iis/README.md` → "Google sign-in setup"). Must match Render's `GoogleAuth__ClientId` and include this Vercel URL as an authorized JS origin in Google Cloud. Leave unset to hide the "Continue with Google" button. |

6. Click **Deploy**

---

## B) After deploy

Your site will be like:
`https://bagly-xxxx.vercel.app`

Test:
- Home / Shop load products
- Admin login: `/admin/login`

---

## C) Optional: custom CORS origin on Render

CORS already allows `*.vercel.app`.  
Optional: on Render env add:

`Cors__AllowedOrigins__0` = `https://YOUR-APP.vercel.app`

---

## D) Troubleshooting — site looks old after push

If https://bagly-one.vercel.app/ still lacks **Login / Register** in the navbar, customer chat, or `/login`:

1. **Confirm Root Directory** (most common cause)
   - Vercel → your **Bagly** project → **Settings** → **Build and Deployment**
   - **Root Directory** must be `frontend` (not empty / repo root)
   - If it was wrong: set to `frontend`, save, then **Deployments** → **Create Deployment** → branch `main` → commit `1040c84` or latest

2. **Deploy the latest commit — do not “Redeploy” an old deployment**
   - **Deployments** tab → top deployment should show commit `Add customer auth, AI chat…` (`1040c84`)
   - If the latest row is **Error**, open it → **Building** logs (wrong root dir shows “package.json not found”)
   - To fix production: ⋮ on the successful `1040c84` build → **Promote to Production**, or **Create Deployment** from `main`

3. **Verify the live build** (View Source on the homepage)
   - New UI: `index.html` includes `<link rel="preconnect" href="https://accounts.google.com" />`
   - New JS bundle is ~370 KB (e.g. `index-7ADus5Fk.js`), not ~294 KB (`index-CRi7abYb.js`)
   - Navbar shows **Login** and **Register**; signed-in users see the chat widget

4. **Repo-root fallback:** `/vercel.json` at repo root builds `frontend/` when Root Directory is unset. Prefer setting **Root Directory = `frontend`** in the dashboard so `frontend/vercel.json` SPA rewrites apply.

5. **Hard refresh** after a good deploy: Ctrl+Shift+R (or incognito) — cache is rarely the issue if bundle filenames differ.

---

## Notes

- First Render API call after idle can take 30–60s (free sleep)
- Azure SQL may need seed data if shop is empty — tell me if products don’t show
