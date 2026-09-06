using AsyncCsvProcessor.Application;
using AsyncCsvProcessor.Domain;
using AsyncCsvProcessor.IntegrationTests.Fixtures;
using AsyncCsvProcessor.Worker.Configuration;
using AsyncCsvProcessor.Worker.Consumer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AsyncCsvProcessor.IntegrationTests;

[Collection("Postgres collection")]
public class StuckJobsCheckConsumerTests : MassTransitConsumerTestBase<StuckJobsCheckConsumer>
{
    private const int ThresholdMinutes = 5;
    private const int MaxAttempts = 3;

    public StuckJobsCheckConsumerTests(PostgresContainerFixture fixture) : base(fixture) { }

    private Task<MassTransit.Testing.ITestHarness> StartHarnessWithOptionsAsync() =>
        StartHarnessAsync(services => services.AddSingleton<IOptions<StuckJobRecoveryOptions>>(
            Options.Create(new StuckJobRecoveryOptions
            {
                ThresholdMinutes = ThresholdMinutes,
                MaxAttempts = MaxAttempts
            })));

    private async Task<Job> SeedJobAsync(JobStatus status, DateTime? processingStartedAt, int recoveryAttempts)
    {
        var job = new Job("products.csv", "tmp/products.csv");

        await using var seedDb = Fixture.CreateDbContext();
        seedDb.Jobs.Add(job);
        await seedDb.SaveChangesAsync();

        await seedDb.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Jobs"
            SET "Status" = {status.ToString()},
                "ProcessingStartedAt" = {processingStartedAt},
                "RecoveryAttempts" = {recoveryAttempts}
            WHERE "Id" = {job.Id}
            """);

        return job;
    }

    [Fact]
    public async Task Consume_reschedules_stuck_job_when_below_max_attempts()
    {
        var job = await SeedJobAsync(
            JobStatus.Processing,
            processingStartedAt: DateTime.UtcNow - TimeSpan.FromMinutes(ThresholdMinutes + 5),
            recoveryAttempts: 0);

        var harness = await StartHarnessWithOptionsAsync();

        await harness.Bus.Publish(new CheckStuckJobs());

        Assert.True(await harness.Consumed.Any<CheckStuckJobs>());
        Assert.True(await harness.Sent.Any<JobSubmitted>(m => m.Context.Message.JobId == job.Id));

        await using var assertDb = Fixture.CreateDbContext();
        var persistedJob = await assertDb.Jobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(JobStatus.Pending, persistedJob.Status);
        Assert.Null(persistedJob.ProcessingStartedAt);
        Assert.Equal(1, persistedJob.RecoveryAttempts);
    }

    [Fact]
    public async Task Consume_marks_job_as_failed_when_max_attempts_exhausted()
    {
        var job = await SeedJobAsync(
            JobStatus.Processing,
            processingStartedAt: DateTime.UtcNow - TimeSpan.FromMinutes(ThresholdMinutes + 5),
            recoveryAttempts: MaxAttempts);

        var harness = await StartHarnessWithOptionsAsync();

        await harness.Bus.Publish(new CheckStuckJobs());

        Assert.True(await harness.Consumed.Any<CheckStuckJobs>());
        Assert.False(await harness.Sent.Any<JobSubmitted>(m => m.Context.Message.JobId == job.Id));

        await using var assertDb = Fixture.CreateDbContext();
        var persistedJob = await assertDb.Jobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(JobStatus.Failed, persistedJob.Status);
    }

    [Fact]
    public async Task Consume_ignores_jobs_within_the_threshold()
    {
        var job = await SeedJobAsync(
            JobStatus.Processing,
            processingStartedAt: DateTime.UtcNow - TimeSpan.FromMinutes(1),
            recoveryAttempts: 0);

        var harness = await StartHarnessWithOptionsAsync();

        await harness.Bus.Publish(new CheckStuckJobs());

        Assert.True(await harness.Consumed.Any<CheckStuckJobs>());
        Assert.False(await harness.Sent.Any<JobSubmitted>(m => m.Context.Message.JobId == job.Id));

        await using var assertDb = Fixture.CreateDbContext();
        var persistedJob = await assertDb.Jobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(JobStatus.Processing, persistedJob.Status);
        Assert.Equal(0, persistedJob.RecoveryAttempts);
    }

    [Fact]
    public async Task Consume_ignores_jobs_that_are_not_in_processing_status()
    {
        var job = await SeedJobAsync(
            JobStatus.Pending,
            processingStartedAt: DateTime.UtcNow - TimeSpan.FromMinutes(ThresholdMinutes + 5),
            recoveryAttempts: 0);

        var harness = await StartHarnessWithOptionsAsync();

        await harness.Bus.Publish(new CheckStuckJobs());

        Assert.True(await harness.Consumed.Any<CheckStuckJobs>());
        Assert.False(await harness.Sent.Any<JobSubmitted>(m => m.Context.Message.JobId == job.Id));

        await using var assertDb = Fixture.CreateDbContext();
        var persistedJob = await assertDb.Jobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(JobStatus.Pending, persistedJob.Status);
        Assert.Equal(0, persistedJob.RecoveryAttempts);
    }
}