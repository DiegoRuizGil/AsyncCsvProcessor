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
        var job = new Job("products.csv", "tmp/products.csv");
        await using (var seedDb = _fixture.CreateDbContext())
        {
            seedDb.Jobs.Add(job);
            await seedDb.SaveChangesAsync();
        }

        var validRows = new List<ProductData>
        {
            new("SKU-1", "Mechanical keyboard", 29.99m, "Peripheral", 10),
            new("SKU-2", "Wireless mouse", 15.50m, "Peripheral", 25)
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
    
    [Fact]
    public async Task Consume_marks_job_completed_with_errors_and_persists_row_errors_when_some_rows_fail()
    {
        var job = new Job("products.csv", "tmp/products.csv");
        await using (var seedDb = _fixture.CreateDbContext())
        {
            seedDb.Jobs.Add(job);
            await seedDb.SaveChangesAsync();
        }

        var validRows = new List<ProductData>
        {
            new("SKU-1", "Mechanical keyboard", 29.99m, "Peripheral", 10),
            new("SKU-2", "Wireless mouse", 15.50m, "Peripheral", 25)
        };
        var errors = new List<RowError>
        {
            new(RowNumber: 3, Message: "The price 'abc' is not a valid number")
        };
        var fakeProcessor = new FakeJobFileProcessor(
            new JobFileProcessingResult(TotalRows: 3, ValidRows: validRows, Errors: errors));

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
        Assert.Equal(JobStatus.CompletedWithErrors, persistedJob.Status);
        Assert.Equal(3, persistedJob.TotalRows);
        Assert.Equal(2, persistedJob.ProcessedRows);

        var products = await assertDb.Products.ToListAsync();
        Assert.Equal(2, products.Count);

        var rowErrors = await assertDb.JobRowErrors.Where(e => e.JobId == job.Id).ToListAsync();
        var rowError = Assert.Single(rowErrors);
        Assert.Equal(3, rowError.RowNumber);
        Assert.Equal("The price 'abc' is not a valid number", rowError.Message);
    }
}