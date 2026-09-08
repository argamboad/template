# Stories — Local + self-hosted CI (`LOCALCI`)

> One file per epic. Makes the finished platform **cheaper to keep green**: the CI gates run on the
> maintainer's own machines, with GitHub-hosted runners as an always-available fallback you switch back
> to from repo settings — never with a commit. Design sketch in `docs/PLATFORM_BACKLOG.md` §13; sequenced
> in `docs/ROADMAP.md` (post-terminal wave). Stories use Gherkin acceptance criteria.
> **Status: 📋 PLANNED (2026-09-08)** — written pick-up-ready: every slice lists its exact edits,
> commands, tests, and doc touch-points. **Decision record: ADR-025 (draft below, paste at pickup).**

**Epic key:** `LOCALCI`

**Why now:** the platform reached terminal state on 2026-07-14 — nothing left to build, everything left
to *run*. The only running cost that hurts is GitHub Actions minutes on a **private** repo, where macOS
bills at **10×** and Windows at **2×**. The Apple build + smoke pair on every code push to `develop` is the
bulk of the spend, and the free plan's **2,000 min/month** goes in about ten develop pushes. Meanwhile
the maintainer owns a Windows desktop and a MacBook that can run every one of those jobs at zero minutes.

**The measured cost model (develop run `34254182634`, 2026-09-08 — a code push, every leg ran):**

| Job | Runner | Wall min | Multiplier | Billed min |
|---|---|---|---|---|
| native-build-apple (ios) | macos-26 | 1.8 | 10× | 18 |
| native-build-apple (maccatalyst) | macos-26 | 2.8 | 10× | 28 |
| native-smoke-apple | macos-26 | 8.7 | 10× | 87 |
| native-build (windows) | windows-latest | 5.0 | 2× | 10 |
| native-smoke-windows | windows-latest | 6.8 | 2× | 14 |
| e2e | ubuntu-latest | 9 | 1× | 9 |
| native-smoke-android | ubuntu-latest | 10 | 1× | 10 |
| native-build (android) | ubuntu-latest | 3.3 | 1× | 4 |
| build-test | ubuntu-latest | 2 | 1× | 2 |
| docker-build, secret-scan, license-scan, qa-artifacts, native-paths, deploy-staging | ubuntu-latest | ≤1 each | 1× | ~6 |
| **Total per develop code push** | | | | **≈ 190** (GitHub rounds each job UP to the minute) |
| PR run (no Apple legs, no smokes) | | | | **≈ 30** |

**macOS is ~70 % of the bill; Windows ~13 %; Ubuntu ~17 %.** Hence the slice order below: move the
Apple legs first, Windows second, and leave Ubuntu hosted unless it becomes worth the WSL setup.

**Current state (verified 2026-09-08 via `gh api`):** repo is `private`; **0 self-hosted runners**
registered; repo variables present: `STAGING_BASE_URL`, `POSTMAN_WORKSPACE_ID` (no `CI_*` yet).

**Prerequisites (external, before LOCALCI-1):**
- The repo **stays private** for as long as any self-hosted runner is registered (a fork PR would
  otherwise run arbitrary code on the maintainer's hardware — GitHub's own hard rule).
- Repo → Settings → Actions → General → *Fork pull request workflows*: **"Require approval for all
  outside collaborators"** (belt-and-braces; private repos have no stranger forks).
- Windows desktop (`x86_64`, Windows 11): Docker Desktop running, ≥ 30 GB free.
- MacBook: Xcode **26.6** installed (the workload set `10.0.400.1` pinned in `ci.yml` requires it —
  `dotnet workload restore` fails loudly on any other), the **iOS 26.5 simulator runtime** installed
  (Xcode → Settings → Components), Homebrew present.
- `gh` CLI authenticated as the repo owner (used for the verification steps).

