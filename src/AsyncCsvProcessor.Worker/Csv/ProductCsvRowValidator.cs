using System.Globalization;

namespace AsyncCsvProcessor.Worker.Csv;

public record ProductRowValidationResult(
    bool IsValid,
    string? ErrorMessage,
    string? Sku,
    string? Name,
    decimal Price,
    string? Category,
    int Stock
)
{
    public static ProductRowValidationResult Invalid(string error) =>
        new(false, error, null, null, 0, null, 0);
    
    public static ProductRowValidationResult Valid(string sku, string name, decimal price, string category, int stock) =>
        new(true, null, sku, name, price, category, stock);
}

public static class ProductCsvRowValidator
{
    public static ProductRowValidationResult Validate(ProductCsvRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Sku))
            return ProductRowValidationResult.Invalid("The Sku field is required");
        if (string.IsNullOrWhiteSpace(row.Name))
            return ProductRowValidationResult.Invalid("The Name field is required");
        if (string.IsNullOrWhiteSpace(row.Category))
            return ProductRowValidationResult.Invalid("The Category field is required");
        if (!decimal.TryParse(row.Price, NumberStyles.Number, CultureInfo.InvariantCulture, out var price) || price < 0)
            return ProductRowValidationResult.Invalid($"The Price field is not a valid number or is negative: '{row.Price}'");
        if (!int.TryParse(row.Stock, out var stock) || stock < 0)
            return ProductRowValidationResult.Invalid($"The Stock field is not a valid number os is negative: '{row.Stock}'");
        
        return ProductRowValidationResult.Valid(row.Sku, row.Name, price, row.Category, stock);
    }
}