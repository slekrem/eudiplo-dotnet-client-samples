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
            Users: await QueryAsync(() => client.GetUsersAsync(ct)));
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
            return new QueryResult<T>(false, default, ex.Message);
        }
    }
}
