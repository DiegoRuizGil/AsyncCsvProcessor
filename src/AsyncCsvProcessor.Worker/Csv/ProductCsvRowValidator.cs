using System.Globalization;
using AsyncCsvProcessor.Domain;

namespace AsyncCsvProcessor.Worker.Csv;

public record ProductRowValidationResult(bool IsValid, string? ErrorMessage, ProductData? Data)
{
    public static ProductRowValidationResult Invalid(string error) => new(false, error, null);
    public static ProductRowValidationResult Valid(ProductData data) => new(true, null, data);
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

        var data = new ProductData(row.Sku, row.Name, price, row.Category, stock);
        return ProductRowValidationResult.Valid(data);
    }
}