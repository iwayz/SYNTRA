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
    ManualAdjustment
}
