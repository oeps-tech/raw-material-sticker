using System.Globalization;
using Oeps.RawMaterialSticker.Core;
using Oeps.RawMaterialSticker.Core.Printing;

namespace Oeps.RawMaterialSticker.Tests;

public static class LotTests
{
    [Test]
    public static void BothLabelsFormatNumericAndLetterPrefixedPartNumbers()
    {
        var normal = new LabelRenderer(LabelRenderer.BuiltInTemplate);
        var expensive = new ExpensiveLabelRenderer(ExpensiveLabelRenderer.BuiltInTemplate);
        foreach (var (pn, expected) in new[] { ("OEPS011234", "OEPS 01 1234"), ("OEPSA011234", "OEPS A01 1234"),
            ("OEPSA01123", "OEPS A01 123"), ("OEPSB02001", "OEPS B02 001") })
        {
            var request = new LabelRequest(pn, "MPN", 9, 2026, 1);
            var textField = "^FD" + LabelValues.EscapeField(expected) + "^FS";
            var l3 = normal.Render(request);
            var l3Expensive = expensive.Render(request);
            Assert.True(l3.Zpl.Contains(textField));
            Assert.True(l3Expensive.Contains(textField));
            Assert.True(l3.Zpl.Contains("^FD" + LabelValues.EscapeField(pn) + "^FS"));
            Assert.True(l3Expensive.Contains("^FD" + LabelValues.EscapeField(Code128Encoder.Encode(pn).ZplFieldData) + "^FS"));
        }
    }

    [Test]
    public static void LotMappingsPreserveExactCodesAndRejectInvalidInputs()
    {
        var request = new LabelRequest("OEPS010243", "MPN", 9, 2026, 1, Packaging: "Tray", RandomCode: "t8sQ");
        foreach (var (name, code) in new[] { ("Reel", "REEL"), ("Tube", "TUBE"), ("Tape", "TAPE"), ("Bag", "_BAG"),
            ("Box", "_BOX"), ("Tray", "TRAY"), ("Spool", "SPOL"), ("Other", "OTHR") })
            Assert.Equal($"0926_{code}_t8sQ", LabelValues.FormatLot(request with { Packaging = name }));
        Assert.Equal("0000_TRAY_t8sQ", LabelValues.FormatLot(request with { MonthUnavailable = true, YearUnavailable = true, Month = 0, Year = 0 }));
        Assert.Equal("0026_TRAY_t8sQ", LabelValues.FormatLot(request with { MonthUnavailable = true, Month = 0 }));
        Assert.Equal("0900_TRAY_t8sQ", LabelValues.FormatLot(request with { YearUnavailable = true, Year = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => LabelValues.FormatLot(request with { MonthUnavailable = true, Year = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => LabelValues.FormatLot(request with { YearUnavailable = true, Month = 0 }));
        Assert.Throws<ArgumentException>(() => LabelValues.FormatLot(request with { Packaging = "Unknown" }));
        Assert.Throws<ArgumentException>(() => LabelValues.FormatLot(request with { RandomCode = "^XZ!" }));
        Assert.Equal("OEPS X01 0000", LabelValues.FormatOepsPn("OEPSX010000"));
        Assert.Equal("OEPS 01 0243", LabelValues.FormatOepsPn("OEPS010243"));
    }

    [Test]
    public static void DecimalQuantityAndLotReachTheCorrectSymbols()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-PT");
            var renderer = new LabelRenderer(LabelRenderer.BuiltInTemplate);
            var request = new LabelRequest("OEPS010243", "MPN", 9, 2026, 100.245m, Packaging: "Bag", RandomCode: "t8sQ");
            var result = renderer.Render(request);
            Assert.Equal("0926__BAG_t8sQ", result.LotCode);
            Assert.True(result.Zpl.Contains("^FDLA," + LabelValues.EscapeField(result.LotCode) + "^FS"));
            Assert.True(result.Zpl.Contains("^FD" + LabelValues.EscapeField("0100.245") + "^FS"));
            Assert.True(result.Zpl.Contains("^FD" + LabelValues.EscapeField("100.245") + "^FS"));
            Assert.True(renderer.Render(request with { Quantity = 0 }).Zpl.Contains("^FD^FS"));
            var zero = renderer.Render(request with { Quantity = 0 }).Zpl;
            Assert.False(zero.Contains("^FT204,115"));
            Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(zero, @"\^BX").Count);
            Assert.True(zero.Contains("^FD" + LabelValues.EscapeField(request.OepsPn) + "^FS"));
            Assert.Equal(result.Zpl, renderer.Render(request).Zpl);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Test]
    public static void QuantityDataMatrixPadsToSevenCharactersIncludingDecimalPoint()
    {
        foreach (var (quantity, expected) in new[] { (0m, ""), (1m, "0000001"), (42m, "0000042"),
            (123456m, "0123456"), (1.5m, "00001.5"), (0.125m, "000.125"), (100.245m, "0100.245") })
        {
            var payload = LabelValues.FormatQuantityDataMatrix(quantity);
            Assert.Equal(expected, payload);
            if (quantity == 0) continue;
            Assert.True(payload.Length >= 7);
            Assert.Equal(quantity, decimal.Parse(payload, CultureInfo.InvariantCulture));
        }
    }

    [Test]
    public static void SessionRandomCodeRemainsStableDuringRefresh()
    {
        var session = new OperationSession();
        var random = session.RandomCode;
        Assert.Equal(4, random.Length);
        Assert.True(random.All(char.IsAsciiLetterOrDigit));
        session.Revalidate([]);
        Assert.Equal(random, session.RandomCode);
        session.Packaging = "Tray";
        Assert.Equal(random, session.RandomCode);
        session.NextLot();
        Assert.Equal(4, session.RandomCode.Length);
        Assert.True(session.RandomCode.All(char.IsAsciiLetterOrDigit));
    }
}
