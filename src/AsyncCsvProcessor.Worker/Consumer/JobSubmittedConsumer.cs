using AsyncCsvProcessor.Application;
using MassTransit;

namespace AsyncCsvProcessor.Worker.Consumer;

public class JobSubmittedConsumer : IConsumer<JobSubmitted>
{
    private readonly IAsyncCsvProcessorDbContext _db;
    private readonly ILogger<JobSubmittedConsumer> _logger;

    public JobSubmittedConsumer(IAsyncCsvProcessorDbContext db, ILogger<JobSubmittedConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }
    
    public async Task Consume(ConsumeContext<JobSubmitted> context)
    {
        var message = context.Message;
        var job = await _db.Jobs.FindAsync([message.JobId], context.CancellationToken);

        if (job == null)
        {
            _logger.LogWarning("Job {jobId} not found, the message is discarded", message.JobId);
            return;
        }
        
        job.MarkAsProcessing();
        await _db.SaveChangesAsync(context.CancellationToken);
        _logger.LogInformation("Job {JobId} marked as Processing", message.JobId);
        
        // TODO - parse CSV
        // just for debugging
        var totalRows = 100;
        var processedRows = 100;
        
        job.MarkAsCompleted(totalRows, processedRows);
        await _db.SaveChangesAsync(context.CancellationToken);
        _logger.LogInformation("Job {JobId} marked as {Status}", job.Id, job.Status);
    }
}