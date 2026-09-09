# Rebranding checklist

When you stand up a new app from this platform, replace every Perezosoft brand touchpoint
below.

> **Don't stop at the UI.** The transactional **email templates** carry their own copy of the
> name, logo, colours, and tagline — they're inline (email HTML can't share the app's CSS or
> static assets), so they're the most commonly missed spot. Every rebranding task **must**
> include the `Infrastructure/Email/` items flagged below.

Backstop after working through the list: `git grep -i perezosoft` and a search for the tagline
`Lazy reputation. Efficient engineering.` should both return nothing (outside this doc).

## 0. Solution, projects, namespaces — `Perezosoft.*` → `<Brand>.*` (do this FIRST, as one commit)

§1–§5 list the *strings*; the verify at the end demands more. The solution file, every `*.csproj`,
the root namespaces, the RCL static-asset path (`_content/Perezosoft.Shared.Ui/…` in **both** hosts'
`index.html`), the CI smoke identifiers (`Perezosoft.Maui.app`, the `PEREZOSOFT_API_BASE_URL` env
prefix), the Docker tag (`perezosoft-app:ci`), the Postman file names, the test issuer strings and the
project names inside every `packages.lock.json` all carry the brand. The first downstream app
(JiggerJot, 2026-09-08) measured **545 text files rewritten and 15 files renamed** — do it
mechanically, case-preserving, over `git ls-files` only, then run restore/build/tests exactly as
`ci.yml` does before touching anything else.

1. Remove the template-only material first (§6): `git rm -r docs/tutorial _PLATFORM_PRIMER.md`.
2. Rename the brand-named files: `git ls-files | grep -i perezosoft` → `git mv` each
   (`Perezosoft.slnx`, `src/*/Perezosoft.*.csproj`, `tests/*/Perezosoft.*.csproj`,
   `src/Api/Perezosoft.Api.http`, `docs/postman/Perezosoft.*.json`).
3. Rewrite the text of every tracked, non-binary file **except this doc**. Special cases go first:
   `com.perezosoft.platform` → your `ApplicationId` (e.g. `com.<brand>.app`),
   `Perezosoft Platform` → `<Brand>`, `template-staging` → `<brand>-staging`, the tagline; then the
   three case variants `Perezosoft` / `perezosoft` / `PEREZOSOFT` → `<Brand>` / `<brand>` / `<BRAND>`.
   **Keep the upstream slug `perezosoft-platform`** where it names this repo as provenance (ADRs, the
   brief, `STATUS.md`, the gitleaks title); it is the one sanctioned survivor of the verify grep.
4. Detect binaries by content (a NUL byte in the first 8 KB), not only by extension. Lockfiles are
   text and **must** be rewritten — the project-reference names live in them — and locked-mode
   restore then passes without `--force-evaluate`.
5. Rename the platform's palette constants while you are in `BrandedEmail.cs` (§4).

```python
# rename.py — run from the repo root after steps 1–2; adjust BRAND / APP_ID / STAGING / TAGLINE.
import os, re, subprocess
BRAND, APP_ID, STAGING, TAGLINE = "JiggerJot", "com.jiggerjot.app", "jiggerjot-staging", "Mix what you have."
special = [("com.perezosoft.platform", APP_ID), ("Perezosoft Platform", BRAND), ("template-staging", STAGING),
           ("Lazy reputation. Efficient engineering.", TAGLINE)]
generic = [("Perezosoft", BRAND), ("perezosoft", BRAND.lower()), ("PEREZOSOFT", BRAND.upper())]
for f in filter(None, subprocess.check_output(["git", "ls-files", "-z"]).decode().split("\0")):
    if f == "docs/REBRANDING.md": continue
    raw = open(f, "rb").read()
    if b"\0" in raw[:8192]: continue
    s = o = raw.decode("utf-8")
    s = s.replace("perezosoft-platform", "\0SLUG\0")
    for a, b in special + generic: s = s.replace(a, b)
    s = s.replace("\0SLUG\0", "perezosoft-platform")
    if s != o: open(f, "wb").write(s.encode("utf-8"))
```

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
- **`src/Api/Services/MfaService.cs`** — the `Issuer` const ("Perezosoft"): what authenticator apps
  display for enrolled accounts, and the letter-monogram they draw derives from it. Easy to miss —
  users only ever see it inside Google Authenticator & co.
