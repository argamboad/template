# Native parity audit (NATIVE-2, ADR-018)

> The scoping artifact for the NATIVE epic's Wave 2/3: every place **WebView-hosted Blazor (MAUI
> Hybrid)** differs from **browser Blazor (WASM)**, audited concern-by-concern and screen-by-screen.
> Verdicts: ✅ works (verified in code) · ⚠️ gap (mapped to a slice) · 🔍 likely-OK, **verify on
> device** (NATIVE-6) · **N-A** not applicable.
>
> Method: source inspection at `develop@198319d` (grep + code-path tracing of `src/Shared.Ui`,
> `src/Maui`, the API's link-building services, and both `index.html` hosts). Static analysis can't
> prove rendering/OS behavior — those cells are 🔍 and become checklist items in the NATIVE-6 QA
> pass. Platforms: **Android**, **Windows**; iOS/macCatalyst compile in CI (NATIVE-1) but have never
> been run — every cell is implicitly 🔍 there until NATIVE-6 gets Apple hardware.

## 1. Cross-cutting concerns

| Concern | Verdict | Evidence / note |
|---|---|---|
| **File download** (GDPR export signed URL) | ✅ **G1 — fixed (NATIVE-3)** | Was: the export anchor used `target=_blank`, which Android WebView drops silently, and the signed URL was served **inline** (no Content-Disposition) so even a browser showed raw JSON in a tab. **Fixed end-to-end:** `FilesController` now serves signed files as `Content-Disposition: attachment` (named by the key basename), and the anchor became an `IFileDownloadLauncher` seam — web navigates same-tab (real download, page stays, no popup risk), MAUI fetches the bytes and opens the **OS share sheet** (`ShareFileDownloadLauncher`, all four targets; filename from the header, sanitized). E2E: `Owner_Downloads_The_Data_Export` asserts a real browser download. Share-sheet UX device-checked in NATIVE-6. |
| **File upload** | N-A (today) | No UI consumer of `IFileStorage` upload exists yet (FILES is API-only). Becomes NATIVE-3's second half the day a feature adds an uploader. |
| **External navigation** (billing checkout/portal) | ⚠️ **G2** | `Billing.razor:126,147` does `Nav.NavigateTo(providerUrl, forceLoad: true)` to the Stripe-hosted page. BlazorWebView's default `UrlLoading` policy opens **external hosts in the system browser** (app keeps running) — but the provider's post-checkout redirect returns to **`Auth:AppBaseUrl` (the web app)**, not the native app; the native `/billing` page only refreshes on next visit. Flow completes, return-trip UX is web. → **NATIVE-4** (verify default-external-open on device; document/handle the return) |
| **`mailto:` / other `target=_blank`** | ✅ | Grep: the export link is the **only** `_blank` and there are no `mailto:` links in the RCL. Nothing else to route. |
| **Android hardware back** | ⚠️ **G3** | No `OnBackPressed`/`OnBackInvoked` handling anywhere in `src/Maui`. MAUI default: the back button backgrounds/exits the Activity instead of navigating Blazor history — every mis-tap dumps the user out. → **NATIVE-4** |
| **Deep links into the app** | ⚠️ **G4** (by omission) | The only registered scheme is the OAuth callback (`perezosoft://`, `WebAuthenticatorCallbackActivity`). **Emailed links (invite `/join?token=…`, magic link) always open the web app** — there is no https App Link/Universal Link. Magic link is web-only **by design** (documented); the invite link is not — see G5. |
| **Join-by-invitation on native** | ✅ **G5 — fixed (NATIVE-4b)** | Was: `Join.razor` read the token **only from the query string**, no manual entry, and a native app has no address bar — a native-only member could not join. **Fixed:** bare `/join` (reachable via the Household page's "Have an invite?" button) now shows an invite-code entry form that accepts the raw token from the email; failures are inline + retryable. Works on web too (mangled-link fallback). E2E: `Member_Joins_By_Pasting_The_Invite_Code` + invalid-code case. https deep links remain optional-later (G4). |
| **Culture / i18n** | ✅ **G6 — fixed (NATIVE-5)** | Correction to the original audit wording: the **runtime** switch already worked on native by design (`LanguageSwitcher` sets the in-process culture before the WebView reload), and signed-in users were already reconciled to their server locale by `MainLayout`. The real gap was **cold-start persistence for anonymous users**: the choice was saved to WebView localStorage, which C# can't read when the native process boots — so it died with the process. **Fixed:** `ICulturePersistence` seam (web → localStorage, unchanged behavior; MAUI → OS `Preferences`) + a bootstrap in `MauiProgram` that applies the saved culture before first render (OS-culture fallback unchanged). Verified on real native Windows via WebView2 CDP: switch to ES → kill → cold start → still ES on `/login` (anonymous, OS culture en-US — only the bootstrap can explain it). |
| **RTL** | 🔍 | No RTL language is shipped (EN/ES); revisit when one is. |
| **Safe areas / status bar / edge-to-edge** | 🔍 | `MainPage.xaml` is a bare `BlazorWebView` (no `SafeArea`/padding). Android 15 (API 35) enforces edge-to-edge — content may draw under the status bar. Prior QA (AND-05) passed, so likely acceptable; **verify on a modern device** in NATIVE-6 (fix as a follow-up if it fails). |
| **Desktop window sizing** | ✅ Win / 🔍 mac | Windows verified during the NATIVE-5 device check: WinUI default opens at a usable ~1140×571 WebView, login renders and is fully interactive. macCatalyst still 🔍 (NATIVE-6). |
| **Session across restart** | ✅ | `SecureStorageSessionStore` (OS secure store) + body-transport refresh (`AuthService.RunRefreshAsync` native branch). QA-DSK-03/AND-03 cover it. |
| **Auth: OTP / OAuth / MFA step-up** | ✅ | All native-wired and previously verified: OTP + OAuth via system browser (`LoopbackOAuthInitiator` desktop, custom scheme Android), MFA-4 native step-up, provider discovery (`GET /api/auth/providers`) works over the native client (anonymous endpoint). Magic link N-A by design. |
| **Impersonation (admin)** | ✅ | In-memory token swap (`BeginImpersonation`); **Stop** calls `TryRefreshAsync`, whose native branch refreshes from SecureStorage — the staff identity restores without a cookie. Banner reads the `impersonated_by` claim (shared UI). 🔍 spot-check on device (NATIVE-6). |
| **JS interop / vendored scripts** | ✅ | `src/Maui/wwwroot/index.html` includes the same `_content/Template.Shared.Ui/js/qrcode-generator.min.js` + `mfa-qr.js` as web — the MFA QR renders natively. (Keep the two `index.html` files in sync — noted as a maintainer rule below.) |
| **Clipboard** | N-A | No copy-to-clipboard buttons exist (MFA manual key is selectable text; WebView selection works). |
| **Polling (bell)** | ✅ | `NotificationBell` uses a C# `PeriodicTimer` (60 s) — no browser API dependency. |
| **Dev networking** | ✅ (documented) | Android dev needs `adb reverse` (auto-run by the csproj target on deploy); prod points at the real URL. `docs/MOBILE_TESTING.md`. |

## 2. Per-feature screens (all shared-RCL — render everywhere; deltas only)

| Screen | Android | Windows | Delta notes |
|---|---|---|---|
| Login (OTP, OAuth, MFA step-up) | ✅ | ✅ | Fully native-wired; QA-AND/DSK cover. The magic-link button is already hidden on native (`@if (!Auth.IsNative)` in `Login.razor`) — correct by construction. |
| Home / AppHeader / bell | ✅ | ✅ | Polling is C#; mark-read etc. are plain fetches. |
| Household (roster, roles, invite) | ✅ | ✅ | Invite **send** works; the invited member's **join** was G5 — fixed (NATIVE-4b). |
| Household → Data export | ✅ | ✅ | Was ⚠️ G1 — fixed by NATIVE-3 (attachment disposition + download-launcher seam; native = share sheet). 🔍 share-sheet device pass in NATIVE-6. |
| Join | ✅ | ✅ | Was ⚠️ G5 (query-string-only token) — fixed by NATIVE-4b's invite-code entry form. 🔍 device pass in NATIVE-6. |
| Settings (linked accounts, prefs, MFA card, danger zone) | ✅ | ✅ | Link-provider has a native branch (`LinkProviderAsync`); MFA QR scripts included; delete-account is a plain API call. 🔍 device pass in NATIVE-6. |
| Billing (BILLING-8) | ⚠️ G2 | ⚠️ G2 | Summary renders; Upgrade/Manage leave for the system browser; return-trip lands on web. |
| Admin console + impersonation (ADMIN-1..3) | ✅ | ✅ | Staff probe + announce form are plain fetches; impersonation verified by code path. 🔍 device spot-check. |
| Language switcher | ✅ | ✅ | Was ⚠️ G6 — fixed by NATIVE-5 (Preferences persistence + MauiProgram bootstrap); Windows verified via CDP, Android device pass in NATIVE-6. |

## 3. Gap register → Wave-2 backlog (drives the epic)

| Gap | What breaks | Fix slice | Sketch |
|---|---|---|---|
| **G1** ✅ | GDPR export (any future signed-URL download) dead/awkward in WebView | **NATIVE-3 — shipped** | Intercept/route download URLs to a native save/share bridge (per-platform: Android `Share`/MediaStore, Windows save dialog); or open externally as a stopgap. |
| **G2** | Billing checkout/portal round-trip returns to web, not the app | **NATIVE-4** | Confirm default external-open on each platform; on return, poll/refresh the billing summary when the app foregrounds; document the web-return as accepted if so decided. |
| **G3** | Android back button exits instead of navigating | **NATIVE-4** | Handle back → `blazorWebView` JS `history.back()` when the WebView can go back, else default. |
| **G5** ✅ | Native member cannot join a household | **NATIVE-4b — shipped** | Token-entry input on `/join` (benefits web too — email clients that mangle links); optionally https App Links later. |
| **G6** ✅ | Language switching inert on native; saved culture ignored | **NATIVE-5 — shipped** | Bootstrap culture in `MauiProgram`/`MainPage` from the same store the switcher writes (localStorage via WebView, or move the pref to `Preferences` behind an abstraction), apply before root component renders, and re-apply on switch. |
| 🔍 edge-to-edge / window sizing | Cosmetic risk | **NATIVE-5** (only if device check fails) | SafeArea padding / default window size. |

**Explicitly N-A / deferred:** magic link on native (web-only by design); file upload (no UI consumer yet);
RTL (no RTL language shipped); clipboard (no copy affordances exist).

## 4. Maintainer rules surfaced by this audit

- **The two `index.html` hosts must stay in sync** (`src/Web/wwwroot/index.html` ↔
  `src/Maui/wwwroot/index.html`): any script/CSS the RCL depends on (e.g. the MFA QR vendor script)
  must be added to **both**, or the feature silently breaks on one host.
- **Every emailed link lands on `Auth:AppBaseUrl` (the web app)** — when adding an email that links
  into the product, either the flow must also be reachable in-app without a URL (like G5's token
  input) or it is web-only and should say so.
- **`Nav.NavigateTo(external, forceLoad: true)` means "leave the app" on native** — any new external
  round-trip (payment, OAuth-like flows) needs a return-trip story on native, not just a redirect URL.
