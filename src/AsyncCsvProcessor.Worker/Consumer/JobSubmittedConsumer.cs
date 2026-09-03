using System.Globalization;
using AsyncCsvProcessor.Application;
using AsyncCsvProcessor.Domain;
using AsyncCsvProcessor.Worker.Csv;
using CsvHelper;
using CsvHelper.Configuration;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace AsyncCsvProcessor.Worker.Consumer;

public class JobSubmittedConsumer : IConsumer<JobSubmitted>
{
    private readonly IAsyncCsvProcessorDbContext _db;
    private readonly ILogger<JobSubmittedConsumer> _logger;

    public JobSubmittedConsumer(IAsyncCsvProcessorDbContext db, ILogger<JobSubmittedConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }
    
    public async Task Consume(ConsumeContext<JobSubmitted> context)
    {
        var message = context.Message;
        var job = await _db.Jobs.FindAsync([message.JobId], context.CancellationToken);

        if (job == null)
        {
            _logger.LogWarning("Job {jobId} not found, the message is discarded", message.JobId);
            return;
        }
        
        job.MarkAsProcessing();
        await _db.SaveChangesAsync(context.CancellationToken);
        _logger.LogInformation("Job {JobId} marked as Processing", message.JobId);

        var result = await ProcessCsv(job, message.FilePath, context.CancellationToken);
        
        job.MarkAsCompleted(result.TotalRows, result.ValidRows.Count);
        await _db.SaveChangesAsync(context.CancellationToken);
        _logger.LogInformation(
            "Job {JobId} marked as {Status} ({Processed}/{Total} rows)",
            job.Id, job.Status, result.ValidRows.Count, result.TotalRows);
    }

    private async Task<CsvProcessingResult> ProcessCsv(Job job, string filePath, CancellationToken ct)
    {
        var result = await ReadAndValidateCsv(job, filePath, ct);
        await UpsertProducts(result.ValidRows, ct);
        return result;
    }

    private async Task<CsvProcessingResult> ReadAndValidateCsv(Job job, string filePath, CancellationToken ct)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            HeaderValidated = null
        };
        
        using var reader = new StreamReader(filePath);
        using var csv = new CsvReader(reader, config);

        csv.Read();
        csv.ReadHeader();

        var totalRows = 0;
        var rowNumber = 1;
        var validRows = new List<ProductRowValidationResult>();
        
        while (csv.Read())
        {
            totalRows++;

            var row = TryReadRow(csv, job, rowNumber);

            if (row is null)
            {
                rowNumber++;
                continue;
            }

            var validationResult = ProductCsvRowValidator.Validate(row);

            if (!validationResult.IsValid)
            {
                AddRowError(job, rowNumber, validationResult.ErrorMessage);
                rowNumber++;
                continue;
            }

            validRows.Add(validationResult);
            rowNumber++;
        }

        return new CsvProcessingResult(totalRows, validRows);
    }

    private async Task UpsertProducts(List<ProductRowValidationResult> validRows, CancellationToken ct)
    {
        var skus = validRows.Select(result => result.Data!.Sku).Distinct().ToList();
        var existingProducts = await _db.Products
            .Where(product => skus.Contains(product.Sku))
            .ToDictionaryAsync(product => product.Sku, ct);
        
        foreach (var result in validRows)
        {
            if (existingProducts.TryGetValue(result.Data!.Sku, out var product))
            {
                product.UpdateFrom(result.Data);
            }
            else
            {
                var newProduct = new Product(result.Data);
                _db.Products.Add(newProduct);
                existingProducts[result.Data.Sku] = newProduct;
            }
        }
    }

    private ProductCsvRow? TryReadRow(CsvReader csv, Job job, int rowNumber)
    {
        try
        {
            return csv.GetRecord<ProductCsvRow>();
        }
        catch (Exception ex)
        {
            AddRowError(job, rowNumber, $"Error reading row: {ex.Message}");
            return null;
        }
    }

    private void AddRowError(Job job, int rowNumber, string message)
    {
        _db.JobRowErrors.Add(new JobRowError(job.Id, rowNumber, message));
    }

    private sealed record CsvProcessingResult(int TotalRows, List<ProductRowValidationResult> ValidRows);
}