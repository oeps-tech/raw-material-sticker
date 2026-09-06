using Oeps.RawMaterialSticker.Core;
using Oeps.RawMaterialSticker.Core.Data;

namespace Oeps.RawMaterialSticker.Tests;

public static class SessionTests
{
    [Test] public static void SyncPreservesInputsAndRevalidatesExactPair()
    {
        var session = new OperationSession { Month = "09", Year = "2026", Quantity = "42" };
        var selected = new Component("OEPS001234", "MPN-1");
        session.Select(selected);
        Assert.True(session.Revalidate([selected, new("OEPS001234", "MPN-2")]));
        Assert.False(session.Revalidate([new("OEPS001234", "MPN-2")]));
        Assert.True(session.Selected is null);
        Assert.Equal("09", session.Month); Assert.Equal("2026", session.Year); Assert.Equal("42", session.Quantity);
    }
    [Test] public static void FreshSessionDoesNotReuseQuantityAndEditingInvalidatesSelection()
    {
        var session = new OperationSession(); Assert.Equal("", session.Quantity);
        session.Select(new("OEPS001234", "MPN")); session.InvalidateSelection(); Assert.True(session.Selected is null);
    }
}
