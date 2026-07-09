# Eudiplo.Client samples

Runnable samples for [`Eudiplo.Client`](https://github.com/slekrem/eudiplo-dotnet-client)
([NuGet](https://www.nuget.org/packages/Eudiplo.Client)) — an unofficial .NET HTTP client
for [EUDIPLO](https://github.com/openwallet-foundation-labs/eudiplo). Each sample below
references the published package, not a local source checkout, so it builds the way a
real consumer would use it.

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

Most samples below pick one of these named examples and build the realistic integration
pattern for it; one (`Explorer`) is a general-purpose developer tool instead. All of them
use only `Eudiplo.Client` — against a **real** EUDIPLO instance, not a mock.

| Sample | Pattern | Status |
|---|---|---|
| [`Eudiplo.Client.Sample.AccessControl`](src/Eudiplo.Client.Sample.AccessControl) | 3-tier gate: Lit+TS UI → ASP.NET backend → EUDIPLO | ✅ |
| [`Eudiplo.Client.Sample.Explorer`](src/Eudiplo.Client.Sample.Explorer) | Enter an EUDIPLO URL + credentials in a UI, browse what's reachable | ✅ |
| `Eudiplo.Client.Sample.CRM` | Verified data via webhook → enrich a record | planned |
| `Eudiplo.Client.Sample.ERP` | Issue a credential ("credential creation") | planned |

`Eudiplo.Client.Sample.AccessControl` and `Eudiplo.Client.Sample.Explorer` aren't single
console projects — each has its own `Backend/` (the only piece using `Eudiplo.Client`) and
`Frontend/` (Lit + TypeScript UI) subfolders. For AccessControl that split shows the
boundary between "your UI" and "your backend" from the diagram above; for Explorer it's
just where a browser-facing form naturally lives.

## Pointing at an EUDIPLO instance

Every sample needs a running EUDIPLO instance and client credentials for it — these
samples don't provision one for you. Set these three environment variables before running
any sample:

```bash
export EUDIPLO_BASE_URL=https://your-eudiplo-instance.example
export AUTH_CLIENT_ID=...
export AUTH_CLIENT_SECRET=...
```

What kind of client `AUTH_CLIENT_ID`/`AUTH_CLIENT_SECRET` need to be (a tenant-less root
client, or a specific tenant's own client) differs per sample — see its own README.

`Eudiplo.Client.Sample.Explorer` is the exception: it needs no environment variables at
all, since it takes an EUDIPLO URL and credentials from a form in the browser instead.

Then `cd` into whichever sample under `src/` you want to run — see its own README for exact steps.
