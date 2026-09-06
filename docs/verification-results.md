# Verification performed — 6 September 2026

Expensive-item follow-up: the Release build is clean and **37 tests pass**. Live anonymous integration loaded 1,060 pairings and matched **13 expensive OEPS PNs**, then verified their persisted-cache status and a manual two-tab refresh. The hidden UI check passed the exact display suffix, two-label job creation, and existing date/input behavior. Both saved sample dry runs contain exactly two `^XA` formats and two one-copy commands. The supplied warning graphic and variable field encoding are covered by tests. Physical output of the extra label remains unverified and requires its own template hash in production configuration. Use the rebuilt app via Run.cmd; earlier installer ZIPs contain the previous implementation.

Compact UI follow-up: the default window is now 620 × 500 with no label preview, and autocomplete overlays the fields. The updated Release build has no warnings/errors and all **32 tests pass**. The hidden UI check verifies arrows/Enter/Escape, dropdown placement, dry-run saving, and the new **Not available** date option. Both printed date fields decode to `0000`; refresh preserves the option, and unchecking restores the entered month/year. Both the normal form and autocomplete captures were inspected. Earlier installer artifacts listed below contain the initial layout; use the rebuilt app via Run.cmd for these changes, or create a new versioned package.

- Repository-local official .NET SDK 10.0.400, Windows x64.
- Release solution build: **0 warnings, 0 errors**.
- Focused console tests: **31 passed, 0 failed**. Includes 300 generated PN barcode encoding roundtrips within the printing tests.
- Real anonymous spreadsheet integration: **1,060 distinct complete PN/MPN pairings**, immediate persisted-cache startup, successful manual refresh, and a five-minute next-refresh schedule.
- Hidden WinForms check: explicit multiple-MPN selection, date/quantity entry, dry-run ZPL output, refresh input preservation, selection invalidation after editing, and sample-mode production lock all passed. A rendered window capture was inspected.
- Final real update ZIP: SHA-256, safe extraction, required files and manifest checks passed. Its packaged app started, signalled ready, completed the UI dry run, and was recorded/reloaded as the working installed version in an isolated repository test directory.
- Installer and packaging PowerShell scripts: syntax checks passed. Runtime discovery was checked with the local .NET 10 host and a missing path.
- App update and initial installer ZIPs plus SHA-256 files are in `artifacts/`. Original label files have no changes.

The default Debug build initially encountered a Bitdefender file lock. The Release build and final packaged executable succeeded. Anonymous HTTP checks required execution outside the tool sandbox because the sandbox blocked TLS; the application's normal Windows HTTP client then succeeded.

Not physically verified: Zebra output, scanner results, real spool submission, device fonts/media/DPI, monitor scaling beyond the captured window, Windows sleep/resume, a fresh-account runtime download/installation, and a real tagged GitHub Release rollout. See [manual acceptance checks](manual-verification.md). No repository publication or physical print was performed.