**Rejected alternative (recorded so it isn't re-explored):** `nektos/act` — executes `ci.yml` in
Docker locally with zero drift, but on this stack it re-downloads the SDK per run (~10 min+), cannot run
the Windows leg (Linux containers), and the Postgres/Mailpit service containers need privileged
fiddling. Piece 1 + Piece 2 below cover its use cases better.

---

## ADR-025 draft — paste into `docs/DECISIONS.md` in LOCALCI-1's first commit

**ADR-025 — CI runner selection is variable-driven with a hosted fallback; deploy jobs stay hosted; a
local gate runner mirrors the PR-blocking jobs behind a drift tripwire. (2026-09-08; planned)**

*Context.* The platform is complete (ADR-024 / ADR-017 amendment) and its only recurring cost pressure
is Actions minutes on a private repo (macOS 10×, Windows 2×; ≈190 billed minutes per develop code push
against a 2,000/month free plan). The maintainer owns hardware that can run every job.

*Decision.* (1) Every non-deploy `runs-on` in `ci.yml` reads a per-OS repo **variable** with the current
hosted label as fallback — `${{ vars.CI_MACOS_RUNNER || 'macos-26' }}` and siblings for Windows/Linux.
Setting the variable routes that OS's jobs to a self-hosted runner; deleting it restores hosted runners.
The workflow file does not change between the two modes. (2) `deploy-staging` / `deploy-prod` keep
hardcoded hosted labels: they are short, they hold the Render deploy-hook secrets, and a deploy must not
depend on a desk being awake or expose the hook to it. (3) A repo-root `ci-local.ps1` mirrors the
PR-blocking jobs natively for the pre-push loop; an `EnforcementGateTests` fact pins its pinned versions
and gate list to `ci.yml` so the mirror cannot drift silently. (4) The repo remains private while any
runner is registered; runner tokens/labels live only in GitHub settings.

*Consequences.* Self-hosted runners are not clean machines — `global.json` (`rollForward: disable`), the
committed lockfiles, and the CLAUDE.md bump-together playbook remain the toolchain-drift authority, and
two steps gain idempotency (`DROP DATABASE IF EXISTS` before `CREATE`). Queued Apple jobs wait for the
Mac to be online (24 h expiry, one-click re-run). `DEVELOPER_DIR` keeps naming the Xcode the pinned
workload set needs; the Mac satisfies it with a symlink so the pin still tells the truth. Ubuntu jobs
stay hosted by default (cheap; the WSL runner is optional Phase B). Rollback = delete the variables.

---

### LOCALCI-1 — Switchable runners: per-OS repo variable → self-hosted, hosted fallback

**As a** platform maintainer
**I want** each OS's CI jobs to run on my own machine when a repo variable says so, and on GitHub's
runners otherwise
**So that** the 10×/2× minutes stop being billed while every gate still runs, and switching back is a
settings change, not a commit

**Context / notes:** `ci.yml` today has 13 `runs-on` sites (lines as of `89a22ec`): `build-test:18`,
`secret-scan:80`, `qa-artifacts:105`, `license-scan:142`, `docker-build:180`, `native-build:202`
(matrix), `native-paths:254`, `native-build-apple:295`, `native-smoke-apple:338`,
`native-smoke-windows:481`, `native-smoke-android:588`, `e2e:745`, `deploy-staging:845`,
`deploy-prod:893`. `EnforcementGateTests.SdkPin…` already reads `ci.yml` and forbids `dotnet-version:`;
the new gate lives next to it. Branch: `feat/LOCALCI-1-switchable-runners` off `develop`.

#### 1a. The `ci.yml` edit (one commit, no behaviour change until a variable exists)

Replace each non-deploy label with the expression for its OS. `vars` is a valid context for
`jobs.<id>.runs-on`; `||` returns the right operand when the variable is unset **or empty**, so an empty
variable also means "hosted".

| Job(s) | Before | After |
|---|---|---|
| build-test, secret-scan, qa-artifacts, license-scan, docker-build, native-paths, native-smoke-android, e2e | `runs-on: ubuntu-latest` | `runs-on: ${{ vars.CI_LINUX_RUNNER \|\| 'ubuntu-latest' }}` |
| native-smoke-windows | `runs-on: windows-latest` | `runs-on: ${{ vars.CI_WINDOWS_RUNNER \|\| 'windows-latest' }}` |
| native-build-apple, native-smoke-apple | `runs-on: macos-26` | `runs-on: ${{ vars.CI_MACOS_RUNNER \|\| 'macos-26' }}` |
| deploy-staging, deploy-prod | `runs-on: ubuntu-latest` | **unchanged** (hosted on purpose — ADR-025) |

`native-build` is a matrix; keep the matrix `os` values as-is (they are part of the job *names* the
branch-protection checks reference) and map in `runs-on`:

```yaml
    # LOCALCI-1: matrix.os stays the hosted label (it is in the job name); vars route it home.
    runs-on: ${{ (matrix.os == 'windows-latest' && vars.CI_WINDOWS_RUNNER) || (matrix.os == 'ubuntu-latest' && vars.CI_LINUX_RUNNER) || matrix.os }}
```

Add one comment block above `jobs:` explaining the scheme and pointing at `DEPLOYMENT.md` §10.
`native-paths` keeps its `if:`; `DEVELOPER_DIR: /Applications/Xcode_26.6.app` stays **unchanged** (see 1c).

#### 1b. Idempotency fixes for non-clean machines (same commit)

A hosted runner is fresh every run; a self-hosted one is not. `actions/checkout` still resets the
workspace (`clean: true` → `git clean -ffdx`), but anything **outside** the workspace persists:

- `native-smoke-windows` → "Start Postgres": today it requires a preinstalled `postgresql*` Windows
  service (hosted image, `postgres/root`). Make the step self-hosted-aware:
  ```powershell
  $svc = Get-Service postgresql* -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($svc) {
    Set-Service $svc.Name -StartupType Manual; Start-Service $svc.Name
    $env:PGPASSWORD = "root"; $psql = "$env:PGBIN\psql"; $psqlArgs = @("-U","postgres")
  } else {
    # Self-hosted (LOCALCI-1): a persistent container standing in for the image's service.
    if (-not (docker ps -a --format '{{.Names}}' | Select-String -Quiet '^ci-smoke-pg$')) {
      docker run -d --name ci-smoke-pg -e POSTGRES_PASSWORD=root -p 5432:5432 postgres:17 | Out-Null
    }
    docker start ci-smoke-pg | Out-Null
    foreach ($i in 1..30) { docker exec ci-smoke-pg pg_isready -U postgres *> $null; if ($LASTEXITCODE -eq 0) { break }; Start-Sleep 2 }
    $psql = "docker"; $psqlArgs = @("exec","ci-smoke-pg","psql","-U","postgres")
  }
  & $psql @psqlArgs -c "DROP DATABASE IF EXISTS smoke;"
  & $psql @psqlArgs -c "CREATE DATABASE smoke;"
  ```
  Keep `postgres:17` literal (the `EnforcementGateTests` pin check in LOCALCI-2 reads it).
- `native-smoke-apple` → "Start Postgres (Homebrew)": `brew install postgresql@17` is a no-op when
  present; add `"$PGBIN/dropdb" -h localhost --if-exists smoke` before `createdb`, and an `if: always()`
  final step `brew services stop postgresql@17 || true` so the Mac isn't left running Postgres.
- `native-smoke-apple` → simulator: the `xcrun simctl list devices available` lookup already prefers an
  existing iPhone; nothing to change, but the runner user must have booted Xcode once (license accepted:
  `sudo xcodebuild -license accept`).
- `native-smoke-android` → "Free disk space": the `sudo rm -rf /usr/share/swift …` paths are hosted-image
  bloat; guard the step with `if: ${{ !contains(runner.name, 'desk') }}` **or** leave Android on hosted
  (recommended — see Phase B). Do not run that step on a personal machine.
- Port collisions on the desk: the smoke/e2e jobs bind **5432, 5238, 5169, 1025, 8025**. The dev
  `docker-compose.yml` db + Mailpit and a running dev API hold the same ports. Rule (documented in §10):
  **stop the dev stack before letting the Windows runner take jobs** (`docker compose stop db mail`,
  stop F5 sessions). Add a fail-fast guard at the top of the Windows smoke's boot step:
  `foreach ($p in 5432,5238) { if (Get-NetTCPConnection -State Listen -LocalPort $p -EA SilentlyContinue | ? OwningProcess -ne (docker inspect -f '{{.State.Pid}}' ci-smoke-pg 2>$null)) { … } }`
  — simpler: `if ((Get-NetTCPConnection -State Listen -LocalPort 5238 -EA SilentlyContinue)) { Write-Error "port 5238 busy — a dev API is running on this runner"; exit 1 }`.
  Escalation if collisions keep biting: run the Windows runner inside a Hyper-V VM (out of scope here).

#### 1c. Runner installation (operator steps → `DEPLOYMENT.md` §10, written in this slice)

**Common to all runners**
1. Repo → Settings → Actions → Runners → **New self-hosted runner** → pick the OS → copy the download +
   `config` commands (the registration token expires in **1 hour**; re-open the page for a fresh one).
2. Labels: leave the defaults (`self-hosted`, `<os>`, `x64`/`ARM64`) and add **one custom label** per
   machine that the repo variable will name: `desk-win`, `desk-linux`, `mac-allan`. Never route on the
   bare `self-hosted` label (a future second machine would steal jobs it can't run).
3. `DOTNET_INSTALL_DIR`: `actions/setup-dotnet` on a self-hosted runner installs into a machine-wide path
   that may need admin. Pin it user-local in the runner's **`.env` file** (runner root, one `KEY=value` per
   line — the runner exports these into every job):
   - Windows: `DOTNET_INSTALL_DIR=C:\actions-runner\_tool\dotnet`
   - macOS: `DOTNET_INSTALL_DIR=/Users/<user>/actions-runner/_tool/dotnet`
   - Linux: `DOTNET_INSTALL_DIR=/home/<user>/actions-runner/_tool/dotnet`
   The MAUI workloads (`dotnet workload restore … --version 10.0.400.1`) install into that SDK, so no
   elevation and no collision with the desk's Visual Studio SDK.
4. Runner root on a fast local disk, **not** OneDrive / a synced folder (the `_work` tree churns).

**Windows desktop (`desk-win`)** — routes `native-build (windows)`, `native-smoke-windows`
```powershell
mkdir C:\actions-runner; cd C:\actions-runner
# download + unzip per the settings page, then:
.\config.cmd --url https://github.com/argamboad/perezosoft-platform --token <REG_TOKEN> `
  --name desk-win --labels desk-win --runasservice --unattended
# service "GitHub Actions Runner (argamboad-perezosoft-platform.desk-win)" starts automatically
```
The service account must be able to reach Docker Desktop (run as the logged-in user, which
`--runasservice` prompts for; **do not** run as LocalSystem — WebView2 needs a user session and Docker
Desktop's named pipe is per-user). Requirements the jobs assume: WebView2 Runtime (Windows 11 ships it),
Docker Desktop running (for `ci-smoke-pg`), ≥ 30 GB free.

**MacBook (`mac-allan`)** — routes `native-build-apple`, `native-smoke-apple`
```bash
mkdir -p ~/actions-runner && cd ~/actions-runner
# download + untar per the settings page, then:
./config.sh --url https://github.com/argamboad/perezosoft-platform --token <REG_TOKEN> \
  --name mac-allan --labels mac-allan --unattended
./svc.sh install && ./svc.sh start        # LaunchAgent: runs while this user is logged in
# ci.yml pins DEVELOPER_DIR to the versioned path the hosted image uses — satisfy it, don't edit it:
sudo ln -s /Applications/Xcode.app /Applications/Xcode_26.6.app
sudo xcodebuild -license accept
xcrun simctl list runtimes | grep iOS     # must list iOS 26.5 (install via Xcode → Settings → Components)
```
Why a symlink and not a variable: the `DEVELOPER_DIR` value is the *documented* Xcode requirement of the
pinned workload set (`ci.yml` comment block, `CLAUDE.md` bump playbook step ⑤). A symlink to the wrong
Xcode fails the build exactly like the hosted image would; a variable would let the two drift. When the
pin moves (next SDK bump), re-point the symlink — add that to the playbook's step ⑤.
Lid-closed behaviour: the LaunchAgent stops when the session sleeps; queued jobs wait (GitHub expires a
queued job after **24 h** → the run shows "cancelled", re-run from the Actions tab). Optional:
`sudo pmset -c sleep 0` while docked.

**Linux on the desktop via WSL2 (`desk-linux`) — optional Phase B**, routes the Ubuntu jobs
Only worth it if the ~30–40 Ubuntu min/push matter after Apple + Windows are home. Needs: WSL2 Ubuntu
with `systemd=true` in `/etc/wsl.conf` (for `./svc.sh`), Docker (Docker Desktop WSL integration or
`docker-ce` inside the distro — `build-test` uses Testcontainers, `e2e`/`native-smoke-android` use
`services:` which the runner materialises with the host's Docker), Python 3.12 (`qa-artifacts`), and a
way to keep the distro alive at logon (Task Scheduler: `wsl -d Ubuntu -- sleep infinity`). Leave
**`native-smoke-android` hosted** even in Phase B unless `/dev/kvm` exists in the distro (nested
virtualisation) — its "Free disk space" step is hosted-image-specific (1b).

#### 1d. The drift tripwire (test first — TDD)

`tests/Api.Tests/EnforcementGateTests.cs`, new fact `CiRunnerSelection_IsVariableDrivenWithHostedFallback`
(**R77**):

```csharp
[Fact]
public void CiRunnerSelection_IsVariableDrivenWithHostedFallback() // R77 (LOCALCI-1, ADR-025)
{
    var ci = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "ci.yml"));
    // Split into jobs on the 2-space-indented "  <id>:" lines; inspect each job's runs-on.
    var jobs = Regex.Split(ci, @"(?m)^(?=  [a-z-]+:\s*$)").Where(j => j.StartsWith("  ")).ToList();
    var hostedDeploys = new[] { "deploy-staging", "deploy-prod" };
    foreach (var job in jobs)
    {
        var id = Regex.Match(job, @"^  ([a-z-]+):").Groups[1].Value;
        var runsOn = Regex.Match(job, @"(?m)^\s+runs-on:\s*(.+)$").Groups[1].Value.Trim();
        if (hostedDeploys.Contains(id))
            Assert.True(runsOn == "ubuntu-latest", $"{id} must stay on a hosted runner (ADR-025): {runsOn}");
        else
            Assert.Matches(@"vars\.CI_(LINUX|WINDOWS|MACOS)_RUNNER", runsOn); // + fallback literal present
    }
    Assert.DoesNotContain("runs-on: self-hosted", ci); // never route on the bare label
    Assert.Contains("DEVELOPER_DIR: /Applications/Xcode_26.6.app", ci); // the Xcode pin stays literal
}
```
Red first (current `ci.yml` has bare labels) → green after 1a. Also assert every expression carries a
fallback literal (`\|\| '(ubuntu-latest|windows-latest|macos-26)'` or `\|\| matrix\.os`).

**Acceptance criteria**

```gherkin
Scenario: No variables set — the workflow behaves exactly as before
  Given no CI_LINUX_RUNNER / CI_WINDOWS_RUNNER / CI_MACOS_RUNNER repo variable exists
  When a PR run and a develop push run complete
  Then every job ran on the same hosted label it used before the change
  And the job names in the checks list are unchanged (branch protection still matches)

