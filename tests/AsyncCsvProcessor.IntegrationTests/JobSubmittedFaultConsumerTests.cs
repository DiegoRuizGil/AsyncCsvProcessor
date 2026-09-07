using AsyncCsvProcessor.Application;
using AsyncCsvProcessor.Domain;
using AsyncCsvProcessor.IntegrationTests.Fixtures;
using AsyncCsvProcessor.Worker.Consumer;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace AsyncCsvProcessor.IntegrationTests;

[Collection("Postgres collection")]
public class JobSubmittedFaultConsumerTests : MassTransitConsumerTestBase<JobSubmittedFaultConsumer>
{
    public JobSubmittedFaultConsumerTests(PostgresContainerFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Consume_marks_job_as_failed_when_fault_is_received()
    {
        var job = new Job("products.csv", "tmp/products.csv");
        job.MarkAsProcessing();

        await using (var seedDb = Fixture.CreateDbContext())
        {
            seedDb.Jobs.Add(job);
            await seedDb.SaveChangesAsync();
        }

        var harness = await StartHarnessAsync();

        await harness.Bus.Publish<Fault<JobSubmitted>>(new
        {
            FaultId = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Message = new JobSubmitted(job.Id, job.FileName, job.FilePath)
        });

        Assert.True(await harness.Consumed.Any<Fault<JobSubmitted>>());

        await using var assertDb = Fixture.CreateDbContext();
        var persistedJob = await assertDb.Jobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(JobStatus.Failed, persistedJob.Status);
    }

    [Fact]
    public async Task Consume_discards_fault_without_error_when_job_does_not_exist()
    {
        var nonExistentJobId = Guid.NewGuid();

        var harness = await StartHarnessAsync();

        await harness.Bus.Publish<Fault<JobSubmitted>>(new
        {
            FaultId = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Message = new JobSubmitted(nonExistentJobId, "products.csv", "tmp/products.csv")
        });

        Assert.True(await harness.Consumed.Any<Fault<JobSubmitted>>());

        await using var assertDb = Fixture.CreateDbContext();
        var jobs = await assertDb.Jobs.ToListAsync();
        Assert.Empty(jobs);
    }
    
    [Fact]
    public async Task Consume_deletes_uploaded_file_when_fault_is_received()
    {
        var job = new Job("products.csv", "tmp/products.csv");
        job.MarkAsProcessing();

        await using (var seedDb = Fixture.CreateDbContext())
        {
            seedDb.Jobs.Add(job);
            await seedDb.SaveChangesAsync();
        }

        var harness = await StartHarnessAsync();

        await harness.Bus.Publish<Fault<JobSubmitted>>(new
        {
            FaultId = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Message = new JobSubmitted(job.Id, job.FileName, job.FilePath)
        });

        Assert.True(await harness.Consumed.Any<Fault<JobSubmitted>>());

        Assert.Contains(job.FilePath, FileCleaner.DeletedFilePaths);
    }
}