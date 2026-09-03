using AsyncCsvProcessor.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace AsyncCsvProcessor.IntegrationTests.Fixtures;

public class PostgresContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container;

    public PostgresContainerFixture()
    {
        _container = new PostgreSqlBuilder("postgres:17")
            .WithDatabase("asynccsv_test")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
    }
    
    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public AsyncCsvProcessorDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AsyncCsvProcessorDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        return new AsyncCsvProcessorDbContext(options);
    }
}