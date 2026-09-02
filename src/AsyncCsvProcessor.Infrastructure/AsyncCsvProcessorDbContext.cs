using AsyncCsvProcessor.Application;
using AsyncCsvProcessor.Domain;
using Microsoft.EntityFrameworkCore;

namespace AsyncCsvProcessor.Infrastructure;

public class AsyncCsvProcessorDbContext : DbContext, IAsyncCsvProcessorDbContext
{
    public AsyncCsvProcessorDbContext(DbContextOptions<AsyncCsvProcessorDbContext> options) 
        : base(options) { }
    
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Job>(entity =>
        {
            entity.HasKey(job => job.Id);
            entity.Property(job => job.FileName).IsRequired().HasMaxLength(260);
            entity.Property(job => job.Status).HasConversion<string>();
            entity.Property(job => job.Priority).HasConversion<string>();
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(product => product.Id);
            entity.HasIndex(product => product.Sku).IsUnique();
            entity.Property(product => product.Sku).IsRequired().HasMaxLength(100);
            entity.Property(product => product.Name).IsRequired().HasMaxLength(300);
            entity.Property(product => product.Price).HasPrecision(18, 2);
            entity.Property(product => product.Category).IsRequired().HasMaxLength(150);
        });
    }
}