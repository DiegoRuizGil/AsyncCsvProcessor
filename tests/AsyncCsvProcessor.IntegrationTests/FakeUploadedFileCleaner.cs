using AsyncCsvProcessor.Application;

namespace AsyncCsvProcessor.IntegrationTests;

public class FakeUploadedFileCleaner : IUploadedFileCleaner
{
    public List<string> DeletedFilePaths { get; } = new();
    
    public void TryDeleteUploadedFile(string filePath)
    {
        DeletedFilePaths.Add(filePath);
    }
}