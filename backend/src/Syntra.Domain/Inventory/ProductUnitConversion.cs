namespace Syntra.Domain.Inventory;

// How many of the product's base unit one of this (non-base) unit equals -
// e.g. UnitOfMeasure "BOX", ConversionFactor 12 means 1 BOX = 12 base units.
public class ProductUnitConversion
{
    public int ProductUnitConversionId { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; } = default!;

    public int UnitOfMeasureId { get; set; }
    public UnitOfMeasure UnitOfMeasure { get; set; } = default!;

    public decimal ConversionFactor { get; set; }
}
