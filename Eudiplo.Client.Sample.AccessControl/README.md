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

## Verified against a real EUDI Wallet

This exact flow (tenant, key-chain, presentation config, `EnforceAgeGate` server-side
check) has been run against a **real EUDI Wallet holding a real, Bundesdruckerei-issued
German PID**, using a EUDIPLO instance registered with the German EUDI sandbox registrar
(real Access + Registration Certificates, obtained fully via `Eudiplo.Client`'s
`CreateAccessCertificateViaRegistrarAsync` — see `EudiploApiClient.Registrar.cs`). Result:
`status: completed`, real `birthdate` disclosed, session `consumed`. Locally, without your
own registrar registration, the flow runs identically up to the point of a real wallet
scanning it — see "Completing the flow for real" below.

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
  messages, plus a 3s polling fallback against `GET /api/gate/sessions/{id}` — see "why
  this mattered" below for why pure SSE alone wasn't enough on a real phone.

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

Set `GATE_CLIENT_ID`/`GATE_CLIENT_SECRET` instead to point this backend at an
already-provisioned tenant (e.g. one with a real registrar-issued access certificate, see
"Registrar registration" below) — skips all of the above and reuses that tenant as-is; see
`GateService.InitializeAsync`'s doc comment.

## Live frontend development

For edit-and-reload on the frontend without rebuilding into `wwwroot` each time, run the
Vite dev server alongside the backend instead of step 2:

```bash
cd Frontend
npm run dev   # serves on :5173, proxies /api to :5050 — no CORS needed either way
```

## Completing the flow for real

The button opens a presentation request for the holder's **`birthdate`** from a German PID
SD-JWT credential (`vct: urn:eudi:pid:de:1`) — the gate itself checks the age-18 threshold
server-side (`EnforceAgeGate` in `Backend/Program.cs`) once the wallet discloses it; see
"the real DE-PID has no age-only claim" below for why. Scanning the QR requires an EUDI
Wallet holding one, e.g. the [DE-Sandbox-Wallet](https://sandbox.eudi-wallet.org). Without
one, the gate still runs everything for real (tenant, key-chain, config, offer, live SSE
connection) and the session simply expires after 120 seconds — the frontend shows that
honestly rather than faking a result.

Without your own registrar registration, a real wallet will still reject the gate's
self-signed access certificate ("Could not trust certificate chain") — that's the one part
of the flow that genuinely needs registrar-issued certificates (see below) to complete.

## Why building this against a real server (and a real wallet) mattered, five times over

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
3. **The real DE-PID has no `age_over_18`/`age_equal_or_over` claim at all** — only
   `birthdate`. EUDIPLO's own demo assets (and this sample, originally) request the former;
   it works fine against a self-issued test credential, but a real Bundesdruckerei-issued
   PID in a real wallet simply doesn't carry it. Selective age-only disclosure isn't
   possible for this credential today — the gate now requests `birthdate` (full disclosure)
   and checks the 18-year threshold itself.
4. **`SubscribeToSessionEventsAsync`'s stream was bound by the shared `HttpClient`'s
   `Timeout`** (15s default) — `HttpClient.Timeout` covers the entire request, including
   reads on the response stream long after `SendAsync` returns, not just until headers
   arrive. Every real subscription (waiting on a human to unlock their wallet) was getting
   silently killed before anything interesting happened. Fixed in `Eudiplo.Client` itself —
   see its own CHANGELOG.
5. **Even after that fix, a real phone's browser can still silently drop the *browser →
   backend* SSE connection** while the tab is backgrounded — switching away to the wallet
   app to scan/confirm is exactly that. No `onerror` ever fires (the page's JS itself can be
   paused, not just the connection), so a `visibilitychange`-triggered resubscribe alone
   wasn't fully reliable in practice. Fixed by adding a plain 3-second poll
   (`GET /api/gate/sessions/{id}`) alongside the SSE subscription — it doesn't depend on any
   connection surviving backgrounding, only on browser timers resuming once the tab is
   foregrounded again, which is far more consistently supported. Verified against a real
   phone browser going through the full backgrounding round-trip.

## Registrar registration (what makes a real wallet actually trust the gate)

A self-signed access key-chain (what `GateService` provisions by default) makes a real EUDI
Wallet reject the request outright — it has nothing to anchor trust to. `Eudiplo.Client`'s
`EudiploApiClient.Registrar.cs` covers the fix EUDIPLO itself supports: register the
tenant's registrar credentials once (`CreateRegistrarConfigAsync`, e.g. against the German
sandbox registrar), then `CreateAccessCertificateViaRegistrarAsync(keyChainId)` gets a
real, registrar-signed certificate — no manual dashboard cert-request flow needed. A
presentation config's `registration_cert.body` field works the same way for the
Registration Certificate, with claims auto-derived from the DCQL query. Requires your own
relying-party registration with a EUDI wallet ecosystem's registrar — not something this
sample can set up for you, which is why it isn't part of the default local flow.

## Known limitation

The tenant admin client's secret is only ever returned once, at creation time — EUDIPLO
has no way to re-fetch it later. So the backend can't restore a previous tenant across a
restart; it deletes any leftover tenant with the same id and creates a fresh one every
time it starts. Fine for a sample; a production gate would persist the tenant credentials
in a secret store instead.
