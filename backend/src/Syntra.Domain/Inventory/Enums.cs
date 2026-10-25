using System.Text.Json.Serialization;

namespace Syntra.Domain.Inventory;

// JsonConverter applied directly on the type, not just globally in
// Program.cs - the OpenAPI generator only honors the type-level attribute,
// not a converter added via JsonSerializerOptions.Converters at startup.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MovementType
{
    Purchase,
    Sale,
    ManualAdjustment,
    TransferOut,
    TransferIn,
    TransferReversal,
    StockCount
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StockTransferStatus
{
    // Prepared only: no stock has moved and none is reserved.
    Draft,

    // Dispatched: the stock has left the source and has not arrived yet.
    InTransit,

    // Some, but not all, of the dispatched stock has been received.
    PartiallyReceived,

    // Fully received, or closed short with the remainder written off.
    Received,

    Cancelled
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StockCountStatus
{
    // Being counted; nothing has changed in stock yet.
    Open,

    // The counted differences have been applied to stock.
    Posted,

    Cancelled
}
