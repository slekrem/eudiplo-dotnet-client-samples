# Eudiplo.Client sample

Runs against a **real** EUDIPLO instance — not a mock — to demonstrate the multi-tenant
flow this client is built around: authenticate as the root client, create an isolated
tenant, then use that tenant's auto-generated admin client to create a key-chain.

## 1. Point at your EUDIPLO instance

See [`../../README.md`](../../README.md) for details:

```bash
export EUDIPLO_BASE_URL=https://your-eudiplo-instance.example
export AUTH_CLIENT_ID=...
export AUTH_CLIENT_SECRET=...
```

## 2. Run the sample

```bash
dotnet run --project .
```

Expected output:

```
Connecting to EUDIPLO at https://your-eudiplo-instance.example ...
Found 0 existing tenant(s).

Creating tenant 'sample-xxxxxxxxxxxxx' with an issuance-scoped admin client...
Tenant created. Admin client: sample-xxxxxxxxxxxxx-admin

Creating a key-chain as the new tenant...
Key-chain created: <uuid>
Tenant now has 2 key-chain(s).

Deleting tenant 'sample-xxxxxxxxxxxxx'...
Done.
```

(The count is 2, not 1 — EUDIPLO auto-provisions a default attestation key-chain for every
new tenant, on top of the "access" one the sample creates explicitly.)

The sample deletes the tenant it created at the end, so it's safe to run repeatedly.