Scenario: One variable routes one OS home, the others stay hosted
  Given the desk-win runner is Online and CI_WINDOWS_RUNNER = "desk-win"
  When a develop code push runs
  Then native-build (windows) and native-smoke-windows show runner "desk-win" in the job header
  And every macOS and Ubuntu job still shows a GitHub-hosted runner
  And the run's billable Windows minutes are 0

Scenario: Switching back is a settings change
  Given CI_MACOS_RUNNER = "mac-allan" routed the Apple legs home on the previous run
  When the variable is deleted and a develop code push runs
  Then native-build-apple and native-smoke-apple run on macos-26
  And git log shows no change to .github/workflows/ci.yml between the two runs

Scenario: Second run on the same self-hosted machine is green (idempotency)
  Given native-smoke-windows and native-smoke-apple each passed once on their self-hosted runner
  When the same commit is re-run from the Actions tab
  Then both smokes pass again (no "database smoke already exists", no stale container/state)

Scenario: The Mac is offline
  Given CI_MACOS_RUNNER = "mac-allan" and the MacBook is asleep
  When a develop code push runs
  Then the non-Apple jobs complete and deploy-staging runs on a hosted runner
  And the Apple jobs sit "Queued" until the Mac is online, or expire after 24 h with a one-click re-run

Scenario: The drift tripwire refuses a hardcoded label
  Given a change edits any non-deploy runs-on back to a bare hosted label
  When Api.Tests run
  Then CiRunnerSelection_IsVariableDrivenWithHostedFallback fails naming the job

