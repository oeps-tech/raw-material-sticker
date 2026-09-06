using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Oeps.RawMaterialSticker.Core.Printing;

public sealed record LabelRequest(string OepsPn, string Mpn, int Month, int Year, int Quantity, bool DateUnavailable = false);

public static partial class LabelValues
{
    public static string FormatOepsPn(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var match = OepsPattern().Match(value);
        if (!match.Success)
            throw new ArgumentException("OEPS PN must start with OEPS, optionally followed by a hyphen, then at least three ASCII letters or digits.", nameof(value));
        var suffix = match.Groups[1].Value;
        return $"OEPS {suffix[..2]} {suffix[2..]}";
    }

    public static string FormatDate(int month, int year, bool dateUnavailable = false)
    {
        if (dateUnavailable) return "0000";
        if (month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month), "Reception month must be 01–12.");
        if (year is < 2000 or > 2099)
            throw new ArgumentOutOfRangeException(nameof(year), "Reception year must be a four-digit year from 2000 to 2099.");
        return month.ToString("D2", CultureInfo.InvariantCulture) + (year % 100).ToString("D2", CultureInfo.InvariantCulture);
    }

    public static string FormatQuantity(int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be a positive whole number of components.");
        return quantity.ToString(CultureInfo.InvariantCulture);
    }

    // Every byte, including ZPL prefixes and the hex introducer itself, is encoded.
    // The template declares ^CI28 and ^FH\, so the wire stream stays ASCII without a BOM.
    public static string EscapeField(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var bytes = new UTF8Encoding(false, true).GetBytes(value);
        var result = new StringBuilder(bytes.Length * 3);
        foreach (var valueByte in bytes)
            result.Append('\\').Append(valueByte.ToString("X2", CultureInfo.InvariantCulture));
        return result.ToString();
    }

    [GeneratedRegex(@"\AOEPS-?([A-Za-z0-9]{3,})\z", RegexOptions.CultureInvariant)]
    private static partial Regex OepsPattern();
}
