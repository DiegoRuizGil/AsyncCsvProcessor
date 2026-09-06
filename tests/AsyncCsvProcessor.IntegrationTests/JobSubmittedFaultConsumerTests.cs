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
public class JobSubmittedFaultConsumerTests : IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;
    private ServiceProvider? _provider;

    public JobSubmittedFaultConsumerTests(PostgresContainerFixture fixture)
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
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<JobSubmittedFaultConsumer>();
            })
            .BuildServiceProvider(true);

        var harness = _provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        return harness;
    }

    [Fact]
    public async Task Consume_marks_job_as_failed_when_fault_is_received()
    {
        var job = new Job("products.csv", "tmp/products.csv");
        job.MarkAsProcessing();

        await using (var seedDb = _fixture.CreateDbContext())
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

        await using var assertDb = _fixture.CreateDbContext();
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

        await using var assertDb = _fixture.CreateDbContext();
        var jobs = await assertDb.Jobs.ToListAsync();
        Assert.Empty(jobs);
    }
}