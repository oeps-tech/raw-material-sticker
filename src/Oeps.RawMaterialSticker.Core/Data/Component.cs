namespace Oeps.RawMaterialSticker.Core.Data;

public sealed record Component(string OepsPn, string Mpn, string? Manufacturer = null, string? Description = null, bool IsExpensive = false)
{
    public override string ToString() => $"{OepsPn}  |  {Mpn}";
}

public enum SearchMode { OepsPn, Mpn }

public static class ComponentSearch
{
    public static IReadOnlyList<Component> Search(IEnumerable<Component> components, string query, SearchMode mode, int limit = 30)
    {
        ArgumentNullException.ThrowIfNull(components);
        if (limit <= 0) return [];
        query = WithoutWhitespace(query ?? string.Empty);
        string Identifier(Component component) => mode == SearchMode.Mpn ? component.Mpn : component.OepsPn;
        return components
            .Select(component => (Component: component, SearchIdentifier: WithoutWhitespace(Identifier(component))))
            .Where(item => item.SearchIdentifier.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.SearchIdentifier.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => Identifier(item.Component), StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Component.OepsPn, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Component.Mpn, StringComparer.OrdinalIgnoreCase)
            .Take(limit).Select(item => item.Component).ToArray();
    }

    private static string WithoutWhitespace(string value) => new(value.Where(c => !char.IsWhiteSpace(c)).ToArray());

    public static Component? FindPair(IEnumerable<Component> components, string oepsPn, string mpn) =>
        components.FirstOrDefault(component => string.Equals(component.OepsPn, oepsPn, StringComparison.Ordinal)
            && string.Equals(component.Mpn, mpn, StringComparison.Ordinal));
}
