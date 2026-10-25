# Badlands Bunkers Buddy

A small shared board for tracking DayZ Badlands bunkers decoded from radio morse
transmissions (`XXX,YYY PPPP` — coordinates plus the four-digit lock code).

Three bunkers go active each in-game night. The app keeps a running list of every
bunker you've ever found, lets whoever is on radio duty flag tonight's three and
drop their codes in, and clears the board when a new night starts.

## Stack

| Piece | Choice |
| --- | --- |
| Frontend | Blazor WebAssembly (.NET 8) |
| API | Azure Functions isolated worker (.NET 8), deployed as SWA managed functions |
| Storage | Azure Table Storage |
| Auth | One shared password → HMAC-signed HttpOnly session cookie |
| Hosting | Azure Static Web Apps (Free tier is enough) |
| Infrastructure | Bicep, provisioned with `azd up` |

```
src/Client   Blazor WASM app
src/Api      HTTP-triggered Functions
src/Shared   DTOs used by both
tests/       xUnit suite
infra/       Bicep templates (azd)
scripts/     run-local.sh
```

## How the auth works

There are no individual logins. `POST /api/login` compares the submitted password
against the `SharedPassword` app setting in constant time; on a match it returns an
`HttpOnly; Secure; SameSite=Strict` cookie containing an expiry plus an HMAC-SHA256
of that expiry, signed with `AuthSigningKey`. Every other endpoint rejects requests
whose cookie fails that check, so the data is genuinely unreachable without the
password — not just hidden in the UI.

Changing `AuthSigningKey` in Azure invalidates every existing session, which is your
"someone leaked the password" button.

## Running locally

```bash
./scripts/run-local.sh
```

Then open **http://localhost:4280** — the SWA CLI proxy, *not* :5000. The session
cookie is `SameSite=Strict`, so the app and API have to share an origin; hitting the
client dev server directly means every API call 401s. Local password is `badlands`,
set in `src/Api/local.settings.json`.

The script starts Azurite, builds and runs the Functions host, serves the Blazor app
and puts the SWA proxy in front. Logs land in `.local-run/`; Ctrl+C stops everything.

### Tooling

```bash
npm install -g azurite @azure/static-web-apps-cli
```

**Core Tools must not come from npm.** Its postinstall crashes on modern Node
(`chalk.red is not a function`) and leaves a dangling `func` symlink. Use the
standalone build instead — the `_net8` variant is 68 MB rather than 596 MB and
matches this project's target framework:

```bash
curl -L -o /tmp/func.zip https://github.com/Azure/azure-functions-core-tools/releases/download/4.0.5907/Azure.Functions.Cli.linux-x64_net8.4.0.5907.zip
unzip -q /tmp/func.zip -d ~/.local/share/azure-functions-cli
chmod +x ~/.local/share/azure-functions-cli/func
ln -sf ~/.local/share/azure-functions-cli/func ~/.local/bin/func
```

### Two traps worth knowing

**Never run `func start` from `src/Api`.** Its project detection globs recursively
and counts the SDK-generated `obj/**/WorkerExtensions.csproj` files, then refuses to
start with *"Expected 1 .csproj but found 2"*. Build first and point it at the
output — `func start --script-root src/Api/bin/output` — which is what the script
does.

**If port 4280 is taken, the SWA CLI hangs on a y/n prompt** rather than failing.
If it seems to stall after the banner, something already holds the port.

## Tests

```bash
dotnet test
```

122 tests across four areas:

| Area | Covers |
| --- | --- |
| `SessionServiceTests` | Password checking, token signing, expiry, tampering, cookie flags |
| `EndpointAuthTests` | Every guarded endpoint refuses missing, forged, expired and misnamed cookies; login issues a cookie the gate then accepts |
| `HttpExtensionsTests` | Cookie parsing, JSON body handling, input validation, coord formatting |
| `BunkerStoreTests` | Activation, code entry, night clearing and archiving, list ordering |

The 22 storage tests need Azurite. They **skip rather than fail** when it isn't
running, so `dotnet test` stays green on a bare checkout — but to actually exercise
them:

