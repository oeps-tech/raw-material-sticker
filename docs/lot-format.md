# Lot and L3 format (September 25, 2026)

L3 has one Data Matrix and no QR codes. It prints the formatted PN, MPN, full
packaging name, full lot, and quantity using the updated `label/L3.prn` layout.
L3 expensive retains its existing template, PN text grouping, barcode and MPN.

Lots use `MMYY-PACK-RAND`, for example `0926-TRAY-Q5R2`. The user confirmed hyphens,
as recorded in the updated map. RAND is four random
uppercase ASCII letters or digits. It stays stable during edits and refreshes;
a successful print submission (or diagnostic dry run) generates the next suffix.
These short random codes do not guarantee global uniqueness.

| Packaging | Lot code |
| --- | --- |
| Reel | REEL |
| Tube | TUBE |
| Tape | TAPE |
| Bag | BAG |
| Box | BOX |
| Tray | TRAY |
| Spool | SPOL |
| Other | OTHR |

The packaging text prints the name as selected (`Bag`, `Tray`, etc.). Other is the
initial choice. Month/Year have independent `Use '00'` checkboxes: September 2026
becomes `0026` with unknown month, `0900` with unknown year, or `0000` with both
unknown. The full lot shown in the GUI is the same lot in print and the Data Matrix.

L3 follows the new map: ten-character PNs starting with a letter after OEPS use
`XXXX XXX XXX`; numeric ones use `XXXX XX XXXX`. Eleven-character PNs use
`XXXX XX XXXXX`, e.g. `OEPSA010123` becomes `OEPS A0 10123`. L3 expensive keeps its
previous grouping (`OEPS A01 0123` for the same PN). Barcode serial numbers keep
the original case and leading zeros, without display spaces.

Quantity must be greater than zero and is always printed. Human-readable quantity
has no padding or trailing fractional zeros: `1.500` prints `1.5`. The GUI's
`Use decimal number` option defaults off. It permits up to nine decimal places;
quantities are limited to ten digits, counting the zero before a decimal point.
The decimal point does not count as a digit. Unchecking decimal mode drops the
fractional part.

## Data Matrix version 1

The scanner receives `1*<serial_number>*<lot>*<quantity>*<checksum>`.
Encoded quantity has at least 7 characters, with leading zeros added as needed.
The decimal dot counts toward this minimum; the maximum remains 10 digits,
excluding the dot. There is no additional mandatory zero prefix.

| Entered quantity | Printed quantity | Encoded quantity |
| --- | --- | --- |
| 1 | 1 | 0000001 |
| 42 | 42 | 0000042 |
| 1.500 | 1.5 | 00001.5 |
| 100.245 | 100.245 | 100.245 |
| 0.00211234 | 0.00211234 | 0.00211234 |

The CRC-32/IEEE checksum is compatible with Python's
[`zlib.crc32`](https://docs.python.org/3/library/zlib.html#zlib.crc32). It covers the
exact ASCII bytes of `1*<serial_number>*<lot>*<encoded quantity>` before ZPL
escaping, preserving case and leading zeros. No spaces, newline or trailing `*`
are added. The unsigned CRC is eight uppercase hexadecimal characters.

User-supplied unpadded reference:
`1*OEPSA010123*0926-TRAY-Q5R2*1*D91C90D6`

Actual label after the map's quantity padding:
`1*OEPSA010123*0926-TRAY-Q5R2*0000001*65C0007E`

The Data Matrix uses `!` as its ZPL control escape character, which cannot occur in
the protocol, so literal data is not interpreted as an escape sequence. Variable
fields are byte-escaped with `^FH` after the checksum is calculated. Speed/darkness
remain controlled by packaged printer profiles; the exported initialization commands
are not imported. Local queue offsets continue to apply to the complete label.
