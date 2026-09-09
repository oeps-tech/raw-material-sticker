# Lot and revised L3 layout

Lots use `MMYY_PACK_RAND`. September 2026 with Tray packaging could print
`0926_TRAY_t8sQ`, matching the label map and its `Lot_MMYY` object.

| Packaging | Printed code |
| --- | --- |
| Reel | REEL |
| Tube | TUBE |
| Tape | TAPE |
| Bag | _BAG |
| Box | _BOX |
| Tray | TRAY |
| Spool | SPOL |
| Other | OTHR |

Codes preserve the supplied mapping exactly: Bag and Box create two consecutive
underscores in the complete lot. Spool uses the corrected four-character code SPOL. RAND contains
four randomly generated ASCII letters or digits, preserving case. It stays
stable during input changes and database refreshes. A successful print submission
(or diagnostic dry run) generates the next suffix; a failed submission preserves it.
These short random codes do not guarantee global uniqueness.

Separate `Use '00'` checkboxes below Month and Year disable their corresponding
selector and replace only that segment with `00`. For September 2026, unknown
month prints `0026`, unknown year prints `0900`, and both unknown print `0000`.
Unchecking restores the previous selection. Year choices run from 2020 through
the computer's current year, inclusive. Packaging and RAND remain present. The
preview and printed QR use the same complete lot. Other is the initial packaging
choice.

The revised L3 template comes from the supplied exports in `label/`. It contains
the raw OEPS PN in a Data Matrix, a formatted PN and MPN, the complete lot in a
QR code, separate date/packaging/random text, quantity text and a quantity Data
Matrix. Numeric PNs such as `OEPS011234` print as `OEPS 01 1234`;
`OEPSA011234` prints as `OEPS A01 1234`. The shorter letter-prefixed form
`OEPSA01123` prints as `OEPS A01 123` (a letter followed by five digits after OEPS).
Both labels use these text formats; barcode payloads retain the original PN.
Quantity accepts up to three decimal places in the GUI and prints
with a decimal dot when the initially unchecked `Use decimal number` option is
selected. Otherwise only whole numbers are displayed and accepted. Unchecking
the option drops the fractional part of an entered quantity.

Zero leaves quantity text blank and omits the quantity Data Matrix entirely.
Positive quantities retain their required `0` prefix and receive further leading
zeros until the payload contains at least seven characters, including the decimal
dot when present. Examples: `42` becomes `0000042`, `1.5` becomes `00001.5`, and
`100.245` becomes `0100.245`. Quantity text is not padded.

L3 expensive retains its PN barcode, formatted PN and MPN, with positions from the
updated export. It still follows L3 when the selected component is expensive.
Printer profiles retain their separate speed, darkness, routing and media settings.
