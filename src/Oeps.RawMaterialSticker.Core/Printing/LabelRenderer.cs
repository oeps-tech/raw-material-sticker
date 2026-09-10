using System.Security.Cryptography;
using System.Text;
using Oeps.RawMaterialSticker.Core.Configuration;

namespace Oeps.RawMaterialSticker.Core.Printing;

public sealed record RenderedLabel(byte[] Bytes, string Zpl, string OepsPnText, string BarcodePayload, string DateMmyy, int BarcodeWidthDots, string LotCode);

public sealed class LabelRenderer
{
    public const int LabelLengthDots = 591;
    public static string BuiltInTemplate { get; } = ReadTemplate();
    private readonly string template;
    public string TemplateSha256 { get; }
    public LabelRenderer(string templateText)
    {
        ArgumentNullException.ThrowIfNull(templateText);
        template = Normalize(templateText);
        if (template != Normalize(BuiltInTemplate))
            throw new ArgumentException("The label template differs from the supplied L3 layout. Restore templates/production-label.zpl.", nameof(templateText));
        TemplateSha256 = Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(templateText))).ToLowerInvariant();
    }
    public RenderedLabel Render(LabelRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var pnText = LabelValues.FormatOepsPn(request.OepsPn);
        var date = LabelValues.FormatDate(request.Month, request.Year, request.MonthUnavailable, request.YearUnavailable);
        var lot = LabelValues.FormatLot(request);
        var quantity = LabelValues.FormatQuantity(request.Quantity);
        if (request.OepsPn.Replace("-", "", StringComparison.Ordinal).Length > 11)
            throw new ArgumentException("OEPS PN exceeds the supplied label's 11-character layout.");
        if (quantity.Length > 7) throw new ArgumentException("Quantity exceeds the supplied label's seven-character field.");
        if (string.IsNullOrWhiteSpace(request.Mpn) || request.Mpn.Length > 256 || request.Mpn.Any(char.IsControl))
            throw new ArgumentException("MPN must contain 1–256 characters and no line breaks or control characters.");
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["OEPS_PN"] = pnText, ["OEPS_PN_Datamatrix"] = request.OepsPn, ["MPN"] = request.Mpn,
            ["OEPS_Lot_QR"] = lot, ["Lot_Date"] = date,
            ["Lot_Packing"] = LabelValues.PackagingCodes[request.Packaging], ["Lot_Random"] = request.RandomCode,
            ["Quantity"] = quantity, ["OEPS_Quantity_Datamatrix"] = LabelValues.FormatQuantityDataMatrix(request.Quantity)
        };
        var zpl = template;
        if (request.Quantity == 0)
            zpl = zpl.Replace("^FT204,115^BXB,7,200,0,0,1,_,1\n^FH\\^FD{{OEPS_Quantity_Datamatrix}}^FS\n", "", StringComparison.Ordinal);
        foreach (var (key, value) in fields)
            zpl = zpl.Replace("{{" + key + "}}", LabelValues.EscapeField(value), StringComparison.Ordinal);
        if (zpl.Contains("{{", StringComparison.Ordinal) || zpl.Contains("}}", StringComparison.Ordinal))
            throw new InvalidOperationException("The label contains an unresolved placeholder.");
        zpl += "\n";
        return new(Encoding.ASCII.GetBytes(zpl), zpl, pnText, request.OepsPn, date, 0, lot);
    }
    public RenderedLabel RenderProduction(LabelRequest request, PrinterConfiguration printer, string selectedQueue)
    {
        var errors = ProductionGuard.Validate(printer, selectedQueue, this, request);
        if (errors.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        var rendered = Render(request);
        var zpl = LabelPrinterSetup.Apply(rendered.Zpl, printer);
        return rendered with { Zpl = zpl, Bytes = Encoding.ASCII.GetBytes(zpl) };
    }
    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
    private static string ReadTemplate()
    {
        using var stream = typeof(LabelRenderer).Assembly.GetManifestResourceStream("Oeps.ProductionLabelTemplate")
            ?? throw new InvalidOperationException("The embedded L3 layout is missing.");
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        return reader.ReadToEnd();
    }
}
