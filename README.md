# TFS System Info Extractor (local UI edition)

Same extraction logic as the console-only spec, plus a tiny local browser UI
so you can paste/upload Work Item IDs and **download the JSON and Markdown
files straight from the browser**, in addition to the files it still writes
to `C:\TfsSystemInfoExport`.

Nothing is deployed anywhere — running the exe starts a web server that only
listens on `localhost`, and opens your default browser to it automatically.
Windows Authentication (your logged-in user) is used for TFS, same as before.

## 1. Configure

Open `Program.cs`, edit the constants at the top (`Config` class):

- `TfsCollectionUrl` — your on-prem collection URL.
- `ApiVersion` — REST API version compatible with your TFS server (default `3.0`;
  change to `1.0`/`2.0`/etc. if your on-prem version needs it).
- `HttpPort` — local port for the UI (default `5050`).
- `ExportFolder` — default `C:\TfsSystemInfoExport`.

## 2. Build

Requires Windows + the .NET Framework 4.7.2 targeting pack (already present
if you have Visual Studio 2017+ installed).

**Visual Studio:** open the folder, let it generate a solution, Build.

**Command line** (needs the .NET SDK installed, even though the app targets
.NET Framework):

```
dotnet build -c Release
```

## 3. Run

```
bin\Release\net472\TfsSystemInfoExtractor.exe
```

Your browser opens automatically at `http://localhost:5050`. Paste Work Item
IDs (comma / space / newline separated) or upload a `.txt`/`.csv` file with
IDs, click **Run**, watch the live progress log, then use the tree view or
the **Download JSON** / **Download Markdown** buttons.

## Notes / assumptions carried over from the original spec

- Only `System.LinkTypes.Hierarchy-Forward` relations are traversed as
  children; everything else (Parent, Related, attachments, links, etc.) is
  ignored.
- The `System Info` field's technical reference name is discovered at
  startup from `_apis/wit/fields` by display name — never hardcoded.
- A Work Item already fetched in the current run (by ID) is never re-fetched;
  this also guards against hierarchy loops and infinite recursion. If the
  same ID legitimately appears under two different parents, it is only
  expanded once (under whichever parent reaches it first) — this was an
  explicit assumption to keep the loop guard simple; say the word if you'd
  rather see it repeated under every parent instead.
- Rich text `System Info` is converted to plain text (`<br>`, `<p>`, `<div>`,
  `<li>` become line breaks; all other tags are stripped; HTML entities are
  decoded). Empty `System Info` never stops traversal.
- The app is fully read-only against TFS.
