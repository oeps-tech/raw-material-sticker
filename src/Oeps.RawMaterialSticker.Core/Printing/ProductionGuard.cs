using Oeps.RawMaterialSticker.Core.Configuration;

namespace Oeps.RawMaterialSticker.Core.Printing;

public static class ProductionGuard
{
    public static IReadOnlyList<string> Validate(PrinterConfiguration printer, string selectedQueue, LabelRenderer renderer, LabelRequest request, string? templateHash = null)
    {
        ArgumentNullException.ThrowIfNull(printer);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(request);
        var errors = new List<string>();
        if (!printer.ProductionValidated)
            errors.Add("Production printing is locked until the actual printer and label have been validated. Dry runs remain available.");
        if (string.IsNullOrWhiteSpace(printer.Model) || printer.Dpi is not (152 or 203 or 300 or 305 or 600) || string.IsNullOrWhiteSpace(printer.Connection))
            errors.Add("Configure the actual Zebra model, supported DPI, and connection before production printing.");
        if (string.IsNullOrWhiteSpace(selectedQueue))
            errors.Add("Select an installed Windows printer.");
        else if (!printer.UseSelectedPrinter && !string.Equals(printer.QueueName, selectedQueue, StringComparison.OrdinalIgnoreCase))
            errors.Add("Select the Windows printer queue configured for this label.");
        if (!string.Equals(printer.ValidatedTemplateSha256, templateHash ?? renderer.TemplateSha256, StringComparison.OrdinalIgnoreCase))
            errors.Add("The label template SHA-256 does not match the physically validated template.");
        if (printer.PrintSpeedIps is null or < 1 or > 14 || printer.Darkness is null or < 0 or > 30)
            errors.Add("Configure the model-supported print speed (1–14 IPS) and darkness (0–30). Their suitability must be physically validated.");
        if (printer.MediaTracking is not ("continuous" or "web" or "mark") || printer.PrintMethod is not ("direct-thermal" or "thermal-transfer"))
            errors.Add("Configure media tracking (continuous/web/mark) and print method (direct-thermal/thermal-transfer).");
        if (string.IsNullOrWhiteSpace(printer.ValidationNotes))
            errors.Add("Record physical validation of text fit, fonts, date and PN barcode scans, quiet zones, media/ribbon, speed and darkness in ValidationNotes.");
        try
        {
            _ = renderer.Render(request);
            if (printer.RequireTestedPnLimit && string.IsNullOrWhiteSpace(printer.LongestValidatedOepsPn))
                errors.Add("Record the longest/widest OEPS PN physically printed and scanned in LongestValidatedOepsPn.");
            else if (!string.IsNullOrWhiteSpace(printer.LongestValidatedOepsPn))
            {
                _ = renderer.Render(request with { OepsPn = printer.LongestValidatedOepsPn });
                if (Code128Encoder.Encode(request.OepsPn).WidthWithQuietZonesDots() > Code128Encoder.Encode(printer.LongestValidatedOepsPn).WidthWithQuietZonesDots()
                    || LabelValues.FormatOepsPn(request.OepsPn).Length > LabelValues.FormatOepsPn(printer.LongestValidatedOepsPn).Length)
                    errors.Add("This OEPS PN exceeds the barcode or text length tested on the actual printer. Validate it before production printing.");
            }
            // Conservative bounds for the unchanged text boxes. Font 0 is proportional, so the
            // physical check must also use wide letters (for example W), not only narrow digits.
            if (request.Mpn.Length > 34)
                errors.Add("MPN exceeds the conservative 34-character limit of the supplied text field; validate a revised layout.");
            if (request.Mpn.Any(c => c is < ' ' or > '~') || request.Mpn.Contains('\\'))
                errors.Add("The supplied MPN font is production-validated only for printable ASCII without backslashes; review its glyphs/layout for this MPN.");
            if (LabelValues.FormatQuantity(request.Quantity).Length > 7)
                errors.Add("Quantity exceeds the conservative 7-digit limit of the supplied text field; validate a revised layout.");
        }
        catch (ArgumentException ex)
        {
            errors.Add(ex.Message);
        }
        return errors;
    }
}
