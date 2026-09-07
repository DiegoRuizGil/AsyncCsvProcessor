namespace AsyncCsvProcessor.Application;

public interface IUploadedFileCleaner
{
    void TryDeleteUploadedFile(string filePath);
}