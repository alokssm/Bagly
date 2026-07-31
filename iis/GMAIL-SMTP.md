# Gmail SMTP for local IIS (order confirmation emails)

IIS runs the API with `ASPNETCORE_ENVIRONMENT=Development`, so email settings come from:

`backend\Bagly.Api\appsettings.Development.json` (copied to `publish\iis\api\` by `Publish-IIS.ps1`).

## 1. Fill in two values

Edit the `Email` section and replace placeholders (use the **same Gmail address** for `Username` and `FromAddress`):

| Key | Value |
|-----|--------|
| `Username` | Your Gmail address, e.g. `you@gmail.com` |
| `Password` | [Gmail App Password](https://myaccount.google.com/apppasswords) (16 characters, no spaces) |
| `FromAddress` | Same Gmail address as `Username` |

`Host`, `Port`, `UseSsl`, `Provider`, and `Enabled` are already set for Gmail.

## 2. Create a Gmail App Password

1. Turn on [2-Step Verification](https://myaccount.google.com/signinoptions/two-step-verification) for the Google account.
2. Open [App passwords](https://myaccount.google.com/apppasswords).
3. Create an app password for **Mail** / **Other (Bagly)**.
4. Copy the 16-character password into `Email:Password` (not your normal Google password).

## 3. Apply to IIS

```powershell
powershell -ExecutionPolicy Bypass -File D:\Projects\Bagly\scripts\Publish-IIS.ps1
```

Or copy only the settings file:

```powershell
Copy-Item D:\Projects\Bagly\backend\Bagly.Api\appsettings.Development.json `
  D:\Projects\Bagly\publish\iis\api\appsettings.Development.json -Force
Restart-WebAppPool BaglyApiAppPool
```

## 4. Verify

Open http://localhost:8081/api/health and check the `email` section:

- `configured`: `true`
- `willSend`: `true`
- `hostSet`: `true`
- `usernameSet` / `passwordSet`: `true` (after you replace placeholders)

## 5. Test a real email

Complete a test checkout with Razorpay test mode. After payment succeeds, the customer email on the order should receive a confirmation message.

Check API logs if nothing arrives: `publish\iis\api\logs\`
