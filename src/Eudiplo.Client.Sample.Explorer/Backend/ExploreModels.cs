using System.Text.Json;

namespace Eudiplo.Client.Sample.Explorer.Backend;

public sealed record ExploreRequest(string BaseUrl, string ClientId, string ClientSecret);

/// <summary>Result of one EUDIPLO query, isolated from the others — see <see cref="EudiploExplorerService"/>.</summary>
public sealed record QueryResult<T>(bool Ok, T? Data, string? Error);

public sealed record ExploreResult(
    QueryResult<string?> Version,
    QueryResult<IReadOnlyList<JsonElement>> KeyChains,
    QueryResult<IReadOnlyList<JsonElement>> Clients,
    QueryResult<IReadOnlyList<JsonElement>> VerifierConfigs,
    QueryResult<IReadOnlyList<JsonElement>> CredentialConfigs,
    QueryResult<IReadOnlyList<JsonElement>> Users);
