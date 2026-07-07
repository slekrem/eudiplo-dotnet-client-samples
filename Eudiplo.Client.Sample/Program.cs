using Eudiplo.Client;
using Microsoft.Extensions.DependencyInjection;

// Runs against a REAL EUDIPLO instance (see docker-compose.yml in this folder) — not a
// mock. Demonstrates the multi-tenant flow this client is built around: authenticate as
// the tenant-less root client, create an isolated tenant, then use that tenant's
// auto-generated admin client for a real business operation (creating a key-chain).

var baseUrl = Environment.GetEnvironmentVariable("EUDIPLO_BASE_URL") ?? "http://localhost:3000";
var rootClientId = Environment.GetEnvironmentVariable("AUTH_CLIENT_ID")
    ?? throw new InvalidOperationException("Set AUTH_CLIENT_ID (same value as in your EUDIPLO .env).");
var rootClientSecret = Environment.GetEnvironmentVariable("AUTH_CLIENT_SECRET")
    ?? throw new InvalidOperationException("Set AUTH_CLIENT_SECRET (same value as in your EUDIPLO .env).");

var services = new ServiceCollection();
services.AddEudiploClient(o =>
{
    o.BaseUrl = baseUrl;
    o.ClientId = rootClientId;
    o.ClientSecret = rootClientSecret;
});
using var provider = services.BuildServiceProvider();

// The DI-registered EudiploApiClient is authenticated as the root client — good for
// tenant management (Role.Tenants), not for tenant-scoped operations like key-chains.
var rootClient = provider.GetRequiredService<EudiploApiClient>();

Console.WriteLine($"Connecting to EUDIPLO at {baseUrl} ...");

var existingTenants = await rootClient.GetTenantsAsync();
Console.WriteLine($"Found {existingTenants.Count} existing tenant(s).");

var tenantId = $"sample-{Guid.NewGuid():N}"[..20];
Console.WriteLine($"\nCreating tenant '{tenantId}' with an issuance-scoped admin client...");

// roles here become the roles of the auto-generated "{tenantId}-admin" client (EUDIPLO
// always adds clients:manage to it automatically) — issuance:manage is enough for the
// key-chain call below (key-chain requires issuance:manage OR presentation:manage).
var createTenantJson = $$"""
    {
        "id": "{{tenantId}}",
        "name": "Eudiplo.Client sample tenant",
        "roles": ["issuance:manage"]
    }
    """;
var createTenantResponseJson = await rootClient.CreateTenantAsync(createTenantJson);
using var createTenantResponse = System.Text.Json.JsonDocument.Parse(createTenantResponseJson);
var clientElement = createTenantResponse.RootElement.GetProperty("client");
var tenantClientId = clientElement.GetProperty("clientId").GetString()!;
var tenantClientSecret = clientElement.GetProperty("clientSecret").GetString()!;
Console.WriteLine($"Tenant created. Admin client: {tenantClientId}");

try
{
    // A second EudiploApiClient, scoped to the new tenant. EudiploApiClient is a plain
    // class with a public constructor precisely so you can build one per tenant like this
    // — the DI-registered instance above is only ever the root/single-tenant one.
    var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
    using var tenantClient = new EudiploApiClient(
        httpClientFactory.CreateClient(EudiploApiClient.HttpClientName),
        tenantClientId,
        tenantClientSecret);

    Console.WriteLine("\nCreating a key-chain as the new tenant...");
    var keyChainJson = await tenantClient.CreateKeyChainAsync(
        usageType: "access",
        type: "standalone",
        description: "Eudiplo.Client sample key");
    using var keyChainDoc = System.Text.Json.JsonDocument.Parse(keyChainJson);
    var keyChainId = keyChainDoc.RootElement.GetProperty("id").GetString();
    Console.WriteLine($"Key-chain created: {keyChainId}");

    var keyChains = await tenantClient.GetKeyChainsAsync();
    Console.WriteLine($"Tenant now has {keyChains.Count} key-chain(s).");
}
finally
{
    // Clean up so re-running this sample doesn't accumulate tenants.
    Console.WriteLine($"\nDeleting tenant '{tenantId}'...");
    await rootClient.DeleteTenantAsync(tenantId);
    Console.WriteLine("Done.");
}
