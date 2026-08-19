using SignalsDotnet.AspNetCore;
using SignalsDotnet.Query;
using SignalsDotnet.Playground;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingletonSignalIsland<Dashboard>();
builder.Services.AddHostedService<DashboardSeeder>();

var app = builder.Build();

app.MapGet("/dashboard", (SignalIsland<Dashboard> island,
            SignalComputedQueryString query,
            CancellationToken cancellationToken) =>
           TypedResults.SignalIslandComputed(island, query, cancellationToken))
   .WithSignalIslandDiscovery();

app.MapSignalsQueryUi("/signals-ui");

app.Run();
