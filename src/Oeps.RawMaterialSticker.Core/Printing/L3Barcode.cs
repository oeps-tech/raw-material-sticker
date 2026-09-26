using System.Globalization;

namespace Oeps.RawMaterialSticker.Core.Printing;

public static class L3Barcode
{
    public const string Version = "1";

    public static string Encode(string serialNumber, string lot, string quantity)
    {
        foreach (var field in new[] { serialNumber, lot, quantity })
            if (string.IsNullOrEmpty(field) || field.Any(c => c is <= ' ' or > '~' or '*'))
                throw new ArgumentException("Barcode fields must be nonempty ASCII without spaces or '*'.");
        var payload = $"{Version}*{serialNumber}*{lot}*{quantity}";
        return payload + "*" + Checksum(payload);
    }

    // CRC-32/IEEE (reflected polynomial, initial/final XOR): Python zlib.crc32.
    // Checksum the exact ASCII data before ZPL escaping, without a final separator.
    public static string Checksum(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        uint crc = 0xffffffff;
        foreach (var c in payload)
        {
            if (c > 127) throw new ArgumentException("CRC input must contain ASCII bytes only.");
            crc ^= c;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
        }
        return (crc ^ 0xffffffff).ToString("X8", CultureInfo.InvariantCulture);
    }
}
