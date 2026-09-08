# TFS System Info Extractor

Recursively walks a set of TFS / Azure DevOps Server work item hierarchies
(`System.LinkTypes.Hierarchy-Forward` children only), pulls the custom
**System Info** field from every item, and presents the result in a small
local browser UI with Excel / PDF / JSON / Markdown export.

Nothing is deployed anywhere — running the exe starts an `HttpListener` that
only listens on `localhost` and opens your default browser to it. TFS is
accessed read-only with Windows integrated authentication (your logged-in
user); no PAT.

## Solution layout

```
src/
  TfsSystemInfoExtractor.Core            domain models + application services + ports
      Model/            WorkItemNode, RawWorkItem, ExtractionResult, ExportArtifact,
                        FieldDefinition, SourceControl/ (SourceControlInfo, SourceRepository, Commit, Developer)
      Abstractions/     IWorkItemSource, ISystemInfoFieldResolver, IFieldCatalog, IHtmlToText,
                        IProgressListener, IExtractionArtifactStore, ISystemClock
      Extraction/       WorkItemIdParser, HierarchyWalker, ExtractionService
      Export/           ExportArtifact/Format helpers, JsonExportFormatter (the data feed)
      Reporting/        ReportView, IReportRenderer, ReportRendererSelector,
                        Excel (.xlsx) / Markdown renderers, IBrowserPdfEngine
      Exceptions/       TfsExtractorException hierarchy (incl. PdfRenderException, BrowserNotFoundException)
  TfsSystemInfoExtractor.Infrastructure  adapters
      Tfs/              TfsRestClient, TfsWorkItemSource, TfsFieldCatalog,
                        TfsSystemInfoFieldResolver, TfsResponseMapper
      Text/             HtmlToPlainTextConverter
      Export/           ChromiumBrowserLocator, EdgeHtmlToPdfEngine  (headless Edge/Chrome -> PDF)
      Storage/          FileSystemArtifactStore
      Configuration/    TfsOptions, ExportOptions, TfsOptionsValidator
  TfsSystemInfoExtractor.Web             presentation
      Hosting/          LocalHttpServer, BrowserLauncher
      Http/             RequestRouter, ResponseWriter, IHttpEndpoint
      Endpoints/        Index, StartExtraction, JobStatus, Result, Export
      Export/           HtmlToPdfReportRenderer  (wraps the UI's own HTML/CSS)
      Jobs/             ExtractionJob, InMemoryJobStore, JobManager, JobProgressListener
      Ui/Assets/        index.html  (embedded resource — the whole browser UI)
  TfsSystemInfoExtractor.App             composition root (the exe)
tests/
  TfsSystemInfoExtractor.Tests           xUnit
```

Each layer exposes an `AddXxx(IConfiguration)` extension; `App/Program.cs` is
only wiring: build config → `AddExtractorCore` / `AddTfsInfrastructure` /
`AddWebUi` → run `LocalHttpServer` until Ctrl+C.

## 1. Configure

Edit `src/TfsSystemInfoExtractor.App/appsettings.json` (no rebuild needed):

| Setting | Meaning | Default |
|---|---|---|
| `Tfs:CollectionUrl` | on-prem collection URL | `http://192.168.160.17:8080/tfs/Altshuler%20Shaham%20IT` |
| `Tfs:ApiVersion` | REST API version | `3.0` |
| `Tfs:SystemInfoFieldDisplayName` | field to extract, by display name | `System Info` |
| `Tfs:RequestTimeoutSeconds` | per-request timeout | `60` |
| `Export:OutputDirectory` | folder a copy of each export is saved to on click | `C:\TfsSystemInfoExport` |
| `Export:BrowserPath` | explicit path to msedge.exe / chrome.exe for PDF (empty = auto-detect) | `` |
| `Export:PdfTimeoutSeconds` | hard timeout for one PDF render | `40` |
| `Web:Port` | loopback port for the UI | `5050` |
| `Web:OpenBrowserOnStart` | open the browser on launch | `true` |
| `Extraction:MaxDepth` | hierarchy depth safety cap | `50` |

Any value can be overridden with an environment variable using the `TFS_`
prefix and `__` as the separator, e.g.
`set TFS_Tfs__CollectionUrl=http://other:8080/tfs/Coll`.
A git-ignored `appsettings.local.json` next to the exe is also honoured.

## 2. Build

Requires Windows, the .NET SDK, and the .NET Framework 4.7.2 targeting pack
(ships with Visual Studio 2017+).

```
dotnet build TfsSystemInfoExtractor.sln -c Release
dotnet test  TfsSystemInfoExtractor.sln -c Release
```

Visual Studio: open `TfsSystemInfoExtractor.sln`, Build, F5 the `App` project.

## 3. Run

```
src\TfsSystemInfoExtractor.App\bin\Release\net472\TfsSystemInfoExtractor.exe
```

The browser opens at `http://localhost:5050`. Paste work item IDs (comma /
space / newline separated) or upload a `.txt` / `.csv`, click **Run**, watch
the live log, then browse the result (grouped Table view by default; also
Compact, Tree, Outline, Info Focus).

In the Table view the **with info** count is a toggle that filters to only
the Work Items that have System Info, and **Fields** lets advanced users add
extra columns for any additional TFS field found on the extracted items (the
list comes from the TFS field catalogue, `_apis/wit/fields`; selections
persist per browser). The default columns and layout are unchanged; the
hierarchy is shown in the Type column.

## Export

**Download / Export** offers **Excel** (the default), **PDF**, **PDF System
Info**, **JSON** (raw data) and **Markdown**. Nothing is generated until you
click one - loading, browsing, filtering and changing columns produce no files.

At the moment of the click the browser hands the server the exact view it is
showing (columns, order, active filter, hierarchy, the added fields, and the
rendered table HTML). The matching `IReportRenderer` turns that into a file
that is streamed to you and also saved to `Export:OutputDirectory`.

- **Excel** (`.xlsx`) is a formatted workbook of that same prepared view: a
  real Excel table with a column auto-filter and a frozen, styled header row.
  Rows are grouped by root Work Item - each root and all of its descendants
  share one background (groups alternate plain / light blue) and are boxed
  together by a single thick outer border, whatever the group's size. Parent
  rows stay bold, the Type column keeps its hierarchy indentation, columns are
  content-sized, and every value wraps so long System Info / extra fields are
  fully readable and never truncated. Built on `DocumentFormat.OpenXml` only
  (MIT - no commercial dependency, no Excel install needed).
- **PDF / PDF System Info** are produced by the machine's own headless
  **Edge or Chrome** (`--print-to-pdf`) rendering the UI's own HTML/CSS - so
  the PDF looks like the table, and Hebrew / RTL / mixed text render
  correctly (Chromium's bidi). No PDF library, no bundled browser, no
  licence. "PDF System Info" is the same view with the System-Info-only
  filter applied. If no browser is found, PDF export returns a clear message
  and Excel / Markdown still work.
- Adding a new format = one new `IReportRenderer` + one DI line; no change to
  the domain, the extraction pipeline or the UI's view logic.

## Behaviour / assumptions

- Only `System.LinkTypes.Hierarchy-Forward` relations are followed as children.
- The System Info field's reference name is discovered at run time from
  `_apis/wit/fields` by display name — never hard-coded.
- Each work item is fetched once per run; this also guards against link
  cycles. A child reachable from two parents is expanded only under the
  first parent that reaches it.
- Rich-text System Info is converted to plain text (`<br>`, `<p>`, `<div>`,
  `<li>` become newlines; other tags stripped; entities decoded).
- An item that fails to load becomes an error node; the run continues.
- Fully read-only against TFS.
