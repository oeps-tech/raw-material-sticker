using System.Globalization;
using Oeps.RawMaterialSticker.Core.Data;
using Oeps.RawMaterialSticker.Core.Printing;
using Scanner.Contracts;

namespace Oeps.RawMaterialSticker.Core.Camera;

public sealed record CameraReading(string OepsPn, string Lot, decimal Quantity, int Month, int Year,
    bool MonthUnavailable, bool YearUnavailable, string Packaging, string RandomCode)
{
    public static CameraReading Parse(string json)
    {
        var data = PayloadValidator.ReadClientJson(json) ?? throw new ArgumentException("Invalid camera message.");
        if (data.LabelVersion != 1) throw new ArgumentException("Unsupported camera label version.");
        var parts = data.Lot.Split('-');
        var month = int.Parse(parts[0][..2], CultureInfo.InvariantCulture);
        var yearDigits = int.Parse(parts[0][2..], CultureInfo.InvariantCulture);
        if (month > 12) throw new ArgumentException("The scanned lot contains an invalid month.");
        var year = yearDigits == 0 ? 0 : 2000 + yearDigits;
        if (year != 0 && (year < 2020 || year > DateTime.Now.Year))
            throw new ArgumentException("The scanned lot year must be 2020 through the current year, or 00.");
        var packaging = LabelValues.PackagingCodes.FirstOrDefault(p => p.Value == parts[1]).Key
            ?? throw new ArgumentException("The scanned lot contains an unknown packaging code.");
        if (!decimal.TryParse(data.Quantity, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var quantity))
            throw new ArgumentException("The scanned quantity is invalid.");
        _ = LabelValues.FormatQuantityDataMatrix(quantity);
        var result = new CameraReading(data.OepsPn, data.Lot, quantity, month, year, month == 0, year == 0, packaging, parts[2]);
        if (LabelValues.FormatLot(result.Request("MPN", quantity)) != data.Lot)
            throw new ArgumentException("The scanned lot cannot be represented without changing it.");
        return result;
    }

    public LabelRequest Request(string mpn, decimal quantity) =>
        new(OepsPn, mpn, Month, Year, quantity, MonthUnavailable, YearUnavailable, Packaging, RandomCode);

    public Component ResolveComponent(IEnumerable<Component> components, Component? selected)
    {
        var matches = components.Where(c => string.Equals(c.OepsPn, OepsPn, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0) throw new ArgumentException("This OEPS PN is not in the database. Update the database and scan again.");
        if (matches.Length == 1) return matches[0];
        var exact = selected is null ? null : matches.FirstOrDefault(c => c.OepsPn == selected.OepsPn && c.Mpn == selected.Mpn);
        return exact ?? throw new ArgumentException("This PN has multiple MPNs. Leave, select its PN/MPN pair in the main window, then scan again.");
    }
}

public sealed class CameraInbox
{
    private string? _lastJson;
    public bool Accept(string json)
    {
        if (string.Equals(_lastJson, json, StringComparison.Ordinal)) return false;
        _lastJson = json;
        return true;
    }
    public void ActionPressed() => _lastJson = null;
}
