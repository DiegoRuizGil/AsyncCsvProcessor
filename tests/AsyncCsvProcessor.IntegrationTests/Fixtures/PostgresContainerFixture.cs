using AsyncCsvProcessor.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;

namespace AsyncCsvProcessor.IntegrationTests.Fixtures;

public class PostgresContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container;
    private NpgsqlConnection _respawnConnection = null!;
    private Respawner _respawner = null!;

    public string ConnectionString => _container.GetConnectionString();
    
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

        _respawnConnection = new NpgsqlConnection(ConnectionString);
        await _respawnConnection.OpenAsync();
        
        _respawner = await Respawner.CreateAsync(_respawnConnection, new RespawnerOptions
        {
            SchemasToInclude = ["public"],
            TablesToIgnore = ["__EFMigrationsHistory"],
            DbAdapter = DbAdapter.Postgres
        });
    }

    public async Task DisposeAsync()
    {
        await _respawnConnection.DisposeAsync();
        await _container.DisposeAsync();
    }

    public AsyncCsvProcessorDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AsyncCsvProcessorDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        return new AsyncCsvProcessorDbContext(options);
    }

    public Task ResetAsync() => _respawner.ResetAsync(_respawnConnection);
}