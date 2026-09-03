using AsyncCsvProcessor.IntegrationTests.Fixtures;

namespace AsyncCsvProcessor.IntegrationTests;

[CollectionDefinition("Postgres collection")]
public class PostgresCollection : ICollectionFixture<PostgresContainerFixture> { }