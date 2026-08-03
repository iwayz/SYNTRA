namespace Syntra.Domain.Inventory;

public class Product
{
    public int ProductId { get; set; }
    public string Sku { get; set; } = default!;
    public string Name { get; set; } = default!;

    public int BaseUnitOfMeasureId { get; set; }
    public UnitOfMeasure BaseUnitOfMeasure { get; set; } = default!;

    public ICollection<ProductUnitConversion> UnitConversions { get; set; } = new List<ProductUnitConversion>();
}
