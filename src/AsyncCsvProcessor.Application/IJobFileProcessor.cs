using AsyncCsvProcessor.Domain;

namespace AsyncCsvProcessor.Application;

public interface IJobFileProcessor
{
    bool CanProcess(string filePath);
    Task<JobFileProcessingResult> ProcessAsync(string filePath, CancellationToken ct);
}

public record JobFileProcessingResult(int TotalRows, IReadOnlyList<ProductData> ValidRows, IReadOnlyList<RowError> Errors);
public record RowError(int RowNumber, string Message);