Scenario: Deploys never leave hosted runners
  Given CI_LINUX_RUNNER = "desk-linux"
  When a develop push reaches deploy-staging
  Then deploy-staging runs on ubuntu-latest (the RENDER_DEPLOY_HOOK secret never reaches the desk)
```

**Verification commands (paste into the PR):**
```bash
gh api repos/argamboad/perezosoft-platform/actions/runners --jq '.runners[]|{name,status,labels:[.labels[].name]}'
gh variable set CI_WINDOWS_RUNNER --body desk-win      # route; `gh variable delete CI_WINDOWS_RUNNER` = back to hosted
gh run list --branch develop --workflow CI --limit 1 --json databaseId --jq '.[0].databaseId'
gh api repos/argamboad/perezosoft-platform/actions/runs/<id>/jobs --jq '.jobs[]|"\(.name) | \(.runner_name) | \(.labels|join(","))"'
```

**Docs touched in this slice:** `docs/DECISIONS.md` (ADR-025), `docs/DEPLOYMENT.md` **new §10
"Self-hosted CI runners"** (1c verbatim + the port rule + rollback = delete the variables),
`docs/audits/v3-2026-07/FOUNDATION_RULES_v2.md` (**R77 [machine]** after R76, same format),
`CLAUDE.md` (tech-stack bullet: bump playbook step ⑤ gains "re-point the Mac's `Xcode_26.6.app` symlink";
doc-map row for `DEPLOYMENT.md` mentions §10), `docs/tutorial/lessons/1.6-ci-from-commit-one.md` (it
quotes `runs-on` lines — sweep the quote, then regen `docs/tutorial/COVERAGE.md` + the course PDF per the
tutorial memory rule), `.env.example` untouched (runner config is not app config).

**Out of scope:** the Ubuntu jobs (Phase B), `act`, a Hyper-V/VM runner, runner auto-scaling,
`postman-sync` (already paths-filtered and cheap).
**Definition of done:** R77 test written first and green; `ci.yml` variable-driven with zero behaviour
change when unset; both idempotency fixes in; runners `desk-win` + `mac-allan` registered and Online;
one develop run observed with Windows + Apple jobs on the self-hosted runners and the run's macOS/Windows
billable minutes at 0; one run observed after deleting the variables back on hosted; §10 written; PR
merged after green CI.

---

### LOCALCI-2 — Local gate runner: land `ci-local.ps1` behind a drift tripwire

**As a** platform maintainer
**I want** to run the PR-blocking gates on my desk before pushing
**So that** Actions minutes (hosted or not) and wall-clock go to confirming green work, not discovering
red work — a red push re-bills every job in the run

**Context / notes:** a working, dogfooded script already exists on the **unpushed local branch
`ci/local-gates`** (commit `a624329`; four default gates passed in 1 m 54 s on the desk). ⚠️ **That
branch is stale — 44 files behind develop** (it predates the OTLP logs export, the VS Code launch files,
Android release signing, and more; its diff against develop is mostly *deletions*). **Do not merge,
rebase, or cherry-pick the branch.** Extract the two files and delete it:

```bash
git switch -c feat/LOCALCI-2-local-gates origin/develop
git show ci/local-gates:ci-local.ps1 > ci-local.ps1
git show ci/local-gates:docs/WAYS_OF_WORKING.md | sed -n '/### Run the gates locally first/,/^### Merge discipline/p' | sed '$d'   # the 12-line section
git branch -D ci/local-gates
```

#### 2a. Reconcile the extracted script with today's `ci.yml` (gate by gate)

| Gate | Parked script has | `ci.yml` today has | Action |
|---|---|---|---|
| build-test | locked restore ×6, Release build ×3, `dotnet test` ×3 | + **"No pending EF model changes"** (`dotnet tool restore` + `dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/Infrastructure/… --startup-project src/Api/…` with `ConnectionStrings__DefaultConnection` placeholder) | **add the step** (set/unset the env var around it like the license gate does) |
| qa-artifacts | pip install + `check_qa_artifacts.py` + append-only guard vs `origin/develop` merge-base | same (CI uses the PR base SHA) | keep; requires `python` + `bash` on PATH (Git Bash) |
| secret-scan | `docker run zricethezav/gitleaks:v8.21.2 … --config .gitleaks.toml` | binary `8.21.2` + sha256 pin | keep the image form; version string must equal `GITLEAKS_VERSION` (tripwire) |
| license-scan | `DOTNET_ROLL_FORWARD=LatestMajor`, tool restore, scan Api + Web vs `.github/forbidden-licenses.json` | same | keep |
| e2e (opt-in) | `postgres:17` + `axllent/mailpit:v1.30.4` containers, same env block as CI, ports 5432/1025/8025/5238/5169 guard | same images/env | keep; image tags must equal CI's (tripwire) |
| docker-build (opt-in) | `docker build -t perezosoft-app:ci-local .` | same | keep |
| native-build (windows) (opt-in) | `dotnet build src/Maui/… -f net10.0-windows10.0.19041.0 -c Debug` | preceded by `dotnet workload restore … --version 10.0.400.1` | add the workload restore (no-op when present; keeps the set pin honest) |
| not mirrored | — | native-paths, Apple/Android legs + smokes, deploys, postman-sync | header already says so; keep |

Also add to `.gitignore` (the parked branch's version is stale, don't copy it):
```
# ci-local.ps1 scratch (LOCALCI-2)
ci-local-*.log
src/Web/wwwroot/appsettings.json.ci-local.bak
```

#### 2b. The drift tripwire (test first)

`tests/Api.Tests/EnforcementGateTests.cs`, new fact `CiLocalScript_MirrorsTheGateJobsAndPins` (**R78**):
- Gate list: extract every `Invoke-Gate "<name>"` from `ci-local.ps1`; assert the set is exactly
  `{ build-test, qa-artifacts, secret-scan, license-scan, e2e, docker-build, native-build (windows) }`
  **and** each name (minus the parenthetical) is a job id in `ci.yml`. A new PR-blocking job added to
  `ci.yml` that isn't in the script fails here too → assert the four *default* gates equal the set of
  `ci.yml` jobs that run on `pull_request` and are not `native-build`/`docker-build`/`e2e` (encode the
  intended list explicitly; don't infer).
- Pins: regex the same literal out of both files and assert equality — gitleaks
  (`GITLEAKS_VERSION: "(\S+)"` vs `gitleaks:v(\S+)`), Mailpit (`axllent/mailpit:v[\d.]+`), Postgres
  (`postgres:17`), workload set (`--version 10\.0\.400\.1`). A bump to one file without the other fails.
- Env parity: the six `e2e` env keys in `ci.yml` (`ASPNETCORE_ENVIRONMENT`, `ConnectionStrings__…`,
  `Jwt__Secret`, `Auth__RateLimit__…`, `Admin__StaffEmails__0`, `Auth__AppBaseUrl`) each appear in the
  script's `$env:` block.

#### 2c. Docs
- `docs/WAYS_OF_WORKING.md`: insert the extracted "Run the gates locally first" section before
  "Merge discipline"; add one bullet: "a green `ci-local.ps1` run is the expected state of a branch
  before its first push".
- `CLAUDE.md` doc map: add a `ci-local.ps1` row ("run before pushing; mirrors the PR-blocking gates;
  R78 keeps it honest").
- `.github/pull_request_template.md`: one checkbox "`./ci-local.ps1` green locally before the first push".
- `FOUNDATION_RULES_v2.md`: **R78 [machine]** after R77.

**Acceptance criteria**

```gherkin
Scenario: Default gates mirror the PR-blocking jobs
  Given a clean develop checkout with Docker running and the dev compose stack stopped
  When ./ci-local.ps1 runs
  Then build-test, qa-artifacts, secret-scan, license-scan all PASS in the summary table
  And the exit code is 0

