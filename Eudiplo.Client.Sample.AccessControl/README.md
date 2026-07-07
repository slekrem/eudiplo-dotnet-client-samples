# Eudiplo.Client access-control sample

The **Access Control System** pattern from EUDIPLO's own architecture diagram
([`docs/overview.excalidraw.svg`](https://github.com/openwallet-foundation-labs/eudiplo/blob/main/docs/overview.excalidraw.svg)):
a gate that verifies a presentation before granting entry — the "verified data" direction.

Runs against a **real** EUDIPLO instance, not a mock. Creates its own tenant (a gate has its
own identity), provisions an access key-chain, registers an age-over-18 presentation config
(DCQL lifted from EUDIPLO's own demo assets), opens a presentation request, and polls the
session — printing `ACCESS GRANTED`/`ACCESS DENIED` based on the real verification result.

## Prerequisites

Start the shared EUDIPLO instance first — see [`../README.md`](../README.md):

```bash
cd samples
cp .env.example .env   # if you haven't already
docker compose up -d
```

## Run it

```bash
cd samples/Eudiplo.Client.Sample.AccessControl
export AUTH_CLIENT_ID=sample-root-client       # whatever you set in samples/.env
export AUTH_CLIENT_SECRET=...                  # whatever you set in samples/.env
dotnet run --project .
```

It prints an `openid4vp://` URI (and, if you have [`qrencode`](https://fukuchi.org/works/qrencode/)
installed, an ASCII QR code) and waits up to 90 seconds (`POLL_TIMEOUT_SECONDS` to change
that) for a wallet to respond.

## Completing the flow for real

Scanning the printed link requires an **EUDI Wallet holding a German PID SD-JWT
credential** (`vct: urn:eudi:pid:de:1`) — e.g. the
[DE-Sandbox-Wallet](https://sandbox.eudi-wallet.org). Without one, the sample still runs
everything up to that point for real (tenant, key-chain, presentation config, offer
creation, polling) and times out with an honest message rather than pretending to succeed.

If you do scan it successfully, you'll see the verified session data printed and either:

```
✅ ACCESS GRANTED — presentation verified.
```
or
```
❌ ACCESS DENIED — verification failed: <reason>
```

The sample deletes the tenant it created at the end (in a `finally` block, so this happens
even on failure/timeout), so it's safe to run repeatedly.

## Why a real server mattered here

Building this sample against the real EUDIPLO instance (rather than a mock) caught a real
gap on the first run: creating a presentation offer requires the tenant to already have an
**access key-chain** to sign the request with — EUDIPLO returns `404 Not Found` otherwise.
That's now step 2 of the flow above. A mock we controlled ourselves would have just
reflected back whatever we assumed, silently hiding this requirement.
