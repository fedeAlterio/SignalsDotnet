using SignalsDotnet.AspNetCore;
using SignalsDotnet.Playground;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingletonSignalIsland<Dashboard>();
builder.Services.AddHostedService<DashboardSeeder>();

var app = builder.Build();

app.MapSignalIsland<Dashboard>("/api/dashboard/stream");
app.MapSignalsQueryUi("/signals");

app.Run();
