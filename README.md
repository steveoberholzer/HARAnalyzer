# HAR Analyzer

A WPF desktop application for quickly analysing Chrome and Edge `.HAR` (HTTP Archive) network trace files.

HAR files are JSON archives that browsers produce via DevTools → Network → "Save all as HAR with content". They capture every HTTP request made during a session, including full timing breakdowns. A typical trace can run to tens of megabytes, making manual inspection impractical. HAR Analyzer parses the file and presents the data as a set of focused, interactive HTML reports in a split-pane interface.

---

## Requirements

| Requirement | Notes |
|---|---|
| Windows 10 / 11 / Server 2022 | WPF is Windows-only |
| [.NET 8 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) | Desktop runtime (not just ASP.NET) |
| [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) | Ships with Edge; usually already present |

---

## Building

```bash
git clone <repo-url>
cd HARAnalyzer
dotnet build -c Release
```

### Run directly

```bash
dotnet run
```

### Publish a standalone EXE

```bash
dotnet publish -c Release -r win-x64 --self-contained false -o publish/
```

Drop the `publish/` folder anywhere; run `HARAnalyzer.exe`.

### Open a file from the command line

```bash
HARAnalyzer.exe "C:\traces\session.har"
```

---

## Getting Started

1. Launch the application.
2. Use **File → Open** (`Ctrl+O`) or drag-and-drop a `.har` file onto the left pane.
3. The file is parsed asynchronously; a progress indicator is shown in the status bar.
4. Click any node in the tree to load its report in the right pane.

---

## Interface Overview

```
┌────────────────────────────────────────────────────────────┐
│  File   View                                  [status bar] │
├──────────────────┬─────────────────────────────────────────┤
│  📄 session.har  │                                         │
│  ├ 📊 Summary    │  HTML Report                            │
│  ├ 🐌 Slowest…  │  (rendered by WebView2 / Chromium)      │
│  │  ├ By Total   │                                         │
│  │  └ By TTFB    │                                         │
│  ├ ❌ Errors     │                                         │
│  ├ 🌐 By Domain  │                                         │
│  │  ├ api.…      │                                         │
│  │  └ cdn.…      │                                         │
│  ├ 📅 Timeline   │                                         │
│  └ 📋 All Reqs   │                                         │
└──────────────────┴─────────────────────────────────────────┘
```

The divider between the two panes can be dragged to resize them.

---

## Tree View Sections

### 📊 Summary
High-level statistics for the whole trace:
- **Stat cards** — total requests, total/avg/max time, max TTFB, slow request count, error count, recording duration.
- **Status breakdown** — counts per 2xx / 3xx / 4xx / 5xx / 0 group.
- **Top domains by total time** — quick dominance view.

### 🐌 Slowest Requests
Two sub-nodes with colour-coded tables and per-row timing bars:

| Sub-node | Sorted by | Rows |
|---|---|---|
| By Total Time | `blocked + dns + connect + ssl + send + wait + receive` | Top 30 |
| By TTFB | `wait` (server think time only) | Top 20 |

### ❌ / ✅ Errors
All requests with status **0** (no response), **4xx**, or **5xx**. The node label turns green and shows "none" when the trace is clean.

### 🌐 By Domain
A summary table of every host, sorted by total time consumed, showing request count, avg/max timing, max TTFB, slow count, and error count.

Expanding the node reveals one child per domain; clicking it shows all requests for that host in a full timing table.

### 📅 Timeline
Requests grouped into 5-second buckets from the start of the recording. Includes a colour-coded sparkline bar chart at the top (blue = normal, orange = slow bucket, red = errors present).

### 📋 All Requests
Every entry in chronological order. The view is capped at **500 rows** to keep the DOM responsive; for full exports use "Export to CSV".

---

## CSV Export

Right-click **any node** in the tree for two export options:

| Option | Behaviour |
|---|---|
| **Export to CSV…** | Opens a Save dialog; writes a UTF-8 CSV file (with BOM, for Excel compatibility). |
| **Copy as CSV** | Copies the CSV text directly to the clipboard — paste straight into Excel, Sheets, etc. |

The suggested filename is automatically derived from the HAR file name and the section, e.g. `session-slowest-ttfb.csv`.

### CSV schemas

**Summary** — two-column key/value, followed by status-breakdown block.

**Request tables** (Slowest, Errors, Domain detail, All Requests):

```
#, Started, Method, Status, Status Text,
Total (ms), TTFB (ms), Receive (ms), Connect (ms), SSL (ms),
Blocked (ms), DNS (ms), Send (ms), MIME Type, Host, URL
```

**By Domain**:
```
Domain, Requests, Total Time (ms), Avg Time (ms), Max Time (ms),
Max TTFB (ms), Slow (>1s), Errors
```

**Timeline**:
```
Offset (s), Label, Requests, Total Time (ms), Max Time (ms),
Slow (>1s), Errors
```

> **Tip:** Folder/group nodes (e.g. "🐌 Slowest Requests") do not carry data and have their CSV options greyed out in the context menu.

---

## Compare Two HAR Files

**File → Compare Two Files…** lets you diff a baseline capture against a newer one to quantify which endpoints got faster or slower — useful for validating performance work.

Two sequential Open dialogs pick **File A** (baseline) then **File B** (new). Both files are parsed and a dedicated **Compare Window** opens non-modally, so the main window remains interactive alongside it.

### Matching logic

Requests are matched by `Method + URL-without-querystring`. Multiple hits to the same endpoint within a file are **averaged**. Delta = B − A.

A change is classified as a **Regression** or **Improvement** only when *both* thresholds are exceeded:

