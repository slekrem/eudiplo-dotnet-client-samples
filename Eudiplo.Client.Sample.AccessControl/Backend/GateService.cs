using System.Text.Json;
using Eudiplo.Client;

namespace Eudiplo.Client.Sample.AccessControl.Backend;

/// <summary>
/// Owns the gate's EUDIPLO tenant for the lifetime of this backend process. Unlike the
/// original console sample (which created and tore down a tenant per run), a real backend
/// provisions its tenant once at startup and reuses it for every request — a gate has a
/// stable identity, it doesn't get a new one per visitor.
///
/// Known limitation: the tenant admin client's secret is only ever returned once, at
/// creation time (EUDIPLO doesn't let you re-fetch it later). So this can't restore a
/// previous tenant across a backend restart — it deletes any leftover tenant with the same
/// id and creates a fresh one every time <see cref="InitializeAsync"/> runs. Fine for a
/// sample; a production gate would persist the tenant credentials in a secret store instead.
/// </summary>
public sealed class GateService
{
    public const string PresentationConfigId = "access-control-age-check";

    private readonly EudiploApiClient _rootClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _tenantId;

    public GateService(EudiploApiClient rootClient, IHttpClientFactory httpClientFactory, string tenantId)
    {
        _rootClient = rootClient;
        _httpClientFactory = httpClientFactory;
        _tenantId = tenantId;
    }

    /// <summary>The tenant-scoped client, ready to use once <see cref="InitializeAsync"/> has completed.</summary>
    public EudiploApiClient GateClient { get; private set; } = null!;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        Console.WriteLine($"Provisioning gate tenant '{_tenantId}'...");

        if (await _rootClient.GetTenantAsync(_tenantId, ct) is not null)
        {
            Console.WriteLine("Found a leftover tenant from a previous run — deleting it first...");
            await _rootClient.DeleteTenantAsync(_tenantId, ct);
        }

        var createTenantJson = $$"""
            {
                "id": "{{_tenantId}}",
                "name": "Eudiplo.Client access-control backend",
                "roles": ["presentation:manage", "presentation:request"]
            }
            """;
        var createTenantResponseJson = await _rootClient.CreateTenantAsync(createTenantJson, ct);
        using var createTenantResponse = JsonDocument.Parse(createTenantResponseJson);
        var clientElement = createTenantResponse.RootElement.GetProperty("client");
        var tenantClientId = clientElement.GetProperty("clientId").GetString()!;
        var tenantClientSecret = clientElement.GetProperty("clientSecret").GetString()!;
        Console.WriteLine($"Tenant created. Gate client: {tenantClientId}");

        GateClient = new EudiploApiClient(
            _httpClientFactory.CreateClient(EudiploApiClient.HttpClientName),
            tenantClientId,
            tenantClientSecret);

        // A presentation request must be signed with the tenant's access certificate — EUDIPLO
        // 404s at offer-creation time otherwise (see samples/Eudiplo.Client.Sample.AccessControl).
        Console.WriteLine("Provisioning the gate's access key-chain...");
        await GateClient.CreateKeyChainAsync(usageType: "access", type: "standalone", description: "Gate signing key", ct: ct);

        Console.WriteLine($"Registering presentation config '{PresentationConfigId}' (birthdate, PID SD-JWT)...");
        // Verified against a real German PID (Bundesdruckerei preprod) held in a real EUDI
        // Wallet: it does not carry an `age_equal_or_over`/`age_over_18` claim at all — only
        // `birthdate`. EUDIPLO's own demo assets request the former, which works against a
        // simulator but not a real DE-PID. Requesting `birthdate` means the wallet discloses
        // the exact date (no selective age-only disclosure is possible for this credential
        // today) — the age-18 check itself happens server-side, in Program.cs's SSE handler.
        var presentationConfigJson = $$"""
            {
                "id": "{{PresentationConfigId}}",
                "description": "Access Control — verifies the holder's birthdate to confirm 18+ (the real DE-PID has no dedicated age-over-18 claim, so this discloses the exact date; age-18 is then checked server-side)",
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
                "lifeTime": 120
            }
            """;
        await GateClient.PostVerifierConfigAsync(presentationConfigJson, ct);

        Console.WriteLine("Gate ready.");
    }
}
