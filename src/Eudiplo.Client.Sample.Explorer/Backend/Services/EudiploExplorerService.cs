using System.Net;
using System.Text.Json.Nodes;
using Eudiplo.Client;
using Eudiplo.Client.Sample.Explorer.Backend.Models;

namespace Eudiplo.Client.Sample.Explorer.Backend.Services;

/// <summary>
/// Builds a fresh <see cref="EudiploApiClient"/> per call and queries a handful of
/// "basic info" endpoints independently. Stateless and registered as a singleton — nothing
/// about a call (base URL, credentials, results) is kept beyond the call itself.
///
/// <c>AddEudiploClient()</c> isn't used here — it configures a named <see cref="HttpClient"/>'s
/// base address once at startup from a URL that's already known, but this service doesn't
/// know the target EUDIPLO instance until <see cref="ExploreAsync"/> is called.
/// <see cref="EudiploApiClient"/>'s constructor is public precisely to support this: build
/// one per call against whatever base address and credentials were given, instead of
/// registering one fixed instance in DI at startup.
/// </summary>
public sealed class EudiploExplorerService(IHttpClientFactory httpClientFactory)
{
    public async Task<ExploreResult> ExploreAsync(Uri baseUri, string clientId, string clientSecret, CancellationToken ct)
    {
        var http = httpClientFactory.CreateClient();
        http.BaseAddress = baseUri;
        using var client = new EudiploApiClient(http, clientId, clientSecret);

        return new ExploreResult(
            Version: await QueryAsync(() => client.GetVersionAsync(ct)),
            KeyChains: await QueryAsync(() => client.GetKeyChainsAsync(ct: ct)),
            Clients: await QueryAsync(() => client.GetClientsAsync(ct)),
            VerifierConfigs: await QueryAsync(() => client.GetVerifierConfigsAsync(ct)),
            CredentialConfigs: await QueryAsync(() => client.GetCredentialConfigsAsync(ct)),
            Users: await QueryAsync(() => client.GetUsersAsync(ct)),
            WebhookEndpoints: await QueryAsync(() => client.GetWebhookEndpointsAsync(ct)),
            TrustLists: await QueryAsync(() => client.GetTrustListsAsync(ct)),
            StatusLists: await QueryAsync(() => client.GetStatusListsAsync(ct)),
            RegistrarConfig: await QueryAsync(async () =>
            {
                // Returned as a raw JSON string, not a deserialized type — parse it so the
                // frontend's generic object renderer shows labeled fields instead of one
                // long escaped-JSON string. EUDIPLO already omits the actual secret value
                // from this payload (clientSecret comes back null), so nothing sensitive
                // ends up on screen.
                var json = await client.GetRegistrarConfigAsync(ct);
                return string.IsNullOrEmpty(json) ? null : JsonNode.Parse(json);
            }));
    }

    // Runs one EUDIPLO query in isolation — a tenant client typically doesn't hold every
    // role (issuance:manage, presentation:manage, clients:manage, ...), so some of these
    // can 403 depending on what the submitted credentials are scoped to. Catching
    // per-query means ExploreAsync's result shows exactly what's reachable instead of the
    // whole call failing because of one missing permission.
    private static async Task<QueryResult<T>> QueryAsync<T>(Func<Task<T>> query)
    {
        try
        {
            return new QueryResult<T>(true, await query(), null);
        }
        catch (Exception ex)
        {
            return new QueryResult<T>(false, default, DescribeError(ex));
        }
    }

    // EudiploApiClient throws via HttpResponseMessage.EnsureSuccessStatusCode() on a
    // non-success response, whose default message ("Response status code does not
    // indicate success: 401 (Unauthorized).") is accurate but not something a developer
    // can act on at a glance. Translates the status codes actually worth distinguishing;
    // anything else (including a genuinely unexpected exception type) falls back to the
    // exception's own message rather than hiding it.
    private static string DescribeError(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.Unauthorized } =>
            "Authentication failed — check the client ID and secret.",
        HttpRequestException { StatusCode: HttpStatusCode.Forbidden } =>
            "Not authorized — this client doesn't hold the role this needs.",
        HttpRequestException { StatusCode: HttpStatusCode.NotFound } =>
            "Not found on this EUDIPLO instance.",
        HttpRequestException { StatusCode: { } status } when (int)status >= 500 =>
            $"The EUDIPLO server returned an error ({(int)status}).",
        HttpRequestException =>
            "Couldn't reach this EUDIPLO instance — check the base URL.",
        _ => ex.Message,
    };
}