| Threshold | Value |
|---|---|
| Absolute delta | > 50 ms |
| Relative change | > 10 % |

Everything else is **Negligible**.

### Compare tree sections

| Node | Contents |
|---|---|
| 📊 Summary | Stat cards (Matched, Regressions, Improvements, Only in A/B, Sum Δ) + side-by-side file info |
| 📋 All Differences | Every matched endpoint sorted by \|Δ ms\| descending |
| ❌ Regressions | Matched endpoints that got slower beyond the threshold |
| 🚀 Improvements | Matched endpoints that got faster beyond the threshold |
| Only in A | Endpoints present only in the baseline (removed or unreached in B) |
| Only in B | Endpoints present only in the new capture (new or previously uncalled) |
| 🌐 By Domain | Per-domain rollup: matched count, sum Δ, avg Δ, regression/improvement counts |

All nodes support the same **right-click → Export to CSV / Copy as CSV** as the main window.

### Compare CSV schemas

**Compare Summary** — key/value pairs covering both files, counts, and overall delta.

**Diff tables** (All Differences, Regressions, Improvements):
```
#, Method, Host, Base URL,
A Avg (ms), B Avg (ms), Delta (ms), % Change,
Count A, Count B, Category
```

**Compare By Domain**:
```
Domain, Matched, Sum Delta (ms), Avg Delta (ms), Regressions, Improvements
```

---

## Trim HAR

**File → Trim HAR…** removes unwanted requests from the beginning of a trace and saves the result as a new file. The original is never modified.

**Typical use case:** you recorded Page 1 traffic, forgot to clear the network log, navigated to Page 2, and exported. The second HAR contains both pages. Open it, trim everything before Page 2 starts, and save a clean copy.

### How to use

1. Open the HAR file you want to trim (**File → Open**).
2. Choose **File → Trim HAR…**.
3. The dialog shows the trace divided into **5-second buckets**:

```
Offset   Start Time   Requests   Total Time   Will Keep
+0s      00:47:40     47         4.21 s       89
+5s      00:47:45     23         2.10 s       42
+10s     00:47:50     19         1.88 s       19
```

4. Click the first bucket you want to **keep**. Rows above it grey out. The status line shows *"Keeping 42 of 89 requests · Removing 47 requests · From 00:47:50 onwards"*.
5. Click **Save Trimmed HAR…** and choose a destination. The default name is `{original}-trimmed.har`.

The **Will Keep** column shows how many requests will be in the saved file if that row is chosen as the starting point, making it easy to see the impact before committing.

> **Note:** The trimmer works directly on the raw JSON, so all original fields (cookies, cache timing, server IP, etc.) are preserved in the output file.

---

## Keyboard Shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl+O` | Open HAR file |
| `Alt+F4` | Exit |

---

## Timing Definitions

HAR timings follow the [HAR 1.2 specification](http://www.softwareishard.com/blog/har-12-spec/). Negative values (indicating a phase did not occur) are clamped to zero.

| Field | Meaning |
|---|---|
| **Blocked** | Waiting for a network connection (queue or connection limit) |
| **DNS** | DNS resolution time |
| **Connect** | TCP handshake |
| **SSL** | TLS handshake (subset of Connect) |
| **Send** | Time to transmit the request body |
| **TTFB / Wait** | Server think time — from last byte sent to first byte received |
| **Receive** | Time to download the response body |
| **Total** | Sum of all above phases |

---

## Architecture

```
HARAnalyzer/
├── App.xaml / App.xaml.cs          WPF entry point; handles CLI .har argument
├── MainWindow.xaml                  Layout: Menu + TreeView | GridSplitter | WebView2
├── MainWindow.xaml.cs               All UI logic
├── CompareWindow.xaml               Non-modal compare results window
├── CompareWindow.xaml.cs            Compare tree builder and CSV export
├── TrimWindow.xaml                  Modal trim-by-time dialog
├── TrimWindow.xaml.cs               Bucket list, selection logic, save
├── Models/
│   ├── HarFile.cs                   HAR JSON model (System.Text.Json attributes)
│   ├── AnalysisResult.cs            Computed summary + flat AnalysisEntry list
│   ├── CompareResult.cs             RequestDiff list + regression/improvement sets
│   └── TreeNodeData.cs              Per-node HTML and CSV generator lambdas
└── Services/
    ├── HarParser.cs                 Streaming JSON parse (handles large files)
    ├── HarAnalyzerService.cs        Derives AnalysisResult from HarFile
    ├── HarCompareService.cs         Diffs two AnalysisResults by Method + URL key
    ├── HarWriter.cs                 Trims HAR JSON in-place via JsonNode (preserves all fields)
    ├── HtmlBuilder.cs               Generates self-contained HTML reports
    ├── CsvBuilder.cs                Generates RFC-4180 CSV (UTF-8 + BOM)
    └── MruService.cs                Persists MRU list to %AppData%\HARAnalyzer\mru.json
```

- **Parsing** is done via `System.Text.Json` with streaming (`File.OpenRead`) to handle files of any size without loading them entirely into memory.
- **HTML reports** are self-contained (all CSS is embedded); no internet connection is required.
- **WebView2** (Chromium-based) renders the HTML with full support for modern CSS including flexbox and custom properties.
- Each `TreeViewItem` stores a `TreeNodeData` instance (in `.Tag`) containing lazy `Func<string>` generators for both the HTML and CSV representations. Nothing is computed until the node is clicked or exported.
- **Trimming** uses `System.Text.Json.Nodes.JsonNode` to manipulate the raw JSON directly, preserving every field that our model does not explicitly capture (cookies, cache entries, etc.).

---

## Licence

MIT — do whatever you like with it.
