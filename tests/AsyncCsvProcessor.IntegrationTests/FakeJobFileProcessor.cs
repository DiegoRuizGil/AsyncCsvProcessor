using AsyncCsvProcessor.Application;

namespace AsyncCsvProcessor.IntegrationTests;

public class FakeJobFileProcessor : IJobFileProcessor
{
    private readonly JobFileProcessingResult _result;

    public FakeJobFileProcessor(JobFileProcessingResult result)
    {
        _result = result;
    }

    public bool CanProcess(string filePath) => true;

    public Task<JobFileProcessingResult> ProcessAsync(string filePath, CancellationToken ct) =>
        Task.FromResult(_result);
}