- **`docs/postman/`** — the collection's `info.name` ("Perezosoft Platform API"), both environment
  `name`s, and the `Perezosoft.*.json` **file names**. If you rename the files, update the hardcoded
  collection path in `.github/workflows/postman-sync.yml` in the same commit; and since the sync
  matches workspace items **by name**, delete the old-brand copies in the Postman workspace once
  after the first post-rename sync.
- **`render.yaml`** (v3 audit DEP-12) — two spots: the blueprint's `Email__Smtp__FromName` value
  (currently "Perezosoft"; pairs with the appsettings default above), and the **service `name`**
  (`template-staging`) — it becomes your public `*.onrender.com` URL, and that URL is what you put in
  the `STAGING_BASE_URL` repo variable and register with OAuth providers/Stripe, so rename it **before**
  the first deploy, not after.
- **`src/Maui/Perezosoft.Maui.csproj` — `<ApplicationTitle>`** ("Perezosoft Platform"): the installed
  app's display name on every native platform (launcher label, window title, app switcher). It sits
  right next to `ApplicationId` (§5) — change them together.

## 2. Tagline — "Lazy reputation. Efficient engineering." → yours
- `src/Shared.Ui/wwwroot/brand/lockup_light.svg` — the wordmark-lockup text
- **`src/Infrastructure/Email/BrandedEmail.cs`** — email footer tagline

## 3. Logos & images — replace the files (keep the filenames to avoid touching references)
In-app UI (shared RCL — used by web + desktop + mobile):
- `src/Shared.Ui/wwwroot/brand/{icon_light.svg, icon_light_1024.png, lockup_light.svg, lockup_light_1520.png, lockup_dark.svg, lockup_dark_1520.png}`
- **The UI references the PNG lockups, not the SVGs.** Webfonts don't load inside an `<img>`-embedded
  SVG, so an SVG lockup's wordmark silently falls back to Helvetica/Arial. Keep the SVGs as the
  editable source, render the PNGs from them, and point `Login.razor`/`Home.razor` at the PNGs.

**Email logo (CID-embedded — shown in every transactional email):**
- **`src/Infrastructure/Email/Assets/logo.png`** — keep it a **PNG** (email clients strip SVG and block data-URIs); a ~128px square is plenty.

Web host chrome:
- `src/Web/wwwroot/{favicon.ico, favicon.svg, favicon.png, apple_touch_180.png, og_image_1200x630.png}`
- PWA icon set (ready for a future manifest): `src/Web/wwwroot/{icon-192.png, icon-512.png, icon-maskable-512.png}`

Native launcher icon + splash (MAUI):
- `src/Maui/Resources/AppIcon/{appicon.svg, appiconfg.svg}` — background layer + foreground mark
  (foreground sized to the Android adaptive-icon safe zone, ~61% of canvas)
- `src/Maui/Resources/Splash/splash.svg` + the `Color` attrs on `MauiIcon`/`MauiSplashScreen` in
  `src/Maui/Perezosoft.Maui.csproj` (brand background colour behind icon + splash)

Marketing & store submission (not shipped in the app):
- `docs/brand/{linkedin_banner_1128x191.png, linkedin_logo_300.png}`
- `docs/brand/{app_store_icon_1024.png, play_store_icon_512.png, android_adaptive_foreground_432.png}` — store assets for the downstream release checklist (`NEW_APP_GUIDE.md` Phase 9; ADR-024)

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
- `src/Maui/Perezosoft.Maui.csproj` — `ApplicationId` (`com.perezosoft.…`)
- Provider consoles — register `{scheme}://auth` and your `signin-*` redirect URIs

