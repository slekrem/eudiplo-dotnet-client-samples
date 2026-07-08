# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

Runnable samples for [`Eudiplo.Client`](https://github.com/slekrem/eudiplo-dotnet-client) ([NuGet](https://www.nuget.org/packages/Eudiplo.Client)) — an unofficial .NET HTTP client for [EUDIPLO](https://github.com/openwallet-foundation-labs/eudiplo). Every sample references the **published NuGet package**, not a local source checkout, so it builds and behaves the way a real consumer of the client would experience it.

Every sample runs against a **real, dockerized EUDIPLO instance** — never a mock. This is a deliberate, load-bearing choice repeated throughout the READMEs: several real bugs (in `Eudiplo.Client` itself, not these samples) were only found this way — see "Why building this against a real server mattered" in `Eudiplo.Client.Sample.AccessControl/README.md`. When modifying or extending a sample, keep testing it against the real instance, not by mocking `EudiploApiClient`.

EUDIPLO's architecture names the integration point this client covers as "your services" (CRM, ERP, Access Control System). Each sample picks one of these named examples and builds a realistic integration for it:

| Sample | Pattern | Status |
|---|---|---|
| `Eudiplo.Client.Sample` | Generic: root client → tenant → key-chain | done |
| `Eudiplo.Client.Sample.AccessControl` | 3-tier gate: Lit+TS UI → ASP.NET backend → EUDIPLO | done |
| `Eudiplo.Client.Sample.CRM` | Verified data via webhook → enrich a record | planned |
| `Eudiplo.Client.Sample.ERP` | Issue a credential ("credential creation") | planned |

## Shared EUDIPLO instance

All samples share **one** EUDIPLO instance defined in the repo-root `docker-compose.yml` — start it once regardless of how many samples you run.

```bash
cp .env.example .env
# Edit .env: set MASTER_SECRET (openssl rand -base64 32) and AUTH_CLIENT_SECRET to real
# random values. AUTH_CLIENT_ID can stay as-is or be anything.
docker compose up -d
docker compose ps        # wait for healthy
docker compose logs -f eudiplo   # if you need to watch startup
```

This also starts EUDIPLO's own admin UI at <http://localhost:4200> (log in with `AUTH_CLIENT_ID`/`AUTH_CLIENT_SECRET`).

Tear down: `docker compose down -v` (from repo root; `-v` also drops the named config volume — only do this when done with every sample).

The compose file pins EUDIPLO to an explicit image tag (currently `5.1.0`), not `latest` — bump deliberately.

## Build / run commands

Solution file: `Eudiplo.Client.Samples.slnx` (only includes the two .NET *executable* projects — the AccessControl frontend is a separate npm project, not part of the .sln).

```bash
dotnet build Eudiplo.Client.Samples.slnx
```

### `Eudiplo.Client.Sample` (generic console sample)

```bash
export AUTH_CLIENT_ID=sample-root-client   # same value as in .env
export AUTH_CLIENT_SECRET=...              # same value as in .env
dotnet run --project Eudiplo.Client.Sample
```
Creates a tenant, creates a key-chain as that tenant, then deletes the tenant — safe to re-run.

### `Eudiplo.Client.Sample.AccessControl` (3-tier sample)

```bash
# 1. build the frontend (outputs into ../Backend/wwwroot — the backend serves it from there;
#    re-run after any frontend change, there's no watcher wired into the backend)
cd Eudiplo.Client.Sample.AccessControl/Frontend
npm install
npm run build

# 2. run the backend
cd ../Backend
export AUTH_CLIENT_ID=sample-root-client
export AUTH_CLIENT_SECRET=...
dotnet run --project .
# → http://localhost:5050
```

For frontend edit-and-reload without rebuilding into `wwwroot`, run the Vite dev server instead of `npm run build`:
```bash
cd Eudiplo.Client.Sample.AccessControl/Frontend
npm run dev   # :5173, proxies /api to :5050, no CORS setup needed
```

There are no automated test suites in this repo — verification is "run the sample against the real docker-composed EUDIPLO instance and observe the console output / browser behavior," as documented in each sample's README.

## Architecture

### Central client-usage pattern: root client → tenant-scoped client

Every sample follows the same two-step DI/auth shape that `Eudiplo.Client` is built around:

1. A **root client** (`EudiploApiClient`, DI-registered via `services.AddEudiploClient(o => {...})`) is authenticated with the tenant-less root credentials (`AUTH_CLIENT_ID`/`AUTH_CLIENT_SECRET`). It can manage tenants (`GetTenantsAsync`, `CreateTenantAsync`, `DeleteTenantAsync`, `GetTenantAsync`) but not tenant-scoped resources like key-chains.
2. Creating a tenant (`CreateTenantAsync`) returns an auto-generated **tenant admin client**'s `clientId`/`clientSecret` (parsed out of the JSON response, under `.client.clientId`/`.client.clientSecret`). A **second** `EudiploApiClient` is constructed manually (its constructor is deliberately public, not just DI-injectable) from an `IHttpClientFactory`-created `HttpClient` (via `EudiploApiClient.HttpClientName`) plus those tenant credentials. This second client does tenant-scoped operations (key-chains, presentation configs, sessions).

The tenant admin client's secret is returned **only once**, at creation time — EUDIPLO cannot re-issue it. Long-lived services (like `GateService`) that can't safely persist it just delete-and-recreate the tenant on every startup unless pointed at an already-provisioned tenant via env vars (`GATE_CLIENT_ID`/`GATE_CLIENT_SECRET` in the AccessControl sample) — see `GateService.InitializeAsync`'s doc comment for the full reasoning.

### `Eudiplo.Client.Sample.AccessControl`'s three real tiers

Unlike the single-project generic sample, this one deliberately has separate `Backend/` and `Frontend/` folders to demonstrate the trust boundary from EUDIPLO's architecture diagram — the browser never talks to EUDIPLO directly, only to this sample's own backend:

```
Browser (gate-app, Lit + TypeScript)  →  Backend (ASP.NET Core minimal API)  →  EUDIPLO
              fetch + EventSource            Eudiplo.Client (only piece with credentials)
```

- **`Backend/Program.cs`** — minimal API with three endpoints: `POST /api/gate/sessions` (opens a presentation request), `GET /api/gate/sessions/{id}/events` (SSE passthrough of `SubscribeToSessionEventsAsync`, enriched with the full session via a REST call once a terminal status is reached), `GET /api/gate/sessions/{id}` (one-shot polling fallback, same enrichment). Also serves the built frontend as static files from `wwwroot/`.
- **`Backend/GateService.cs`** — owns the gate's EUDIPLO tenant for the process lifetime (a gate has a stable identity, unlike a per-request tenant). Provisions tenant → access key-chain → presentation config (`PresentationConfigId = "access-control-age-check"`) once at startup.
- **`Frontend/src/gate-app.ts`** — single Lit 3 + TypeScript component, no Shadow DOM, no router, built with Vite. Renders the `openid4vp://` request as a QR code and reacts to both the SSE stream and a 3s poll.

Non-obvious constraints baked into this code (don't "fix" these without re-reading the README's numbered list first):
- A presentation offer 404s without an access key-chain provisioned first.
- `SubscribeToSessionEventsAsync`'s SSE endpoint takes the token via a `?token=` query param, not an `Authorization` header (browsers' `EventSource` can't send custom headers).
- The real German PID (`vct: urn:eudi:pid:de:1`) has no `age_over_18`/`age_equal_or_over` claim — only `birthdate`. The gate requests `birthdate` (full disclosure) and checks the 18-year threshold itself, server-side, in `Program.cs`'s `EnforceAgeGate` — rewriting a `completed` session to `failed` with an `errorReason` when underage, so the frontend needs no special-casing.
- The polling fallback (`GET /api/gate/sessions/{id}`) exists alongside SSE because a backgrounded mobile browser tab (e.g. switching to the wallet app to scan/confirm) can silently kill the SSE connection with no `onerror` ever firing.

### Solution/package structure

- `Directory.Build.props` — shared MSBuild settings for every .csproj: nullable enabled, implicit usings, latest analyzers.
- `Directory.Packages.props` — central package version management (`ManagePackageVersionsCentrally`); add new package versions here, not in individual `.csproj` files.
- `Eudiplo.Client.Samples.slnx` — the new XML-light solution format; only lists the .NET executable projects.
