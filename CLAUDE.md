# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

Runnable samples for [`Eudiplo.Client`](https://github.com/slekrem/eudiplo-dotnet-client) ([NuGet](https://www.nuget.org/packages/Eudiplo.Client)) — an unofficial .NET HTTP client for [EUDIPLO](https://github.com/openwallet-foundation-labs/eudiplo). Every sample references the **published NuGet package**, not a local source checkout, so it builds and behaves the way a real consumer of the client would experience it.

Every sample runs against a **real EUDIPLO instance** — never a mock. This is a deliberate, load-bearing choice repeated throughout the READMEs: several real bugs (in `Eudiplo.Client` itself, not these samples) were only found this way — see "Why building this against a real server mattered" in `src/Eudiplo.Client.Sample.AccessControl/README.md`. When modifying or extending a sample, keep testing it against the real instance, not by mocking `EudiploApiClient`.

These samples don't provision EUDIPLO themselves — they expect an existing, network-reachable instance and client credentials for it. No Docker, no local `.env` file. AccessControl gets those credentials from environment variables, fixed for the process's lifetime; Explorer gets them from a browser form, submitted fresh with every request — see "Architecture" below for why that difference exists.

EUDIPLO's architecture names the integration point this client covers as "your services" (CRM, ERP, Access Control System). Most samples pick one of these named examples and build a realistic integration for it; `Explorer` is the exception — a general-purpose dashboard, not tied to a named example:

| Sample | Pattern | Status |
|---|---|---|
| `Eudiplo.Client.Sample.AccessControl` | 3-tier gate: Lit+TS UI → ASP.NET backend → EUDIPLO | done |
| `Eudiplo.Client.Sample.Explorer` | Enter EUDIPLO URL + credentials in a UI, browse what's reachable | done |
| `Eudiplo.Client.Sample.CRM` | Verified data via webhook → enrich a record | planned |
| `Eudiplo.Client.Sample.ERP` | Issue a credential ("credential creation") | planned |

## Pointing at an EUDIPLO instance

AccessControl needs the same three environment variables, pointing at your own running EUDIPLO instance:

```bash
export EUDIPLO_BASE_URL=https://your-eudiplo-instance.example
export AUTH_CLIENT_ID=...
export AUTH_CLIENT_SECRET=...
```

All three are required — it throws an `InvalidOperationException` at startup if any is unset (see `Program.cs`). `AUTH_CLIENT_ID`/`AUTH_CLIENT_SECRET` are the gate tenant's own client credentials, not a tenant-less root client — see "Architecture" below.

Explorer needs **none** of these — it takes the base URL and credentials from a form in the browser instead, per request.

## Build / run commands

All project code lives under `src/`; the repo root holds only shared build/tooling config (`Directory.Build.props`, `Directory.Packages.props`, `global.json`, etc.).

Solution file: `Eudiplo.Client.Samples.slnx` (lists the .NET *executable* projects — each sample's frontend is a separate npm project, not part of the .sln).

```bash
dotnet build Eudiplo.Client.Samples.slnx
```

### `Eudiplo.Client.Sample.AccessControl` (3-tier sample)

```bash
# 1. build the frontend (outputs into ../Backend/wwwroot — the backend serves it from there;
#    re-run after any frontend change, there's no watcher wired into the backend)
cd src/Eudiplo.Client.Sample.AccessControl/Frontend
npm install
npm run build

# 2. run the backend
cd ../Backend
dotnet run --project .
# → http://localhost:5050
```

For frontend edit-and-reload without rebuilding into `wwwroot`, run the Vite dev server instead of `npm run build`:
```bash
cd src/Eudiplo.Client.Sample.AccessControl/Frontend
npm run dev   # :5173, proxies /api to :5050, no CORS setup needed
```

### `Eudiplo.Client.Sample.Explorer` (dashboard sample)

```bash
cd src/Eudiplo.Client.Sample.Explorer/Frontend
npm install
npm run build

cd ../Backend
dotnet run --project .
# → http://localhost:5070 — no env vars needed, fill in the form
```

Vite dev server: same pattern, `npm run dev` in `Frontend/` proxies `/api` to `:5070`.

Port note: this sample deliberately avoids `5060` — Chrome and Firefox both hard-block navigation to it (`ERR_UNSAFE_PORT`, it's the SIP port). Don't reuse it for a future sample either.

There are no automated test suites in this repo — verification is "run the sample against a real EUDIPLO instance and observe the console output / browser behavior," as documented in each sample's README.

## Architecture

### `Eudiplo.Client.Sample.AccessControl` never provisions EUDIPLO itself

The backend has a real, pre-existing EUDIPLO tenant with its own client credentials (not a
tenant-less root client) — `AUTH_CLIENT_ID`/`AUTH_CLIENT_SECRET` for this sample *are* that
tenant's own credentials. It authenticates directly as that tenant via a single
DI-registered `EudiploApiClient` (`services.AddEudiploClient(o => {...})`) and never calls
any tenant-management endpoint (`CreateTenantAsync`, `DeleteTenantAsync`, key-chain or
presentation-config creation) at all — this is enforced by the code no longer containing
that logic, not just by a default it happens to take. If you're tempted to add
"auto-provision if missing" back in, don't — that was a deliberate, requested removal (a
previous version did self-provision a self-signed-cert tenant by default, which a real
wallet then rejected as untrusted; see "Registrar registration" in its README).

### `Eudiplo.Client.Sample.AccessControl`'s three real tiers

This sample deliberately has separate `Backend/` and `Frontend/` folders to demonstrate the trust boundary from EUDIPLO's architecture diagram — the browser never talks to EUDIPLO directly, only to this sample's own backend:

```
Browser (gate-app, Lit + TypeScript)  →  Backend (ASP.NET Core minimal API)  →  EUDIPLO
              fetch + EventSource            Eudiplo.Client (only piece with credentials)
```

- **`Backend/Program.cs`** — single file, no separate service class (nothing left to own beyond a DI-resolved `EudiploApiClient` and a `PresentationConfigId` constant). Minimal API with three endpoints: `POST /api/gate/sessions` (opens a presentation request), `GET /api/gate/sessions/{id}/events` (SSE passthrough of `SubscribeToSessionEventsAsync`, enriched with the full session via a REST call once a terminal status is reached), `GET /api/gate/sessions/{id}` (one-shot polling fallback, same enrichment). Also serves the built frontend as static files from `wwwroot/`.
- **`Frontend/src/gate-app.ts`** — single Lit 3 + TypeScript component, no Shadow DOM, no router, built with Vite. Renders the `openid4vp://` request as a QR code and reacts to both the SSE stream and a 3s poll.

The tenant's access key-chain and the `access-control-age-check` presentation config must already exist in EUDIPLO before this backend starts — it assumes, never creates, them.

Non-obvious constraints baked into this code (don't "fix" these without re-reading the README's numbered list first):
- A presentation offer 404s without an access key-chain provisioned first (on the tenant, ahead of time — not something this backend does).
- `SubscribeToSessionEventsAsync`'s SSE endpoint takes the token via a `?token=` query param, not an `Authorization` header (browsers' `EventSource` can't send custom headers).
- The real German PID (`vct: urn:eudi:pid:de:1`) has no `age_over_18`/`age_equal_or_over` claim — only `birthdate`. The gate requests `birthdate` (full disclosure) and checks the 18-year threshold itself, server-side, in `Program.cs`'s `EnforceAgeGate` — rewriting a `completed` session to `failed` with an `errorReason` when underage, so the frontend needs no special-casing.
- The polling fallback (`GET /api/gate/sessions/{id}`) exists alongside SSE because a backgrounded mobile browser tab (e.g. switching to the wallet app to scan/confirm) can silently kill the SSE connection with no `onerror` ever firing.
- A self-signed access key-chain gets rejected by real wallets ("Could not trust certificate chain") — the tenant's key-chain needs a registrar-issued certificate for a real wallet to trust it (see "Registrar registration" in the README).

### `Eudiplo.Client.Sample.Explorer`: one `EudiploApiClient` per request, not per process

Unlike AccessControl, this backend's `Program.cs` is a thin composition root only — no
inline endpoint logic, no inline DTOs. Responsibilities are split into folders (namespace
matches folder, e.g. `Eudiplo.Client.Sample.Explorer.Backend.Services`), current minimal-API
convention:

- **`Program.cs`** — builder setup, DI registration (`AddHttpClient()`,
  `AddSingleton<EudiploExplorerService>()`), `app.MapExploreEndpoints()`, `app.Run()`. Reads
  top to bottom as a summary of what the app does, not how.
- **`Endpoints/ExploreEndpoints.cs`** — `MapExploreEndpoints(this IEndpointRouteBuilder)`
  extension method + the `POST /api/explore` handler. Owns HTTP concerns only: request
  validation (blank fields, malformed `baseUrl`) and translating the service's result into
  `Results.Ok`/`Results.BadRequest`.
- **`Services/EudiploExplorerService.cs`** — the actual EUDIPLO-querying logic, injected
  via DI (registered as a singleton — it's stateless, nothing about a call survives past
  `ExploreAsync`'s return). This is where `AddEudiploClient` gets skipped: every other
  sample calls `services.AddEudiploClient(o => { o.BaseUrl = ...; ... })` once at startup,
  which configures a named `HttpClient`'s base address from a URL that's already known.
  `EudiploExplorerService` can't do that — the target EUDIPLO instance isn't known until
  `ExploreAsync` is called. Instead it takes a plain `HttpClient` from
  `IHttpClientFactory`, sets `.BaseAddress` to the caller-supplied URL, and constructs
  `new EudiploApiClient(http, clientId, clientSecret)` directly — built fresh per call,
  discarded after.
- **`Models/ExploreModels.cs`** — `ExploreRequest` (the request body), `QueryResult<T>`
  (one query's outcome), `ExploreResult` (the six named `QueryResult<T>` fields returned to
  the frontend).

`ExploreAsync` queries six endpoints (`GetVersionAsync`, `GetKeyChainsAsync`,
`GetClientsAsync`, `GetVerifierConfigsAsync`, `GetCredentialConfigsAsync`,
`GetUsersAsync`) through a private `QueryAsync<T>` wrapper that catches per-call, not
globally — a tenant client rarely holds every EUDIPLO role, so partial failure (some
sections `403`, others succeed) is the expected case, not an edge case. `ExploreResult` is
a concrete named record (not an anonymous type) for the C# side's sake — it still
serializes to the same JSON shape either way, an object keyed by query name
(`{ "keyChains": { "ok": true, "data": [...] }, ... }`). The frontend (`explorer-app.ts`)
renders that generically by iterating `Object.entries(...)` and humanizing each key —
adding a query server-side needs no matching frontend change.

### Solution/package structure

- `Directory.Build.props` — shared MSBuild settings for every .csproj: nullable enabled, implicit usings, latest analyzers.
- `Directory.Packages.props` — central package version management (`ManagePackageVersionsCentrally`); add new package versions here, not in individual `.csproj` files.
- `Eudiplo.Client.Samples.slnx` — the new XML-light solution format.
