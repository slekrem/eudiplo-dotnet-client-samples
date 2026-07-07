using Eudiplo.Client;
using Microsoft.Extensions.DependencyInjection;

// Runs against a REAL EUDIPLO instance (see ../docker-compose.yml) — not a mock.
// Demonstrates the "Access Control System" integration pattern from EUDIPLO's own
// architecture diagram (docs/overview.excalidraw.svg): a gate that verifies a
// presentation before granting entry — the "verified data" direction, EUDIPLO's own
// vocabulary for what a CRM or access-control system consumes.
//
// The check here is an age-over-18 presentation against a German PID SD-JWT credential
// (dcql_query lifted from EUDIPLO's own demo assets: assets/config/demo/presentation/
// age-over-18.json), the same style of check Entryix's own guest-list age gate uses.
//
// Completing the flow end-to-end requires a real EUDI Wallet holding a matching PID
// credential (e.g. the DE-Sandbox-Wallet) to scan the printed QR/URL — this sample runs
// fully up to that point without one; see README.md.

var baseUrl = Environment.GetEnvironmentVariable("EUDIPLO_BASE_URL") ?? "http://localhost:3000";
var rootClientId = Environment.GetEnvironmentVariable("AUTH_CLIENT_ID")
    ?? throw new InvalidOperationException("Set AUTH_CLIENT_ID (same value as in samples/.env).");
var rootClientSecret = Environment.GetEnvironmentVariable("AUTH_CLIENT_SECRET")
    ?? throw new InvalidOperationException("Set AUTH_CLIENT_SECRET (same value as in samples/.env).");
var pollTimeout = TimeSpan.FromSeconds(
    int.TryParse(Environment.GetEnvironmentVariable("POLL_TIMEOUT_SECONDS"), out var s) ? s : 90);

var services = new ServiceCollection();
services.AddEudiploClient(o =>
{
    o.BaseUrl = baseUrl;
    o.ClientId = rootClientId;
    o.ClientSecret = rootClientSecret;
});
using var provider = services.BuildServiceProvider();
var rootClient = provider.GetRequiredService<EudiploApiClient>();

Console.WriteLine($"Connecting to EUDIPLO at {baseUrl} ...");

var tenantId = $"gate-{Guid.NewGuid():N}"[..16];
Console.WriteLine($"\nCreating tenant '{tenantId}' (an access-control gate's own tenant)...");

var createTenantJson = $$"""
    {
        "id": "{{tenantId}}",
        "name": "Eudiplo.Client access-control sample",
        "roles": ["presentation:manage", "presentation:request"]
    }
    """;
var createTenantResponseJson = await rootClient.CreateTenantAsync(createTenantJson);
using var createTenantResponse = System.Text.Json.JsonDocument.Parse(createTenantResponseJson);
var clientElement = createTenantResponse.RootElement.GetProperty("client");
var tenantClientId = clientElement.GetProperty("clientId").GetString()!;
var tenantClientSecret = clientElement.GetProperty("clientSecret").GetString()!;
Console.WriteLine($"Tenant created. Gate client: {tenantClientId}");

try
{
    var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
    using var gateClient = new EudiploApiClient(
        httpClientFactory.CreateClient(EudiploApiClient.HttpClientName),
        tenantClientId,
        tenantClientSecret);

    // A presentation request must be signed with the tenant's access certificate — EUDIPLO
    // looks one up when creating the offer and 404s if none exists yet, so the gate needs
    // one before it can open (same reason the other sample creates an "access" key-chain).
    Console.WriteLine("\nProvisioning the gate's access key-chain...");
    await gateClient.CreateKeyChainAsync(usageType: "access", type: "standalone", description: "Gate signing key");

    const string presentationConfigId = "access-control-age-check";
    Console.WriteLine($"\nRegistering presentation config '{presentationConfigId}' (age-over-18, PID SD-JWT)...");
    var presentationConfigJson = $$"""
        {
            "id": "{{presentationConfigId}}",
            "description": "Access Control — proves the holder is 18+ without revealing their exact birthdate",
            "dcql_query": {
                "credentials": [
                    {
                        "id": "pid-sd-jwt",
                        "format": "dc+sd-jwt",
                        "meta": { "vct_values": ["urn:eudi:pid:de:1"] },
                        "claims": [ { "path": ["age_equal_or_over", "18"] } ]
                    }
                ]
            },
            "lifeTime": 300
        }
        """;
    // Each run creates a brand-new tenant, so there's no risk of colliding with a config
    // left over from a previous run — no pre-cleanup needed.
    await gateClient.PostVerifierConfigAsync(presentationConfigJson);

    Console.WriteLine("\nOpening the gate — creating a presentation request...");
    var (requestUrl, sessionId) = await gateClient.CreateOfferAsync(presentationConfigId);

    Console.WriteLine("\nScan this with an EUDI Wallet holding a German PID credential to walk through the gate:");
    Console.WriteLine(requestUrl);
    TryPrintAsciiQrCode(requestUrl);

    Console.WriteLine($"\nWaiting up to {pollTimeout.TotalSeconds:0}s for a wallet to present a credential...");
    var deadline = DateTime.UtcNow + pollTimeout;
    while (DateTime.UtcNow < deadline)
    {
        var sessionJson = await gateClient.GetSessionAsync(sessionId);
        if (sessionJson is not null)
        {
            using var sessionDoc = System.Text.Json.JsonDocument.Parse(sessionJson);
            var status = sessionDoc.RootElement.TryGetProperty("status", out var st) ? st.GetString() : null;

            // SessionStatus enum (EUDIPLO source): active | fetched | completed | expired | failed
            switch (status)
            {
                case "completed":
                    Console.WriteLine("\n✅ ACCESS GRANTED — presentation verified.");
                    Console.WriteLine("Verified credential data (what a real gate would check the claim value in):");
                    Console.WriteLine(sessionJson);
                    goto done;
                case "failed":
                    var reason = sessionDoc.RootElement.TryGetProperty("errorReason", out var r) ? r.GetString() : null;
                    Console.WriteLine($"\n❌ ACCESS DENIED — verification failed: {reason ?? "(no reason given)"}");
                    goto done;
                case "expired":
                    Console.WriteLine("\n⏱ Session expired before a wallet responded.");
                    goto done;
                default:
                    Console.Write(".");
                    break;
            }
        }
        await Task.Delay(2000);
    }
    Console.WriteLine("\n\nTimed out waiting for a wallet — no EUDI Wallet available in this environment scanned the " +
        "request above. Everything up to this point (tenant, presentation config, offer creation, polling) ran for real " +
        "against EUDIPLO; completing the last step needs an actual EUDI Wallet with a matching PID credential.");

done:
    await gateClient.DeleteVerifierConfigAsync(presentationConfigId);
}
finally
{
    Console.WriteLine($"\nDeleting tenant '{tenantId}'...");
    await rootClient.DeleteTenantAsync(tenantId);
    Console.WriteLine("Done.");
}

static void TryPrintAsciiQrCode(string content)
{
    try
    {
        using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "qrencode",
            ArgumentList = { "-t", "ANSIUTF8", content },
            RedirectStandardOutput = false,
            UseShellExecute = false,
        });
        proc?.WaitForExit(2000);
    }
    catch
    {
        // qrencode not installed — the printed URL above is enough, just without ASCII art.
    }
}
