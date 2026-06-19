# Localization (i18n)

The app is localized with standard .NET resources (`.resx` + `IStringLocalizer`), shared
across web/desktop/mobile through the RCL.

**Shipped languages:** English (`en`, the neutral/fallback) and Spanish (`es`).
`fr` / `de` / `pt` are pre-cleared in the API's accepted set and the runtime supports them,
but their translations are **not done yet** — see "Adding a language" below.

## Where the strings live
| Surface | Resource files |
|---|---|
| UI (all razor pages/components) | `src/Shared.Ui/Resources/AppStrings.resx` (en) + `AppStrings.{culture}.resx` |
| Transactional emails | `src/Infrastructure/Email/EmailStrings.resx` (en) + `EmailStrings.{culture}.resx` |

Any key missing from a culture file falls back to the neutral (English) file.

## How the language is chosen
- **Signed-in users:** a per-user `User.Locale` (saved via `PUT /api/auth/locale`, carried in
  the JWT `locale` claim) — it follows the user across devices. `MainLayout` reconciles to it
  on load.
- **Anonymous / pre-login:** device-local `localStorage["app_culture"]` (web) / OS culture
  (MAUI), with the in-app `LanguageSwitcher` (header + login card).
- **Emails:** OTP / magic-link use the requester's current UI culture (the Login page sends
  it); invitations use the **inviter's** saved locale.

The WASM host loads full globalization data (`BlazorWebAssemblyLoadAllGlobalizationData`) so
non-English cultures format dates/numbers correctly.

## Adding a language (example: French, `fr`)
1. Copy `src/Shared.Ui/Resources/AppStrings.resx` → `AppStrings.fr.resx` and translate every
   `<value>`.
2. Copy `src/Infrastructure/Email/EmailStrings.resx` → `EmailStrings.fr.resx` and translate.
3. Add it to the dropdown: append `("fr", "Français")` to `Cultures` in
   `src/Shared.Ui/Components/LanguageSwitcher.razor`.
4. Confirm the code is in `SupportedLocales` in `src/Api/Controllers/AuthController.cs`
   (already includes `en, es, fr, de, pt`).

That's all — the DB column, the `/api/auth/locale` endpoint, and globalization data already
accept any of the five. No other code changes.

> ⚠️ **Translate the email + auth copy with a native speaker before release.** Machine
> translation is fine as a starting point, but this microcopy carries nuance.

## Rebranding note
The `.resx` files contain the brand name ("Perezosoft") and product copy in **each language**
— they're part of the rebranding checklist (`docs/REBRANDING.md`), not just the razor markup.
