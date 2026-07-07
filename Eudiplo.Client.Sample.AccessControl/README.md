# Eudiplo.Client access-control sample

The **Access Control System** pattern from EUDIPLO's own architecture diagram
([`docs/overview.excalidraw.svg`](https://github.com/openwallet-foundation-labs/eudiplo/blob/main/docs/overview.excalidraw.svg)) —
built as three real, separate tiers instead of one script:

```
Browser (gate-app, Lit + TypeScript)  →  Backend (ASP.NET Core)  →  EUDIPLO
              fetch + EventSource            Eudiplo.Client         (Docker)
```

The browser never talks to EUDIPLO directly — only to this sample's own small backend
API, which is the only piece that holds EUDIPLO credentials and uses `Eudiplo.Client`.
That's the point of the sample: showing where the client library actually sits in a real
integration, not just calling its methods from a script.

- **`Backend/`** — ASP.NET Core minimal API. Provisions its own EUDIPLO tenant once at
  startup (a gate has a stable identity, unlike a per-request tenant), then exposes:
  - `POST /api/gate/sessions` — opens a presentation request, returns `{ sessionId, requestUrl }`.
  - `GET /api/gate/sessions/{id}/events` — Server-Sent Events passthrough of
    `EudiploApiClient.SubscribeToSessionEventsAsync`, enriched with the full session
    (`errorReason`, verified claims) once a terminal status is reached.
  - Serves the built frontend as static files from `wwwroot/`.
- **`Frontend/`** — a single Lit 3 + TypeScript component (`gate-app`), built with Vite.
  No Shadow DOM (global stylesheet), no router — one page, one job. Renders the
  `openid4vp://` request as a QR code (via the `qrcode` package) and reacts to `EventSource`
  messages — no polling timer needed.

## 1. Start EUDIPLO

Shared across every sample under `samples/` — see [`../README.md`](../README.md) if you
haven't started it yet:

```bash
cd samples
cp .env.example .env   # if you haven't already
docker compose up -d
```

## 2. Build the frontend

```bash
cd samples/Eudiplo.Client.Sample.AccessControl/Frontend
npm install
npm run build
```

This builds into `../Backend/wwwroot` — the backend serves it from there. Re-run this
after any frontend change; there's no file-watcher wired into the backend.

## 3. Run the backend

```bash
cd ../Backend
export AUTH_CLIENT_ID=sample-root-client       # whatever you set in samples/.env
export AUTH_CLIENT_SECRET=...                  # whatever you set in samples/.env
dotnet run --project .
```

Startup provisions the gate's tenant, access key-chain, and presentation config against
the real EUDIPLO instance — watch the console for each step. Once you see
`Listening on http://localhost:5050`, open that URL in a browser.

## Live frontend development

For edit-and-reload on the frontend without rebuilding into `wwwroot` each time, run the
Vite dev server alongside the backend instead of step 2:

```bash
cd Frontend
npm run dev   # serves on :5173, proxies /api to :5050 — no CORS needed either way
```

## Completing the flow for real

The button opens a presentation request for an **age-over-18 check against a German PID
SD-JWT credential** (`vct: urn:eudi:pid:de:1`) — same DCQL as EUDIPLO's own demo assets.
Scanning the QR requires an EUDI Wallet holding one, e.g. the
[DE-Sandbox-Wallet](https://sandbox.eudi-wallet.org). Without one, the gate still runs
everything for real (tenant, key-chain, config, offer, live SSE connection) and the
session simply expires after 120 seconds — the frontend shows that honestly rather than
faking a result.

## Why building this against a real server mattered (twice)

1. **Creating a presentation offer 404s without an access key-chain first** — not
   documented anywhere, only found by running the original console version of this sample
   against a live EUDIPLO instance.
2. **`SubscribeToSessionEventsAsync` sent the token as an `Authorization` header and got a
   401** — building this backend's SSE endpoint and testing it end-to-end (not just against
   our own test suite's fake `HttpMessageHandler`) surfaced that EUDIPLO's session-events
   endpoint only accepts the token via a `?token=` query parameter (browsers' `EventSource`
   can't send custom headers, so the server had to be built that way). Our own fake mock
   couldn't have caught this — it only reflects back what we assumed. Fixed in
   `Eudiplo.Client` itself (`EudiploApiClient.Session.cs`), not just here.

## Known limitation

The tenant admin client's secret is only ever returned once, at creation time — EUDIPLO
has no way to re-fetch it later. So the backend can't restore a previous tenant across a
restart; it deletes any leftover tenant with the same id and creates a fresh one every
time it starts. Fine for a sample; a production gate would persist the tenant credentials
in a secret store instead.
