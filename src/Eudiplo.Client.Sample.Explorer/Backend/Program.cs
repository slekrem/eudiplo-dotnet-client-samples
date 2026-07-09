using Eudiplo.Client;

// Eudiplo.Client.Sample.Explorer/Backend — a small "point it at any EUDIPLO tenant and see
// what's reachable" dashboard. Unlike every other sample in this repo, credentials aren't
// fixed at startup via environment variables — the browser submits EUDIPLO base URL,
// client id, and client secret with each query. This backend never stores them anywhere:
// a fresh EudiploApiClient is built per request and discarded once the response is sent.

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5070");
builder.Logging.SetMinimumLevel(LogLevel.Warning); // quiet per-request noise, but keep errors visible
builder.Services.AddHttpClient();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/api/explore", async (ExploreRequest req, IHttpClientFactory httpClientFactory, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.BaseUrl) || string.IsNullOrWhiteSpace(req.ClientId) || string.IsNullOrWhiteSpace(req.ClientSecret))
        return Results.BadRequest(new { error = "baseUrl, clientId, and clientSecret are all required." });

    if (!Uri.TryCreate(req.BaseUrl, UriKind.Absolute, out var baseUri))
        return Results.BadRequest(new { error = "baseUrl must be an absolute URL." });

    // AddEudiploClient() isn't used here — it configures a named HttpClient's BaseAddress
    // once at startup from a fixed, known URL, but this backend doesn't know the target
    // EUDIPLO instance until a request arrives. EudiploApiClient's constructor is public
    // precisely to support this: build one per request against whatever base address and
    // credentials the caller submitted.
    var http = httpClientFactory.CreateClient();
    http.BaseAddress = baseUri;
    using var client = new EudiploApiClient(http, req.ClientId, req.ClientSecret);

    var result = new
    {
        version = await QueryAsync(() => client.GetVersionAsync(ct)),
        keyChains = await QueryAsync(() => client.GetKeyChainsAsync(ct: ct)),
        clients = await QueryAsync(() => client.GetClientsAsync(ct)),
        verifierConfigs = await QueryAsync(() => client.GetVerifierConfigsAsync(ct)),
        credentialConfigs = await QueryAsync(() => client.GetCredentialConfigsAsync(ct)),
        users = await QueryAsync(() => client.GetUsersAsync(ct)),
    };

    return Results.Ok(result);
});

Console.WriteLine("Listening on http://localhost:5070");
app.Run();

// Runs one EUDIPLO query in isolation — a tenant client typically doesn't hold every role
// (issuance:manage, presentation:manage, clients:manage, ...), so some of these can 403
// depending on what the submitted credentials are scoped to. Catching per-query means the
// dashboard shows exactly what's reachable instead of the whole response failing because
// of one missing permission.
static async Task<QueryResult<T>> QueryAsync<T>(Func<Task<T>> query)
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

sealed record ExploreRequest(string BaseUrl, string ClientId, string ClientSecret);
sealed record QueryResult<T>(bool Ok, T? Data, string? Error);
