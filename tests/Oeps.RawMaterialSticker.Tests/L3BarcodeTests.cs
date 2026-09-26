using Oeps.RawMaterialSticker.Core.Printing;

namespace Oeps.RawMaterialSticker.Tests;

public static class L3BarcodeTests
{
    [Test]
    public static void CrcMatchesZlibAndPreservesExactData()
    {
        Assert.Equal("00000000", L3Barcode.Checksum(""));
        Assert.Equal("CBF43926", L3Barcode.Checksum("123456789"));
        Assert.Equal("D91C90D6", L3Barcode.Checksum("1*OEPSA010123*0926-TRAY-Q5R2*1"));
        Assert.Equal("1*OEPSA010123*0926-TRAY-Q5R2*1*D91C90D6", L3Barcode.Encode("OEPSA010123", "0926-TRAY-Q5R2", "1"));
        Assert.Equal("1*OEPSA010123*0926-TRAY-Q5R2*000001*08B4F8D9", L3Barcode.Encode("OEPSA010123", "0926-TRAY-Q5R2", "000001"));
        Assert.False(L3Barcode.Checksum("abc") == L3Barcode.Checksum("ABC"));
        Assert.False(L3Barcode.Checksum("1") == L3Barcode.Checksum("01"));
        Assert.Throws<ArgumentException>(() => L3Barcode.Checksum("é"));
        Assert.Throws<ArgumentException>(() => L3Barcode.Encode("OEPS 01 0123", "0926-TRAY-Q5R2", "1"));
        Assert.Throws<ArgumentException>(() => L3Barcode.Encode("OEPS010123", "0926-TRAY-Q5R2\n", "1"));
        Assert.Throws<ArgumentException>(() => L3Barcode.Encode("OEPS010123", "0926-TRAY-Q5R2", "1*FAKE"));
    }
}
