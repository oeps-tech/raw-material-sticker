Install **Oeps.RawMaterialSticker-0.1.5-setup-win-x64.msi** for a new installation. Existing installations can update through the OEPS desktop shortcut.

Changes:
- Added a cogwheel beside the printer selector to configure X/Y offsets in millimetres. Offsets save per Windows printer queue in local user settings and survive app updates. Each label uses its destination printer's correction.
- Added Save, Cancel and Reset to zero controls for printer offsets.
- Updated L3 with the two divider lines from the revised label export.
- Increased minimum window height to 610 pixels so the status and report remain visible; smaller saved windows expand automatically.
- Widened Quantity, added a subtle separator above the component details, and matched the cogwheel height to the printer field.
- Simplified the update reminder.

The MSI includes the Windows .NET runtime. The app ZIP and its SHA-256 file are used by the updater; the MSI checksum is also included.
