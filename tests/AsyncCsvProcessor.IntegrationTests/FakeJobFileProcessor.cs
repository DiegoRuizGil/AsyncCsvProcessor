using AsyncCsvProcessor.Application;

namespace AsyncCsvProcessor.IntegrationTests;

public class FakeJobFileProcessor : IJobFileProcessor
{
    private readonly JobFileProcessingResult _result;
    private readonly bool _canProcess;

    public FakeJobFileProcessor(JobFileProcessingResult result, bool canProcess)
    {
        _result = result;
        _canProcess = canProcess;
    }

    public bool CanProcess(string filePath) => _canProcess;

    public Task<JobFileProcessingResult> ProcessAsync(string filePath, CancellationToken ct) =>
        Task.FromResult(_result);
}