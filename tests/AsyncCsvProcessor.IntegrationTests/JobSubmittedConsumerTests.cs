using AsyncCsvProcessor.Application;
using AsyncCsvProcessor.Domain;
using AsyncCsvProcessor.IntegrationTests.Fixtures;
using AsyncCsvProcessor.Worker.Consumer;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AsyncCsvProcessor.IntegrationTests;

[Collection("Postgres collection")]
public class JobSubmittedConsumerTests : MassTransitConsumerTestBase<JobSubmittedConsumer>
{
    public JobSubmittedConsumerTests(PostgresContainerFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Consume_marks_job_completed_and_upserts_products_when_all_rows_are_valid()
    {
        var job = new Job("products.csv", "tmp/products.csv");
        await using (var seedDb = Fixture.CreateDbContext())
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
            new JobFileProcessingResult(TotalRows: 2, ValidRows: validRows, Errors: []), true);

        var harness = await StartHarnessAsync(services => services.AddSingleton<IJobFileProcessor>(fakeProcessor));

        await harness.Bus.Publish(new JobSubmitted(job.Id, job.FileName, job.FilePath));

        Assert.True(await harness.Consumed.Any<JobSubmitted>());

        await using var assertDb = Fixture.CreateDbContext();

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
        await using (var seedDb = Fixture.CreateDbContext())
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
            new JobFileProcessingResult(TotalRows: 3, ValidRows: validRows, Errors: errors), true);

        var harness = await StartHarnessAsync(services => services.AddSingleton<IJobFileProcessor>(fakeProcessor));

        await harness.Bus.Publish(new JobSubmitted(job.Id, job.FileName, job.FilePath));

        Assert.True(await harness.Consumed.Any<JobSubmitted>());

        await using var assertDb = Fixture.CreateDbContext();

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

    [Fact]
    public async Task Consume_updates_existing_product_by_sku_instead_of_creating_a_duplicate()
    {
        var job = new Job("products.csv", "tmp/products.csv");
        var existingProduct = new Product("SKU-1", "Mechanical keyboard", 29.99m, "Peripheral", 10);

        await using (var seedDb = Fixture.CreateDbContext())
        {
            seedDb.Jobs.Add(job);
            seedDb.Products.Add(existingProduct);
            await seedDb.SaveChangesAsync();
        }

        var originalId = existingProduct.Id;
        var originalCreatedAt = existingProduct.CreatedAt;

        var validRows = new List<ProductData>
        {
            new("SKU-1", "Mechanical keyboard v2", 34.99m, "Peripheral", 5)
        };
        var fakeProcessor = new FakeJobFileProcessor(
            new JobFileProcessingResult(TotalRows: 1, ValidRows: validRows, Errors: []), true);

        var harness = await StartHarnessAsync(services => services.AddSingleton<IJobFileProcessor>(fakeProcessor));

        await harness.Bus.Publish(new JobSubmitted(job.Id, job.FileName, job.FilePath));

        Assert.True(await harness.Consumed.Any<JobSubmitted>());

        await using var assertDb = Fixture.CreateDbContext();

        var products = await assertDb.Products.Where(p => p.Sku == "SKU-1").ToListAsync();
        var updatedProduct = Assert.Single(products);

        Assert.Equal(originalId, updatedProduct.Id);
        Assert.Equal(originalCreatedAt, updatedProduct.CreatedAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal("Mechanical keyboard v2", updatedProduct.Name);
        Assert.Equal(34.99m, updatedProduct.Price);
        Assert.Equal(5, updatedProduct.Stock);
        Assert.NotNull(updatedProduct.UpdateAt);
    }

    [Fact]
    public async Task Consume_discards_message_without_error_when_job_does_not_exist()
    {
        var nonExistentJobId = Guid.NewGuid();
        var fakeProcessor = new FakeJobFileProcessor(
            new JobFileProcessingResult(TotalRows: 0, ValidRows: [], Errors: []), true);

        var harness = await StartHarnessAsync(services => services.AddSingleton<IJobFileProcessor>(fakeProcessor));

        await harness.Bus.Publish(new JobSubmitted(nonExistentJobId, "products.csv", "tmp/products.csv"));

        Assert.True(await harness.Consumed.Any<JobSubmitted>());
        Assert.False(await harness.Published.Any<Fault<JobSubmitted>>());

        await using var assertDb = Fixture.CreateDbContext();
        var products = await assertDb.Products.ToListAsync();
        Assert.Empty(products);
    }

    [Fact]
    public async Task Consume_marks_job_as_failed_when_no_processor_can_handle_the_file()
    {
        var job = new Job("products.csv", "tmp/products.csv");
        await using (var seedDb = Fixture.CreateDbContext())
        {
            seedDb.Jobs.Add(job);
            await seedDb.SaveChangesAsync();
        }

        var fakeProcessor = new FakeJobFileProcessor(
            new JobFileProcessingResult(TotalRows: 0, ValidRows: [], Errors: []), false);

        var harness = await StartHarnessAsync(services => services.AddSingleton<IJobFileProcessor>(fakeProcessor));

        await harness.Bus.Publish(new JobSubmitted(job.Id, job.FileName, job.FilePath));

        Assert.True(await harness.Consumed.Any<JobSubmitted>());

        await using var assertDb = Fixture.CreateDbContext();
        var persistedJob = await assertDb.Jobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(JobStatus.Failed, persistedJob.Status);
    }
    
    [Fact]
    public async Task Consume_deletes_uploaded_file_when_job_completes_successfully()
    {
        var job = new Job("products.csv", "tmp/products.csv");
        await using (var seedDb = Fixture.CreateDbContext())
        {
            seedDb.Jobs.Add(job);
            await seedDb.SaveChangesAsync();
        }

        var fakeProcessor = new FakeJobFileProcessor(
            new JobFileProcessingResult(TotalRows: 0, ValidRows: [], Errors: []), true);

        var harness = await StartHarnessAsync(services => services.AddSingleton<IJobFileProcessor>(fakeProcessor));

        await harness.Bus.Publish(new JobSubmitted(job.Id, job.FileName, job.FilePath));

        Assert.True(await harness.Consumed.Any<JobSubmitted>());

        Assert.Contains(job.FilePath, FileCleaner.DeletedFilePaths);
    }

    [Fact]
    public async Task Consume_deletes_uploaded_file_when_no_processor_can_handle_the_file()
    {
        var job = new Job("products.csv", "tmp/products.csv");
        await using (var seedDb = Fixture.CreateDbContext())
        {
            seedDb.Jobs.Add(job);
            await seedDb.SaveChangesAsync();
        }

        var fakeProcessor = new FakeJobFileProcessor(
            new JobFileProcessingResult(TotalRows: 0, ValidRows: [], Errors: []), false);

        var harness = await StartHarnessAsync(services => services.AddSingleton<IJobFileProcessor>(fakeProcessor));

        await harness.Bus.Publish(new JobSubmitted(job.Id, job.FileName, job.FilePath));

        Assert.True(await harness.Consumed.Any<JobSubmitted>());

        Assert.Contains(job.FilePath, FileCleaner.DeletedFilePaths);
    }
}