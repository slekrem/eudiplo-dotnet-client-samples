# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

Runnable samples for [`Eudiplo.Client`](https://github.com/slekrem/eudiplo-dotnet-client) ([NuGet](https://www.nuget.org/packages/Eudiplo.Client)) — an unofficial .NET HTTP client for [EUDIPLO](https://github.com/openwallet-foundation-labs/eudiplo). Every sample references the **published NuGet package**, not a local source checkout, so it builds and behaves the way a real consumer of the client would experience it.

Every sample runs against a **real EUDIPLO instance** — never a mock. This is a deliberate, load-bearing choice repeated throughout the READMEs: several real bugs (in `Eudiplo.Client` itself, not these samples) were only found this way — see "Why building this against a real server mattered" in `src/Eudiplo.Client.Sample.AccessControl/README.md`. When modifying or extending a sample, keep testing it against the real instance, not by mocking `EudiploApiClient`.

These samples don't provision EUDIPLO themselves — they expect an existing, network-reachable instance and root-client credentials for it, supplied via `EUDIPLO_BASE_URL`/`AUTH_CLIENT_ID`/`AUTH_CLIENT_SECRET`. No Docker, no local `.env` file.

EUDIPLO's architecture names the integration point this client covers as "your services" (CRM, ERP, Access Control System). Each sample picks one of these named examples and builds a realistic integration for it:

| Sample | Pattern | Status |
|---|---|---|
| `Eudiplo.Client.Sample` | Generic: root client → tenant → key-chain | done |
| `Eudiplo.Client.Sample.AccessControl` | 3-tier gate: Lit+TS UI → ASP.NET backend → EUDIPLO | done |
| `Eudiplo.Client.Sample.CRM` | Verified data via webhook → enrich a record | planned |
| `Eudiplo.Client.Sample.ERP` | Issue a credential ("credential creation") | planned |

## Pointing at an EUDIPLO instance

Every sample needs the same three environment variables, pointing at your own running EUDIPLO instance:

```bash
export EUDIPLO_BASE_URL=https://your-eudiplo-instance.example
export AUTH_CLIENT_ID=...
export AUTH_CLIENT_SECRET=...
```

All three are required — the samples throw an `InvalidOperationException` at startup if any is unset (see each `Program.cs`). What kind of client `AUTH_CLIENT_ID`/`AUTH_CLIENT_SECRET` must be differs per sample — see "Architecture" below.

## Build / run commands

All project code lives under `src/`; the repo root holds only shared build/tooling config (`Directory.Build.props`, `Directory.Packages.props`, `global.json`, etc.).

Solution file: `Eudiplo.Client.Samples.slnx` (only includes the two .NET *executable* projects — the AccessControl frontend is a separate npm project, not part of the .sln).

```bash
dotnet build Eudiplo.Client.Samples.slnx
```

### `Eudiplo.Client.Sample` (generic console sample)

```bash
dotnet run --project src/Eudiplo.Client.Sample
```
Creates a tenant, creates a key-chain as that tenant, then deletes the tenant — safe to re-run.

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

There are no automated test suites in this repo — verification is "run the sample against a real EUDIPLO instance and observe the console output / browser behavior," as documented in each sample's README.

## Architecture

### `Eudiplo.Client.Sample`: root client → tenant-scoped client

This sample demonstrates the two-step DI/auth shape `Eudiplo.Client` is built around for
*provisioning* use cases:

1. A **root client** (`EudiploApiClient`, DI-registered via `services.AddEudiploClient(o => {...})`) is authenticated with the tenant-less root credentials (`AUTH_CLIENT_ID`/`AUTH_CLIENT_SECRET`). It can manage tenants (`GetTenantsAsync`, `CreateTenantAsync`, `DeleteTenantAsync`, `GetTenantAsync`) but not tenant-scoped resources like key-chains.
2. Creating a tenant (`CreateTenantAsync`) returns an auto-generated **tenant admin client**'s `clientId`/`clientSecret` (parsed out of the JSON response, under `.client.clientId`/`.client.clientSecret`). A **second** `EudiploApiClient` is constructed manually (its constructor is deliberately public, not just DI-injectable) from an `IHttpClientFactory`-created `HttpClient` (via `EudiploApiClient.HttpClientName`) plus those tenant credentials. This second client does tenant-scoped operations (key-chains, presentation configs, sessions).

The tenant admin client's secret is returned **only once**, at creation time — EUDIPLO cannot re-issue it; this sample sidesteps that entirely by deleting the tenant it created at the end of every run (see its README).

**`Eudiplo.Client.Sample.AccessControl` deliberately does *not* follow this pattern.** It
has a real, pre-existing EUDIPLO tenant with its own client credentials (not a root
client) — `AUTH_CLIENT_ID`/`AUTH_CLIENT_SECRET` for this sample *are* that tenant's own
credentials. The backend authenticates directly as that tenant via a single DI-registered
`EudiploApiClient` and never calls any tenant-management endpoint (`CreateTenantAsync`,
`DeleteTenantAsync`, key-chain or presentation-config creation) at all — this is enforced
by the code no longer containing that logic, not just by a default it happens to take. If
you're tempted to add "auto-provision if missing" back in, don't — that was a deliberate,
requested removal (a previous version did self-provision a self-signed-cert tenant by
default, which a real wallet then rejected as untrusted; see "Registrar registration" in
its README).

### `Eudiplo.Client.Sample.AccessControl`'s three real tiers

Unlike the single-project generic sample, this one deliberately has separate `Backend/` and `Frontend/` folders to demonstrate the trust boundary from EUDIPLO's architecture diagram — the browser never talks to EUDIPLO directly, only to this sample's own backend:

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

### Solution/package structure

- `Directory.Build.props` — shared MSBuild settings for every .csproj: nullable enabled, implicit usings, latest analyzers.
- `Directory.Packages.props` — central package version management (`ManagePackageVersionsCentrally`); add new package versions here, not in individual `.csproj` files.
- `Eudiplo.Client.Samples.slnx` — the new XML-light solution format; only lists the .NET executable projects.
