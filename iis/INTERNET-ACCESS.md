# Make Bagly reachable from the internet (this PC)

## Goal

Allow users outside your home network to open:

- UI: `http://YOUR_PUBLIC_IP:8080`
- API: `http://YOUR_PUBLIC_IP:8081/api/health`

## Step 1 — Run on this PC (Administrator PowerShell)

```powershell
powershell -ExecutionPolicy Bypass -File D:\Projects\Bagly\scripts\Enable-InternetAccess.ps1
```

This script will:

1. Bind IIS sites to all interfaces (`*:8080`, `*:8081`)
2. Open Windows Firewall for ports **8080** and **8081**
3. Rebuild the UI so it calls your **public IP** API URL
4. Republish and restart IIS app pools

## Step 2 — Router port forwarding (required)

Your PC LAN IP is typically like `192.168.1.6`.

In your router admin page (often `http://192.168.1.1`):

| External port | Forward to | Protocol |
|---------------|------------|----------|
| 8080 | `192.168.1.6:8080` | TCP |
| 8081 | `192.168.1.6:8081` | TCP |

Save settings. Exact menu name varies: **Port Forwarding**, **Virtual Server**, **NAT**.

## Step 3 — Test

1. **Same Wi‑Fi phone/PC:** `http://192.168.1.6:8080`
2. **Internet (use mobile data, not Wi‑Fi):** `http://YOUR_PUBLIC_IP:8080`

Find public IP: https://api.ipify.org

## If internet still fails

- ISP may use **CGNAT** (no real inbound ports) — ask ISP for a public IP, or use a tunnel (ngrok/Cloudflare Tunnel)
- Windows Firewall / antivirus blocking
- Router firewall still enabled for those ports
- Public IP changed — re-run `Enable-InternetAccess.ps1`

## Security warning

Exposing IIS + admin login to the internet is for **temporary testing only**.

- Change admin password
- Do not keep this open permanently without HTTPS and proper hardening
