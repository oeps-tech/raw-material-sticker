using System.Text.RegularExpressions;

namespace Oeps.RawMaterialSticker.Core.Data;

/// <summary>The headerless sheet's column A is exported with a deliberate OEPS_PN adapter header.</summary>
public static class ExpensiveComponentParser
{
    public static HashSet<string> Parse(string csv)
    {
        ArgumentNullException.ThrowIfNull(csv);
        var input = csv.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        if (string.IsNullOrWhiteSpace(input) || input.StartsWith('<'))
            throw new InvalidDataException("The expensive-components tab returned empty/HTML data instead of its CSV export.");
        var rows = CsvComponentParser.ReadRecords(input);
        if (rows.Count == 0 || rows[0].Count != 1 || rows[0][0].Trim() != "OEPS_PN")
            throw new InvalidDataException("The expensive-components CSV must have its OEPS_PN adapter header and exactly one column.");
        var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.Skip(1))
        {
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            if (row.Count != 1 || !Regex.IsMatch(row[0].Trim(), @"\AOEPS-?[A-Za-z0-9]{3,}\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new InvalidDataException("The expensive-components list contains an invalid OEPS PN. The last complete database has been kept.");
            identifiers.Add(row[0].Trim());
        }
        // Header-only is a valid intentionally cleared list, unlike an empty/error HTTP response.
        return identifiers;
    }
}
