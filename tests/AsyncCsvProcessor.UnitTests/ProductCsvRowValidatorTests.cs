using AsyncCsvProcessor.Worker.Csv;

namespace AsyncCsvProcessor.UnitTests;

public class ProductCsvRowValidatorTests
{
    private static ProductCsvRow ValidRow() => new()
    {
        Sku = "SKU-1",
        Name = "Mechanical keyboard",
        Price = "29.99",
        Category = "Peripheral",
        Stock = "10"
    };

    [Fact]
    public void Validate_returns_valid_result_with_correct_data_for_a_well_formed_row()
    {
        var result = ProductCsvRowValidator.Validate(ValidRow());

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
        Assert.NotNull(result.Data);
        Assert.Equal("SKU-1", result.Data!.Sku);
        Assert.Equal("Mechanical keyboard", result.Data.Name);
        Assert.Equal(29.99m, result.Data.Price);
        Assert.Equal("Peripheral", result.Data.Category);
        Assert.Equal(10, result.Data.Stock);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_returns_invalid_when_sku_is_empty_or_whitespace(string sku)
    {
        var row = ValidRow();
        row.Sku = sku;

        var result = ProductCsvRowValidator.Validate(row);

        Assert.False(result.IsValid);
        Assert.Contains("Sku", result.ErrorMessage);
        Assert.Null(result.Data);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_returns_invalid_when_name_is_empty_or_whitespace(string name)
    {
        var row = ValidRow();
        row.Name = name;

        var result = ProductCsvRowValidator.Validate(row);

        Assert.False(result.IsValid);
        Assert.Contains("Name", result.ErrorMessage);
        Assert.Null(result.Data);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_returns_invalid_when_category_is_empty_or_whitespace(string category)
    {
        var row = ValidRow();
        row.Category = category;

        var result = ProductCsvRowValidator.Validate(row);

        Assert.False(result.IsValid);
        Assert.Contains("Category", result.ErrorMessage);
        Assert.Null(result.Data);
    }

    [Fact]
    public void Validate_returns_invalid_when_price_is_not_a_valid_number()
    {
        var row = ValidRow();
        row.Price = "abc";

        var result = ProductCsvRowValidator.Validate(row);

        Assert.False(result.IsValid);
        Assert.Contains("Price", result.ErrorMessage);
        Assert.Null(result.Data);
    }

    [Fact]
    public void Validate_returns_invalid_when_price_is_negative()
    {
        var row = ValidRow();
        row.Price = "-5";

        var result = ProductCsvRowValidator.Validate(row);

        Assert.False(result.IsValid);
        Assert.Null(result.Data);
    }

    [Fact]
    public void Validate_returns_valid_when_price_is_exactly_zero()
    {
        var row = ValidRow();
        row.Price = "0";

        var result = ProductCsvRowValidator.Validate(row);

        Assert.True(result.IsValid);
        Assert.Equal(0m, result.Data!.Price);
    }

    [Fact]
    public void Validate_returns_invalid_when_stock_is_not_a_valid_number()
    {
        var row = ValidRow();
        row.Stock = "abc";

        var result = ProductCsvRowValidator.Validate(row);

        Assert.False(result.IsValid);
        Assert.Contains("Stock", result.ErrorMessage);
        Assert.Null(result.Data);
    }

    [Fact]
    public void Validate_returns_invalid_when_stock_is_negative()
    {
        var row = ValidRow();
        row.Stock = "-1";

        var result = ProductCsvRowValidator.Validate(row);

        Assert.False(result.IsValid);
        Assert.Null(result.Data);
    }

    [Fact]
    public void Validate_returns_valid_when_stock_is_exactly_zero()
    {
        var row = ValidRow();
        row.Stock = "0";

        var result = ProductCsvRowValidator.Validate(row);

        Assert.True(result.IsValid);
        Assert.Equal(0, result.Data!.Stock);
    }
}