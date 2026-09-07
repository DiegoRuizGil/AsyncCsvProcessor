using AsyncCsvProcessor.Application;
using MassTransit;

namespace AsyncCsvProcessor.Worker.Consumer;

public class JobSubmittedFaultConsumer : IConsumer<Fault<JobSubmitted>>
{
    private readonly IAsyncCsvProcessorDbContext _db;
    private readonly IUploadedFileCleaner _fileCleaner;
    private readonly ILogger<JobSubmittedConsumer> _logger;

    public JobSubmittedFaultConsumer(
        IAsyncCsvProcessorDbContext db,
        IUploadedFileCleaner fileCleaner,
        ILogger<JobSubmittedConsumer> logger)
    {
        _db = db;
        _fileCleaner = fileCleaner;
        _logger = logger;
    }
    
    public async Task Consume(ConsumeContext<Fault<JobSubmitted>> context)
    {
        var jobId = context.Message.Message.JobId;
        var job = await _db.Jobs.FindAsync([jobId], context.CancellationToken);

        if (job == null)
        {
            _logger.LogWarning("Job {JobId} not found while handling fault, message discarded", jobId);
            return;
        }
        
        job.MarkAsFailed();
        await _db.SaveChangesAsync(context.CancellationToken);
        _fileCleaner.TryDeleteUploadedFile(context.Message.Message.FilePath);

        var reason = context.Message.Exceptions is { Length: > 0 } exceptions
            ? exceptions[0].Message
            : "unknown error";
        
        _logger.LogError(
            "Job {JobId} marked as Failed after exhausting retries. Reason: {Reason}",
            jobId, reason);
    }
}