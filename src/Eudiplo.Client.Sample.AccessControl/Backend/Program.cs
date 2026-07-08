using System.Text.Json;
using System.Text.Json.Nodes;
using Eudiplo.Client;

// The "Access Control Backend" from EUDIPLO's own architecture diagram
// (docs/overview.excalidraw.svg): the only piece that talks to EUDIPLO, via
// Eudiplo.Client. The frontend (served from wwwroot/, built separately from ../Frontend)
// never sees EUDIPLO or its credentials — only this backend's own small REST+SSE API.
//
// Talks to a single, already-provisioned EUDIPLO tenant — this backend never creates,
// deletes, or reconfigures anything in EUDIPLO. The tenant, its access key-chain (ideally
// registrar-signed, see ../README.md), and the presentation config below must already
// exist before this starts.
const string PresentationConfigId = "access-control-age-check";

var baseUrl = Environment.GetEnvironmentVariable("EUDIPLO_BASE_URL")
    ?? throw new InvalidOperationException("Set EUDIPLO_BASE_URL (the URL of your EUDIPLO instance).");
var clientId = Environment.GetEnvironmentVariable("AUTH_CLIENT_ID")
    ?? throw new InvalidOperationException("Set AUTH_CLIENT_ID (the gate tenant's client id).");
var clientSecret = Environment.GetEnvironmentVariable("AUTH_CLIENT_SECRET")
    ?? throw new InvalidOperationException("Set AUTH_CLIENT_SECRET (the gate tenant's client secret).");

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5050");
builder.Logging.SetMinimumLevel(LogLevel.Warning); // quiet per-request noise, but keep errors visible

builder.Services.AddEudiploClient(o =>
{
    o.BaseUrl = baseUrl;
    o.ClientId = clientId;
    o.ClientSecret = clientSecret;
});

var app = builder.Build();

Console.WriteLine($"Connecting to EUDIPLO at {baseUrl} ...");
var gateClient = app.Services.GetRequiredService<EudiploApiClient>();
Console.WriteLine("Gate ready.");

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/api/gate/sessions", async (CancellationToken ct) =>
{
    var (requestUrl, sessionId) = await gateClient.CreateOfferAsync(PresentationConfigId, ct: ct);
    return Results.Ok(new { sessionId, requestUrl });
});

app.MapGet("/api/gate/sessions/{id}/events", async (string id, HttpContext ctx, CancellationToken ct) =>
{
    ctx.Response.Headers.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers["X-Accel-Buffering"] = "no"; // in case this ever sits behind a buffering reverse proxy
    await ctx.Response.Body.FlushAsync(ct);

    await foreach (var line in gateClient.SubscribeToSessionEventsAsync(id, ct))
    {
        // EUDIPLO's SSE payload is deliberately small ({id, status, updatedAt}) — on a
        // terminal status, fetch the full session once via the regular REST call so the
        // frontend gets errorReason/credentials too, then stop forwarding (the session is
        // done; no need to keep this connection or the upstream one open any longer).
        var status = JsonDocument.Parse(line).RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;
        var isTerminal = status is "completed" or "failed" or "expired";
        var payload = isTerminal ? await gateClient.GetSessionAsync(id, ct) ?? line : line;

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
app.MapGet("/api/gate/sessions/{id}", async (string id, CancellationToken ct) =>
{
    var sessionJson = await gateClient.GetSessionAsync(id, ct);
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
