namespace AsyncCsvProcessor.Domain;

public class Job
{
    public Guid Id { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public JobStatus Status { get; private set; }
    public int TotalRows { get; private set; }
    public int ProcessedRows { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    
    private Job() { } // EF Core necesita un constructor vacío

    public Job(string fileName)
    {
        Id = Guid.NewGuid();
        FileName = fileName;
        Status = JobStatus.Pending;
        CreatedAt = DateTime.UtcNow;
    }

    public void MarkAsProcessing()
    {
        Status = JobStatus.Processing;
        UpdatedAt = DateTime.UtcNow;
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
}