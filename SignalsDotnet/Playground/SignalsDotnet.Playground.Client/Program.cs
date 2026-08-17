using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SignalsDotnet.Playground.Client;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddServiceDiscovery();
builder.Services.AddHttpClient<DashboardStreamReader>(client => client.BaseAddress = new Uri("http://playground"))
                .AddServiceDiscovery();

builder.Services.AddHostedService<DashboardStreamWorker>();

await builder.Build().RunAsync();
