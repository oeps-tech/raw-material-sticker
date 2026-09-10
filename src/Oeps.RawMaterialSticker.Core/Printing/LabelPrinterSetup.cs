using Oeps.RawMaterialSticker.Core.Configuration;

namespace Oeps.RawMaterialSticker.Core.Printing;

public static class LabelPrinterSetup
{
    public static string Apply(string zpl, PrinterConfiguration printer)
    {
        if (printer.PrintSpeedIps is null or < 1 or > 14 || printer.Darkness is null or < 0 or > 30)
            throw new ArgumentException("Invalid label speed or darkness.");
        if (printer.Dpi != 300 || printer.LabelWidthMm != 30 || printer.LabelHeightMm != 50)
            throw new ArgumentException("The supplied label templates require 300 DPI and 30 × 50 mm media.");
        if (printer.OffsetXDots is < -9999 or > 9999 || printer.OffsetYDots is < -120 or > 120)
            throw new ArgumentException("Combined printer offsets exceed the supported range (X: ±9999 dots; Y: ±120 dots).");
        var media = printer.MediaTracking switch { "continuous" => "N", "web" => "W", "mark" => "M", _ => throw new ArgumentException("Invalid media sensing.") };
        var method = printer.PrintMethod switch { "direct-thermal" => "D", "thermal-transfer" => "T", _ => throw new ArgumentException("Invalid print method.") };
        if (!zpl.StartsWith("^XA\n", StringComparison.Ordinal)) throw new ArgumentException("Invalid label format.");
        // ZPL ^LS is a shift LEFT; UI X is positive to the right. Remove the
        // template reset so it cannot cancel the queue correction later in the job.
        var body = zpl[4..].Replace("^LS0\n", "", StringComparison.Ordinal);
        return FormattableString.Invariant($"^XA\n^MN{media}\n^MT{method}\n^PR{printer.PrintSpeedIps}\n~SD{printer.Darkness}\n^MD0\n^MUD\n^PON\n^PMN\n^LH0,0\n^LS{-printer.OffsetXDots}\n^LT{printer.OffsetYDots}\n^LRN\n") + body;
    }
}
