namespace AsyncCsvProcessor.Domain;

public class Product
{
    public Guid Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public decimal Price { get; private set; }
    public string Category { get; private set; } = string.Empty;
    public int Stock { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdateAt { get; private set; }
    
    private Product() { }

    public Product(string sku, string name, decimal price, string category, int stock)
    {
        Id = Guid.NewGuid();
        Sku = sku;
        Name = name;
        Price = price;
        Category = category;
        Stock = stock;
        CreatedAt = DateTime.UtcNow;
    }

    public void UpdateFrom(string name, decimal price, string category, int stock)
    {
        Name = name;
        Price = price;
        Category = category;
        Stock = stock;
        UpdateAt = DateTime.UtcNow;
    }
}