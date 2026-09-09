using System.Text;
using System.Text.RegularExpressions;
using Oeps.RawMaterialSticker.Core.Configuration;
using Oeps.RawMaterialSticker.Core.Printing;

namespace Oeps.RawMaterialSticker.Tests;

public static class PrintingTests
{
    private static LabelRenderer Renderer() => new(LabelRenderer.BuiltInTemplate);
    private static LabelRequest Request(string pn = "OEPS101234") => new(pn, "ABM8AIG-12.000MHZ-12-2Z-T3", 9, 2026, 4);

    [Test]
    public static void PnFormattingPreservesLettersAndZeros()
    {
        foreach (var (input, expected) in new[]
        {
            ("OEPS101234", "OEPS 10 1234"), ("OEPS-101234", "OEPS 10 1234"),
            ("OEPS-AB1234", "OEPS AB 1234"), ("OEPS-0012", "OEPS 00 12"),
            ("OEPSabC", "OEPS ab C"), ("OEPS001234567890", "OEPS 00 1234567890")
        })
            Assert.Equal(expected, LabelValues.FormatOepsPn(input));
        foreach (var invalid in new[] { "OEPS12", "OEPS-", "oeps123", " OEPS123", "OEPS123 ", "OEPS--123", "OEPS-AB_1", "OEPS12é", "OEPS123\n", "OTHER123" })
            Assert.Throws<ArgumentException>(() => LabelValues.FormatOepsPn(invalid));
    }

    [Test]
    public static void DateAndQuantityRequireValidOperatorValues()
    {
        Assert.Equal("0926", LabelValues.FormatDate(9, 2026));
        Assert.Equal("0100", LabelValues.FormatDate(1, 2000));
        Assert.Equal("1299", LabelValues.FormatDate(12, 2099));
        Assert.Throws<ArgumentOutOfRangeException>(() => LabelValues.FormatDate(0, 2026));
        Assert.Throws<ArgumentOutOfRangeException>(() => LabelValues.FormatDate(13, 2026));
        Assert.Throws<ArgumentOutOfRangeException>(() => LabelValues.FormatDate(9, 26));
        Assert.Throws<ArgumentOutOfRangeException>(() => LabelValues.FormatDate(9, 2100));
        Assert.Equal("", LabelValues.FormatQuantity(0));
        Assert.True(Renderer().Render(Request() with { Quantity = 0 }).Zpl.Contains("^FD^FS"));
        Assert.Throws<ArgumentOutOfRangeException>(() => LabelValues.FormatQuantity(-1));
    }

    [Test]
    public static void UnavailableDateUsesFourZerosForTextAndBarcode()
    {
        var request = Request();
        foreach (var unavailable in new[] { request with { MonthUnavailable = true, YearUnavailable = true }, request with { MonthUnavailable = true, YearUnavailable = true, Month = 0, Year = 0 } })
        {
            var rendered = Renderer().Render(unavailable);
            var fields = Fields(rendered.Zpl);
            Assert.Equal("0000", rendered.DateMmyy);
            Assert.Equal("0000", fields[2]);
            Assert.Equal("LA,0000_OTHR_0000", fields[7]);
            Assert.True(rendered.Zpl.Contains("^PQ1,0,1,Y"));
        }
        Assert.Equal("0926", Renderer().Render(request).DateMmyy);
        Assert.Throws<ArgumentOutOfRangeException>(() => Renderer().Render(request with { Month = 0 }));
    }

