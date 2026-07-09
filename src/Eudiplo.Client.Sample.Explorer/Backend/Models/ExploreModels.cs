using System.Text.Json;
using System.Text.Json.Nodes;
using Eudiplo.Client;

namespace Eudiplo.Client.Sample.Explorer.Backend.Models;

public sealed record ExploreRequest(string BaseUrl, string ClientId, string ClientSecret);

/// <summary>Result of one EUDIPLO query, isolated from the others — see <c>EudiploExplorerService</c>.</summary>
public sealed record QueryResult<T>(bool Ok, T? Data, string? Error);

public sealed record ExploreResult(
    QueryResult<string?> Version,
    QueryResult<IReadOnlyList<JsonElement>> KeyChains,
    QueryResult<IReadOnlyList<JsonElement>> Clients,
    QueryResult<IReadOnlyList<JsonElement>> VerifierConfigs,
    QueryResult<IReadOnlyList<JsonElement>> CredentialConfigs,
    QueryResult<IReadOnlyList<JsonElement>> Users,
    QueryResult<IReadOnlyList<JsonElement>> WebhookEndpoints,
    QueryResult<IReadOnlyList<EudiploTrustList>> TrustLists,
    QueryResult<IReadOnlyList<JsonElement>> StatusLists,
    QueryResult<JsonNode?> RegistrarConfig);
