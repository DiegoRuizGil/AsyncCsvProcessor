using System.Text;
using AsyncCsvProcessor.Worker.Csv;

namespace AsyncCsvProcessor.IntegrationTests;

public class CsvJobFileProcessorTests
{
    private readonly CsvJobFileProcessor _processor = new();

    [Fact]
    public async Task ProcessAsync_separates_valid_rows_from_errors_in_a_mixed_csv_file()
    {
        var csvContent =
            "Sku,Name,Price,Category,Stock\n" +
            "SKU-1,Mechanical keyboard,29.99,Peripheral,10\n" +
            "SKU-2,Wireless mouse,abc,Peripheral,25\n" +
            "SKU-3,USB-C Hub,19.99,Peripheral,-5\n" +
            ",Missing sku,9.99,Peripheral,3\n";
        
        var filePath = await WriteTempCsvAsync(csvContent);
        try
        {
            var result = await _processor.ProcessAsync(filePath, CancellationToken.None);

            Assert.Equal(4, result.TotalRows);
            Assert.Single(result.ValidRows);
            Assert.Equal("SKU-1", result.ValidRows[0].Sku);

            Assert.Equal(3, result.Errors.Count);
            Assert.Contains(result.Errors, e => e.RowNumber == 2 && e.Message.Contains("Price"));
            Assert.Contains(result.Errors, e => e.RowNumber == 3 && e.Message.Contains("Stock"));
            Assert.Contains(result.Errors, e => e.RowNumber == 4 && e.Message.Contains("Sku"));
        }
        finally
        {
            File.Delete(filePath);
        }
    }
    
    private static async Task<string> WriteTempCsvAsync(string content, Encoding? encoding = null)
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.csv");
        await File.WriteAllTextAsync(filePath, content, encoding ?? Encoding.UTF8);
        return filePath;
    }
}