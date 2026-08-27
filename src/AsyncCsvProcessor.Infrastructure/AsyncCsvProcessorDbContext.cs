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
            entity.HasKey(j => j.Id);
            entity.Property(j => j.FileName).IsRequired().HasMaxLength(260);
            entity.Property(j => j.Status).HasConversion<string>();
        });
    }
}