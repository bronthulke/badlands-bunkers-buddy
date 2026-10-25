# Handoff notes

Context for picking this project up in a fresh Claude Code session.
Written 2026-10-25. The app now runs locally and the whole API has been exercised
end to end against Azurite. It has **not** been deployed, and the UI has not been
clicked through in a browser.

---

## What this is

A shared web board for tracking DayZ **Badlands** bunkers. The group decodes morse
radio transmissions into `XXX,YYY PPPP` — map coordinates plus a four-digit lock
code. Three bunkers go active each in-game night. The app keeps a permanent list of
every bunker ever found, lets whoever is on radio duty flag tonight's three and
enter their codes, and resets the board when a new night starts.

Everything is in `README.md` — stack, local dev, full Azure deployment steps, API
reference. Read that first; this file only covers the things the README doesn't say.

## Decisions already made (don't relitigate these)

The user is a .NET developer and **explicitly ruled out React**. They were asked and
chose each of these:

| Decision | Choice | Why |
| --- | --- | --- |
| Frontend | Blazor WebAssembly, .NET 8 | C# end to end |
| API | Azure Functions isolated worker, .NET 8 | SWA managed functions support 8, **not 9** |
| Storage | Azure Table Storage | Cents a month, right size for a few dozen rows |
| Auth | One shared password → HMAC-signed HttpOnly cookie | Group password, not per-user logins |
| Hosting | Azure Static Web Apps, free tier | User's stated target |

Target framework is **net8.0 everywhere**, even though the machine has a 9.0 SDK.
That is deliberate — SWA managed Functions do not support .NET 9. Don't "helpfully"
upgrade it.

## State: what's done

- Three projects under `src/` (`Client`, `Api`, `Shared`) plus `tests/`, all in
  `BadlandsBunkers.sln`. Solution builds clean, zero warnings.
- Three pages: **Tonight** (the main board), **Bunkers** (master list, CRUD),
  **History** (archived nights).
- Full API — login/logout/session, bunker CRUD, activate, set code, clear night,
  list nights. Every data endpoint is auth-gated.
- **122 tests, all passing.** 22 of them need Azurite and *skip* (not fail) when it
  isn't running, so `dotnet test` is green on a bare checkout.
- GitHub Actions workflow at `.github/workflows/azure-static-web-apps.yml`, which
  starts Azurite and runs the tests before deploying.

## State: what is NOT done

1. **The UI has never been looked at in a browser.** The stack runs and every API
   endpoint has been verified end to end with curl, but no human has seen a single
   screen render. Layout, the inline code field, and the three-active cap are
   unverified. **This is the highest-priority next step** — `./scripts/run-local.sh`
   then open http://localhost:4280 (password `badlands`).
2. **Not a git repository.** No `git init` has been run. `.gitignore` exists.
3. **Nothing exists in Azure.** No resource group, no storage account, no Static Web
   App. Infrastructure is now defined in Bicep under `infra/` and provisioned with
   `azd up`, but it has never been run — the templates compile cleanly and the ARM
   output has been inspected, nothing more. No Azure subscription has been touched.
4. **No UI tests.** No bUnit. The Blazor components — the three-active cap, inline
   code editing, the login screen — are untested. The user was offered bUnit and
   hasn't said yes or no.

## Suggested next steps, in order

### 1. Click through the UI

`./scripts/run-local.sh`, then open **http://localhost:4280** (password `badlands`).
See the README's "Running locally" section for the tooling and the two traps
(`func` must not be run from `src/Api`; npm's Core Tools package is broken on
modern Node).

Walk the real workflow: add a bunker, activate it, type a code, reload in a second
browser to confirm it's shared, then hit **New night** and check History.

**Already verified by curl, so don't re-test these** — unauthenticated requests
rejected, wrong password rejected, correct password issues an HttpOnly cookie,
bunker create, code entry auto-activating, night archive, board reset preserving
`activeCount`, and history listing. Static assets, SPA deep-link fallback and
same-origin API routing through the proxy all check out too.

### 2. `git init` and first commit

The user was asked about this and never answered. Ask again before committing
anything.

### 3. Deploy to Azure

`azd auth login`, set `SHARED_PASSWORD` and `AUTH_SIGNING_KEY` via `azd env set`,
then `azd up`. Bicep wires all four app settings automatically, so there is no manual
`az staticwebapp appsettings set` step any more. See the README.

**`azure.yaml` has no `services:` section on purpose.** azd's `host: staticwebapp`
deploys only a static artifact — its service schema has no property for a managed
functions API. Adding a service entry would make `azd up` push the Blazor client and
silently skip the API, producing a site that looks deployed and fails every call.
Deployment belongs to the GitHub Actions workflow, which builds both halves via Oryx.
Don't "complete" the azure.yaml by adding a service.

SWA managed Functions only run in a few regions; `eastasia` is closest to the user
(Australia) and is the Bicep default. That only affects the API — static content is
CDN-edge served.

## Bug already found and fixed

`AddHttpClient<BunkerApi>` registers a typed client as **transient**, so every
component got its own `BunkerApi`. `MainLayout` subscribes to `SessionLost` to swap
in the login screen — on an instance nothing else ever raised. An expired session
would have left the user on a dead page. `src/Client/Program.cs` now uses a named
client plus a **scoped** `BunkerApi`. Don't refactor it back to a typed client.

## Things worth knowing before you change code

**The three-active cap is client-side only** (`src/Client/Pages/Home.razor`). The API
will happily activate a fourth bunker. This was left soft on purpose, so the app
doesn't fight the user if the game changes or they want to pre-stage intel. The user
was told and didn't ask for it to be hardened. Don't "fix" it without asking.

**Codes accept 1–8 digits, not strictly 4.** Same reasoning — digits are validated,
length is not. Deliberate.

**Three testability seams exist in `src/Api`** and should not be tidied away:
- `BunkerStore` takes an optional `tablePrefix` so tests use throwaway tables
- `BunkerStore.DeleteTablesAsync()` is `internal` — test cleanup only
- `BunkerFunctions.Validate` is `internal`, exposed via `AssemblyInfo.cs`
  (`InternalsVisibleTo`)

**`EndpointAuthTests` is the most valuable test file.** It drives all eight guarded
endpoints against missing, forged, expired and misnamed cookies. It was verified to
actually bite: removing the auth gate from one endpoint failed exactly the four
tests covering it. If you add a new endpoint, add it to that theory's endpoint list.

**Auth design.** `POST /api/login` compares the password constant-time, then issues
`HttpOnly; Secure; SameSite=Strict` containing `{expiry}.{HMAC-SHA256(expiry)}`.
Rotating `AuthSigningKey` in Azure invalidates every session — that's the
"password leaked" button. There is no user identity anywhere by design.

## Naming

The app was renamed from "Badlands Bunkers Tracker" to **Badlands Bunkers Buddy**.
UI strings and the README are updated. Two things deliberately left alone:

- **The `BadlandsBunkers.*` namespaces and project names.** They never contained
  "Tracker", so they're still accurate. Renaming them to `BadlandsBunkersBuddy.*`
  is pure churn — only do it if the user asks.
- **Azure resource names in the README** (`badlands-bunkers`). Still fine, and
  nothing has been created in Azure yet anyway.

If you're reading this in a session that doesn't remember the old name: the project
directory was renamed from `badlands-bunkers-tracker` to `badlands-bunkers-buddy`,
and the matching session history directory under `~/.claude/projects/` should have
been renamed alongside it.
