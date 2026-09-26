# Camera input

Start the scanner server, then select **Local computer** or **External** below Printer. For External, enter the server's IP address. Click **Update fields from camera** to connect and open the popup. The app connects only while this popup is open.

The camera mode and IP are saved in `%LOCALAPPDATA%\OEPS\RawMaterialSticker\user-settings.json` as `cameraExternal` and `cameraIp`. The SDK's example port, **8765**, is used by default. A different server port can be set with `cameraPort` in that same file while the app is closed. An older settings file defaults to Local computer. The packaged app includes both `Scanner.Client.dll` and `Scanner.Contracts.dll` from `client-sdk`.

## Reading and actions

The popup shows OEPS PN, MPN, Lot and Quantity. Only Quantity is editable; each new reading focuses and selects its quantity. Quantity must be greater than zero and satisfy the existing maximum of ten digits. Decimals are supported directly in this popup.

| Action | Result |
| --- | --- |
| **Update quantity and print (Enter)** | Submit L3 with the edited quantity, preserving the scanned PN and exact lot and recalculating the checksum. Submit L3 expensive too when the database marks the PN expensive. Keep the popup open for another reading. |
| **Update fields (Space)** | Copy the selected component, scanned lot and edited quantity into the main window and close the popup. This also sets the month/year zero checkboxes, packaging and decimal mode as needed. |
| **Leave (Escape)** or the window's close button | Close without copying fields into the main window. |

All close paths disconnect from the server, including closing during connection establishment. A print already being submitted must finish before closing. Printing from the popup leaves the main window's input values unchanged. Nothing prints automatically when a reading arrives.

A different JSON reading immediately replaces the current reading, including any unfinished quantity edit. A short, high-pitched scanner-style beep (2800 Hz, 100 ms) sounds when a valid reading updates the fields, independently of the Windows notification sound theme. Only consecutive identical JSON messages are ignored: A → A is ignored, but A → B → C → A displays A again. Pressing an action also allows the same reading again. During print submission, the confirmed values are fixed; the newest accepted reading received during submission is displayed, with its beep, when printing finishes. Holding Enter does not submit additional jobs.

Printing uses the selected Windows printer and the same per-label settings, routing and saved printer offsets as normal printing. Submission errors remain visible separately from new readings; check any uncertain or partially accepted job before retrying. Sample mode writes ZPL files instead of sending printer jobs.

## Component matching

The server JSON contains `label_version`, `oeps_pn`, `lot` and `quantity`; **it does not include MPN**. The app resolves MPN and expensive-item status from the downloaded component database:

- A PN with one MPN is selected automatically.
- A PN with multiple MPNs uses its PN/MPN pairing already selected in the main window. Otherwise, leave the popup, select that pairing, and reopen the camera popup.
- A missing PN cannot print or update fields. Update the database first.

Version 1 and the current `MMYY-PACK-RAND` lot format are supported. Scanned months and years may independently be `00`. Nonzero years must be from 2020 through the current year, matching the main window's choices. Unknown packaging codes, invalid dates and zero quantities are rejected rather than silently changed.

Connection failures are displayed in the popup. Close and reopen it to retry; the supplied SDK does not reconnect automatically. No camera drivers are needed on this client computer when using an external scanner server.

Opening the camera popup prepares the default Windows audio output and continuously streams silence between beeps, keeping the playback stream active rather than only retaining a device handle. Three small audio buffers carry each requested tone once; its completion is tracked until the final buffer returns from the driver. The app checks the default device and its endpoint identity every 250 ms while idle, and again before each beep. A change closes the old output and opens the new one; an active beep finishes before switching. Closing the popup stops the stream, releases the output and cancels pending beeps. A short silent lead and tail surround the 100 ms tone. Playback runs off the UI thread. Audio failures appear in the popup; monitoring and subsequent scans retry opening the output. The default Windows playback device and its volume still apply.

## Verification

For an audible check of five complete beeps through the actual Windows audio driver, run `dotnet run --project tests/Oeps.RawMaterialSticker.Tests -c Release -- --verify-sound`. This reports driver completion timing; it cannot establish whether the speaker physically produced sound.

The console test runner exercises the supplied SDK against a local WebSocket test server in both Local and External modes, validates camera data, matches components, checks duplicate suppression and round-trips camera settings. The `--sample --ui-smoke` check simulates cross-thread readings and checks immediate replacement, quantity focus, Enter/Space/Escape, expensive-label output, unchanged main inputs while printing, field import and disconnect during connection. It saves `ui-smoke-camera.png` alongside the existing screenshots. These checks do not access the actual camera or print physical labels.
