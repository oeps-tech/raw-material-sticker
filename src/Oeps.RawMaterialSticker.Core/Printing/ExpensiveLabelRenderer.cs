using System.Security.Cryptography;
using System.Text;

namespace Oeps.RawMaterialSticker.Core.Printing;

/// <summary>Preserves the supplied L3 expensive layout, including its warning graphic.</summary>
public sealed class ExpensiveLabelRenderer
{
    public static string BuiltInTemplate { get; } = ReadTemplate();
    private readonly string _template;
    public string TemplateSha256 { get; }

    public ExpensiveLabelRenderer(string templateText)
    {
        ArgumentNullException.ThrowIfNull(templateText);
        _template = Normalize(templateText);
        if (_template != Normalize(BuiltInTemplate))
            throw new ArgumentException("The expensive-label template differs from the supplied L3 expensive layout. Restore templates/expensive-label.zpl.");
        TemplateSha256 = Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(templateText))).ToLowerInvariant();
    }

    public string Render(LabelRequest request)
    {
        // The same component validity, escaping and PN barcode geometry apply to both designs.
        _ = new LabelRenderer(LabelRenderer.BuiltInTemplate).Render(request);
        var fields = new Dictionary<string, string>
        {
            ["OEPS_PN_text"] = LabelValues.FormatOepsPn(request.OepsPn),
            ["OEPS_PN_barcode"] = Code128Encoder.Encode(request.OepsPn).ZplFieldData,
            ["MPN"] = request.Mpn
        };
        var zpl = _template;
        foreach (var (field, value) in fields) zpl = zpl.Replace("{{" + field + "}}", LabelValues.EscapeField(value), StringComparison.Ordinal);
        if (zpl.Contains("{{", StringComparison.Ordinal) || zpl.Contains("}}", StringComparison.Ordinal))
            throw new InvalidOperationException("The expensive label contains an unresolved placeholder.");
        return zpl + "\n";
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
    private static string ReadTemplate()
    {
        using var stream = typeof(ExpensiveLabelRenderer).Assembly.GetManifestResourceStream("Oeps.ExpensiveLabelTemplate")
            ?? throw new InvalidOperationException("The embedded expensive-label layout is missing.");
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        return reader.ReadToEnd();
    }
}
