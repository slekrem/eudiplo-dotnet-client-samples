# Eudiplo.Client signing spike (NOT a sample) — concluded, negative result

Investigated [#7](https://github.com/slekrem/eudiplo-dotnet-client-samples/issues/7):
can OID4VP `transaction_data` be used as a lightweight, identity-bound "confirm this
text" mechanism? This is **not** a Qualified Electronic Signature — see the issue for
what that actually requires (a separate RQES/CSC-API/QTSP path outside EUDIPLO).

**Conclusion: not currently possible against the real German sandbox wallet (iOS,
`org.sprind.wallet.sandbox`), tested 2026-07-09.** Two rounds, both rejected outright by
the wallet before ever reaching EUDIPLO's response endpoint:

1. A made-up `type: "text_confirmation"` — rejected: `transaction_data`'s `type` isn't
   freeform, wallets validate it against a fixed set of known values.
2. `type: "qes_authorization"` (the *real*, standardized RQES-authorization type per
   [EWC-consortium RFC-010](https://github.com/EWC-consortium/eudi-wallet-rfcs/blob/main/ewc-rfc010-long-term-certifice-qes-creation.md),
   with `signatureQualifier`/`documentDigests`) — rejected the same way:
   `Unsupported transaction data type: qes_authorization`.

Both times, EUDIPLO itself accepted and correctly forwarded the `transaction_data` block
(verified by decoding the signed request JWT — the exact JSON we sent came back
base64url-encoded in the `transaction_data` claim, unmodified). The rejection happens
entirely wallet-side, in `EudiWalletKit`'s DCQL/request validation, before the user ever
sees a confirmation screen. Matches what the earlier research suggested: Germany's
sandbox wallet is rolling out PID identification first, with QES support to follow later
— this build simply doesn't implement `qes_authorization` yet.

Deliberately not wired into `Eudiplo.Client.Samples.slnx`, the CI workflow, or
Dependabot — this was a throwaway console script to test a hypothesis, not a maintained
sample. Worth re-running once a wallet build adds `qes_authorization` support.

## Run it

```bash
export EUDIPLO_BASE_URL=https://your-eudiplo-instance.example
export AUTH_CLIENT_ID=...      # a tenant client with presentation:manage/presentation:request —
export AUTH_CLIENT_SECRET=...  # e.g. AccessControl's gate tenant, since it already has a
                                # registrar-trusted access certificate a real wallet will accept
dotnet run --project .
```

Prints the `openid4vp://` request URL, writes a scannable QR code to `qr.png` (or
`$QR_OUTPUT_PATH` if set) next to it, then subscribes to session events and prints the
full session JSON once the wallet responds — that's what tells us whether/how
`transaction_data` comes back.
