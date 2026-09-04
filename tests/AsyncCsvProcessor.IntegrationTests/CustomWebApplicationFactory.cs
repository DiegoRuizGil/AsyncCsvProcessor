using AsyncCsvProcessor.IntegrationTests.Fixtures;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace AsyncCsvProcessor.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgresContainerFixture _dbFixture;

    public string UploadsPath { get; }

    public CustomWebApplicationFactory(PostgresContainerFixture dbFixture)
    {
        _dbFixture = dbFixture;
        UploadsPath = Path.Combine(Path.GetTempPath(), $"uploads-{Guid.NewGuid()}");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Postgres", _dbFixture.ConnectionString);
        builder.UseSetting("Storage:UploadsPath", UploadsPath);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();

            var massTransitDescriptors = services
                .Where(d => d.ServiceType.Namespace != null && d.ServiceType.Namespace.StartsWith("MassTransit"))
                .ToList();
            foreach (var descriptor in massTransitDescriptors)
                services.Remove(descriptor);

            services.AddMassTransitTestHarness();
        });
    }

    Task IAsyncLifetime.InitializeAsync()
    {
        Directory.CreateDirectory(UploadsPath);
        return Task.CompletedTask;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (Directory.Exists(UploadsPath))
            Directory.Delete(UploadsPath, recursive: true);

        await base.DisposeAsync();
    }
}