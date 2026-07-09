namespace Eudiplo.Client.Sample.Explorer.Backend;

public static class ExploreEndpoints
{
    public static IEndpointRouteBuilder MapExploreEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/explore", HandleExploreAsync);
        return app;
    }

    private static async Task<IResult> HandleExploreAsync(
        ExploreRequest req,
        EudiploExplorerService explorer,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.BaseUrl) || string.IsNullOrWhiteSpace(req.ClientId) || string.IsNullOrWhiteSpace(req.ClientSecret))
            return Results.BadRequest(new { error = "baseUrl, clientId, and clientSecret are all required." });

        if (!Uri.TryCreate(req.BaseUrl, UriKind.Absolute, out var baseUri))
            return Results.BadRequest(new { error = "baseUrl must be an absolute URL." });

        var result = await explorer.ExploreAsync(baseUri, req.ClientId, req.ClientSecret, ct);
        return Results.Ok(result);
    }
}
