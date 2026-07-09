using Eudiplo.Client.Sample.Explorer.Backend;

// Eudiplo.Client.Sample.Explorer/Backend — a small "point it at any EUDIPLO tenant and see
// what's reachable" dashboard. Unlike every other sample in this repo, credentials aren't
// fixed at startup via environment variables — the browser submits EUDIPLO base URL,
// client id, and client secret with each query. See EudiploExplorerService for why nothing
// is stored: a fresh EudiploApiClient is built per request and discarded once the response
// is sent.

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5070");
builder.Logging.SetMinimumLevel(LogLevel.Warning); // quiet per-request noise, but keep errors visible
builder.Services.AddHttpClient();
builder.Services.AddSingleton<EudiploExplorerService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapExploreEndpoints();

Console.WriteLine("Listening on http://localhost:5070");
app.Run();
