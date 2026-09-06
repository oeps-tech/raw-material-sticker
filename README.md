# OEPS Raw Material Sticker

Windows Forms application for selecting an OEPS PN–MPN pairing from the public component database and printing its Zebra label. Parts marked expensive also receive the supplied **L3 expensive** warning label. C# / .NET 10, Windows x64, code-defined UI. Quantity means components in the package; each label format contains `^PQ1,0,1,Y`.

## Try it in this repository

Double-click **Run.cmd**. The current workspace has a local .NET 10 SDK under `.tools/dotnet`; the script uses it when present. The script starts the built Release app in **dry-run mode** with the real public spreadsheet. After source changes, rebuild using the commands below: Run.cmd reuses an existing Release build.

```powershell
.\Run.cmd
.\Run.cmd --sample
```

The compact form keeps the printer, search mode, component, selected OEPS PN/MPN, reception date and quantity together. Suggestions open beneath the component field while typing; use Down/Up and Enter or click a suggestion to select its exact PN/MPN pairing. The date row shows the resulting printed MMYY. Enter a positive quantity, then select **Save dry run**. The status shows the saved `.zpl` path. A printer is unnecessary for a dry run. Sample mode is explicitly marked and permanently disables production for that session.

`--data-dir <absolute-directory>` isolates cache, settings and dry runs for testing. Normal use stores them in `%LOCALAPPDATA%\OEPS\RawMaterialSticker`. For example, `Run.cmd --sample --data-dir C:\path\to\repo\.local\demo` keeps the demonstration's generated data inside this repository. The application never writes receptions, inventory or print records to Google Sheets.

**Production is disabled by default.** Printer model, DPI, connection, Zebra software edition, physical text fit and barcode scans have not been verified. The form has no label preview; the supplied printing template is unchanged. See [the template and physical-validation notes](templates/README.md).

Check **Not available** beside the reception-date hint when the date is unknown. The hint, printed date text and date barcode all become **0000**. Month/year controls are disabled while checked, and their previous values return when unchecked. Database refreshes preserve this choice; it starts unchecked on a fresh launch.

