namespace AsyncCsvProcessor.Application;

public record JobSubmitted(Guid JobId, string FileName, string FilePath);