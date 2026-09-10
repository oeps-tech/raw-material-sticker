using System.Text;
using Oeps.RawMaterialSticker.Core.Configuration;

namespace Oeps.RawMaterialSticker.Core.Printing;

public sealed record RenderedPrintJob(byte[] Bytes, int LabelCount);
public sealed record RoutedLabel(string Name, string Queue, byte[] Bytes);

/// <summary>Build every required format before one RAW submission, so failures cannot silently omit the extra label.</summary>
public sealed class LabelJobRenderer(LabelRenderer normal, ExpensiveLabelRenderer? expensive)
{
    public IReadOnlyList<RoutedLabel> RenderProfiles(LabelRequest request, PrinterConfiguration primary, string selectedQueue,
        PrinterConfiguration extra, bool isExpensive, bool production, UserSettings? settings = null)
    {
        var extraQueue = extra.UseSelectedPrinter ? selectedQueue : extra.QueueName;
        if (settings is not null)
        {
            primary = settings.ApplyPrinterOffsets(primary, selectedQueue);
            if (isExpensive && extraQueue is not null) extra = settings.ApplyPrinterOffsets(extra, extraQueue);
        }
        var errors = production ? ProductionGuard.Validate(primary, selectedQueue, normal, request).ToList() : new List<string>();
        if (isExpensive)
        {
            if (expensive is null) throw new InvalidOperationException("L3 expensive template is unavailable.");
            if (string.IsNullOrWhiteSpace(extraQueue)) throw new InvalidOperationException("Configure the L3 expensive printer queue.");
            if (production) errors.AddRange(ProductionGuard.Validate(extra, extraQueue, normal, request, expensive.TemplateSha256)
                .Select(error => "L3 expensive: " + error));
        }
        if (errors.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        var labels = new List<RoutedLabel>
        {
            new("L3", selectedQueue, Encoding.ASCII.GetBytes(LabelPrinterSetup.Apply(normal.Render(request).Zpl, primary)))
        };
        if (isExpensive) labels.Add(new("L3 expensive", extraQueue!, Encoding.ASCII.GetBytes(LabelPrinterSetup.Apply(expensive!.Render(request), extra))));
        return labels;
    }

    public RenderedPrintJob Render(LabelRequest request, bool isExpensive) =>
        Combine(normal.Render(request).Zpl, request, isExpensive);

    public IReadOnlyList<string> ValidateProduction(LabelRequest request, PrinterConfiguration printer, string queue, bool isExpensive)
    {
        var errors = ProductionGuard.Validate(printer, queue, normal, request).ToList();
        if (isExpensive)
        {
            if (expensive is null) errors.Add("The L3 expensive template is unavailable. Both labels are required for this component.");
            else if (!string.Equals(expensive.TemplateSha256, printer.ValidatedExpensiveTemplateSha256, StringComparison.OrdinalIgnoreCase))
                errors.Add("Validate the L3 expensive label on the configured printer and record its template SHA-256 before production printing.");
        }
        return errors;
    }

    public RenderedPrintJob RenderProduction(LabelRequest request, PrinterConfiguration printer, string queue, bool isExpensive)
    {
        var errors = ValidateProduction(request, printer, queue, isExpensive);
        if (errors.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        return Combine(normal.RenderProduction(request, printer, queue).Zpl, request, isExpensive);
    }

    private RenderedPrintJob Combine(string normalZpl, LabelRequest request, bool isExpensive)
    {
        var extraZpl = isExpensive
            ? (expensive ?? throw new InvalidOperationException("The L3 expensive template is unavailable. Both labels are required for this component.")).Render(request)
            : "";
        return new(Encoding.ASCII.GetBytes(normalZpl + extraZpl), isExpensive ? 2 : 1);
    }
}
