using AsyncCsvProcessor.Application;
using AsyncCsvProcessor.Domain;
using Microsoft.EntityFrameworkCore;

namespace AsyncCsvProcessor.Infrastructure;

public class AsyncCsvProcessorDbContext : DbContext, IAsyncCsvProcessorDbContext
{
    public AsyncCsvProcessorDbContext(DbContextOptions<AsyncCsvProcessorDbContext> options) 
        : base(options) { }
    
    public DbSet<Job> Jobs => Set<Job>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Job>(entity =>
        {
            entity.HasKey(job => job.Id);
            entity.Property(job => job.FileName).IsRequired().HasMaxLength(260);
            entity.Property(job => job.Status).HasConversion<string>();
            entity.Property(job => job.Priority).HasConversion<string>();
        });
    }
}