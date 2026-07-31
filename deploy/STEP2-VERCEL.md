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

## Notes

- First Render API call after idle can take 30–60s (free sleep)
- Azure SQL may need seed data if shop is empty — tell me if products don’t show
