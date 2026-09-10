using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

namespace Oeps.RawMaterialSticker.Core.Printing;

public sealed record LabelRequest(string OepsPn, string Mpn, int Month, int Year, decimal Quantity, bool MonthUnavailable = false, bool YearUnavailable = false,
    string Packaging = "Other", string RandomCode = "0000");

public static partial class LabelValues
{
    public static IReadOnlyDictionary<string, string> PackagingCodes { get; } = new Dictionary<string, string>
    {
        ["Reel"] = "REEL", ["Tube"] = "TUBE", ["Tape"] = "TAPE", ["Bag"] = "_BAG",
        ["Box"] = "_BOX", ["Tray"] = "TRAY", ["Spool"] = "SPOL", ["Other"] = "OTHR"
    };
    public static string NewRandomCode()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        return new string(Enumerable.Range(0, 4).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
    }
    public static string FormatLot(LabelRequest request)
    {
        if (!PackagingCodes.TryGetValue(request.Packaging, out var pack)) throw new ArgumentException("Select a packaging type.");
        if (request.RandomCode.Length != 4 || !request.RandomCode.All(char.IsAsciiLetterOrDigit)) throw new ArgumentException("Lot random code must contain four ASCII letters or digits.");
        return $"{FormatDate(request.Month, request.Year, request.MonthUnavailable, request.YearUnavailable)}_{pack}_{request.RandomCode}";
    }
    public static string FormatOepsPn(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var match = OepsPattern().Match(value);
        if (!match.Success)
            throw new ArgumentException("OEPS PN must start with OEPS, optionally followed by a hyphen, then at least three ASCII letters or digits.", nameof(value));
        var suffix = match.Groups[1].Value;
        var shortLetterSeries = suffix.Length == 6 && char.IsAsciiLetter(suffix[0]) && suffix.Skip(1).All(char.IsAsciiDigit);
        var split = suffix.Length == 7 || shortLetterSeries ? 3 : 2;
        return $"OEPS {suffix[..split]} {suffix[split..]}";
    }

    public static string FormatDate(int month, int year, bool monthUnavailable = false, bool yearUnavailable = false)
    {
        if (!monthUnavailable && month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month), "Reception month must be 01–12.");
        if (!yearUnavailable && year is < 2000 or > 2099)
            throw new ArgumentOutOfRangeException(nameof(year), "Reception year must be a four-digit year from 2000 to 2099.");
        return (monthUnavailable ? "00" : month.ToString("D2", CultureInfo.InvariantCulture))
            + (yearUnavailable ? "00" : (year % 100).ToString("D2", CultureInfo.InvariantCulture));
    }

    public static string FormatQuantityDataMatrix(decimal quantity)
    {
        var text = FormatQuantity(quantity);
        if (text.Length == 0) return "";
        var payload = "0" + text;
        return payload.PadLeft(7, '0');
    }

    public static string FormatQuantity(decimal quantity)
    {
        if (quantity < 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be zero or a positive number of components.");
        return quantity == 0 ? "" : quantity.ToString("0.############################", CultureInfo.InvariantCulture);
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
