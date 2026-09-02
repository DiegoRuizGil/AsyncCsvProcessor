namespace AsyncCsvProcessor.Domain;

public class JobRowError
{
    public Guid Id { get; private set; }
    public Guid JobId { get; private set; }
    public int RowNumber { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }
    
    private JobRowError() { }

    public JobRowError(Guid jobId, int rowNumber, string message)
    {
        Id = Guid.NewGuid();
        JobId = jobId;
        RowNumber = rowNumber;
        Message = message;
        CreatedAt = DateTime.UtcNow;
    }
}