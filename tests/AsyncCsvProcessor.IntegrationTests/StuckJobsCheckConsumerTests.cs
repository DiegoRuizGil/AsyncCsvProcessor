using AsyncCsvProcessor.Application;
using AsyncCsvProcessor.Domain;
using AsyncCsvProcessor.Infrastructure;
using AsyncCsvProcessor.IntegrationTests.Fixtures;
using AsyncCsvProcessor.Worker.Configuration;
using AsyncCsvProcessor.Worker.Consumer;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AsyncCsvProcessor.IntegrationTests;

[Collection("Postgres collection")]
public class StuckJobsCheckConsumerTests : IAsyncLifetime
{
    private const int ThresholdMinutes = 5;
    private const int MaxAttempts = 3;

    private readonly PostgresContainerFixture _fixture;
    private ServiceProvider? _provider;

    public StuckJobsCheckConsumerTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetAsync();

    public async Task DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
    }

    private async Task<ITestHarness> StartHarnessAsync()
    {
        _provider = new ServiceCollection()
            .AddLogging()
            .AddDbContext<AsyncCsvProcessorDbContext>(options => options.UseNpgsql(_fixture.ConnectionString))
            .AddScoped<IAsyncCsvProcessorDbContext>(sp => sp.GetRequiredService<AsyncCsvProcessorDbContext>())
            .AddSingleton<IOptions<StuckJobRecoveryOptions>>(Options.Create(new StuckJobRecoveryOptions
            {
                ThresholdMinutes = ThresholdMinutes,
                MaxAttempts = MaxAttempts
            }))
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<StuckJobsCheckConsumer>();
            })
            .BuildServiceProvider(true);

        var harness = _provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        return harness;
    }

    private async Task<Job> SeedJobAsync(JobStatus status, DateTime? processingStartedAt, int recoveryAttempts)
    {
        var job = new Job("products.csv", "tmp/products.csv");

        await using var seedDb = _fixture.CreateDbContext();
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

        var harness = await StartHarnessAsync();

        await harness.Bus.Publish(new CheckStuckJobs());

        Assert.True(await harness.Consumed.Any<CheckStuckJobs>());
        Assert.True(await harness.Sent.Any<JobSubmitted>(m => m.Context.Message.JobId == job.Id));

        await using var assertDb = _fixture.CreateDbContext();
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

        var harness = await StartHarnessAsync();

        await harness.Bus.Publish(new CheckStuckJobs());

        Assert.True(await harness.Consumed.Any<CheckStuckJobs>());
        Assert.False(await harness.Sent.Any<JobSubmitted>(m => m.Context.Message.JobId == job.Id));

        await using var assertDb = _fixture.CreateDbContext();
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

        var harness = await StartHarnessAsync();

        await harness.Bus.Publish(new CheckStuckJobs());

        Assert.True(await harness.Consumed.Any<CheckStuckJobs>());
        Assert.False(await harness.Sent.Any<JobSubmitted>(m => m.Context.Message.JobId == job.Id));

        await using var assertDb = _fixture.CreateDbContext();
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

        var harness = await StartHarnessAsync();

        await harness.Bus.Publish(new CheckStuckJobs());

        Assert.True(await harness.Consumed.Any<CheckStuckJobs>());
        Assert.False(await harness.Sent.Any<JobSubmitted>(m => m.Context.Message.JobId == job.Id));

        await using var assertDb = _fixture.CreateDbContext();
        var persistedJob = await assertDb.Jobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(JobStatus.Pending, persistedJob.Status);
        Assert.Equal(0, persistedJob.RecoveryAttempts);
    }
}