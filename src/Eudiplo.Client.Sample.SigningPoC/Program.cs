using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Eudiplo.Client;
using QRCoder;

// SPIKE — see https://github.com/slekrem/eudiplo-dotnet-client-samples/issues/7. Not part
// of the sample family: deliberately not wired into Eudiplo.Client.Samples.slnx, the CI
// workflow, or Dependabot. Tests the hypothesis that OID4VP `transaction_data` can serve
// as a lightweight, identity-bound "confirm this text" mechanism — NOT a Qualified
// Electronic Signature (that runs through a separate RQES/CSC-API/QTSP path entirely
// outside EUDIPLO; see the issue for what we found there).
//
// Round 1 tried a made-up `type: "text_confirmation"` — the real wallet rejected it
// outright ("Unsupported transaction data type: text_confirmation"). transaction_data's
// `type` isn't freeform; wallets validate it against known, registered values. Round 2
// (this version) uses the real registered type for RQES authorization —
// `qes_authorization`, with `signatureQualifier`/`documentDigests` — per EWC-consortium's
// RFC-010 (https://github.com/EWC-consortium/eudi-wallet-rfcs/blob/main/ewc-rfc010-long-term-certifice-qes-creation.md).
// This still won't complete a real signature (there's no QTSP behind this test), but
// tells us whether the wallet at least *recognizes* the type and gets further.
//
// Reuses AccessControl's already-provisioned, registrar-certified "gate" tenant (via the
// same AUTH_CLIENT_ID/AUTH_CLIENT_SECRET convention every other sample uses) specifically
// because a real wallet will only render/accept a request signed with a trusted access
// certificate — a fresh self-signed tenant would fail before we ever got to observe
// whether transaction_data itself works.

var baseUrl = Environment.GetEnvironmentVariable("EUDIPLO_BASE_URL")
    ?? throw new InvalidOperationException("Set EUDIPLO_BASE_URL.");
var clientId = Environment.GetEnvironmentVariable("AUTH_CLIENT_ID")
    ?? throw new InvalidOperationException("Set AUTH_CLIENT_ID (a tenant client with presentation:manage/presentation:request — e.g. AccessControl's gate tenant).");
var clientSecret = Environment.GetEnvironmentVariable("AUTH_CLIENT_SECRET")
    ?? throw new InvalidOperationException("Set AUTH_CLIENT_SECRET.");

const string ConfigId = "signing-poc-transaction-data";
const string TestDocumentText = "I confirm I have read and agree to this test transaction. (signing-poc spike)";

var documentHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(TestDocumentText)));

using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
using var client = new EudiploApiClient(http, clientId, clientSecret);

Console.WriteLine($"Connecting to EUDIPLO at {baseUrl} ...");
Console.WriteLine($"Test document: \"{TestDocumentText}\"");
Console.WriteLine($"SHA-256 (base64): {documentHash}");

// Same DCQL shape as AccessControl's age-check config (birthdate from the real German
// PID) — reusing a claim request we already know a real wallet handles, so the only new
// variable in this test is the transaction_data block itself.
var configJson = $$"""
    {
        "id": "{{ConfigId}}",
        "description": "Spike: qes_authorization transaction_data test",
        "dcql_query": {
            "credentials": [
                {
                    "id": "pid-sd-jwt",
                    "format": "dc+sd-jwt",
                    "meta": { "vct_values": ["urn:eudi:pid:de:1"] },
                    "claims": [ { "path": ["birthdate"] } ]
                }
            ]
        },
        "transaction_data": [
            {
                "type": "qes_authorization",
                "credential_ids": ["pid-sd-jwt"],
                "signatureQualifier": "eu_eidas_qes",
                "documentDigests": [
                    {
                        "hash": {{JsonSerializer.Serialize(documentHash)}},
                        "label": "signing-poc-spike-test-document.txt",
                        "hashAlgorithmOID": "2.16.840.1.101.3.4.2.1"
                    }
                ]
            }
        ],
        "lifeTime": 300
    }
    """;

Console.WriteLine($"Registering presentation config '{ConfigId}' with transaction_data...");
await client.PostVerifierConfigAsync(configJson);

Console.WriteLine("Creating offer...");
var (requestUrl, sessionId) = await client.CreateOfferAsync(ConfigId);
Console.WriteLine($"Session: {sessionId}");
Console.WriteLine($"Request URL: {requestUrl}");

var qrPath = Environment.GetEnvironmentVariable("QR_OUTPUT_PATH") ?? Path.Combine(Directory.GetCurrentDirectory(), "qr.png");
using (var qrGenerator = new QRCodeGenerator())
using (var qrData = qrGenerator.CreateQrCode(requestUrl, QRCodeGenerator.ECCLevel.M))
{
    var qrCode = new PngByteQRCode(qrData);
    await File.WriteAllBytesAsync(qrPath, qrCode.GetGraphic(12));
}
Console.WriteLine($"QR code written to {qrPath}");
Console.WriteLine("\nScan with a real EUDI Wallet. Polling for the session result...\n");

// Plain polling, not SubscribeToSessionEventsAsync — AccessControl's own README already
// documents that this SSE endpoint's connection can die without warning (mid-wait, with
// no exception even, on a backgrounded mobile browser tab). Here it threw
// HttpIOException("The response ended prematurely") outright on a *server* connection
// with nothing backgrounding it, so for a short-lived one-shot script, skip it entirely
// rather than adding back the dual SSE+poll complexity a real backend needs.
while (true)
{
    await Task.Delay(3000);
    var full = await client.GetSessionAsync(sessionId);
    if (full is null)
    {
        Console.WriteLine("Session not found (expired?).");
        break;
    }

    var status = JsonDocument.Parse(full).RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;
    Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] status={status}");

    if (status is "completed" or "failed" or "expired")
    {
        Console.WriteLine("\nFull session (raw JSON — look for how transaction_data comes back):");
        Console.WriteLine(full);
        break;
    }
}
