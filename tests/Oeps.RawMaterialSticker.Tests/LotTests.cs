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
        foreach (var (pn, l3Text, expensiveText) in new[] { ("OEPS011234", "OEPS 01 1234", "OEPS 01 1234"),
            ("OEPSA011234", "OEPS A0 11234", "OEPS A01 1234"),
            ("OEPSA01123", "OEPS A01 123", "OEPS A01 123"), ("OEPSB02001", "OEPS B02 001", "OEPS B02 001") })
        {
            var request = new LabelRequest(pn, "MPN", 9, 2026, 1);
            var l3 = normal.Render(request);
            var l3Expensive = expensive.Render(request);
            Assert.True(l3.Zpl.Contains("^FD" + LabelValues.EscapeField(l3Text) + "^FS"));
            Assert.True(l3Expensive.Contains("^FD" + LabelValues.EscapeField(expensiveText) + "^FS"));
            Assert.Equal(pn, l3.BarcodePayload.Split('*')[1]);
            Assert.True(l3Expensive.Contains("^FD" + LabelValues.EscapeField(Code128Encoder.Encode(pn).ZplFieldData) + "^FS"));
        }
    }

    [Test]
    public static void LotMappingsPreserveExactCodesAndRejectInvalidInputs()
    {
        var request = new LabelRequest("OEPS010243", "MPN", 9, 2026, 1, Packaging: "Tray", RandomCode: "T8SQ");
        foreach (var (name, code) in new[] { ("Reel", "REEL"), ("Tube", "TUBE"), ("Tape", "TAPE"), ("Bag", "BAG"),
            ("Box", "BOX"), ("Tray", "TRAY"), ("Spool", "SPOL"), ("Other", "OTHR") })
            Assert.Equal($"0926-{code}-T8SQ", LabelValues.FormatLot(request with { Packaging = name }));
        Assert.Equal("0000-TRAY-T8SQ", LabelValues.FormatLot(request with { MonthUnavailable = true, YearUnavailable = true, Month = 0, Year = 0 }));
        Assert.Equal("0026-TRAY-T8SQ", LabelValues.FormatLot(request with { MonthUnavailable = true, Month = 0 }));
        Assert.Equal("0900-TRAY-T8SQ", LabelValues.FormatLot(request with { YearUnavailable = true, Year = 0 }));
        Assert.Throws<ArgumentException>(() => LabelValues.FormatLot(request with { RandomCode = "t8sQ" }));
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
            var request = new LabelRequest("OEPS010243", "MPN", 9, 2026, 100.245m, Packaging: "Bag", RandomCode: "T8SQ");
            var result = renderer.Render(request);
            Assert.Equal("0926-BAG-T8SQ", result.LotCode);
            Assert.Equal("1*OEPS010243*0926-BAG-T8SQ*100.245*32621FAC", result.BarcodePayload);
            Assert.True(result.Zpl.Contains("^FD" + LabelValues.EscapeField(result.BarcodePayload) + "^FS"));
            Assert.True(result.Zpl.Contains("^FD" + LabelValues.EscapeField("Bag") + "^FS"));
            Assert.True(result.Zpl.Contains("^FD" + LabelValues.EscapeField("100.245") + "^FS"));
            Assert.Throws<ArgumentOutOfRangeException>(() => renderer.Render(request with { Quantity = 0 }));
            Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(result.Zpl, @"\^BX").Count);
            Assert.False(result.Zpl.Contains("^BQ"));
            Assert.Equal(result.Zpl, renderer.Render(request).Zpl);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Test]
    public static void QuantityDataMatrixPadsToSevenCharactersWithMaximumTenDigits()
    {
        foreach (var (quantity, expected) in new[] { (1m, "0000001"), (42m, "0000042"),
            (123456m, "0123456"), (1.5m, "00001.5"), (0.125m, "000.125"), (100.245m, "100.245"),
            (9999999999m, "9999999999"), (0.00211234m, "0.00211234") })
        {
            var payload = LabelValues.FormatQuantityDataMatrix(quantity);
            Assert.Equal(expected, payload);
            Assert.True(payload.Length >= 7 && payload.Count(char.IsAsciiDigit) <= 10);
            Assert.Equal(quantity, decimal.Parse(payload, CultureInfo.InvariantCulture));
        }
        Assert.Throws<ArgumentException>(() => LabelValues.FormatQuantityDataMatrix(10000000000m));
        Assert.Throws<ArgumentException>(() => LabelValues.FormatQuantityDataMatrix(0));
        Assert.Throws<ArgumentException>(() => LabelValues.FormatQuantityDataMatrix(0.1234567891m));
        Assert.Equal("1.5", LabelValues.FormatQuantity(001.5000m));
    }

    [Test]
    public static void SessionRandomCodeRemainsStableDuringRefresh()
    {
        var session = new OperationSession();
        var random = session.RandomCode;
        Assert.Equal(4, random.Length);
        Assert.True(random.All(char.IsAsciiLetterOrDigit));
        Assert.False(random.Any(char.IsAsciiLetterLower));
        session.Revalidate([]);
        Assert.Equal(random, session.RandomCode);
        session.Packaging = "Tray";
        Assert.Equal(random, session.RandomCode);
        session.NextLot();
        Assert.Equal(4, session.RandomCode.Length);
        Assert.True(session.RandomCode.All(char.IsAsciiLetterOrDigit));
    }
}
