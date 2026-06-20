# E2E tests (Playwright / NUnit)

End-to-end tests that drive a real browser against the running Web app + API, reading OTP
codes from **Mailpit** (the same flow a manual tester uses). They cover the QA plan's web
smoke path that doesn't need an external OAuth provider.

## Prerequisites

1. **Backing services** — `docker compose up -d` (Postgres + Mailpit).
2. **API — must send email to Mailpit, not a real provider.** The dev default
   (`appsettings.Development.json`) already points at Mailpit (`localhost:1025`). **If your
   repo-root `.env` overrides `Email__Smtp__*` to a real SMTP provider (e.g. Brevo), override
   it back for the test run** via command-line args (command-line config beats `.env`, and
   leaves `.env` untouched):
   ```sh
   dotnet run --project src/Api --launch-profile https -- \
     --Email:Smtp:Host=localhost --Email:Smtp:Port=1025 --Email:Smtp:Username= --Email:Smtp:Password=
   ```
   If your `.env` doesn't override email, just `dotnet run --project src/Api --launch-profile https`.
3. **Web** — `dotnet run --project src/Web --launch-profile https` (serves <https://localhost:7008>).
4. **Browser (once)** — `pwsh tests/E2E.Tests/bin/Debug/net10.0/playwright.ps1 install chromium`.

## Run

```sh
dotnet test tests/E2E.Tests
```

Base URL defaults to `https://localhost:7008`; override with `PLAYWRIGHT_BASE_URL`. The
dev self-signed cert is accepted (`IgnoreHTTPSErrors`).

## Coverage

| Test | QA plan |
|------|---------|
| Login page renders | — |
| Email OTP sign-in → lands in the app shell | QA-SMK-01 |
| Sign out → back to login | QA-SMK-03 |
| Invalid email rejected before the code step | QA-AUTH-09 |

OAuth (Google/Microsoft), desktop, and Android are intentionally **not** automated here —
they need external provider accounts / native runners. See `docs/QA_TEST_PLAN.md` for that
manual coverage. Selectors use stable `data-testid` hooks on the shared UI components.