To view a dry-run label, press Win+R and open `%LOCALAPPDATA%\OEPS\RawMaterialSticker\dry-runs` (or the path shown after saving if using `--data-dir`). Open the latest `.zpl` file in a text editor, copy its contents into the [Labelary ZPL viewer](https://labelary.com/viewer.html), and select **Redraw**. This uses an external rendering service. Match the viewer density to the printer when known. For a viewing example at 8 dots/mm, the supplied 354 × 591 dot layout is 44.25 × 73.875 mm; that example is not a confirmation of the physical printer's resolution. The app itself saves commands, not a rendered image.

## Develop, run and verify

Use a supported x64 Windows installation and the .NET **10 SDK**. A machine that only has .NET 6 or 8 cannot build this solution. Microsoft documents the [Windows SDK and Desktop Runtime installation options](https://learn.microsoft.com/en-us/dotnet/core/install/windows). Visual Studio is unnecessary; install VS Code's C# Dev Kit for debugging.

```powershell
dotnet build Oeps.RawMaterialSticker.sln -c Release
dotnet run --project tests/Oeps.RawMaterialSticker.Tests -c Release
dotnet run --project src/Oeps.RawMaterialSticker.App -- --dry-run
dotnet run --project src/Oeps.RawMaterialSticker.App -- --sample
```

In this workspace substitute `.\.tools\dotnet\dotnet.exe` for `dotnet`, or add the absolute `.tools\dotnet` directory to your terminal's PATH. `global.json` accepts stable .NET 10 feature-band SDKs. VS Code includes build/debug configurations in `.vscode/`; they use `dotnet` from PATH. `Run.cmd` does not require a PATH change.

The focused verification project is a dependency-free console test runner; run it with `dotnet run`, **not `dotnet test`**. It exits nonzero on any failed test and covers strict CSV/UTF-8 validation, cache preservation and offline startup, fake-clock five-minute scheduling, concurrent refresh suppression, exact pairing selection, PN display/encoding roundtrips, dates, quantity, field escaping, print guards, semantic versions, hostile ZIPs, checksums, immutable staging and recovery state. It also verifies that refresh preserves operator inputs and a fresh session has no quantity.

Optional integration checks (use separate empty test directories):

```powershell
dotnet run --project tests/Oeps.RawMaterialSticker.Tests -c Release -- --verify-live .local/live-check
dotnet run --project src/Oeps.RawMaterialSticker.App -c Release -- --sample --ui-smoke --data-dir .local/ui-check
dotnet run --project tests/Oeps.RawMaterialSticker.Tests -c Release -- --verify-package artifacts/Oeps.RawMaterialSticker-0.1.0-win-x64.zip .local/package-check
```

The UI check opens a hidden sample window, saves a ZPL job, checks input behavior, writes `ui-smoke.txt` and `ui-smoke.png`, then closes. Close an already-running OEPS instance before running it: single-instance protection applies to test runs too. Package verification checks the real ZIP, launches its app, waits for its ready signal and records the working version. No physical print is issued. See [manual checks](docs/manual-verification.md) for hardware and Windows behavior that automation cannot establish.

## Spreadsheet and cache

Configured source: [OEPS spreadsheet](https://docs.google.com/spreadsheets/d/1c0Wh_HY_bz6y2l1Jr0D6rPmvKgyRPSEPphRE5ZAqndk/edit), tab **components list**. Anonymous CSV access was verified. This tab is currently headerless: A is Description, B is OEPS PN, D is MPN. Its configured Google Visualization CSV query is:

```sql
select A,B,D where B is not null and D is not null
label A 'Description', B 'OEPS_PN', D 'MPN'
```

`headers=0` preserves the first data row. The adapter gives the app a named-header CSV without changing the sheet. Rows missing either required identifier are deliberately excluded at the source; all complete pairings are downloaded. Values such as `-` or a URL that the sheet uses as an MPN remain visible as supplied, and production text-fit checks may reject them. This query is the only column-letter mapping; the parser itself reads configured header names, never column positions. If columns move or a header row is added, update the source URL accordingly.

Keep the sheet shared as **Anyone with the link → Viewer**; only team members need edit rights. Check the CSV URL in a private browser without signing in. A login or HTML error page is rejected. No Google credentials or OAuth are used. For another source, configure `spreadsheetCsvUrl` as a public HTTPS CSV export with `OEPS_PN` (or `OEPS PN`) and `MPN` headers; `Manufacturer` and `Description` are optional. `headerAliases` can supply other exact header names. No fabricated IDs are included.

At startup the last valid cache loads immediately and a full asynchronous download begins. The in-app timer checks every second whether the next full refresh is due, so resuming from sleep triggers an overdue refresh. Downloads run at startup, every **300 seconds**, and on **Update database**. A manual attempt restarts the five-minute countdown; failures keep the schedule alive. Only one refresh runs at a time.

Identifiers remain strings, preserving case, spelling and leading zeros. Identical pairs are deduplicated; alternate MPNs remain separate. CSV parsing supports quoted commas, quotes, newlines and UTF-8. A malformed, headerless or empty downloaded dataset is rejected in full. Only a complete validated snapshot atomically replaces the disk cache and in-memory data. On failure the footer shows the error and last valid cache age. Without any valid cache, component printing remains unavailable. Sample data is never substituted automatically or written into the real cache.

## Expensive components

The headerless **expensive components** tab lists OEPS PNs in column **A**. Its public CSV adapter selects A, labels it `OEPS_PN`, and uses `headers=0` so the first listed PN is retained. `expensiveSpreadsheetCsvUrl` in `appsettings.json` contains the actual configured export URL.

Both tabs refresh at startup, every 300 seconds, and on **Update database**. Only after both downloads validate does the app atomically save and activate the combined snapshot. If either download fails, the previous complete data and expensive flags remain available offline. Older caches still allow searching but require one successful two-tab refresh before printing. A CSV with the adapter header and no rows represents an intentionally empty expensive list; blank or malformed responses are rejected.

Matching compares complete identifiers, ignoring case and surrounding whitespace, while preserving their spelling and leading zeros. An optional hyphen remains significant. Every MPN pairing for a listed PN is marked expensive. The read-only **OEPS PN** field appends exactly ` - EXPENSIVE ITEM💰`; this UI annotation never changes a printed PN or barcode payload.

For an ordinary part, Print Sticker submits one label. For an expensive part, it builds the standard label **followed by L3 expensive** and submits both in one RAW job. Both labels use the selected PN/MPN. The supplied warning graphic, ATTENTION caption, Very Expensive caption, positions and fonts are retained. A missing expensive template prevents submission for an expensive part. If a job is interrupted, inspect whether one or both labels printed before retrying; there is no automatic retry.

Dry runs contain the same one or two complete ZPL formats in a single `.zpl` file. In a viewer, choose **Show Label 2** to inspect the expensive label. `--sample` includes an expensive `OEPS101234` with two MPN options and ordinary sample parts for comparison.

The extra template is `templates/expensive-label.zpl`, derived from `label/L3 expensive.prn`; the original PRN/NLBL files remain unchanged. Before production use, physically validate the extra label on the configured printer and set `printer.validatedExpensiveTemplateSha256` to its SHA-256. Existing normal-label validation alone does not enable the extra template. `printer.expensiveTemplatePath` configures its location.

## Configuration and production printing

Packaged `appsettings.json` contains your spreadsheet and GitHub coordinates. User overrides in `%LOCALAPPDATA%\OEPS\RawMaterialSticker\appsettings.json` merge over packaged values and survive updates. Use [config.example.json](config.example.json); all missing hardware values are marked. Preferences live separately in `user-settings.json`. Printer, search mode and window placement persist; quantity never persists. The default window is now 620 × 500 logical client pixels. An existing saved window at the previous 740 × 650 default adopts that size once; other saved sizes are preserved within the supported bounds. A missing saved printer is reported and another queue is never selected silently.

The original `label/L3.prn` and `label/L3.nlbl` are preserved. The final PRN format block matches the supplied brief's L3(2) layout with two native Code 128 barcodes. `templates/production-label.zpl` retains its 354 × 591 dot geometry, positions, orientation, fonts, fixed captions and one-label command. Physical dimensions depend on the actual DPI.

Only uniquely named field placeholders are replaced. PN display accepts `OEPS`, an optional following hyphen and at least three alphanumeric suffix characters; barcode payload preserves the exact database identifier. Code 128 B/C selection handles numeric pairs and letters correctly. The renderer deliberately uses UTF-8 (`^CI28`) and hex-escaped field bytes. Setup cancellation/save commands and the unusual source preamble are excluded. Production emits only explicit configured media/ribbon, speed and darkness settings. It sends RAW bytes to the selected Windows queue with partial-write handling and proper spooler cleanup.

Reception years are restricted to **2000–2099** because the printed MMYY encodes only two year digits. Quantity is a positive 32-bit integer; the unchanged production text field additionally limits it to seven digits. Production MPN text is conservatively limited to 34 printable ASCII characters without backslashes until another layout/font is implemented and validated. Barcode fit is checked in printer dots including quiet zones; this is not proof of scannability.

After physical validation, record the exact model/DPI/queue/connection, media tracking, print method, speed, darkness, widest validated PN and validation notes. Calculate the **exact template file's** SHA-256 with `Get-FileHash templates/production-label.zpl -Algorithm SHA256`, put it in `validatedTemplateSha256`, then set `productionValidated=true`. A layout change requires updating the supported template in code and revalidating, not just editing the file. Uncheck Dry run in the app to use the production transport once all guards pass. Sample mode cannot be unlocked this way.

The app reports **Sent to printer** only after spooler submission succeeds. That means Windows accepted the job; inspect the physical sticker and scan both barcodes. A failed or uncertain job is never retried automatically. Printing is disabled during submission, and closing the window is deferred until it finishes.

## Build an installer and update package

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1 -Version 0.1.0
```

The script builds and runs the tests, publishes framework-dependent Windows x64 app/launcher binaries, includes templates and original label sources, and creates:

- `artifacts/Oeps.RawMaterialSticker-0.1.0-win-x64.zip` and `.zip.sha256`: ordinary app update.
- `artifacts/Oeps.RawMaterialSticker-0.1.0-setup-win-x64.zip` and `.zip.sha256`: initial installer bundle.

Staging remains under `artifacts/package-<unique-id>` for inspection. Existing versioned artifacts are not overwritten. Package generation requires no external dependencies beyond the SDK's framework/runtime targeting assets; publishing may restore those assets from Microsoft's NuGet source.

On another PC, extract the **entire setup ZIP**, then run **Install.cmd**. It installs per-user shortcuts and a separate launcher under application data. The shortcut targets Windows PowerShell's native bootstrap, which runs before .NET 10 is available. On each start it detects the x64 .NET 10 Desktop Runtime; if absent it offers a per-user installation from official Microsoft metadata and verifies the runtime archives' SHA-512. It installs both the base runtime/host and Desktop Runtime. No administrator account is required. A refused or failed installation displays a useful error and can be retried by starting the shortcut again. Environments that prohibit unsigned PowerShell scripts can deploy the Microsoft Desktop Runtime through IT and review/sign these bootstrap scripts.

The SDK and normal update packages do **not** bundle .NET. The one-time runtime download is larger; subsequent ordinary app updates are small. A private runtime is **not automatically serviced by this app**. Prefer an IT-managed system Desktop Runtime for normal security patch servicing, or periodically update/reinstall the private runtime using Microsoft's documented process. Future .NET-major or architecture changes are rejected by the old launcher with an instruction to use the new full installer; the previous app remains available. A new major release must ship an updated native bootstrap and launcher. Launcher/bootstrap changes also require the full installer.

## Software releases and recovery

The desktop shortcut starts a separate launcher. It first activates an existing operator window, otherwise checks the public [oeps-tech/raw-material-sticker](https://github.com/oeps-tech/raw-material-sticker) latest **published stable GitHub Release**. Drafts, prereleases and older/equal semantic versions are ignored; a leading `v` is supported. It expects the exact versioned package/checksum naming above. No GitHub token is needed for ordinary app reading. API rate limits or missing releases fall back to the last working version or bundled installer package.

Packages download over HTTPS, have bounded sizes, are checked against the release SHA-256, and are extracted with path/link/duplicate/size checks into a fresh version directory. The manifest and required app/template files must match the release and runtime. Existing executables are never overwritten. A per-user launcher lock prevents concurrent update sessions. The app signals readiness when its main window is shown; only then is the version marked working in `update-state.json`. The previous version and state backup remain available if startup exits early. If startup stalls while the process is still alive, the launcher preserves recovery state and asks the operator to close that window and Retry; it does not force-kill a possible print job. Version directories are retained rather than automatically pruned.

The checksum detects corruption; **it does not authenticate the publisher**. Authenticode signing of binaries/bootstrap and/or separately signed update metadata are optional production improvements. Protect release-writing access. The app's optional 30-minute release check only displays **Update available — restart to install**; close the app and use its desktop shortcut to install. It never downloads executables on the five-minute database schedule.

The Windows workflow `.github/workflows/release.yml` builds/tests/packages on PRs, main pushes, and manual runs, and uploads reviewable artifacts. A deliberate stable tag such as `v0.1.0` additionally creates a published GitHub Release with the ZIPs and checksums. Tag pushes are the publication step; no repository, commit, tag, push or release was created by this implementation. Review the artifacts first. When authorized to release, commit the reviewed code, push it, then create/push the stable tag. `GITHUB_TOKEN` is used only by the CI publication job.

## Source layout

- `src/Oeps.RawMaterialSticker.Core`: component/cache/configuration, operator state, label renderer and update services.
- `src/Oeps.RawMaterialSticker.App`: compact code-defined WinForms UI and Windows RAW print transport.
- `src/Oeps.RawMaterialSticker.Launcher`: independent startup/update/recovery interface.
- `installer`: native PowerShell bootstrap and per-user setup.
- `templates`, `label`: variable production template and untouched original source files.
- `tests`, `scripts`, `.github`: focused verification, packaging and deliberate release workflow.
