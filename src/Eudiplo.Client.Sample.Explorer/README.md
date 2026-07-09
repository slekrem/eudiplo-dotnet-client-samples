# Eudiplo.Client explorer sample

A small "point it at any EUDIPLO tenant and see what's reachable" dashboard — ASP.NET Core
backend + Lit/TypeScript frontend. Unlike every other sample in this repo, credentials
aren't fixed at startup via environment variables — the EUDIPLO base URL, client id, and
client secret are entered live in the browser and submitted with each query.

```
Browser (explorer-app, Lit + TypeScript)  →  Backend (ASP.NET Core)  →  EUDIPLO
              fetch                              Eudiplo.Client
```

This backend needs **no environment variables** to start. It never stores what you submit
either — a fresh `EudiploApiClient` is built per request from the submitted credentials and
discarded once the response is sent. Nothing is written to browser storage (`localStorage`,
cookies, …) either — reload the page and you'll need to re-enter everything.

## What it queries

A tenant-scoped client typically doesn't hold every EUDIPLO role — a
`presentation:manage` tenant can't list credential configs, an `issuance:manage` tenant
can't list verifier configs, and so on. Rather than assume, this sample queries a handful
of read-only "basic info" endpoints independently and shows exactly what each returns:

- `GetVersionAsync` — the EUDIPLO server version
- `GetKeyChainsAsync` — the tenant's key-chains
- `GetClientsAsync` — the tenant's OAuth2 clients
- `GetVerifierConfigsAsync` — presentation configs
- `GetCredentialConfigsAsync` — issuance/credential configs
- `GetUsersAsync` — human users

Each is caught independently server-side (see `QueryAsync` in `Backend/Program.cs`), so one
query failing (e.g. a 403 for a role the given credentials don't hold) doesn't take down
the rest of the dashboard — the frontend just shows that section's error inline.

## 1. Build the frontend

```bash
cd Frontend
npm install
npm run build
```

This builds into `../Backend/wwwroot` — the backend serves it from there. Re-run this
after any frontend change; there's no file-watcher wired into the backend.

## 2. Run the backend

```bash
cd ../Backend
dotnet run --project .
# → http://localhost:5070
```

No environment variables needed. Open the URL, fill in the EUDIPLO base URL and a client's
id/secret (root or tenant-scoped both work — the dashboard just shows whatever that client
can reach), and submit.

## Live frontend development

```bash
cd Frontend
npm run dev   # serves on :5173, proxies /api to :5070 — no CORS needed either way
```

## Why this sample skips `AddEudiploClient`

Every other sample calls `services.AddEudiploClient(o => { o.BaseUrl = ...; ... })` once at
startup — that configures a named `HttpClient`'s base address from a URL that's already
known. This backend doesn't know the target EUDIPLO instance until a request arrives, so it
can't use that pattern. Instead, `Backend/Program.cs` takes a plain `HttpClient` from
`IHttpClientFactory`, sets `BaseAddress` to whatever the browser submitted, and constructs
`EudiploApiClient` directly with it. `EudiploApiClient`'s constructor is public precisely to
support this — building a client per request against caller-supplied credentials, not just
DI registration at startup.
