using System.Text.Json.Serialization;

namespace Syntra.Domain.Sales;

// No "Delivered"/fulfilled step: confirming a sales order moves stock
// immediately (see SalesOrdersController.Confirm), unlike PurchaseOrder's
// separate Confirm/Receive split. A direct consequence: Cancelled is only
// reachable from Draft, since by Confirmed stock has already moved.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SalesOrderStatus
{
    Draft,
    Confirmed,
    Cancelled
}
