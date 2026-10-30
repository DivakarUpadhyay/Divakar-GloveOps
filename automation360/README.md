# Automation 360 bot — `GloveOps_SampleRequestEntry`

**Story:** the sales team receives sample requests in a spreadsheet. Instead of typing 20 rows by hand, an RPA (Robotic Process Automation) bot enters each row into the GloveOps web form, the way a person would. This is how you'd automate a system **without an API**, such as SAP GUI screens or a vendor portal.

Input: [`SampleRequests.csv`](SampleRequests.csv). It has 20 rows, and row 17 is deliberately invalid (qty 0) to show error handling.

## 1. Set up (one time, free)
1. Sign up for **Automation Anywhere Community Edition** (cloud Control Room).
2. In the Control Room, install the **Bot Agent** on your PC and register the device.
3. Install the **Automation Anywhere Chrome extension** (Control Room prompts you).
4. Start GloveOps locally (`start-gloveops.bat`), open http://localhost:5080 in Chrome, and sign in as **Anna (Sales)** once.
5. Optional: create a **Credential Vault** locker with the GloveOps URL and user name. This shows you keep secrets out of the bot.

## 2. Bot design (Task Bot)

| # | Action (A360 package) | Details |
|---|---|---|
| 1 | **CSV/TXT: Open** | File = `SampleRequests.csv`, header = yes, session `csv` |
| 2 | **Browser: Open** | Chrome, URL `http://localhost:5080/SampleRequests/Create` (or attach to the open tab) |
| 3 | **Loop: For each row in CSV/TXT** | session `csv`, record variable `rRow` |
| 4 | **Error handler: Try** | everything below goes inside Try |
| 5 | **Browser: Go to URL** | `http://localhost:5080/SampleRequests/Create` |
| 6 | **Browser: Run JavaScript** | fill the form by element **ID** (see the script below) |
| 7 | **Recorder: Capture → Click** | button `id=btn-save-submit` |
| 8 | **Delay / Wait for** | wait until the element `id=request-no` exists (max 10 s) |
| 9 | **Recorder: Capture → Get property** | `id=request-no`, inner text → `$sRequestNo$` |
| 10 | **Data table / CSV: Write row** | write `RowNo, Success, $sRequestNo$` to `Results.csv` |
| 11 | **Error handler: Catch** (AllErrors) | **Screen: Capture window** → `error_row_$rRow[0]$.png`; write `RowNo, Failed, $error message$` to `Results.csv` |
| 12 | *(end loop)* | |
| 13 | **Email: Send** | "GloveOps bot finished: X success / Y failed", attach `Results.csv` |
| 14 | **CSV/TXT: Close**, **Browser: Close** | always clean up |

### JavaScript for step 6 (fill by ID: robust to layout changes)
```javascript
// $rRow[1]$ = CustomerId, [3] = ProductId, [5] = Size, [6] = Qty, [7] = RequiredDate, [8] = Purpose
document.getElementById('CustomerId').value   = '$rRow[1]$';
document.getElementById('ProductId').value    = '$rRow[3]$';
document.getElementById('Size').value         = '$rRow[5]$';
document.getElementById('Qty').value          = '$rRow[6]$';
document.getElementById('RequiredDate').value = '$rRow[7]$';
document.getElementById('Purpose').value      = '$rRow[8]$ [A360 bot]';
```
> Alternative without JavaScript: use **Recorder → Set text** on `#Qty` and `#Purpose`, and **Select item by text** on the dropdowns using the `CustomerText` / `ProductText` columns. Typing into a date picker depends on Chrome's locale, which is why the bot uses JavaScript for the date.

### Detecting a failed row
When validation fails (row 17), the page stays on the form and shows `.alert-danger`. Step 8's wait for `#request-no` times out, so control jumps to **Catch**, which takes a screenshot and logs "Failed", and the loop continues with the next row.

## 3. Run it
- **Attended:** click Run in the Bot Editor while watching Chrome.
- **Unattended:** schedule it in the Control Room (e.g. every weekday at 09:00). In production this runs on a dedicated bot VM (Virtual Machine) with a service account.

Check the result in GloveOps → *Sample requests* (filter "Mine only" as Anna): 19 new requests marked `[A360 bot]`. Then log in as Kevin to see them in *My tasks*.

## 4. What to say in the interview
> "My A360 demo reads 20 sample requests from a CSV file and enters them through the web UI: the same technique I'd use for SAP GUI or a portal with no API. It fills fields by stable element IDs, waits for the confirmation number, writes results back, and in the Catch block screenshots and logs failed rows without stopping the run. At the end it emails a summary. If a system has an API, I'd integrate directly with C# or n8n instead, because UI bots break when screens change."