    [Test]
    public static void BarcodeEncodingChoosesUsefulNumericPairsAndPreservesPayload()
    {
        Assert.Equal(">:OEPS>5101234", Code128Encoder.Encode("OEPS101234").ZplFieldData);
        Assert.Equal(">;0926", Code128Encoder.Encode("0926").ZplFieldData);
        Assert.Equal(231, Code128Encoder.Encode("0926").WidthWithQuietZonesDots());
        foreach (var input in new[] { "OEPS-0012", "OEPS-AB1234", "OEPS12345AB67890", "OEPS001234AB", "12345678AB0012", "123A00", "OEPS-abc123", "0", "00" })
        {
            var encoded = Code128Encoder.Encode(input);
            Assert.Equal(input, DecodeCode128(encoded.ZplFieldData));
            Assert.Equal(input, encoded.Payload);
        }
        Assert.Throws<ArgumentException>(() => Code128Encoder.Encode("OEPS>51234"));
        Assert.Throws<ArgumentException>(() => Code128Encoder.Encode("OEPS^XZ"));
        Assert.Throws<ArgumentException>(() => Code128Encoder.Encode("OEPS~JA"));

        var random = new Random(153);
        const string alphabet = "0000111122223333ABC-";
        for (var iteration = 0; iteration < 300; iteration++)
        {
            var input = new string(Enumerable.Range(0, random.Next(1, 40)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
            Assert.Equal(input, DecodeCode128(Code128Encoder.Encode(input).ZplFieldData));
        }
    }

    [Test]
    public static void RenderedFieldsUseOneSelectedPairAndOneReceptionDate()
    {
        foreach (var input in new[] { "OEPS101234", "OEPS-101234", "OEPS-AB1234", "OEPS-0012" })
        {
            var result = Renderer().Render(Request(input));
            var fields = Fields(result.Zpl);
            Assert.Equal(input, fields[6]);
            Assert.Equal(LabelValues.FormatOepsPn(input), fields[0]);
            Assert.Equal(Request().Mpn, fields[1]);
            Assert.Equal("0926", fields[2]);
            Assert.Equal("4", fields[3]);
            Assert.Equal("OTHR", fields[4]);
            Assert.Equal("0000", fields[5]);
            Assert.Equal("LA,0926_OTHR_0000", fields[7]);
            Assert.Equal("0000004", fields[8]);
            Assert.Equal("0926", result.DateMmyy);
            Assert.Equal(input, result.BarcodePayload);
            Assert.True(result.Zpl.Contains("^PQ1,0,1,Y", StringComparison.Ordinal));
            Assert.False(result.Zpl.Contains("~JA", StringComparison.Ordinal));
            Assert.False(result.Zpl.Contains("^JUS", StringComparison.Ordinal));
            Assert.False(result.Zpl.Contains("{{", StringComparison.Ordinal));
        }
    }

    [Test]
    public static void ZplEscapingPreventsInjectedCommandsAndPreservesUtf8()
    {
        const string hostile = "Málaga^FS^XZ~JA\\26>{{MPN}}";
        var rendered = Renderer().Render(Request() with { Mpn = hostile, Quantity = 99 });
        Assert.Equal(hostile, Fields(rendered.Zpl)[1]);
        Assert.Equal("99", Fields(rendered.Zpl)[3]);
        Assert.Equal(1, Regex.Matches(rendered.Zpl, @"\^XA").Count);
        Assert.Equal(1, Regex.Matches(rendered.Zpl, @"\^XZ").Count);
        Assert.False(rendered.Zpl.Contains("~JA", StringComparison.Ordinal));
        Assert.False(rendered.Zpl.Contains("{{", StringComparison.Ordinal));
        Assert.True(rendered.Zpl.StartsWith("^XA\n^CI28", StringComparison.Ordinal));
        Assert.True(rendered.Bytes.All(b => b < 128));
        Assert.Equal("\\5E\\7E\\5C\\3E\\C3\\A9", LabelValues.EscapeField("^~\\>é"));
    }

    [Test]
    public static void RotatedBarcodeFitReservesQuietZonesWithoutChangingLayout()
    {
        var result = Renderer().Render(Request("OEPS-AB1234"));
        Assert.Equal(2, Regex.Matches(result.Zpl, @"\^BXB").Count);
        Assert.True(result.Zpl.Contains("^FT226,560^BXB,7,200", StringComparison.Ordinal));
        Assert.True(result.Zpl.Contains("^FT128,455^BQN,2,5", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => Renderer().Render(Request("OEPS123456789012")));
        Assert.Throws<ArgumentException>(() => new LabelRenderer(LabelRenderer.BuiltInTemplate.Replace("^PW354", "^PW900", StringComparison.Ordinal)));
        Assert.Throws<ArgumentException>(() => new LabelRenderer("~JA\n" + LabelRenderer.BuiltInTemplate));
        Assert.Throws<ArgumentException>(() => new LabelRenderer(LabelRenderer.BuiltInTemplate.Replace("{{MPN}}", "{{UNKNOWN}}", StringComparison.Ordinal)));
    }

    [Test]
    public static void ProductionRequiresValidatedQueueTemplateHardwareAndPrintSettings()
    {
        var renderer = Renderer();
        Assert.True(ProductionGuard.Validate(new PrinterConfiguration(), "", renderer, Request()).Count > 0);
        var config = ValidatedTestConfiguration(renderer);
        Assert.Equal(0, ProductionGuard.Validate(config, "TEST QUEUE", renderer, Request()).Count);
        var production = renderer.RenderProduction(Request(), config, "TEST QUEUE");
        Assert.True(production.Zpl.Contains("^MNW\n^MTT\n^PR4\n~SD25", StringComparison.Ordinal));
        Assert.True(production.Zpl.Contains("^PQ1,0,1,Y", StringComparison.Ordinal));
        Assert.False(production.Zpl.Contains("^JUS", StringComparison.Ordinal));
        Assert.False(production.Zpl.Contains("~JA", StringComparison.Ordinal));
        Assert.Equal(0, ProductionGuard.Validate(config, "OTHER QUEUE", renderer, Request()).Count);
        config.UseSelectedPrinter = false;
        Assert.True(ProductionGuard.Validate(config, "OTHER QUEUE", renderer, Request()).Count > 0);
        config.UseSelectedPrinter = true;
        Assert.True(ProductionGuard.Validate(config, "TEST QUEUE", renderer, Request("OEPS1234567890")).Count > 0);
        Assert.True(ProductionGuard.Validate(config, "TEST QUEUE", renderer, Request() with { Quantity = int.MaxValue }).Count > 0);
        Assert.True(ProductionGuard.Validate(config, "TEST QUEUE", renderer, Request() with { Mpn = new string('W', 35) }).Count > 0);
        config.ValidatedTemplateSha256 = new string('0', 64);
        Assert.Throws<InvalidOperationException>(() => renderer.RenderProduction(Request(), config, "TEST QUEUE"));
        config.ValidatedTemplateSha256 = renderer.TemplateSha256;
        config.MediaTracking = "web^XZ";
        Assert.True(ProductionGuard.Validate(config, "TEST QUEUE", renderer, Request()).Count > 0);
    }

    [Test]
    public static async Task DryRunSavesExactBytesWithoutPrinterAccess()
    {
        var path = Path.Combine(Path.GetTempPath(), "oeps-dry-run-test-" + Guid.NewGuid().ToString("N") + ".zpl");
        try
        {
            var rendered = Renderer().Render(Request());
            await DryRunWriter.SaveAsync(path, rendered.Bytes);
            var saved = await File.ReadAllBytesAsync(path);
            Assert.True(rendered.Bytes.SequenceEqual(saved));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static PrinterConfiguration ValidatedTestConfiguration(LabelRenderer renderer) => new()
    {
        Model = "TEST ONLY — no hardware validation claimed", Dpi = 300, Connection = "TEST ONLY",
        QueueName = "TEST QUEUE", ProductionValidated = true, ValidatedTemplateSha256 = renderer.TemplateSha256,
        PrintSpeedIps = 4, Darkness = 25, MediaTracking = "web", PrintMethod = "thermal-transfer",
        LongestValidatedOepsPn = "OEPS101234", ValidationNotes = "Synthetic unit-test configuration only."
    };

    private static string[] Fields(string zpl) => Regex.Matches(zpl, @"\^FD(.*?)\^FS")
        .Select(m => DecodeHex(m.Groups[1].Value)).ToArray();

    private static string DecodeHex(string value)
    {
        var bytes = new List<byte>();
        for (var i = 0; i < value.Length;)
        {
            if (value[i] == '\\')
            {
                bytes.Add(Convert.ToByte(value.Substring(i + 1, 2), 16));
                i += 3;
            }
            else
                bytes.Add((byte)value[i++]);
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    // Independent subset decoder checks that latches never consume an odd numeric digit
    // and that no invocation character becomes part of the scanner's value.
    private static string DecodeCode128(string field)
    {
        var inC = field.StartsWith(">;", StringComparison.Ordinal);
        Assert.True(inC || field.StartsWith(">:", StringComparison.Ordinal));
        var result = new StringBuilder();
        for (var i = 2; i < field.Length;)
        {
            if (field[i] == '>')
            {
                Assert.True(field[i + 1] is '5' or '6');
                inC = field[i + 1] == '5';
                i += 2;
            }
            else if (inC)
            {
                Assert.True(i + 1 < field.Length && char.IsAsciiDigit(field[i]) && char.IsAsciiDigit(field[i + 1]));
                result.Append(field, i, 2);
                i += 2;
            }
            else
                result.Append(field[i++]);
        }
        return result.ToString();
    }
}
