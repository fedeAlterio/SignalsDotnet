using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Refit;
using SignalsDotnet.Playground;
using SignalsDotnet.Playground.Client;
using SignalsDotnet.Query;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddServiceDiscovery();
builder.Services.AddRefitGeneratedClient<IDashboardApi>()
                .ConfigureHttpClient(client => client.BaseAddress = new Uri("http://playground"))
                .AddServiceDiscovery();

builder.Services.AddHostedService<DashboardStreamWorker>();

await builder.Build().RunAsync();