Install **Oeps.RawMaterialSticker-0.1.6-setup-win-x64.msi** for a new installation. Existing installations can update through the OEPS desktop shortcut.

Changes:
- Added camera input through the supplied Scanner SDK, with saved Local computer / External server settings.
- Added a camera popup showing OEPS PN, MPN, lot and editable quantity. Enter prints and keeps scanning, Space copies the fields to the main window, and Escape leaves. Expensive components also print their warning label.
- Added a scanner-style beep for each accepted reading. The audio output stays active while the popup is open and follows changes to the default Windows output.
- Ignore only consecutive identical camera readings; scanning A, B, then A accepts and signals A again.
- Updated L3 to use one Data Matrix containing version, serial number, lot and quantity, protected by a CRC-32/IEEE checksum. L3 expensive is unchanged.
- Use hyphens in lots (MMYY-PACK-RAND), require quantity greater than zero, and pad encoded quantity to at least seven characters, including the decimal point when present.
- Enlarged camera field text and adjusted window sizing for the camera controls. The camera popup has a fixed size and cannot be minimized.

The MSI includes the Windows .NET runtime. The app ZIP and its SHA-256 file are used by the updater; the MSI checksum is also included. Existing user preferences and printer offsets are preserved.
