namespace AsyncCsvProcessor.IntegrationTests.Fixtures;

[CollectionDefinition("Postgres collection")]
public class PostgresCollection :
    ICollectionFixture<PostgresContainerFixture>
{ }