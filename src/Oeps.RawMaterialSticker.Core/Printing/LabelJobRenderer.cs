using System.Text;
using Oeps.RawMaterialSticker.Core.Configuration;

namespace Oeps.RawMaterialSticker.Core.Printing;

public sealed record RenderedPrintJob(byte[] Bytes, int LabelCount);

/// <summary>Build every required format before one RAW submission, so failures cannot silently omit the extra label.</summary>
public sealed class LabelJobRenderer(LabelRenderer normal, ExpensiveLabelRenderer? expensive)
{
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
