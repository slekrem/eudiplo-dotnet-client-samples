using System.Text.Json;
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

        await ctx.Response.WriteAsync($"data: {payload}\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);

        if (isTerminal) return;
    }
});

Console.WriteLine("\nListening on http://localhost:5050");
app.Run();
