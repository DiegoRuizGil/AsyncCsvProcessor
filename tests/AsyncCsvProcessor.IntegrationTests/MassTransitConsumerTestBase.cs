using AsyncCsvProcessor.Application;
using AsyncCsvProcessor.Infrastructure;
using AsyncCsvProcessor.IntegrationTests.Fixtures;
using AsyncCsvProcessor.Worker.Consumer;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AsyncCsvProcessor.IntegrationTests;

public abstract class MassTransitConsumerTestBase<TConsumer> : IAsyncLifetime
    where TConsumer : class, IConsumer
{
    protected readonly PostgresContainerFixture Fixture;
    private ServiceProvider? _provider;

    protected MassTransitConsumerTestBase(PostgresContainerFixture fixture)
    {
        Fixture = fixture;
    }

    public Task InitializeAsync() => Fixture.ResetAsync();

    public async Task DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
    }
    
    protected async Task<ITestHarness> StartHarnessAsync(Action<IServiceCollection>? configureExtraServices = null)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddDbContext<AsyncCsvProcessorDbContext>(options => options.UseNpgsql(Fixture.ConnectionString))
            .AddScoped<IAsyncCsvProcessorDbContext>(sp => sp.GetRequiredService<AsyncCsvProcessorDbContext>());

        configureExtraServices?.Invoke(services);

        services.AddMassTransitTestHarness(cfg => cfg.AddConsumer<TConsumer>());

        _provider = services.BuildServiceProvider(true);

        var harness = _provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        return harness;
    }
}