```bash
npx azurite-table --tablePort 10002 --silent &
dotnet test
```

Each test class gets its own prefixed throwaway tables, so runs don't collide with
each other or with local dev data, and they're dropped on the way out.

## Deploying to Azure

Infrastructure is defined in Bicep under `infra/` and provisioned with the
[Azure Developer CLI](https://learn.microsoft.com/azure/developer/azure-developer-cli/).
There is nothing to click in the portal.

```bash
azd auth login
azd env new badlands

azd env set SHARED_PASSWORD "the-password-you-give-your-friends"
azd env set AUTH_SIGNING_KEY "$(openssl rand -base64 48)"

azd up
```

`azd up` creates a resource group, a storage account, and a free-tier Static Web App
with all four app settings already wired. It's idempotent — re-run it whenever you
change `infra/`. `azd down` removes everything.

Optional settings, both with sensible defaults:

```bash
azd env set SWA_LOCATION eastasia   # SWA region (default eastasia)
azd env set SESSION_DAYS 30         # login lifetime
```

### Why azd doesn't deploy the app

`azd up` provisions infrastructure but deliberately does **not** deploy — there is no
`services:` entry in `azure.yaml`, and that is intentional.

azd's `host: staticwebapp` deploys a single static artifact; its service schema has no
property for a managed-functions API. Wiring it up would make `azd up` push the Blazor
client while silently leaving the API behind — a site that looks deployed and 404s on
every call. So deployment stays with GitHub Actions, where Oryx builds and ships both
halves together.

### Wiring up deployment

One-time, after the first `azd up`:

```bash
az staticwebapp secrets list \
  --name "$(azd env get-value AZURE_STATIC_WEB_APP_NAME)" \
  --resource-group "$(azd env get-value AZURE_RESOURCE_GROUP)" \
  --query "properties.apiKey" -o tsv | gh secret set AZURE_STATIC_WEB_APPS_API_TOKEN
```

After that, every push to `main` runs the tests and deploys. The site URL is
`azd env get-value SERVICE_WEB_URI`.

### What gets created

| Resource | SKU | Notes |
| --- | --- | --- |
| Storage account | Standard_LRS, TLS 1.2, no public blob | Holds the `Bunkers` and `Nights` tables, created by the API on first use |
| Static Web App | Free | Hosts the client and the managed API |

The storage connection string is composed in Bicep and injected as an app setting, so
it never passes through your shell or a config file. `SharedPassword` and
`AuthSigningKey` are `@secure()` parameters and are never emitted as outputs.

Rotating the signing key logs everyone out:

```bash
azd env set AUTH_SIGNING_KEY "$(openssl rand -base64 48)" && azd up
```

## Using it

**Tonight** is the page you'll live on. As each transmission is decoded:

1. If the bunker is new, add it on **Bunkers** with its coordinates and a note about
   where it actually is on the ground.
2. On **Tonight**, filter for it and hit *Activate*. The board caps at three.
3. Type the four-digit code into the big field on its card. It saves as you leave
   the field, and everyone else sees it on their next load.

When the night rolls over, hit **New night**. The three bunkers and their codes get
archived to **History**, and the board resets.

## API

All endpoints need the session cookie except `/api/login`.

| Method | Route | Does |
| --- | --- | --- |
| `POST` | `/api/login` | Exchange the shared password for a session cookie |
| `POST` | `/api/logout` | Clear the cookie |
| `GET` | `/api/session` | Is this browser signed in? |
| `GET` | `/api/bunkers` | Every known bunker, active ones first |
| `POST` | `/api/bunkers` | Add a bunker |
| `PUT` | `/api/bunkers/{id}` | Edit coordinates or description |
| `DELETE` | `/api/bunkers/{id}` | Remove a bunker |
| `POST` | `/api/bunkers/{id}/active/{on\|off}` | Flag in or out of tonight's set |
| `POST` | `/api/bunkers/{id}/code` | Set or clear tonight's lock code |
| `POST` | `/api/night/clear` | Archive tonight and reset the board |
| `GET` | `/api/nights` | Last 30 archived nights |
