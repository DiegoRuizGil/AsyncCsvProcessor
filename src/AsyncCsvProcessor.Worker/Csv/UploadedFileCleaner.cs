using AsyncCsvProcessor.Application;

namespace AsyncCsvProcessor.Worker.Csv;

public class UploadedFileCleaner : IUploadedFileCleaner
{
    private readonly ILogger<UploadedFileCleaner> _logger;

    public UploadedFileCleaner(ILogger<UploadedFileCleaner> logger)
    {
        _logger = logger;
    }

    public void TryDeleteUploadedFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogInformation("Deleted uploaded file {FilePath}", filePath);
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not delete uploaded file {FilePath}", filePath);
        }
    }
}