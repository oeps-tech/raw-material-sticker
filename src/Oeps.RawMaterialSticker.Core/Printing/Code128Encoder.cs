using System.Text;

namespace Oeps.RawMaterialSticker.Core.Printing;

public sealed record EncodedBarcode(string Payload, string ZplFieldData, int CodewordCount)
{
    // Count contains the start and data/latch symbols; add checksum and 13-module stop.
    public int BarWidthDots(int moduleWidth = 3) => checked(((CodewordCount + 1) * 11 + 13) * moduleWidth);
    public int WidthWithQuietZonesDots(int moduleWidth = 3) => checked(BarWidthDots(moduleWidth) + 20 * moduleWidth);
}

/// <summary>Minimum-length Code 128 B/C encoding for the identifier alphabet, with explicit ZPL invocation codes.</summary>
public static class Code128Encoder
{
    public static EncodedBarcode Encode(string payload)
    {
        ArgumentException.ThrowIfNullOrEmpty(payload);
        if (payload.Length > 512 || payload.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("Barcode data supports ASCII letters, digits and hyphens only (maximum 512 characters).", nameof(payload));

        var length = payload.Length;
        var costB = new int[length + 1];
        var costC = new int[length + 1];
        var switchToC = new bool[length];
        var stayInC = new bool[length];
        bool IsPair(int index) => index + 1 < length && char.IsAsciiDigit(payload[index]) && char.IsAsciiDigit(payload[index + 1]);

        // Consume a character (B) or pair (C) on each transition, avoiding same-index latch cycles.
        for (var i = length - 1; i >= 0; i--)
        {
            costB[i] = 1 + costB[i + 1];
            if (IsPair(i) && 2 + costC[i + 2] < costB[i])
            {
                costB[i] = 2 + costC[i + 2];
                switchToC[i] = true;
            }
            costC[i] = 2 + costB[i + 1];
            if (IsPair(i) && 1 + costC[i + 2] <= costC[i])
            {
                costC[i] = 1 + costC[i + 2];
                stayInC[i] = true;
            }
        }

        var inC = costC[0] < costB[0];
        var codewords = 1 + (inC ? costC[0] : costB[0]);
        var result = new StringBuilder(inC ? ">;" : ">:");
        for (var i = 0; i < length;)
        {
            if (inC && stayInC[i])
            {
                result.Append(payload, i, 2);
                i += 2;
            }
            else if (!inC && switchToC[i])
            {
                result.Append(">5").Append(payload, i, 2);
                inC = true;
                i += 2;
            }
            else
            {
                if (inC)
                    result.Append(">6");
                inC = false;
                result.Append(payload[i++]);
            }
        }
        return new EncodedBarcode(payload, result.ToString(), codewords);
    }
}