Scenario: A red gate exits non-zero with the gate named
  Given a branch that introduces a build warning (warnings-as-errors)
  When ./ci-local.ps1 runs
  Then build-test shows FAIL, the summary is still printed, and the exit code is 1

Scenario: The EF drift step is mirrored
  Given an entity change without a migration
  When ./ci-local.ps1 runs
  Then build-test FAILS on "has-pending-model-changes" — the same verdict CI would give

Scenario: Opt-in E2E boots and tears down the stack
  Given ports 5432/1025/8025/5238/5169 are free
  When ./ci-local.ps1 -E2E runs
  Then the Playwright suite runs against http://localhost:5169 and both containers + both dotnet
       processes are gone afterwards, and src/Web/wwwroot/appsettings.json is restored byte-for-byte

Scenario: Port clash fails fast
  Given the dev compose db is up on 5432
  When ./ci-local.ps1 -E2E runs
  Then the e2e gate FAILS immediately with the "docker compose stop db mail" hint, nothing was started

Scenario: The tripwire catches a one-sided pin bump
  Given ci.yml's GITLEAKS_VERSION is bumped but ci-local.ps1's image tag is not
  When Api.Tests run
  Then CiLocalScript_MirrorsTheGateJobsAndPins fails naming "gitleaks"
```

**Out of scope:** mirroring the native legs, smokes, deploys; running the script *from* CI.
**Definition of done:** R78 test written first and green; script reconciled per 2a; four default gates
green on the desk (paste the summary table + timings in the PR); `-All` run once green; docs per 2c;
stale branch `ci/local-gates` deleted; merged after green CI.

---

### LOCALCI-3 — Trigger diet: paths gate for every non-deploy job, Apple smoke on a schedule

**As a** platform maintainer
**I want** docs-only pushes to run only the gates that docs can break, and the most expensive smoke to
run on a cadence instead of every push
**So that** the hosted-fallback bill stays small on the days the runners are off

**Context / notes:** `native-paths` (`ci.yml:252`) already computes `changed` for develop pushes and
gates only the Apple legs + smokes on it. A docs-only push today still bills build-test, e2e (9 min),
docker-build, license-scan, native-build ×2 (≈ 30 min). This slice matters most **when LOCALCI-1's
variables are unset** (hosted fallback) — with the runners on, it just saves wall-clock.

#### 3a. Generalise the paths gate
- Rename `native-paths` → `changes`, run it on **both** `push` and `pull_request` (drop its `if:`), with
  base SHA `${{ github.event.pull_request.base.sha || github.event.before }}` (same fail-open rule as
  today: unknown base ⇒ everything runs). Outputs:
  - `code` — `^(src/|tests/|Directory\.Packages\.props|Directory\.Build\.props|global\.json|Dockerfile|docker-compose\.yml|\.config/dotnet-tools\.json|\.github/(workflows/ci\.yml|scripts/)|.*packages\.lock\.json)` 
  - `native` — today's `changed` regex (unchanged semantics; keep the develop-only `if:` **on the
    consumers**, not on the gate)
  - `docs` — `^docs/` (informational)
- Consumers: `build-test`, `license-scan`, `docker-build`, `e2e`, `native-build` gain
  `needs: changes` + `if: needs.changes.outputs.code == 'true'`. `secret-scan` and `qa-artifacts` **always
  run** (a docs commit can leak a secret or desync the QA PDFs). Apple/smoke jobs use `native`.
- `deploy-staging` / `deploy-prod` `needs:` lists include jobs that may now be skipped. A skipped need
  skips the deploy — **correct** for a docs-only push (nothing to ship; Render's build minutes are saved
  too). Verify branch protection: GitHub treats a *skipped* required check as passing, so PRs still merge.
- `EnforcementGateTests`: the R77 job splitter must still find every job (the rename changes an id — the
  test reads ids dynamically, so only the hosted-deploy allowlist is literal).

#### 3b. Apple smoke on a cadence (hosted-fallback mode only)
- `native-smoke-apple`: keep `native-build-apple` on every develop code push (46 billed min hosted, catches
  compile rot within one merge — ADR-018's cost pattern), but run the **smoke** (87 billed min hosted) on
  `schedule: cron '0 6 * * 1'` (Mondays 06:00 UTC) + `workflow_dispatch`, and on develop pushes **only when
  `vars.CI_MACOS_RUNNER` is set** (i.e. it is free):
  `if: needs.changes.outputs.native == 'true' && (vars.CI_MACOS_RUNNER != '' || github.event_name != 'push')`
- Add `schedule` + `workflow_dispatch` to `on:`; every other job gets
  `if: github.event_name != 'schedule'` folded into its condition (a scheduled run is *only* the Apple
  smoke). `permissions: contents: read` unchanged.
- `QA_TEST_PLAN.md` §13c / `STATUS.md`: note that the Apple smoke cadence is weekly in hosted mode.

**Acceptance criteria**

```gherkin
Scenario: Docs-only PR runs only the docs-capable gates
  Given a PR that touches only docs/**
  When CI runs
  Then secret-scan, qa-artifacts, changes run; build-test, e2e, docker-build, license-scan,
       native-build show "skipped"; the PR is mergeable (skipped required checks pass)

Scenario: Docs-only develop push does not deploy
  Given a develop push touching only docs/**
  When CI runs
  Then deploy-staging is skipped and staging's /api/version still reports the previous commit

Scenario: Code push still runs everything
  Given a push touching src/**
  When CI runs
  Then every gate runs exactly as before LOCALCI-3

Scenario: Unknown diff base fails open
  Given a force-push whose before-SHA is unreachable
  When CI runs
  Then changes reports code=true and native=true and every job runs

Scenario: Apple smoke cadence in hosted mode
  Given CI_MACOS_RUNNER is unset
  When a develop code push runs
  Then native-build-apple runs and native-smoke-apple is skipped
  And the Monday 06:00 UTC scheduled run executes native-smoke-apple and nothing else

Scenario: Apple smoke every push when it is free
  Given CI_MACOS_RUNNER = "mac-allan"
  When a develop code push touching src/** runs
  Then native-smoke-apple runs on mac-allan
```

**Out of scope:** per-job `paths:` at the `on:` level (they can't express "always run secret-scan");
caching NuGet/workloads on hosted runners (a separate, smaller win — note it in ROADMAP if ever wanted).
**Definition of done:** R77 still green after the rename; one docs-only PR and one code PR observed with
the expected skip/run pattern; one scheduled run observed (trigger it via `workflow_dispatch` to avoid
waiting a week); `docs/tutorial/lessons/1.6-ci-from-commit-one.md` quotes swept + COVERAGE/PDF regen;
merged after green CI.

---

## Order, sizing, and stop points

| Slice | Size | Saves (hosted-mode billed min per develop code push) | Stop here? |
|---|---|---|---|
| LOCALCI-1 | 1 slice + 2 runner registrations (~half a day incl. the Mac) | **≈ 157 of ≈ 190** (all macOS + Windows) | Yes — this is the money |
| LOCALCI-2 | 1 slice (script exists; reconcile + tripwire + docs) | red pushes (each ≈ 30–190) | Yes |
| LOCALCI-3 | S | ≈ 30 per docs-only push; 87 per push in hosted mode | Yes |

**Do first, separately (unrelated loose end absorbed by the stale branch):** PR #204 added 43
`docs/tutorial/diagrams/*` assets without regenerating `docs/tutorial/COVERAGE.md` (819 listed vs 862
actual; the gate is manual-run only). Land a tiny `docs/coverage-regen` PR on develop **before**
LOCALCI-2 so the script branch stays single-purpose.

## Checklist (all slices)
- [ ] Tests written first (R77 / R78 facts red → green)
- [ ] `ci.yml` behaviour identical with no variables set (LOCALCI-1) — verified by a real run
- [ ] Repo private; runners registered with one custom label each; no job routes on bare `self-hosted`
- [ ] Deploy jobs hosted; deploy-hook secrets never reach a self-hosted runner
- [ ] `DEVELOPER_DIR` pin untouched; Mac symlink documented in the bump playbook step ⑤
- [ ] Idempotency: second run on the same machine green
- [ ] Docs: ADR-025, DEPLOYMENT §10, FOUNDATION_RULES R77/R78, WAYS_OF_WORKING, CLAUDE.md doc map,
      PR template, tutorial lesson 1.6 + COVERAGE/PDF regen
- [ ] ROADMAP post-terminal wave + PLATFORM_BACKLOG §13 marked ✅ per slice as they land
- [ ] Never merge before the branch CI finishes green (WAYS_OF_WORKING merge discipline)
