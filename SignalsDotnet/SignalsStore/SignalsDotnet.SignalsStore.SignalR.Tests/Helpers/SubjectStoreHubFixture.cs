using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SignalsDotnet.SignalsStore.SignalR.Tests.Helpers;

sealed class SubjectStoreHubFixture : WebApplicationFactory<SubjectStoreHubFixture>
{
    public const string HubPath = "/hubs/signal-store";

    public InMemorySubjectStore Store { get; } = new();

    public string HubUrl => Server.BaseAddress + HubPath.TrimStart('/');

    protected override IHostBuilder CreateHostBuilder() =>
        Host.CreateDefaultBuilder().ConfigureWebHostDefaults(builder => builder.UseTestServer());

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(AppContext.BaseDirectory);
        builder.ConfigureServices(services => services.AddSignalR());
        builder.Configure(app =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints => endpoints.MapSubjectStoreHub(HubPath, _ => Store));
        });
    }

    public async Task<HubConnection> ConnectAsync()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(HubUrl, options => options.HttpMessageHandlerFactory = _ => Server.CreateHandler())
            .Build();

        await connection.StartAsync();
        return connection;
    }
}
