Install **Oeps.RawMaterialSticker-0.1.4-setup-win-x64.msi** for a new installation or to upgrade the installer/launcher. The MSI bundles the Windows .NET runtime and does not use PowerShell during installation or app startup.

Existing installations can download the app update through the OEPS desktop shortcut. The `win-x64.zip` and its `.sha256` file are used by the updater. Updating the app alone does not replace an older installer/launcher; use the MSI to migrate from the previous PowerShell-based installation.

Changes:
- Production printing follows the Windows printer selected in the GUI, with separate packaged settings for L3 and L3 expensive.
- Updated L3 layout with PN Data Matrix, lot QR, and quantity Data Matrix; updated L3 expensive positions.
- Lots use `MMYY_PACK_RAND`, packaging options, and independent month/year “Use '00'” checkboxes. Years run from 2020 through the current year.
- Quantity defaults to whole numbers, with optional decimal entry. Zero omits the quantity text and Data Matrix; nonzero Data Matrix payloads are padded to at least seven characters, including the decimal point.
- PN text supports `OEPS 01 1234`, `OEPS A01 1234`, and `OEPS A01 123` on both labels while preserving unspaced barcode values.
- Improved window scaling and an update prompt showing installed and available versions.

The MSI installation was tested on two computers, including one running Bitdefender. The revised label layouts still need a physical print and scan check on the target printer.
