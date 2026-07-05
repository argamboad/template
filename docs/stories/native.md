# Stories — Native (MAUI) client feature parity (`NATIVE`)

> One file per epic. Brings the **MAUI Blazor Hybrid** clients (Android, Windows, **iOS, macOS**) to
> **full parity** with web: every feature verified working on native, WebView-specific gaps closed, the
> native build + UI tested in CI, and **signed, shippable artifacts**. Design decision + accepted costs
> in **ADR-018**. Stories use Gherkin acceptance criteria. **Status: 📝 PLANNED (full-parity scope).**

**Epic key:** `NATIVE`

**The nature of the work (read first).** MAUI here is **Blazor Hybrid** reusing the shared RCL
(`Shared.Ui`), so the native shells already render *every* web screen inside a native WebView, and auth
is already native (OTP, OAuth via system browser, MFA step-up MFA-4, secure-storage tokens). So this epic
is **mostly verification + native-glue + CI/distribution plumbing, not rebuilding features.** The work is:
(1) guard the native build, (2) close the handful of places a WebView differs from a browser, (3) verify
the full feature surface on every platform, (4) test it automatically, (5) ship signed artifacts.

**Prerequisites (external — these are the real cost of "everything", per ADR-018):**
- A **macOS CI runner** (GitHub-hosted `macos-latest`) — required to build/test/sign **iOS + macCatalyst**.
- An **Apple Developer account** ($99/yr) — iOS/macOS signing certs + provisioning profiles.
- **Signing material as repo secrets** (never committed): Android keystore, Windows code-sign cert, Apple
  cert + profile (base64-encoded). Loaded at build time, same discipline as `.env` (ADR-001).
- Android SDK + the `.NET maui` workloads on the runners.

**Current baseline:** targets `net10.0-android` + `net10.0-windows`; not built in CI; QA covers 13
auth-focused desktop/Android cases (`QA-DSK-01..07`, `QA-AND-01..06`). See `docs/MOBILE_TESTING.md`.

---

## Wave 1 — Guardrails (build gate + audit)

### NATIVE-1 — Build MAUI in CI, all target platforms

**Status: ✅ Implemented** (`feat/native-1-ci-build-gate`). `net10.0-ios` + `net10.0-maccatalyst` TFMs
restored, **host-conditional** (iOS/MacCatalyst only on macOS hosts, mirroring the Windows pattern — a
Windows box without a paired Mac can't build them, and `dotnet build src/Maui` with no `-f` must never
try a target the host can't produce; the `Platforms/iOS` + `Platforms/MacCatalyst` scaffolds were already
in git). Two ci.yml jobs: **`native-build`** (matrix: Android on `ubuntu-latest` + JDK 17, Windows on
`windows-latest`; `dotnet workload install maui-<platform>` → compile-only `dotnet build -f <tfm>`) runs
on every trigger; **`native-build-apple`** (iOS + MacCatalyst on `macos-latest`) runs on **develop pushes
only** — a free-tier amendment: macOS runners bill **10×** minutes on a private repo, so per-PR Apple
builds would burn the quota for little added signal (rot is still caught within one merge). **Lockfile
resolved by documented exclusion:** `RestorePackagesWithLockFile=false` for the Maui project — the
host-conditional TFM list makes the resolved graph differ per OS, so one committed lockfile can never
satisfy locked-mode on all three runners; CPM alone pins its versions. (Side-benefit: kills the
recurring untracked `src/Maui/packages.lock.json`.) Android + Windows verified locally; Apple legs
verified on the post-merge develop run.

**As a** maintainer
**I want** the MAUI app compiled in CI on every PR, for every target platform
**So that** a change that breaks the native build fails the PR instead of rotting silently until a manual run

