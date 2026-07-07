using System.Text.Json;
using System.Text.Json.Nodes;
using Eudiplo.Client;
using Eudiplo.Client.Sample.AccessControl.Backend;

// The "Access Control Backend" from EUDIPLO's own architecture diagram
// (docs/overview.excalidraw.svg): the only piece that talks to EUDIPLO, via
// Eudiplo.Client. The frontend (served from wwwroot/, built separately from ../Frontend)
// never sees EUDIPLO or its credentials — only this backend's own small REST+SSE API.

var baseUrl = Environment.GetEnvironmentVariable("EUDIPLO_BASE_URL") ?? "http://localhost:3000";
var rootClientId = Environment.GetEnvironmentVariable("AUTH_CLIENT_ID")
    ?? throw new InvalidOperationException("Set AUTH_CLIENT_ID (same value as in samples/.env).");
var rootClientSecret = Environment.GetEnvironmentVariable("AUTH_CLIENT_SECRET")
    ?? throw new InvalidOperationException("Set AUTH_CLIENT_SECRET (same value as in samples/.env).");
var tenantId = Environment.GetEnvironmentVariable("GATE_TENANT_ID") ?? "access-control-gate";

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5050");
builder.Logging.SetMinimumLevel(LogLevel.Warning); // quiet per-request noise, but keep errors visible

builder.Services.AddEudiploClient(o =>
{
    o.BaseUrl = baseUrl;
    o.ClientId = rootClientId;
    o.ClientSecret = rootClientSecret;
});
builder.Services.AddSingleton(sp => new GateService(
    sp.GetRequiredService<EudiploApiClient>(),
    sp.GetRequiredService<IHttpClientFactory>(),
    tenantId));

var app = builder.Build();

Console.WriteLine($"Connecting to EUDIPLO at {baseUrl} ...");
try
{
    await app.Services.GetRequiredService<GateService>().InitializeAsync();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"\nFailed to provision the gate: {ex.Message}");
    Console.Error.WriteLine("Is EUDIPLO running? See ../README.md (samples/docker-compose.yml).");
    Environment.Exit(1);
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/api/gate/sessions", async (GateService gate, CancellationToken ct) =>
{
    var (requestUrl, sessionId) = await gate.GateClient.CreateOfferAsync(GateService.PresentationConfigId, ct: ct);
    return Results.Ok(new { sessionId, requestUrl });
});

app.MapGet("/api/gate/sessions/{id}/events", async (string id, HttpContext ctx, GateService gate, CancellationToken ct) =>
{
    ctx.Response.Headers.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers["X-Accel-Buffering"] = "no"; // in case this ever sits behind a buffering reverse proxy
    await ctx.Response.Body.FlushAsync(ct);

    await foreach (var line in gate.GateClient.SubscribeToSessionEventsAsync(id, ct))
    {
        // EUDIPLO's SSE payload is deliberately small ({id, status, updatedAt}) — on a
        // terminal status, fetch the full session once via the regular REST call so the
        // frontend gets errorReason/credentials too, then stop forwarding (the session is
        // done; no need to keep this connection or the upstream one open any longer).
        var status = JsonDocument.Parse(line).RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;
        var isTerminal = status is "completed" or "failed" or "expired";
        var payload = isTerminal ? await gate.GateClient.GetSessionAsync(id, ct) ?? line : line;

        if (status == "completed")
            payload = EnforceAgeGate(payload!);

        await ctx.Response.WriteAsync($"data: {payload}\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);

        if (isTerminal) return;
    }
});

// Polling fallback alongside the SSE endpoint above — mobile browsers can silently kill a
// backgrounded tab's long-lived connections while the user is away in their wallet app,
// with no error ever surfacing to the page's JS to react to. The frontend polls this
// periodically in addition to holding an SSE subscription, so a status change still gets
// picked up even if that subscription died unnoticed. One-shot, same enrichment as above.
app.MapGet("/api/gate/sessions/{id}", async (string id, GateService gate, CancellationToken ct) =>
{
    var sessionJson = await gate.GateClient.GetSessionAsync(id, ct);
    if (sessionJson is null) return Results.NotFound();

    var status = JsonDocument.Parse(sessionJson).RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;
    if (status == "completed")
        sessionJson = EnforceAgeGate(sessionJson);

    return Results.Text(sessionJson, "application/json");
});

Console.WriteLine("\nListening on http://localhost:5050");
app.Run();

// The real German PID has no dedicated age-over-18 claim (verified against a real EUDI
// Wallet) — only `birthdate`, disclosed in full. EUDIPLO's "completed" only means "a
// presentation was successfully verified", not "the holder is 18+" — that check is this
// gate's own business logic, done here server-side on the disclosed date. Rewrites the
// session JSON to `failed` (with an explanatory errorReason) when the holder is under 18,
// so the frontend's existing granted/denied rendering needs no changes.
static string EnforceAgeGate(string sessionJson)
{
    var session = JsonNode.Parse(sessionJson)!.AsObject();
    var birthdateText = session["credentials"]?[0]?["values"]?[0]?["birthdate"]?.GetValue<string>();
    if (birthdateText is null || !DateOnly.TryParse(birthdateText, out var birthdate))
        return sessionJson;

    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var age = today.Year - birthdate.Year;
    if (birthdate > today.AddYears(-age)) age--;

    if (age >= 18) return sessionJson;

    session["status"] = "failed";
    session["errorReason"] = $"Holder is under 18 (born {birthdateText}).";
    return session.ToJsonString();
}
