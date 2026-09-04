using AsyncCsvProcessor.Application;
using AsyncCsvProcessor.Domain;
using AsyncCsvProcessor.Infrastructure;
using AsyncCsvProcessor.IntegrationTests.Fixtures;
using AsyncCsvProcessor.Worker.Consumer;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AsyncCsvProcessor.IntegrationTests;

[Collection("Postgres collection")]
public class JobSubmittedConsumerTests : IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;

    public JobSubmittedConsumerTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Consume_marks_job_completed_and_upserts_products_when_all_rows_are_valid()
    {
        var job = new Job("productos.csv", "tmp/productos.csv");
        await using (var seedDb = _fixture.CreateDbContext())
        {
            seedDb.Jobs.Add(job);
            await seedDb.SaveChangesAsync();
        }

        var validRows = new List<ProductData>
        {
            new("SKU-1", "Teclado mecanico", 29.99m, "Perifericos", 10),
            new("SKU-2", "Raton inalambrico", 15.50m, "Perifericos", 25)
        };
        var fakeProcessor = new FakeJobFileProcessor(
            new JobFileProcessingResult(TotalRows: 2, ValidRows: validRows, Errors: []));

        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddDbContext<AsyncCsvProcessorDbContext>(options => options.UseNpgsql(_fixture.ConnectionString))
            .AddScoped<IAsyncCsvProcessorDbContext>(sp => sp.GetRequiredService<AsyncCsvProcessorDbContext>())
            .AddSingleton<IJobFileProcessor>(fakeProcessor)
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<JobSubmittedConsumer>();
            })
            .BuildServiceProvider(true);
        
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new JobSubmitted(job.Id, job.FileName, job.FilePath));
        
        Assert.True(await harness.Consumed.Any<JobSubmitted>());
        
        await using var assertDb = _fixture.CreateDbContext();
        
        var persistedJob = await assertDb.Jobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(JobStatus.Completed, persistedJob.Status);
        Assert.Equal(2, persistedJob.TotalRows);
        Assert.Equal(2, persistedJob.ProcessedRows);

        var products = await assertDb.Products.ToListAsync();
        Assert.Equal(2, products.Count);
        Assert.Contains(products, p => p.Sku == "SKU-1");
        Assert.Contains(products, p => p.Sku == "SKU-2");
    }
}