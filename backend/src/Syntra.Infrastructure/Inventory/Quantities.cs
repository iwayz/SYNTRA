using System.Globalization;

namespace Syntra.Infrastructure.Inventory;

public static class Quantities
{
    // Stock quantities are stored with four decimals; show "4" rather than "4.0000"
    // in messages, and always with a dot regardless of the server's culture.
    public static string Format(decimal quantity) => quantity.ToString("0.####", CultureInfo.InvariantCulture);
}
