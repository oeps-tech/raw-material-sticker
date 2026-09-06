using System.Security.Cryptography;
using System.Text;
using Oeps.RawMaterialSticker.Core.Configuration;

namespace Oeps.RawMaterialSticker.Core.Printing;

public sealed record RenderedLabel(byte[] Bytes, string Zpl, string OepsPnText, string BarcodePayload, string DateMmyy, int BarcodeWidthDots);

public sealed class LabelRenderer
{
    public const int LabelLengthDots = 591;
    public const int PnAnchorY = 487;
    public const int DateAnchorY = 228;
    public const int ModuleWidthDots = 3;
    public const int QuietZoneDots = 10 * ModuleWidthDots;

    // Intentionally tied to this layout: changed geometry/fonts require a code review and physical revalidation.
    // Source: the final format block in label/L3.prn, matching the brief's L3(2).prn block.
    public const string BuiltInTemplate = """
^XA
^CI28
^MMT
^PW354
^LL591
^LS0
^BY3,3,56^FT136,487^BCB,56,N,N,N,N
^FH\^FD{{OEPS_PN_barcode}}^FS
^FPH,8^FT73,591^A0B,54,53^FB575,1,14,C^FH\^FD{{OEPS_PN_text}}^FS
^FPH,8^FT166,584^AQB,28,9^FB584,1,6,C^FH\^FD{{MPN}}^FS
^BY3,3,56^FT241,228^BCB,56,N,N,N,N
^FH\^FD{{Date_MMYY_barcode}}^FS
^FPH,8^FT234,591^A0B,54,53^FB227,1,14,C^FH\^FDDate:^FS
^FPH,8^FT306,591^A0B,54,53^FB187,1,14,C^FH\^FDQty:^FS
^FPH,8^FT234,591^A0B,54,53^FB552,1,14,C^FH\^FD{{Date_MMYY_text}}^FS
^FO318,49^GB0,375,3^FS
^FPH,8^FT307,424^A0B,42,43^FH\^FD{{Quantity_text}}^FS
^PQ1,0,1,Y
^XZ
""";

    private readonly string template;

    public LabelRenderer(string templateText)
    {
        ArgumentNullException.ThrowIfNull(templateText);
        template = templateText.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
        if (template != BuiltInTemplate.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n'))
            throw new ArgumentException("The configured label template differs from the supplied, supported layout. Restore templates/production-label.zpl; layout changes require implementation and validation.", nameof(templateText));
        TemplateSha256 = Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(templateText))).ToLowerInvariant();
    }

    public string TemplateSha256 { get; }

    public RenderedLabel Render(LabelRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var pnText = LabelValues.FormatOepsPn(request.OepsPn);
        var date = LabelValues.FormatDate(request.Month, request.Year, request.DateUnavailable);
        var quantity = LabelValues.FormatQuantity(request.Quantity);
        if (string.IsNullOrWhiteSpace(request.Mpn) || request.Mpn.Length > 256 || request.Mpn.Any(char.IsControl))
            throw new ArgumentException("MPN must contain 1–256 characters and no line breaks or control characters.", nameof(request));
        var pnBarcode = Code128Encoder.Encode(request.OepsPn);
        var dateBarcode = Code128Encoder.Encode(date);
        ValidateBarcodeFit(pnBarcode, PnAnchorY, "OEPS PN");
        ValidateBarcodeFit(dateBarcode, DateAnchorY, "Date");

        // Unique, named placeholders only. No sample-number replacement and no unescaped input.
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["OEPS_PN_text"] = pnText,
            ["OEPS_PN_barcode"] = pnBarcode.ZplFieldData,
            ["MPN"] = request.Mpn,
            ["Date_MMYY_text"] = date,
            ["Date_MMYY_barcode"] = dateBarcode.ZplFieldData,
            ["Quantity_text"] = quantity
        };
        var zpl = template;
        foreach (var (key, value) in fields)
            zpl = zpl.Replace("{{" + key + "}}", LabelValues.EscapeField(value), StringComparison.Ordinal);
        if (zpl.Contains("{{", StringComparison.Ordinal) || zpl.Contains("}}", StringComparison.Ordinal))
            throw new InvalidOperationException("The label contains an unresolved placeholder.");
        zpl += "\n";
        return new RenderedLabel(Encoding.ASCII.GetBytes(zpl), zpl, pnText, request.OepsPn, date, pnBarcode.WidthWithQuietZonesDots());
    }

    public RenderedLabel RenderProduction(LabelRequest request, PrinterConfiguration printer, string selectedQueue)
    {
        var errors = ProductionGuard.Validate(printer, selectedQueue, this, request);
        if (errors.Count != 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        var rendered = Render(request);
        var media = printer.MediaTracking switch { "continuous" => "N", "web" => "W", "mark" => "M", _ => throw new InvalidOperationException() };
        var method = printer.PrintMethod == "direct-thermal" ? "D" : "T";
        // Explicit per-job settings; never replay ~JA, ^JUS, or the export's malformed prefix.
        var setup = FormattableString.Invariant($"^XA\n^MN{media}\n^MT{method}\n^PR{printer.PrintSpeedIps}\n~SD{printer.Darkness}\n^MD0\n^MUD\n^PON\n^PMN\n^LH0,0\n^LT0\n^LRN\n");
        var zpl = setup + rendered.Zpl[4..];
        return rendered with { Zpl = zpl, Bytes = Encoding.ASCII.GetBytes(zpl) };
    }

    private static void ValidateBarcodeFit(EncodedBarcode barcode, int anchorY, string name)
    {
        // Orientation B runs toward decreasing Y. The far quiet zone is beyond the anchor.
        if (barcode.BarWidthDots() + QuietZoneDots > anchorY || anchorY + QuietZoneDots > LabelLengthDots)
            throw new ArgumentException($"{name} barcode requires {barcode.WidthWithQuietZonesDots()} dots including quiet zones; the supplied layout allows {anchorY + QuietZoneDots}. A revised and validated layout is required.");
    }
}
