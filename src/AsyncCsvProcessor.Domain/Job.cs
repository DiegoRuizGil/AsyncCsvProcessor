namespace AsyncCsvProcessor.Domain;

public class Job
{
    public Guid Id { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string FilePath { get; private set; } = string.Empty;
    public JobStatus Status { get; private set; }
    public JobPriority Priority { get; private set; }
    public int TotalRows { get; private set; }
    public int ProcessedRows { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public DateTime? ProcessingStartedAt { get; private set; }
    public int RecoveryAttempts { get; private set; }
    
    private Job() { } // EF Core necesita un constructor vacío

    public Job(string fileName, string filePath, JobPriority priority = JobPriority.Normal)
    {
        Id = Guid.NewGuid();
        FileName = fileName;
        FilePath = filePath;
        Status = JobStatus.Pending;
        Priority = priority;
        CreatedAt = DateTime.UtcNow;
    }

    public void MarkAsProcessing()
    {
        Status = JobStatus.Processing;
        UpdatedAt = DateTime.UtcNow;
        ProcessingStartedAt = DateTime.UtcNow;
    }

    public void MarkAsCompleted(int totalRows, int processedRows)
    {
        TotalRows = totalRows;
        ProcessedRows = processedRows;
        Status = processedRows == totalRows ? JobStatus.Completed : JobStatus.CompletedWithErrors;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkAsFailed()
    {
        Status = JobStatus.Failed;
        UpdatedAt = DateTime.UtcNow;
    }

    public void RegisterRecoveryAttempt()
    {
        RecoveryAttempts++;
        Status = JobStatus.Pending;
        ProcessingStartedAt = null;
        UpdatedAt = DateTime.UtcNow;
    }
}