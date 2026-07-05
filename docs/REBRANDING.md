# Rebranding checklist

When you stand up a new app from this template, replace every Perezosoft brand touchpoint
below.

> **Don't stop at the UI.** The transactional **email templates** carry their own copy of the
> name, logo, colours, and tagline — they're inline (email HTML can't share the app's CSS or
> static assets), so they're the most commonly missed spot. Every rebranding task **must**
> include the `Infrastructure/Email/` items flagged below.

Backstop after working through the list: `git grep -i perezosoft` and a search for the tagline
`Lazy reputation. Efficient engineering.` should both return nothing (outside this doc).

## 1. Name & wordmark — "Perezosoft" → your brand
- `src/Shared.Ui/Components/AppHeader.razor` — header wordmark
- `src/Shared.Ui/Pages/Login.razor`, `Home.razor` — logo alt text + headings
- `src/Web/wwwroot/index.html` — `<title>` + `og:title`
- `src/Maui/wwwroot/index.html` — `<title>`
- `src/Maui/Auth/LoopbackOAuthInitiator.cs` — the "you can close this tab" page title
- **`src/Infrastructure/Email/BrandedEmail.cs`** — email footer wordmark + tagline (brand, not localized)
- **Localization resources (every language!)** — the brand name + product copy live in
  `src/Shared.Ui/Resources/AppStrings.*.resx` and `src/Infrastructure/Email/EmailStrings.*.resx`.
  Update "Perezosoft" in each `.resx` you have (en, es, …). See `docs/LOCALIZATION.md`.
- **Email sender name** — `Email:Smtp:FromName`. The committed runtime default lives in
  `src/Api/appsettings*.json` (currently "Perezosoft"); `Email__Smtp__FromName` in `.env` (dev) or
  env vars (prod) only *overrides* it. Update the appsettings default and any env override together.

## 2. Tagline — "Lazy reputation. Efficient engineering." → yours
- `src/Shared.Ui/wwwroot/brand/lockup_light.svg` — the wordmark-lockup text
- **`src/Infrastructure/Email/BrandedEmail.cs`** — email footer tagline

## 3. Logos & images — replace the files (keep the filenames to avoid touching references)
In-app UI (shared RCL — used by web + desktop + mobile):
- `src/Shared.Ui/wwwroot/brand/{icon_light.svg, icon_light_1024.png, lockup_light.svg, lockup_light_1520.png}`

**Email logo (CID-embedded — shown in every transactional email):**
- **`src/Infrastructure/Email/Assets/logo.png`** — keep it a **PNG** (email clients strip SVG and block data-URIs); a ~128px square is plenty.

Web host chrome:
- `src/Web/wwwroot/{favicon.ico, favicon.png, apple_touch_180.png, og_image_1200x630.png}`

Native launcher icon (MAUI):
- `src/Maui/Resources/AppIcon/{appicon.svg, appiconfg.svg}` — still the stock .NET icon; replace before shipping.

Marketing (not shipped in the app):
- `docs/brand/{linkedin_banner_1128x191.png, linkedin_logo_300.png}`

## 4. Colour palette — derive from your logo
The palette is semantic tokens, single-sourced for web **and** all native shells:
- **`src/Shared.Ui/wwwroot/css/app.css`** — the `:root` vars (`--bs-primary`/`--bs-primary-rgb`,
  `--brand-accent`, `--brand-dark`, `--brand-accent-light`, `--bs-link-color`/`--bs-link-hover-color`,
  `--app-bg`, `--app-border`). One file; both hosts load it via
  `_content/Perezosoft.Shared.Ui/css/app.css`. Typical derivation from a logo: primary = the logo's
  dominant mid tone, dark = its darkest shade (hover/active), accent(-light) = supporting tones,
  bg/border = a near-white and a soft border tinted toward the primary. Check WCAG contrast for
  white text on `--bs-primary`.
- **`src/Infrastructure/Email/BrandedEmail.cs`** — the colour constants at the top (`Green`,
  `GreenDark`, `Sage`, `SageLight`, `Surface`, `Border`, `Ink`, `Muted`) — rename them to match
  your palette while you're there. They're hard-coded because email HTML can't use CSS variables.

## 5. App identifier & OAuth callback scheme — "perezosoft" / app id
These must all match each other **and** your OAuth provider registration:
- `src/Maui/MauiProgram.cs` — `CallbackScheme`
- `src/Maui/Platforms/Android/WebAuthenticatorCallbackActivity.cs` — `CallbackScheme` const + intent-filter `DataScheme`
- `src/Maui/Platforms/iOS/Info.plist` + `src/Maui/Platforms/MacCatalyst/Info.plist` — `CFBundleURLSchemes` entry
- `src/Api/appsettings.json` — `Auth:Native:CallbackScheme`
- `src/Maui/Perezosoft.Maui.csproj` — `ApplicationId` (`com.companyname.…`)
- Provider consoles — register `{scheme}://auth` and your `signin-*` redirect URIs

## Verify the rebrand
- `git grep -i perezosoft` and a tagline search both return nothing (outside this doc).
- Web and desktop show the new brand, **and** a test email (trigger an OTP or invite) arrives with the new logo, colours, name, and tagline.
