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

    [Fact]
    public async Task ProcessAsync_maps_columns_by_name_regardless_of_order_or_extra_columns()
    {
        var csvContent =
            "Category,Notes,Stock,Name,Price,Sku\n" +
            "Peripheral,internal note,10,Mechanical keyboard,29.99,SKU-1\n";

        var filePath = await WriteTempCsvAsync(csvContent);
        try
        {
            var result = await _processor.ProcessAsync(filePath, CancellationToken.None);

            Assert.Empty(result.Errors);
            var product = Assert.Single(result.ValidRows);
            Assert.Equal("SKU-1", product.Sku);
            Assert.Equal("Mechanical keyboard", product.Name);
            Assert.Equal(29.99m, product.Price);
            Assert.Equal("Peripheral", product.Category);
            Assert.Equal(10, product.Stock);
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