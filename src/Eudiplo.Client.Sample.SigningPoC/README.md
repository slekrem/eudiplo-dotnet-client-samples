# Eudiplo.Client signing spike (NOT a sample) — concluded, negative result

Investigated [#7](https://github.com/slekrem/eudiplo-dotnet-client-samples/issues/7):
can OID4VP `transaction_data` be used as a lightweight, identity-bound "confirm this
text" mechanism? This is **not** a Qualified Electronic Signature — see the issue for
what that actually requires (a separate RQES/CSC-API/QTSP path outside EUDIPLO).

**Conclusion: not currently possible against the real German sandbox wallet (iOS,
`org.sprind.wallet.sandbox`), tested 2026-07-09.** Three rounds, each answering a
different part of the question:

1. `type: "text_confirmation"` (made up) — **rejected outright**, wallet-side, before
   any confirmation screen: `Unsupported transaction data type: text_confirmation`.
2. `type: "qes_authorization"` (the *real*, standardized RQES-authorization type per
   [EWC-consortium RFC-010](https://github.com/EWC-consortium/eudi-wallet-rfcs/blob/main/ewc-rfc010-long-term-certifice-qes-creation.md),
   with `signatureQualifier`/`documentDigests`) — **rejected the same way**:
   `Unsupported transaction data type: qes_authorization`.
3. `type: "authorization"` — the literal string
   [`VPConfiguration.default()`](https://github.com/eu-digital-identity-wallet/eudi-lib-ios-openid4vp-swift/blob/main/Sources/Entities/Types/Configuration/VPConfiguration.swift)
   actually whitelists in the wallet's own OID4VP library source, confirmed by reading
   `eudi-lib-ios-openid4vp-swift` directly — **validation passed**, the presentation
   completed normally (birthdate disclosed, session `consumed: true`), but **the wallet's
   UI never displayed our `text` field to the user at all**. Confirmed both by fresh
   wallet-log inspection (no error, no UI-related log line either) and by directly asking
   the person holding the phone what the screen showed: "nur Geburtsdatum, kein weiterer
   Text" (just the birthdate, no extra text).

Every round, EUDIPLO itself handled `transaction_data` correctly — verified by decoding
the signed request JWT each time: our JSON came back exactly as submitted, base64url
per spec, in the `transaction_data` claim. All three outcomes trace to the wallet, not
EUDIPLO or `Eudiplo.Client`:

- Rounds 1–2 rejected because the wallet validates `type` against a fixed whitelist
  (`TransactionData.isSupported` in the library source) — there's no other type value
  worth guessing, since the app doesn't override the SDK's default list (checked
  `eudi-app-ios-wallet-ui`'s `WalletKitConfig.swift` — no `VPConfiguration` override
  present).
- Round 3's type passes that check, but nothing downstream in this wallet build renders
  `specificParameters()` (the mechanism the library itself provides for reading
  type-specific fields like our `text`) to the user. Structurally present in the
  protocol and the library API, not yet wired to any UI in this app build.

**Bottom line:** this isn't a config problem on our side, and there's no remaining
`type` value worth trying against this specific wallet build — we've now exhausted what
the wallet's own whitelist supports. A working "confirm this text" flow would need
either a wallet build that actually implements a `transaction_data` confirmation screen,
or the full RQES path (real QTSP + `qes_authorization` support on both sides) once that
ships.

Deliberately not wired into `Eudiplo.Client.Samples.slnx`, the CI workflow, or
Dependabot — this was a throwaway console script to test a hypothesis, not a maintained
sample. Worth re-running once a wallet build implements `transaction_data` UI.

## Run it

```bash
export EUDIPLO_BASE_URL=https://your-eudiplo-instance.example
export AUTH_CLIENT_ID=...      # a tenant client with presentation:manage/presentation:request —
export AUTH_CLIENT_SECRET=...  # e.g. AccessControl's gate tenant, since it already has a
                                # registrar-trusted access certificate a real wallet will accept
dotnet run --project .
```

Prints the `openid4vp://` request URL, writes a scannable QR code to `qr.png` (or
`$QR_OUTPUT_PATH` if set) next to it, then polls for the session result and prints the
full session JSON once the wallet responds — that's what tells us whether/how
`transaction_data` comes back. (Polls rather than using
`SubscribeToSessionEventsAsync` — that SSE connection threw
`HttpIOException: The response ended prematurely` outright during this spike; see the
code comment for why a one-shot script just avoids it rather than adding back the dual
SSE+poll complexity AccessControl's real backend needs.)
