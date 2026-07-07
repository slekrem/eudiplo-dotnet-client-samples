# Eudiplo.Client samples

EUDIPLO's own architecture diagram frames the integration point this client covers as
"your services" — with **CRM**, **ERP**, and **Access Control System** as its own named
examples ([source](https://github.com/openwallet-foundation-labs/eudiplo/blob/main/docs/overview.excalidraw.svg)):

```
EUDI Wallet ⇄ (OID4VC / SD-JWT VC) ⇄ EUDIPLO ⇄ (HTTP, webhooks, JSON) ⇄ your services
                                                                          - CRM
                                                                          - ERP
                                                                          - Access Control System
```

Two directions, two different integration shapes:
- **verified data & notifications** (EUDIPLO → your service) — a presentation was verified,
  here are the claims. This is what a **CRM** (enrich a customer record) or an
  **Access Control System** (grant/deny entry) cares about.
- **credential creation** (your service → EUDIPLO) — issue a credential to a wallet. This is
  what an **ERP** (issue an employee badge or purchase authorization) cares about.

Each sample below picks one of these named examples and builds the realistic integration
pattern for it, using only `Eudiplo.Client` — against a **real** EUDIPLO instance, not a mock.

| Sample | Pattern | Status |
|---|---|---|
| [`Eudiplo.Client.Sample`](Eudiplo.Client.Sample) | Generic: root client → tenant → key-chain | ✅ |
| [`Eudiplo.Client.Sample.AccessControl`](Eudiplo.Client.Sample.AccessControl) | Verify-loop gate: age-over-18 presentation | ✅ |
| `Eudiplo.Client.Sample.CRM` | Verified data via webhook → enrich a record | planned |
| `Eudiplo.Client.Sample.ERP` | Issue a credential ("credential creation") | planned |

## Shared EUDIPLO instance

All samples run against one shared EUDIPLO instance defined here (`docker-compose.yml`),
so you only start it once regardless of which sample(s) you run.

```bash
cd samples
cp .env.example .env
# Edit .env: set MASTER_SECRET (openssl rand -base64 32) and AUTH_CLIENT_SECRET to real
# random values. AUTH_CLIENT_ID can stay as-is or be anything you like.
docker compose up -d
```

This also starts EUDIPLO's own admin UI at <http://localhost:4200> (log in with the
`AUTH_CLIENT_ID`/`AUTH_CLIENT_SECRET` from your `.env`) if you want to poke around visually
while a sample runs.

Then `cd` into whichever sample you want to run — see its own README for exact steps.

Tear down when done: `docker compose down -v` (from this `samples/` directory).
