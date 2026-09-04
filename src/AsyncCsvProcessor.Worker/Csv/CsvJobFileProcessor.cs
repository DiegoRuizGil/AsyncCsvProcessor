using System.Globalization;
using AsyncCsvProcessor.Application;
using AsyncCsvProcessor.Domain;
using CsvHelper;
using CsvHelper.Configuration;

namespace AsyncCsvProcessor.Worker.Csv;

public class CsvJobFileProcessor : IJobFileProcessor
{
    public bool CanProcess(string filePath) =>
        Path.GetExtension(filePath).Equals(".csv", StringComparison.OrdinalIgnoreCase);
    

    public Task<JobFileProcessingResult> ProcessAsync(string filePath, CancellationToken ct)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            HeaderValidated = null,
            PrepareHeaderForMatch = args => args.Header.Trim().ToLowerInvariant()
        };

        using var reader = new StreamReader(filePath);
        using var csv = new CsvReader(reader, config);

        csv.Read();
        csv.ReadHeader();

        var totalRows = 0;
        var rowNumber = 1;
        var validRows = new List<ProductData>();
        var errors = new List<RowError>();

        while (true)
        {
            bool hasRow;
            try
            {
                hasRow = csv.Read();
            }
            catch (CsvHelperException ex)
            {
                errors.Add(new RowError(rowNumber, $"Improperly formed row: {ex.Message}"));
                totalRows++;
                rowNumber++;
                continue;
            }

            if (!hasRow) break;

            totalRows++;

            var row = TryReadRow(csv, rowNumber, errors);
            if (row is null)
            {
                rowNumber++;
                continue;
            }

            var validationResult = ProductCsvRowValidator.Validate(row);
            if (!validationResult.IsValid)
            {
                errors.Add(new RowError(rowNumber, validationResult.ErrorMessage!));
            }
            else
            {
                validRows.Add(validationResult.Data!);
            }

            rowNumber++;
        }

        return Task.FromResult(new JobFileProcessingResult(totalRows, validRows, errors));
    }

    private static ProductCsvRow? TryReadRow(CsvReader csv, int rowNumber, List<RowError> errors)
    {
        try
        {
            return csv.GetRecord<ProductCsvRow>();
        }
        catch (Exception ex)
        {
            errors.Add(new RowError(rowNumber, $"Error reading row: {ex.Message}"));
            return null;
        }
    }
}