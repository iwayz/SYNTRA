using System.Text.Json.Serialization;

namespace Syntra.Domain.Purchasing;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PurchaseOrderStatus
{
    Draft,
    Confirmed,
    Received,
    Cancelled
}
