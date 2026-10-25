using System.Text.Json.Serialization;

namespace Syntra.Domain.Inventory;

// How a product's stock is tracked. Once a product is tracked it stays tracked:
// every movement of it carries a batch, so switching tracking off would orphan
// that history.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BatchTracking
{
    // Stock is just a quantity per warehouse.
    None,

    // Every movement names a batch (lot number); batches carry no expiry.
    Batch,

    // As Batch, and every new batch must have an expiry date. Sales issue the
    // earliest expiry first and never sell an expired batch.
    BatchAndExpiry
}
