# Label printer profiles

`production-label.json` configures L3. `expensive-label.json` configures L3 expensive.
Both use Zebra ZD421, 300 DPI, 30 mm width × 50 mm feed length, network,
thermal-transfer ribbon, gap sensing (`web`), 2 inches/s, and zero offsets.
Darkness is 23 for L3 and 22 for L3 expensive.

With `useSelectedPrinter: true` (the default), both labels follow the GUI printer
dropdown, allowing different company queue names. To route L3 expensive to a
dedicated printer, set its `useSelectedPrinter: false` and its own `queueName`.
Use a compatible 300 DPI ZPL printer with the configured media. Each label is a
separate RAW job with its own setup commands. All required formats validate before
submission. If a later submission fails, the status identifies earlier accepted
jobs; do not retry the whole operation without checking physical output.

The profiles are packaged beside the ZPL templates and are authoritative for each
release. App updates load the new release's profiles, including speed, darkness,
and queue settings. Local profile files and profile-path overrides are ignored.
Profile values override the legacy shared `printer` section of appsettings.json.

`graphicOption` records Photo/Clipart export preferences. These are driver image
conversion settings, not printer ZPL commands. Existing ZPL graphics are already
encoded; changing this value does not reprocess them. Re-export the graphic if a
different conversion is required and physically validate the revised template.

Production printing is enabled following the user's earlier printer tests. The
September 9 layouts use the newly supplied label exports; these revisions still
need a physical print and scan check. Each profile records its current template
hash in `validatedTemplateSha256` and its validation history in `validationNotes`.
Dry runs include both labels' actual setup commands, without submitting to a printer.
Dimensions/DPI outside the supported layout are rejected. Local printer corrections
are configured through the cogwheel beside the queue selector, in millimetres
(-10 to +10 on each axis). Save writes immediately to `printerOffsets` in
`%LOCALAPPDATA%\OEPS\RawMaterialSticker\user-settings.json`, keyed by Windows queue
name. Cancel leaves saved values unchanged; Reset to zero takes effect on Save.
Settings survive application updates. Renaming a queue requires configuring its
new name. Both labels use the correction belonging to their actual destination.

Local offsets add to the release profile's offsets, rounded to whole printer dots.
X positive moves right; Y positive moves down in the printer's label coordinates,
independent of rotated text. ZPL uses the inverse X value in `^LS` and Y in `^LT`;
the template's `^LS0` is removed so it cannot cancel the correction. Every format
sets both offsets explicitly, including zero, to avoid retaining a previous queue's
correction. Combined offsets must fit X ±9999 dots and Y ±120 dots.
See Zebra's [ZPL programming guide](https://www.zebra.com/content/dam/support-dam/en/documentation/unrestricted/guide/software/zpl-zbi2-pg-en.pdf)
for `^LS` (shift left) and `^LT` (label top). Check physical alignment after changing
offsets; moving content beyond the media edges can clip it.
