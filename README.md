# TFS System Info Extractor

Recursively walks a set of TFS / Azure DevOps Server work item hierarchies
(`System.LinkTypes.Hierarchy-Forward` children only), pulls the custom
**System Info** field from every item, and presents the result in a small
local browser UI with JSON / Markdown / CSV export.

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
                        IProgressListener, IExportFormatter, IExtractionArtifactStore, ISystemClock
      Extraction/       WorkItemIdParser, HierarchyWalker, ExtractionService
      Export/           Json / Markdown / Csv formatters + ExportFormatterSelector
      Exceptions/       TfsExtractorException hierarchy
  TfsSystemInfoExtractor.Infrastructure  adapters
      Tfs/              TfsRestClient, TfsWorkItemSource, TfsFieldCatalog,
                        TfsSystemInfoFieldResolver, TfsResponseMapper
      Text/             HtmlToPlainTextConverter
      Export/           PdfExportFormatter, PdfReportDocumentBuilder  (IExportFormatter with a PDF dependency)
      Storage/          FileSystemArtifactStore
      Configuration/    TfsOptions, ExportOptions, TfsOptionsValidator
  TfsSystemInfoExtractor.Web             presentation
      Hosting/          LocalHttpServer, BrowserLauncher
      Http/             RequestRouter, ResponseWriter, IHttpEndpoint
      Endpoints/        Index, StartExtraction, JobStatus, DownloadArtifact
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
| `Export:OutputDirectory` | folder every export is also written to | `C:\TfsSystemInfoExport` |
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
Compact, Tree, Outline, Info Focus) and use **Download / Export**
(CSV, PDF release report, JSON, Markdown). The same files are written to
the export folder.

In the Table view the **with info** count is a toggle that filters to the
Work Items that have System Info (parent rows kept for context), and
**Fields** lets advanced users add extra columns for any additional TFS
field found on the extracted items (the list is built from the TFS field
catalogue, `_apis/wit/fields`; selections persist per browser). The
default columns and layout are unchanged; the hierarchy is shown in the
Type column.

Every export format is an `IExportFormatter` (Core port). Pure formatters
(JSON / Markdown / CSV) live in Core; the PDF formatter lives in
Infrastructure because it carries a third-party dependency (PDFsharp /
MigraDoc). Adding a format = one new `IExportFormatter` + one DI line;
no Core logic changes.

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
