using Oeps.RawMaterialSticker.Core.Data;

namespace Oeps.RawMaterialSticker.Core;

/// <summary>Operator input has a different lifetime from downloaded component snapshots.</summary>
public sealed class OperationSession
{
    public Component? Selected { get; private set; }
    public string Month { get; set; } = DateTime.Now.Month.ToString("00");
    public string Year { get; set; } = DateTime.Now.Year.ToString("0000");
    public bool DateUnavailable { get; set; }
    public string Quantity { get; set; } = "";
    public void Select(Component component) => Selected = component;
    public void InvalidateSelection() => Selected = null;
    public bool Revalidate(IReadOnlyList<Component> components)
    {
        if (Selected is null) return true;
        Selected = ComponentSearch.FindPair(components, Selected.OepsPn, Selected.Mpn);
        return Selected is not null;
    }
}
