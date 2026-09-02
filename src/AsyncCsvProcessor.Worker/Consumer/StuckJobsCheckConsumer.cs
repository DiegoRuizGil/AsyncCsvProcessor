using AsyncCsvProcessor.Application;
using AsyncCsvProcessor.Domain;
using AsyncCsvProcessor.Worker.Configuration;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AsyncCsvProcessor.Worker.Consumer;

public class StuckJobsCheckConsumer : IConsumer<CheckStuckJobs>
{
    private readonly IAsyncCsvProcessorDbContext _dbContext;
    private readonly ISendEndpointProvider _sendEndpointProvider;
    private readonly IOptions<StuckJobRecoveryOptions> _options;
    private readonly ILogger<StuckJobsCheckConsumer> _logger;

    public StuckJobsCheckConsumer(
        IAsyncCsvProcessorDbContext dbContext,
        ISendEndpointProvider sendEndpointProvider,
        IOptions<StuckJobRecoveryOptions> options,
        ILogger<StuckJobsCheckConsumer> logger)
    {
        _dbContext = dbContext;
        _sendEndpointProvider = sendEndpointProvider;
        _options = options;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<CheckStuckJobs> context)
    {
        var threshold = DateTime.UtcNow - TimeSpan.FromMinutes(_options.Value.ThresholdMinutes);

        var stuckJobs = await _dbContext.Jobs
            .Where(job => job.Status == JobStatus.Processing
                          && job.ProcessingStartedAt != null
                          && job.ProcessingStartedAt < threshold)
            .ToListAsync();
        
        foreach (var job in stuckJobs)
        {
            if (job.RecoveryAttempts < _options.Value.MaxAttempts)
            {
                job.RegisterRecoveryAttempt();

                var queue = $"job-submitted-{job.Priority.ToString().ToLowerInvariant()}";
                var sendEndpoint = await _sendEndpointProvider.GetSendEndpoint(new Uri($"queue:{queue}"));
                await sendEndpoint.Send(new JobSubmitted(job.Id, job.FileName, job.FilePath), context.CancellationToken);
                
                _logger.LogWarning(
                    "Job {JobId} stuck in Processing, rescheduled (attempt {Attempt}/{Max})",
                    job.Id, job.RecoveryAttempts, _options.Value.MaxAttempts);
            }
            else
            {
                job.MarkAsFailed();
                
                _logger.LogWarning(
                    "Job {JobId} has exhausted its recovery attempts ({Max}), marked as Failed",
                    job.Id, _options.Value.MaxAttempts);
            }
        }

        await _dbContext.SaveChangesAsync(context.CancellationToken);
    }
}