**…and everything that hardcodes the OLD `ApplicationId` (v3 audit DEP-12/TR-7).** Renaming
`ApplicationId` per the list above and stopping there **breaks the native CI smokes and Catalyst
SecureStorage** — these four reference `com.perezosoft.platform` directly and must move in the same
commit:
- **`.github/workflows/ci.yml` — the iOS-simulator smoke** (`xcrun simctl launch … com.perezosoft.platform`
  in `native-smoke-apple`): launches the app by bundle id; a renamed app never starts → red canary. The
  same block globs the bundle path `Perezosoft.Maui.app` (follows the project/assembly name) and sets the
  `SIMCTL_CHILD_PEREZOSOFT_API_BASE_URL` env prefix — sweep the whole step, not just the id.
- **`.github/workflows/ci.yml` — the Android smoke** (`adb install …/com.perezosoft.platform-Signed.apk`,
  `adb shell monkey -p …`, `adb shell pidof …` in `native-smoke-android`): the APK filename derives from
  `ApplicationId`, so install, launch, and the crash-log `pidof` all miss after a rename.
- **`tests/native-smoke-android/smoke.js`** — the `PKG` fallback (`process.env.NATIVE_SMOKE_PKG ||
  'com.perezosoft.platform'`). The env override exists, but CI doesn't set it — update the committed
  default.
- **`src/Maui/Platforms/MacCatalyst/Entitlements.plist`** — `keychain-access-groups` entry
  `$(AppIdentifierPrefix)com.perezosoft.platform`. Under real signing (a downstream app's Release/store build — ADR-024) a
  keychain group that doesn't match the new bundle id means **SecureStorage silently fails on Catalyst**
  — sessions won't persist. Debug builds won't catch it (they run unsandboxed via
  `Entitlements.Debug.plist` — see the comment there).

## 6. Template-only material — remove or rewrite
These describe the *platform*, not your app, and carry the old brand in ways a text rename can't fix:
- **`_PLATFORM_PRIMER.md`** — delete. It is the primer for conceptualizing the *next* app and lives here.
- **`docs/tutorial/`** — delete. The platform course, including a binary `PEREZOSOFT_COURSE.pdf` that
  `git grep` cannot see (the file-name check below catches it).
- **`README.md`** — rewrite for the app (what it is, how to run it, where things are, provenance).
- `docs/OVERVIEW.md`, `docs/STATUS.md`, `docs/ROADMAP.md`, `docs/PLATFORM_BACKLOG.md`, `docs/stories/*`,
  `docs/audits/*` — platform history; they rename mechanically and may stay as reference.
- The **`Notes` sample slice** goes when the first real feature lands (`WAYS_OF_WORKING.md`).

## Rasters
`docs/brand/build_assets.py` regenerates **every** PNG in §3, `favicon.ico` and the store/marketing set
from the SVG sources with a headless Chromium (so lockup wordmarks carry the real webfonts) + Pillow.
Swap the SVGs, set the ground colours in its config block, run it — don't hand-export twenty files.

## Verify the rebrand
- `git grep -i perezosoft -- . ':!docs/REBRANDING.md' | grep -v perezosoft-platform` returns nothing —
  the upstream slug is the one sanctioned survivor (§0); everything else is a leak.
- `git ls-files | grep -i perezosoft` returns nothing (file names and binaries).
- A search for the old tagline and for the platform's palette constants (`#465d4d`, `#6b8a72`) returns
  nothing outside this doc.
- Restore (locked mode), build and the test projects pass exactly as `ci.yml` runs them — the rename
  touches lockfiles, namespaces and CI identifiers, and the tests (Postman parity, RLS gate) are what
  prove the mechanical pass didn't miss one.
- Web and desktop show the new brand, **and** a test email (trigger an OTP or invite) arrives with the new logo, colours, name, and tagline.
