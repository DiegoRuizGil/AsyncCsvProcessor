using AsyncCsvProcessor.Domain;
using Microsoft.EntityFrameworkCore;

namespace AsyncCsvProcessor.Application;

public interface IAsyncCsvProcessorDbContext
{
    DbSet<Job> Jobs { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}