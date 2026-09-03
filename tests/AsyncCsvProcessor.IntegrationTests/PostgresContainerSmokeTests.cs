using AsyncCsvProcessor.IntegrationTests.Fixtures;

namespace AsyncCsvProcessor.IntegrationTests;

[Collection("Postgres collection")]
public class PostgresContainerSmokeTests
{
    private readonly PostgresContainerFixture _fixture;

    public PostgresContainerSmokeTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Database_can_connect_and_has_migrations_applied()
    {
        await using var db = _fixture.CreateDbContext();
        
        var canConnect = await db.Database.CanConnectAsync();
        
        Assert.True(canConnect);
    }
}