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
Dimensions/DPI and nonzero offsets outside the supported layout are rejected rather
than silently changing barcode geometry.
