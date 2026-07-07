# Eudiplo.Client sample

Runs against a **real** EUDIPLO instance in Docker — not a mock — to demonstrate the
multi-tenant flow this client is built around: authenticate as the root client, create an
isolated tenant, then use that tenant's auto-generated admin client to create a key-chain.

## 1. Start EUDIPLO

Shared across every sample under `samples/` — see [`../README.md`](../README.md) if you
haven't started it yet:

```bash
cd samples
cp .env.example .env
# Edit .env: set MASTER_SECRET (openssl rand -base64 32) and AUTH_CLIENT_SECRET to
# real random values. AUTH_CLIENT_ID can stay as-is or be anything you like.
docker compose up -d
```

Wait for it to become healthy (`docker compose ps`), or watch logs with
`docker compose logs -f eudiplo`.

## 2. Run the sample

Export the **same** root-client credentials you just put in `.env`:

```bash
export AUTH_CLIENT_ID=sample-root-client       # whatever you set in .env
export AUTH_CLIENT_SECRET=...                  # whatever you set in .env
dotnet run --project .
```

Expected output:

```
Connecting to EUDIPLO at http://localhost:3000 ...
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

## 3. Tear down

From `samples/` (this stops the shared instance — only do this once you're done with every
sample, not just this one):

```bash
docker compose down -v
```

(`-v` also removes the named config volume — drop it if you want to keep EUDIPLO's state
between runs.)
