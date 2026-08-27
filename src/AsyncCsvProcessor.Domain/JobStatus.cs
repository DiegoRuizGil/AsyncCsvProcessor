namespace AsyncCsvProcessor.Domain;

public enum JobStatus
{
    Pending,
    Processing,
    Completed,
    CompletedWithErrors,
    Failed
}