**Context / notes:** add `net10.0-ios` + `net10.0-maccatalyst` to the target frameworks. A CI matrix:
Android on `ubuntu-latest`, Windows on `windows-latest`, iOS + macCatalyst on `macos-latest`
(`dotnet workload install maui-*`, then `dotnet build src/Maui -f <tfm>`). Compile-only — no emulator, no
signing (that's Wave 3/4). CPM lockfile: MAUI's `packages.lock.json` is currently **excluded** from CI
(it regenerates on full-solution builds) — this slice brings it under control or documents the exclusion.

**Acceptance criteria**

```gherkin
Scenario: The native build is a required check
  Given a PR that breaks the MAUI build (any target platform)
  When CI runs
  Then the native-build job fails and blocks the merge

Scenario: All four platforms compile
  Then android, windows, ios, and maccatalyst each build in CI on their respective runners
```

**Out of scope:** running the app; signing; tests. **DoD:** matrix builds green on a clean PR; MAUI
lockfile handled deterministically; ADR-018 referenced.

### NATIVE-2 — Native-concerns audit → `docs/NATIVE_PARITY.md`

**Status: ✅ Implemented** (`docs/native-2-parity-audit`). Source-inspection audit at `develop@198319d`
covering every concern below + all shared-RCL screens (incl. the post-plan BILLING-8 billing page and
ADMIN-3 announcements). **Six gaps registered (G1–G6), each mapped to a Wave-2 slice**; uncertain cells
marked 🔍 for the NATIVE-6 device pass (all iOS/macCatalyst cells implicitly 🔍 — never run yet).
Highlights: G1 GDPR-export `target=_blank` download dead-ends the WebView (→ NATIVE-3); G3 Android
hardware back exits the app (→ NATIVE-4); **G5 (discovered): a native member cannot join a household** —
`Join.razor` reads the token from the query string only, no manual entry, and invite emails link to the
web origin (→ **new slice NATIVE-4b**: token-entry input on `/join`, benefits web too); G6 language
switching is inert on native (no culture bootstrap in `src/Maui`; → NATIVE-5). Confirmed-working: MFA QR
scripts included in both `index.html` hosts, native refresh/impersonation-stop, OAuth callback scheme,
bell polling (C# `PeriodicTimer`), secure-storage sessions; magic link correctly hidden on native.
Also produced three **maintainer rules** (keep the two `index.html` hosts in sync; emailed links land on
web — flows need an in-app path; `forceLoad` external nav = leaving the app).

**As a** developer
**I want** a written matrix of every place WebView-hosted Blazor differs from browser Blazor
**So that** Wave 2 fixes real, enumerated gaps instead of guessing

**Context / notes:** produce `docs/NATIVE_PARITY.md` — a table over: file **download/upload/share**,
external links / `mailto` / `target=_blank`, Android **hardware back**, deep links / OAuth callback,
clipboard, **culture/RTL**, **safe areas / status bar / window sizing**, session-across-restart, cold
networking (dev `adb reverse` vs prod URL), and per **feature** (household, invites, settings, notifications,
MFA, admin, GDPR export, files). Each cell: ✅ works / ⚠️ gap / N-A, with a note. This is the scoping artifact.

**Acceptance criteria**

```gherkin
Scenario: Every WebView-vs-browser delta is enumerated
  Then docs/NATIVE_PARITY.md lists each concern × platform with a done/gap/N-A verdict
  And each ⚠️ gap maps to a Wave-2 slice (or is explicitly deferred)
```

**Out of scope:** fixing the gaps (Wave 2). **DoD:** the matrix is complete and drives the Wave-2 backlog.

---

## Wave 2 — Close the WebView gaps (each built only if the audit confirms it)

### NATIVE-3 — File download / upload / share in the WebView

**Status: ✅ Implemented (download half)** (`feat/native-3-download-bridge`). Two-part fix, and it
improved **web** too: the audit found the signed URL was served **inline** (no Content-Disposition), so
even the browser flow showed raw JSON in a `_blank` tab rather than downloading. (1) `FilesController`
now serves signed files with `Content-Disposition: attachment`, named by the storage key's basename
(server-controlled). (2) The export anchor became a reusable **`IFileDownloadLauncher`** seam:
`BrowserFileDownloadLauncher` (web) navigates same-tab — a real download, the page stays, no
popup-blocker exposure; `ShareFileDownloadLauncher` (MAUI) fetches the bytes to the app cache and opens
the **OS share sheet** (core MAUI `Share`, all four targets; filename from the header, sanitized to a
basename). TDD: red-first `FilesControllerTests.ValidToken_SetsAttachmentFilenameFromKeyBasename` +
new E2E `Owner_Downloads_The_Data_Export` (asserts a real browser download lands as `.json` and the
page is not navigated away); suite 28→29, all green. **Upload half stays N-A** per the audit — no UI
consumer of `IFileStorage` upload exists yet; build it with the first feature that needs an uploader.
Share-sheet UX gets its device pass in NATIVE-6.

**As a** native user
**I want** downloads (GDPR export, future attachments/avatars) and uploads to work
**So that** file features aren't silently broken on native (a browser download won't "just happen" in a WebView)

```gherkin
Scenario: Download a signed-URL file on native
  Given a signed download URL (e.g. the GDPR export)
  When I trigger it in the native app
  Then the file is saved/shared via the platform (not a dead WebView navigation)

Scenario: Upload a file on native
  When a feature needs a file picker
  Then the native picker opens and the file uploads through the same API
```

**DoD:** download + upload verified on Android + one desktop target; a reusable native file bridge; ADR-018.

### NATIVE-4 — External links, mailto, and back-navigation

**Status: ✅ Implemented** (`feat/native-4-back-and-return`). **G3 (Android back):**
`MainPage.OnBackButtonPressed` walks the WebView history (which contains Blazor's pushState route
changes) via `CanGoBack()`/`GoBack()`, falling through to the default exit only at the history root.
**G2 (external return-trip):** external opens were already correct (BlazorWebView's default
`UrlLoading` hands external hosts to the system browser; the provider's redirect landing on web is
accepted by design until https App Links — G4). The stale-page half is fixed with a small
`AppResumeNotifier` seam in the RCL: MAUI fires it from the window's `Resumed` **and** `Activated`
lifecycle events (Android returns from the browser via onStop→Resumed; desktop only loses focus, so
the return is Activated), and `/billing` subscribes to refetch its summary on return — web registers
the notifier but never fires it (its return path is a full redirect). **Windows verification** (CDP +
API request log): launch-Activated→Notify proven from the lifecycle log; Notify→billing-refetch proven
live (timer-fired Notify produced matching `GET /api/billing` requests); a genuine focus transition
can't be synthesized from a background shell (Windows foreground-lock), so the human-click case +
Android land in NATIVE-6. Web regression: full E2E 29/29 green. `mailto:` cells were already ✅ (none
exist in the RCL).

**As a** native user
**I want** external links to open in the system browser and the Android back button to behave
**So that** the app doesn't trap me in the WebView or dead-end on a `target=_blank`

```gherkin
Scenario: External link opens the system browser
  When I tap a target=_blank or mailto link
  Then it opens outside the WebView, and in-app navigation stays in the app

Scenario: Android hardware back
  When I press the device back button
  Then it navigates the in-app history, and exits only at the root
```

**DoD:** link routing + hardware-back verified on Android; ADR-018.

### NATIVE-4b — Join a household by invite code (audit gap G5)

**Status: ✅ Implemented** (`feat/native-4b-join-by-code`). Bare `/join` (no `?token=`) now renders an
invite-code entry form instead of the old "invalid link" dead-end — the entry point already existed (the
Household page's "Have an invite?" button links to `/join`), so the whole fix is in `Join.razor`: the
accept call is shared between the URL-token path (unchanged) and the pasted-code path; failures on the
form are inline + retryable (the URL path keeps its terminal error state). Unauthenticated visitors get
the existing sign-in-first redirect for both paths. No API change — the email already carries the raw
token as a copy fallback. EN+ES strings added (`Join_EnterCode*`); the now-unreachable
`Join_MissingToken`/`Join_InvalidTitle` strings removed. TDD: two new Playwright journeys
(`Member_Joins_By_Pasting_The_Invite_Code`, `Pasting_An_Invalid_Code_Shows_An_Inline_Error`) written
red-first; suite 26→28, full local run green. Android device verification lands in NATIVE-6.

**As a** native user invited to a household
**I want** to enter the invite code from the email directly in the app
**So that** I can join at all — the emailed `/join?token=…` link opens the web app, and a native app has
no address bar to reach it

**Context / notes:** discovered by the NATIVE-2 audit: `Join.razor` reads the token **only** from the
query string and invite emails link to `Auth:AppBaseUrl` (web); the email's raw-token fallback has
nowhere to be pasted. Add a token-entry input on `/join` (reachable from the app, e.g. via Household or
the nav) — this benefits **web** too (email clients that mangle links). Optional later: https App
Links/Universal Links.

```gherkin
Scenario: Join with a pasted invite code
  Given I received an invitation email
  When I open the app's Join screen and paste the invite code
  Then I join the household exactly as the emailed link would have

Scenario: Web keeps working
  When I open the emailed /join?token=… link in a browser
  Then the flow is unchanged
```

**DoD:** token entry verified on Android + web regression (E2E roster journey still green); ADR-018.

### NATIVE-5 — Localization + theming/layout polish per platform

**Status: ✅ Implemented** (`feat/native-5-culture-bootstrap`). The audit's G6 wording was corrected
during the build: the runtime switch already worked on native (the switcher sets the in-process culture
before the WebView reload) and signed-in users were already reconciled to their server locale by
`MainLayout` — the real gap was **cold-start persistence for anonymous users** (the choice lived in
WebView localStorage, unreadable from C# at native startup). Fix: `ICulturePersistence` seam in the RCL
(web impl → the same localStorage key, behavior unchanged; MAUI impl → OS `Preferences`) and a
`MauiProgram` bootstrap that applies the saved culture before first render (OS-culture fallback, per
`docs/LOCALIZATION.md`). The web bootstrap also gained a `CultureNotFoundException` guard.
**Verified on real native Windows** by driving the app's WebView2 over CDP
(`--remote-debugging-port`): EN → switch ES (renders Spanish) → kill → cold start → **still Spanish**
on `/login` (anonymous page, OS culture en-US — only the new bootstrap explains it), then switched back
to EN. Desktop window sizing 🔍 resolved for Windows (usable ~1140×571 default). Web regression: full
E2E 28/28 + Core/Api suites green. Android/RTL/safe-area cells stay with NATIVE-6 (RTL has no shipped
language; safe-areas need a device).

**As a** native user
**I want** device culture (incl. RTL) and correct safe-areas / status bar / window sizing
**So that** the app looks and reads right on each platform, matching web

```gherkin
Scenario: Device locale drives the UI
  Given the device is set to Spanish
  Then the app renders in Spanish (matching web i18n), RTL where applicable

Scenario: Platform chrome is correct
  Then mobile respects safe areas + status bar, and desktop opens at a sensible window size
```

**DoD:** verified on Android + Windows (+ iOS/macOS once runners exist); ADR-018.

---

## Wave 3 — Verification (manual + automated)

### NATIVE-6 — Native QA pass: expand the QA plan to the full feature surface

**Status: ✅ Authored** (`docs/native-6-qa-plan`) — **execution pending a device pass** (needs the
maintainer's hardware; Apple cases need a Mac). `docs/QA_TEST_PLAN.md` grew 96→117 cases:
**QA-DSK-08..14** (desktop per-feature parity: join-by-code, culture persistence, export share,
billing return-refresh, MFA native step-up, bell/prefs, admin console), **QA-AND-07..13** (the same
plus the Android-only hardware back 🔴 and share sheet, and the Android-15 edge-to-edge check that
resolves the audit's 🔍 safe-area cell), the first-ever **iOS/macCatalyst smoke** (§13b,
QA-IOS-01..04 + QA-MAC-01..03 — G7 boot, OTP, core-flow spot, first OAuth run), and a per-release
**native release checklist** (§13c). Traceability matrix + per-client coverage + release gate updated;
both QA PDFs regenerated (B11-8 gate green). **Authoring found G7** — iOS/macCatalyst crashed at boot
(no `IOAuthInitiator` registered; `GetRequiredService` throws) — fixed separately
(`fix/native-6a-apple-boot`): the WebAuthenticator initiator generalized to
Android+iOS+macCatalyst + the callback scheme registered in both Apple Info.plists. The slice
completes when the pass is **run**: results go in the §16 sign-off (run log).

**As a** QA tester
**I want** per-feature native cases (not just the 13 auth-focused ones)
**So that** "works on native" is deliberately verified, not merely inherited from the shared RCL

**Context / notes:** extend `docs/QA_TEST_PLAN.md` §11–13 with a native case per feature area (household,
invites, settings, notifications, MFA, admin, GDPR, files) × {Android, Windows, iOS, macOS} smoke, and a
native release checklist. Regenerate the QA PDFs (B11-8 gate).

```gherkin
Scenario: Full native regression exists
  Then each web feature has a matching native case in §11–13 for each supported platform
```

**DoD:** QA plan expanded + PDFs regenerated; a native smoke suite a human can run per release.

### NATIVE-7 — Automated native UI tests in CI

**Status: ✅ COMPLETE for the non-Apple platforms — both smokes green in CI** (develop run
after #116; Windows leg green 4× consecutively). Getting the Android leg green took five CI
iterations, each fixing one runner-environment delta the local rehearsal couldn't expose — the
sequence is instructive: (1) silent 45-min hang → hardened every phase with deadlines + narration +
`if: always()` diagnostics (a timed-out job archives NO logs); (2) `Unknown AVD name` → avdmanager
and the emulator resolve the AVD home differently on runners, pinned via `ANDROID_AVD_HOME`;
(3) `adb: command not found` → sdkmanager-installed platform-tools isn't on the runner's PATH.
The Windows leg needed one fix (assert `Attached`, not `Visible` — the runner's narrow window
collapses the header). Originally implemented as:

**Windows half** (`feat/native-7-windows-smoke`). No Appium: the smoke
drives the REAL MAUI app's WebView over the **Chrome DevTools Protocol** (the recipe proven during
Wave 2) — the app launches with remote debugging enabled and Playwright attaches with
`ConnectOverCDPAsync`, reusing the E2E project's page test-ids and Mailpit helper.
`NativeSmokeTests` (`[Explicit]` + `Category=NativeSmoke`, so the browser e2e job never runs it):
CDP connect w/ retry → login renders (a G7-style boot crash dies here) → OTP sign-in end-to-end →
Household loads over the native Bearer path. New CI job **`native-smoke-windows`** (develop pushes
only — Windows bills 2×, ~10 min; NOT in deploy-staging needs so native flake can't block web
deploys): preinstalled-Postgres + downloaded Mailpit + API on plain HTTP + the built exe, pointed at
the stack via the new **`PEREZOSOFT_API_BASE_URL`** override in `MauiProgram` (also useful for
physical-device testing against a LAN API). Verified locally with the exact CI shape (smoke green in
5 s against the live app). **Android leg ✅ Implemented**
(`feat/native-7b-android-smoke`): Android WebView's CDP lacks the browser-context management
`ConnectOverCDPAsync` needs, so this leg uses **playwright-core's Node-only `_android` module**
(adb + WebView attach) — a tiny committed spec in `tests/native-smoke-android/` mirroring the
Windows journey; CI job `native-smoke-android` hand-rolls the emulator from the runner's
preinstalled SDK (no marketplace action). Two gotchas encoded: Debug APKs must build with
`EmbedAssembliesIntoApk=true` (a fast-deployment APK silently fails to start from `adb install`),
and the smoke waits for the app process before attaching. **Rehearsed green on a real local
emulator** (boot + OTP + roster). The same PR adds the **`native-paths` cost gate** (docs-only
develop pushes skip the Apple builds + both smokes — the 2026-07-03 sprint exhausted the month's
free Actions minutes in a day) and **deploy-staging concurrency** (back-to-back merges cancel the
older deploy's version-gated smoke instead of failing it). **Remaining:** the iOS-simulator leg
(parked with the Apple pin).

**As a** maintainer
**I want** the native critical paths driven automatically against a real emulator/simulator
**So that** native regressions are caught without a manual pass

**Context / notes:** stand up a native UI-test harness — **Appium** (or .NET MAUI UITest) driving the
**Android emulator** on CI and a desktop target, covering the auth-critical journeys (OTP sign-in, OAuth,
MFA step-up, a core feature flow). **Accepted risk (ADR-018):** native UI tests are slower + flakier than
Playwright-web; budget retries + generous timeouts, and keep the suite small (smoke, not exhaustive). iOS
simulator tests run on the macOS runner.

```gherkin
Scenario: Native smoke runs on every push
  Given the Android emulator (and iOS simulator) in CI
  When the native UI smoke runs
  Then OTP sign-in + a core feature flow pass headlessly, with retries for flakiness
```

**DoD:** an Android emulator smoke green in CI; iOS simulator smoke on macOS runner; flakiness mitigations documented.

---

## Wave 4 — Distribution (signed, shippable artifacts)

### NATIVE-8 — Android: signed AAB/APK in CI

```gherkin
Scenario: A signed Android artifact is produced
  Given the release keystore (from repo secrets, never committed)
  When the release workflow runs on a tag
  Then a signed .aab is built and uploaded as an artifact
```

**DoD:** signed AAB from CI; keystore in secrets; ADR-018.

### NATIVE-9 — Windows: MSIX package + code-signing

```gherkin
Scenario: A signed MSIX is produced
  Then a code-signed MSIX is built in CI (cert from secrets) and uploaded
```

**DoD:** signed MSIX artifact; ADR-018.

### NATIVE-10 — iOS + macOS: signed IPA / pkg (Apple)

**Context / notes:** needs the Apple Developer account + certs/provisioning profiles (secrets, base64),
built + signed on the macOS runner. iOS `.ipa` + macCatalyst `.pkg`.

```gherkin
Scenario: Signed Apple artifacts are produced
  Given Apple signing material in secrets and the macOS runner
  Then a signed .ipa (iOS) and .pkg (macCatalyst) are built and uploaded
```

**DoD:** signed Apple artifacts from CI; ADR-018.

### NATIVE-11 — (optional) Store submission

**Context / notes:** automate (or document the manual path for) Play Console / App Store Connect / MS Store
upload. Store review + accounts are external; the template ships the upload plumbing behind flags/secrets.

**DoD:** upload step wired (guarded/off by default) or the manual submission path documented per store.

---

## Slice plan (sequenced — guardrails first, distribution last)

1. ✅ **NATIVE-1** build gate — DONE (all four TFMs; Apple legs on develop pushes, see the free-tier
   amendment above) + ✅ **NATIVE-2** audit — DONE (`docs/NATIVE_PARITY.md`, incl. the post-plan
   BILLING-8/ADMIN-3 screens): six gaps G1–G6 registered, each mapped to a slice below.
2. ✅ **Wave 2 COMPLETE** — NATIVE-4b (G5 join-by-code), NATIVE-5 (G6 culture bootstrap; Windows
   window-sizing 🔍 resolved), NATIVE-3 (G1 downloads — attachment disposition +
   `IFileDownloadLauncher`; upload half N-A, no consumer yet), NATIVE-4 (G2 refresh-on-resume via
   `AppResumeNotifier` + G3 Android back handler). All six audit gaps closed; OS-chrome behaviors
   (share sheet, hardware back, real focus transitions) queue for the NATIVE-6 device pass.
3. 🚧 **NATIVE-6** manual native QA pass — plan authored (117 cases incl. iOS/macCatalyst first-run
   smoke + §13c release checklist; G7 Apple-boot fix shipped alongside); **execution needs the
   maintainer's devices** (Apple column explicitly PINNED by the maintainer until Apple hardware is
   available — 2026-07-03). **NATIVE-7** Windows smoke ✅ in CI (WebView2-CDP `native-smoke-windows`,
   develop pushes); Android-emulator leg 📝 next; iOS-simulator leg parked with the Apple pin.
4. 📝 **NATIVE-8/9/10** signing + packaging per platform, then **NATIVE-11** submission (optional).

Each slice is an independent, mergeable PR (branch off develop; TDD/verification per slice). Waves gate:
don't automate (7) or distribute (8–11) before the app is verified working (6).

**Known sharp edges (from ADR-018):**
- **iOS/macOS need a Mac** — no macOS runner ⇒ NATIVE-1/7/10 can't cover Apple platforms; sequence Apple
  work once the runner + Apple account exist.
- **Signing material is secret** — keystores/certs/profiles live in repo secrets (base64), never the repo;
  gitleaks stays green.
- **Native UI tests are flaky** — keep the automated suite to smoke, with retries; manual QA (NATIVE-6)
  remains the broader safety net.
- **Parity ≠ more** — this epic makes native do what **web** does. OS push notifications, biometrics, and
  other native-only features are **beyond parity** and explicitly out of scope here (own future epics).
- **Web-first still holds** — new features land + prove on web first (golden rule 5); this epic keeps
  native *caught up*, it doesn't invert the order.
