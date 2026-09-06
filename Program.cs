using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace TfsSystemInfoExtractor
{
    // =====================================================================
    // CONFIGURATION - edit these to match your environment
    // =====================================================================
    internal static class Config
    {
        // TFS / Azure DevOps on-premise collection URL.
        public const string TfsCollectionUrl = "http://server:8080/tfs/CollectionName";

        // REST API version. Change here in one place if your on-prem TFS
        // needs a different version (e.g. "1.0", "2.0", "4.1").
        public const string ApiVersion = "3.0";

        // Local port for the browser UI. Only reachable from this machine.
        public const int HttpPort = 5050;

        // Where exports are written (in addition to the browser Download buttons).
        public const string ExportFolder = @"C:\TfsSystemInfoExport";

        public const string SystemInfoDisplayName = "System Info";
        public const string ChildRelationName = "System.LinkTypes.Hierarchy-Forward";
    }

    // =====================================================================
    // DATA MODEL
    // =====================================================================
    internal class WorkItemNode
    {
        public int Id;
        public string Type;
        public string Title;
        public string State;
        public string Url;
        public string SystemInfo;
        public List<WorkItemNode> Children = new List<WorkItemNode>();
        public string Error; // set when this item failed to load
    }

    internal class Job
    {
        public string Id;
        public volatile bool Done;
        public string ErrorMessage;
        public int ProcessedCount;
        public List<WorkItemNode> Roots = new List<WorkItemNode>();
        public readonly List<string> Log = new List<string>();
        public readonly object LogLock = new object();
        public string JsonPath;
        public string MdPath;

        public void Print(int depth, string message)
        {
            var line = new string(' ', depth * 4) + message;
            lock (LogLock)
            {
                Log.Add(line);
            }
            Console.WriteLine(line);
        }
    }

    // =====================================================================
    // TFS CLIENT
    // =====================================================================
    internal class TfsClient
    {
        private readonly HttpClient _http;
        public string SystemInfoFieldRef { get; private set; }

        public TfsClient()
        {
            var handler = new HttpClientHandler { UseDefaultCredentials = true };
            _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        }

        // Discovers the technical reference name of the "System Info" field
        // by display name, so it never has to be hardcoded.
        public async Task DiscoverSystemInfoFieldAsync()
        {
            var url = $"{Config.TfsCollectionUrl}/_apis/wit/fields?api-version={Config.ApiVersion}";
            string body;
            try
            {
                var resp = await _http.GetAsync(url);
                body = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                {
                    throw new ApplicationException(
                        $"TFS returned {(int)resp.StatusCode} {resp.ReasonPhrase} while requesting field definitions from {url}.\n" +
                        $"Response body: {Truncate(body, 500)}");
                }
            }
            catch (HttpRequestException ex)
            {
                throw new ApplicationException(
                    $"Could not reach TFS at {Config.TfsCollectionUrl}. Check the URL, network access and Windows authentication. Details: {ex.Message}", ex);
            }

            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var parsed = (Dictionary<string, object>)serializer.DeserializeObject(body);
            var values = (ArrayList)parsed["value"];

            foreach (Dictionary<string, object> field in values)
            {
                var name = field.ContainsKey("name") ? (string)field["name"] : null;
                if (string.Equals(name, Config.SystemInfoDisplayName, StringComparison.OrdinalIgnoreCase))
                {
                    SystemInfoFieldRef = (string)field["referenceName"];
                    return;
                }
            }

            throw new ApplicationException(
                $"Could not find a field whose display name is \"{Config.SystemInfoDisplayName}\" in TFS field definitions. " +
                "Verify the field exists and the display name matches exactly.");
        }

        public async Task<Dictionary<string, object>> GetWorkItemAsync(int id)
        {
            var url = $"{Config.TfsCollectionUrl}/_apis/wit/workitems/{id}?$expand=relations&api-version={Config.ApiVersion}";
            HttpResponseMessage resp;
            try
            {
                resp = await _http.GetAsync(url);
            }
            catch (HttpRequestException ex)
            {
                throw new ApplicationException($"Network error loading Work Item {id}: {ex.Message}", ex);
            }

            var body = await resp.Content.ReadAsStringAsync();

            if (resp.StatusCode == HttpStatusCode.NotFound)
                throw new ApplicationException($"Work Item {id} does not exist (404).");
            if (resp.StatusCode == HttpStatusCode.Unauthorized || resp.StatusCode == HttpStatusCode.Forbidden)
                throw new ApplicationException($"No permission to read Work Item {id} ({(int)resp.StatusCode}).");
            if (!resp.IsSuccessStatusCode)
                throw new ApplicationException($"TFS returned {(int)resp.StatusCode} for Work Item {id}: {Truncate(body, 500)}");

            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            Dictionary<string, object> parsed;
            try
            {
                parsed = (Dictionary<string, object>)serializer.DeserializeObject(body);
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Unexpected response shape for Work Item {id}: {ex.Message}");
            }
            return parsed;
        }

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "...";
    }

    // =====================================================================
    // RICH TEXT -> PLAIN TEXT
    // =====================================================================
    internal static class RichText
    {
        public static string ToPlainText(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;

            var text = html;
            text = Regex.Replace(text, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<\s*/\s*(p|div|li)\s*>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<\s*(p|div|li)[^>]*>", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<[^>]+>", "");
            text = WebUtilityHtmlDecode(text);

            var lines = text.Replace("\r\n", "\n").Split('\n')
                .Select(l => l.Trim())
                .SkipWhile(string.IsNullOrEmpty);

            var result = new List<string>();
            bool lastBlank = false;
            foreach (var line in lines)
            {
                var blank = string.IsNullOrEmpty(line);
                if (blank && lastBlank) continue;
                result.Add(line);
                lastBlank = blank;
            }
            while (result.Count > 0 && string.IsNullOrEmpty(result[result.Count - 1]))
                result.RemoveAt(result.Count - 1);

            return string.Join("\n", result);
        }

        private static string WebUtilityHtmlDecode(string s) => System.Net.WebUtility.HtmlDecode(s);
    }

    // =====================================================================
    // TRAVERSAL
    // =====================================================================
    internal class HierarchyExtractor
    {
        private readonly TfsClient _client;
        private readonly HashSet<int> _visited = new HashSet<int>();
        private readonly Job _job;

        public HierarchyExtractor(TfsClient client, Job job)
        {
            _client = client;
            _job = job;
        }

        public async Task<List<WorkItemNode>> RunAsync(IEnumerable<int> rootIds)
        {
            var roots = new List<WorkItemNode>();
            foreach (var id in rootIds)
            {
                _job.Print(0, $"Processing ROOT Work Item: {id}");
                var node = await ProcessAsync(id, 0);
                if (node != null) roots.Add(node);
            }
            return roots;
        }

        private async Task<WorkItemNode> ProcessAsync(int id, int depth)
        {
            if (_visited.Contains(id))
            {
                _job.Print(depth, $"Work Item {id} already processed - skipping duplicate fetch.");
                return null;
            }
            _visited.Add(id);

            _job.Print(depth, $"Loading Work Item {id}...");

            Dictionary<string, object> raw;
            try
            {
                raw = await _client.GetWorkItemAsync(id);
            }
            catch (ApplicationException ex)
            {
                _job.Print(depth, $"[ERROR] {ex.Message}");
                _job.ProcessedCount++;
                return new WorkItemNode { Id = id, Error = ex.Message };
            }

            var fields = (Dictionary<string, object>)raw["fields"];
            var node = new WorkItemNode
            {
                Id = id,
                Type = GetField(fields, "System.WorkItemType"),
                Title = GetField(fields, "System.Title"),
                State = GetField(fields, "System.State"),
                Url = GetWebUrl(raw, id)
            };

            var rawSystemInfo = GetField(fields, _client.SystemInfoFieldRef);
            node.SystemInfo = RichText.ToPlainText(rawSystemInfo);

            if (!string.IsNullOrEmpty(node.SystemInfo))
                _job.Print(depth, "[SYSTEM INFO FOUND]");
            else
                _job.Print(depth, "[NO SYSTEM INFO]");

            var childIds = GetChildIds(raw);
            _job.Print(depth, $"Children found: {childIds.Count}");

            foreach (var childId in childIds)
            {
                var childNode = await ProcessAsync(childId, depth + 1);
                if (childNode != null) node.Children.Add(childNode);
            }

            _job.ProcessedCount++;
            return node;
        }

        private static string GetField(Dictionary<string, object> fields, string refName)
        {
            if (string.IsNullOrEmpty(refName) || fields == null) return null;
            return fields.ContainsKey(refName) ? Convert.ToString(fields[refName]) : null;
        }

        private static string GetWebUrl(Dictionary<string, object> raw, int id)
        {
            try
            {
                if (raw.ContainsKey("_links"))
                {
                    var links = (Dictionary<string, object>)raw["_links"];
                    if (links.ContainsKey("html"))
                    {
                        var html = (Dictionary<string, object>)links["html"];
                        if (html.ContainsKey("href")) return Convert.ToString(html["href"]);
                    }
                }
            }
            catch { /* fall through to constructed URL */ }
            return $"{Config.TfsCollectionUrl}/_workitems/edit/{id}";
        }

        private static List<int> GetChildIds(Dictionary<string, object> raw)
        {
            var result = new List<int>();
            if (!raw.ContainsKey("relations") || raw["relations"] == null) return result;

            var relations = (ArrayList)raw["relations"];
            foreach (Dictionary<string, object> rel in relations)
            {
                var relName = rel.ContainsKey("rel") ? (string)rel["rel"] : null;
                if (relName != Config.ChildRelationName) continue;

                var url = rel.ContainsKey("url") ? (string)rel["url"] : null;
                if (string.IsNullOrEmpty(url)) continue;

                var lastSegment = url.Substring(url.LastIndexOf('/') + 1);
                if (int.TryParse(lastSegment, out var childId))
                    result.Add(childId);
            }
            return result;
        }
    }

    // =====================================================================
    // EXPORT (JSON + MARKDOWN)
    // =====================================================================
    internal static class Exporter
    {
        public static string ToJson(DateTime generatedAt, List<WorkItemNode> roots)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append($"  \"GeneratedAt\": {JsonString(generatedAt.ToString("yyyy-MM-ddTHH:mm:ss"))},\n");
            sb.Append("  \"Roots\": [\n");
            for (int i = 0; i < roots.Count; i++)
            {
                WriteNodeJson(sb, roots[i], 2);
                sb.Append(i < roots.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ]\n}\n");
            return sb.ToString();
        }

        private static void WriteNodeJson(StringBuilder sb, WorkItemNode node, int indent)
        {
            var pad = new string(' ', indent * 2);
            var padIn = new string(' ', (indent + 1) * 2);
            sb.Append(pad).Append("{\n");
            sb.Append(padIn).Append($"\"Id\": {node.Id},\n");
            sb.Append(padIn).Append($"\"Type\": {JsonString(node.Type)},\n");
            sb.Append(padIn).Append($"\"Title\": {JsonString(node.Title)},\n");
            sb.Append(padIn).Append($"\"State\": {JsonString(node.State)},\n");
            sb.Append(padIn).Append($"\"Url\": {JsonString(node.Url)},\n");
            sb.Append(padIn).Append($"\"SystemInfo\": {JsonString(node.SystemInfo)},\n");
            if (!string.IsNullOrEmpty(node.Error))
                sb.Append(padIn).Append($"\"Error\": {JsonString(node.Error)},\n");
            sb.Append(padIn).Append("\"Children\": [");
            if (node.Children.Count == 0)
            {
                sb.Append("]\n");
            }
            else
            {
                sb.Append("\n");
                for (int i = 0; i < node.Children.Count; i++)
                {
                    WriteNodeJson(sb, node.Children[i], indent + 2);
                    sb.Append(i < node.Children.Count - 1 ? ",\n" : "\n");
                }
                sb.Append(padIn).Append("]\n");
            }
            sb.Append(pad).Append("}");
        }

        private static string JsonString(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public static string ToMarkdown(DateTime generatedAt, List<WorkItemNode> roots)
        {
            var sb = new StringBuilder();
            sb.Append("# TFS System Info Export\n\n");
            sb.Append($"Generated: {generatedAt:yyyy-MM-dd HH:mm:ss}\n\n");
            sb.Append("---\n\n");
            foreach (var root in roots)
                WriteNodeMarkdown(sb, root, 1, isRoot: true);
            return sb.ToString();
        }

        private static void WriteNodeMarkdown(StringBuilder sb, WorkItemNode node, int depth, bool isRoot)
        {
            var headerLevel = Math.Min(depth, 6);
            var header = new string('#', headerLevel);
            var label = isRoot ? "Work Item" : "Child Work Item";

            sb.Append($"{header} {label} {node.Id}\n\n");

            if (!string.IsNullOrEmpty(node.Error))
            {
                sb.Append($"**ERROR loading this item:** {node.Error}\n\n---\n\n");
                return;
            }

            sb.Append($"Type: {node.Type}\n\n");
            sb.Append($"Title: {node.Title}\n\n");
            sb.Append($"State: {node.State}\n\n");
            sb.Append($"URL: {node.Url}\n\n");

            var subHeader = new string('#', Math.Min(depth + 1, 6));
            sb.Append($"{subHeader} System Info\n\n");
            sb.Append(string.IsNullOrEmpty(node.SystemInfo) ? "_(empty)_\n\n" : node.SystemInfo + "\n\n");
            sb.Append("---\n\n");

            foreach (var child in node.Children)
                WriteNodeMarkdown(sb, child, depth + 1, isRoot: false);
        }
    }

    // =====================================================================
    // HTTP SERVER (local UI)
    // =====================================================================
    internal class WebServer
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly Dictionary<string, Job> _jobs = new Dictionary<string, Job>();
        private readonly object _jobsLock = new object();

        public void Start()
        {
            var prefix = $"http://localhost:{Config.HttpPort}/";
            _listener.Prefixes.Add(prefix);
            _listener.Start();
            Console.WriteLine($"Local UI listening on {prefix}");
            Task.Run(() => Loop());

            try { Process.Start(prefix); }
            catch { Console.WriteLine($"Open {prefix} in your browser."); }
        }

        private async Task Loop()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { break; }
                _ = Task.Run(() => Handle(ctx));
            }
        }

        private async Task Handle(HttpListenerContext ctx)
        {
            try
            {
                var path = ctx.Request.Url.AbsolutePath;
                if (path == "/" && ctx.Request.HttpMethod == "GET")
                    await WriteHtml(ctx, Ui.IndexHtml);
                else if (path == "/api/run" && ctx.Request.HttpMethod == "POST")
                    await HandleRun(ctx);
                else if (path == "/api/status" && ctx.Request.HttpMethod == "GET")
                    await HandleStatus(ctx);
                else if (path == "/api/download" && ctx.Request.HttpMethod == "GET")
                    await HandleDownload(ctx);
                else
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                }
            }
            catch (Exception ex)
            {
                try
                {
                    ctx.Response.StatusCode = 500;
                    await WriteText(ctx, "Internal error: " + ex.Message);
                }
                catch { /* response may already be closed */ }
            }
        }

        private async Task HandleRun(HttpListenerContext ctx)
        {
            string body;
            using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                body = await reader.ReadToEndAsync();

            var ids = ParseIds(body);
            if (ids.Count == 0)
            {
                ctx.Response.StatusCode = 400;
                await WriteText(ctx, "No valid Work Item IDs found in input.");
                return;
            }

            var job = new Job { Id = Guid.NewGuid().ToString("N") };
            lock (_jobsLock) { _jobs[job.Id] = job; }

            _ = Task.Run(async () =>
            {
                try
                {
                    Console.WriteLine("Connecting to TFS...");
                    job.Print(0, "Connecting to TFS...");
                    var client = new TfsClient();
                    await client.DiscoverSystemInfoFieldAsync();
                    job.Print(0, $"System Info field found: {client.SystemInfoFieldRef}");

                    var extractor = new HierarchyExtractor(client, job);
                    job.Roots = await extractor.RunAsync(ids);

                    Directory.CreateDirectory(Config.ExportFolder);
                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
                    var generatedAt = DateTime.Now;

                    job.JsonPath = Path.Combine(Config.ExportFolder, $"TfsSystemInfo_{timestamp}.json");
                    job.MdPath = Path.Combine(Config.ExportFolder, $"TfsSystemInfo_{timestamp}.md");

                    File.WriteAllText(job.JsonPath, Exporter.ToJson(generatedAt, job.Roots), new UTF8Encoding(false));
                    File.WriteAllText(job.MdPath, Exporter.ToMarkdown(generatedAt, job.Roots), new UTF8Encoding(false));

                    job.Print(0, $"Processed {job.ProcessedCount} Work Items.");
                    job.Print(0, $"JSON: {job.JsonPath}");
                    job.Print(0, $"Markdown: {job.MdPath}");
                }
                catch (Exception ex)
                {
                    job.ErrorMessage = ex.Message;
                    job.Print(0, $"[FATAL] {ex.Message}");
                }
                finally
                {
                    job.Done = true;
                }
            });

            await WriteJson(ctx, $"{{\"jobId\":\"{job.Id}\"}}");
        }

        private async Task HandleStatus(HttpListenerContext ctx)
        {
            var jobId = ctx.Request.QueryString["jobId"];
            Job job;
            lock (_jobsLock)
            {
                if (jobId == null || !_jobs.TryGetValue(jobId, out job))
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    return;
                }
            }

            string[] logSnapshot;
            lock (job.LogLock) { logSnapshot = job.Log.ToArray(); }

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"done\":{(job.Done ? "true" : "false")},");
            sb.Append($"\"processed\":{job.ProcessedCount},");
            sb.Append($"\"error\":{JsonStr(job.ErrorMessage)},");
            sb.Append("\"log\":[").Append(string.Join(",", logSnapshot.Select(JsonStr))).Append("],");
            sb.Append($"\"hasFiles\":{(job.Done && job.JsonPath != null ? "true" : "false")}");
            sb.Append("}");
            await WriteJson(ctx, sb.ToString());
        }

        private async Task HandleDownload(HttpListenerContext ctx)
        {
            var jobId = ctx.Request.QueryString["jobId"];
            var type = ctx.Request.QueryString["type"];
            Job job;
            lock (_jobsLock)
            {
                if (jobId == null || !_jobs.TryGetValue(jobId, out job))
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    return;
                }
            }

            var path = type == "md" ? job.MdPath : job.JsonPath;
            if (path == null || !File.Exists(path))
            {
                ctx.Response.StatusCode = 404;
                await WriteText(ctx, "File not ready yet.");
                return;
            }

            var fileName = Path.GetFileName(path);
            ctx.Response.ContentType = type == "md" ? "text/markdown; charset=utf-8" : "application/json; charset=utf-8";
            ctx.Response.AddHeader("Content-Disposition", $"attachment; filename=\"{fileName}\"");
            var bytes = File.ReadAllBytes(path);
            await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }

        private static List<int> ParseIds(string raw)
        {
            var ids = new List<int>();
            if (string.IsNullOrWhiteSpace(raw)) return ids;
            var parts = raw.Split(new[] { ',', ';', '\n', '\r', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
                if (int.TryParse(part.Trim(), out var id) && !ids.Contains(id))
                    ids.Add(id);
            return ids;
        }

        private static string JsonStr(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (var c in s)
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static async Task WriteHtml(HttpListenerContext ctx, string html)
        {
            ctx.Response.ContentType = "text/html; charset=utf-8";
            var bytes = Encoding.UTF8.GetBytes(html);
            await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }

        private static async Task WriteJson(HttpListenerContext ctx, string json)
        {
            ctx.Response.ContentType = "application/json; charset=utf-8";
            var bytes = Encoding.UTF8.GetBytes(json);
            await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }

        private static async Task WriteText(HttpListenerContext ctx, string text)
        {
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            var bytes = Encoding.UTF8.GetBytes(text);
            await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }
    }

    // =====================================================================
    // EMBEDDED UI
    // =====================================================================
    internal static class Ui
    {
        public const string IndexHtml = @"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<title>TFS System Info Extractor</title>
<style>
  :root { color-scheme: light; }
  * { box-sizing: border-box; }
  body { margin: 0; font-family: Segoe UI, Arial, sans-serif; background: #f4f6f8; color: #1f2937; }
  header { background: #1f2937; color: #fff; padding: 16px 24px; }
  header h1 { margin: 0; font-size: 18px; font-weight: 600; }
  main { max-width: 1000px; margin: 24px auto; padding: 0 16px; }
  .card { background: #fff; border: 1px solid #e5e7eb; border-radius: 10px; padding: 20px; margin-bottom: 20px; }
  textarea { width: 100%; min-height: 100px; font-family: Consolas, monospace; font-size: 13px; padding: 10px; border: 1px solid #d1d5db; border-radius: 6px; resize: vertical; }
  .row { display: flex; gap: 10px; align-items: center; margin-top: 12px; flex-wrap: wrap; }
  button { background: #2563eb; color: #fff; border: none; padding: 10px 18px; border-radius: 6px; font-size: 14px; cursor: pointer; }
  button:disabled { background: #9ca3af; cursor: not-allowed; }
  button.secondary { background: #059669; }
  input[type=file] { font-size: 13px; }
  #log { background: #0f172a; color: #d1fae5; font-family: Consolas, monospace; font-size: 12px; padding: 12px; border-radius: 6px; height: 260px; overflow-y: auto; white-space: pre-wrap; }
  #status { margin-top: 8px; font-size: 13px; color: #374151; }
  .tree { font-size: 14px; }
  .node { border-inline-start: 2px solid #e5e7eb; margin-inline-start: 10px; padding-inline-start: 12px; padding-top: 6px; }
  .node-head { cursor: pointer; display: flex; gap: 8px; align-items: baseline; }
  .badge { font-size: 11px; padding: 2px 6px; border-radius: 4px; background: #eef2ff; color: #4338ca; }
  .title { font-weight: 600; }
  .meta { color: #6b7280; font-size: 12px; }
  .sysinfo { white-space: pre-wrap; background: #fffbeb; border: 1px solid #fde68a; border-radius: 6px; padding: 8px 10px; margin: 6px 0 10px; font-size: 13px; direction: auto; }
  .empty { color: #9ca3af; font-style: italic; font-size: 12px; }
  .error { color: #b91c1c; font-size: 12px; }
  .toggle { user-select: none; width: 14px; display: inline-block; }
  .hidden { display: none; }
</style>
</head>
<body>
<header><h1>TFS System Info Extractor</h1></header>
<main>
  <div class=""card"">
    <label><strong>Work Item IDs</strong> (comma / space / newline separated, or upload a file)</label>
    <textarea id=""ids"" placeholder=""46269, 44065, 42629""></textarea>
    <div class=""row"">
      <input type=""file"" id=""file"" accept="".txt,.csv"">
      <button id=""runBtn"">Run</button>
      <span id=""status""></span>
    </div>
  </div>

  <div class=""card"" id=""logCard"" style=""display:none"">
    <strong>Progress</strong>
    <div id=""log""></div>
  </div>

  <div class=""card"" id=""resultsCard"" style=""display:none"">
    <div class=""row"">
      <strong>Results</strong>
      <button class=""secondary"" id=""dlJson"">Download JSON</button>
      <button class=""secondary"" id=""dlMd"">Download Markdown</button>
    </div>
    <div class=""tree"" id=""tree""></div>
  </div>
</main>
<script>
document.getElementById('file').addEventListener('change', function (e) {
  var f = e.target.files[0];
  if (!f) return;
  var reader = new FileReader();
  reader.onload = function () { document.getElementById('ids').value = reader.result; };
  reader.readAsText(f, 'utf-8');
});

var currentJobId = null;
var pollTimer = null;

document.getElementById('runBtn').addEventListener('click', async function () {
  var ids = document.getElementById('ids').value;
  if (!ids.trim()) { alert('Enter at least one Work Item ID.'); return; }

  document.getElementById('runBtn').disabled = true;
  document.getElementById('status').textContent = 'Starting...';
  document.getElementById('logCard').style.display = 'block';
  document.getElementById('resultsCard').style.display = 'none';
  document.getElementById('log').textContent = '';

  var resp = await fetch('/api/run', { method: 'POST', body: ids });
  if (!resp.ok) {
    document.getElementById('status').textContent = 'Error: ' + (await resp.text());
    document.getElementById('runBtn').disabled = false;
    return;
  }
  var data = await resp.json();
  currentJobId = data.jobId;
  poll();
});

function poll() {
  pollTimer = setInterval(async function () {
    var resp = await fetch('/api/status?jobId=' + currentJobId);
    if (!resp.ok) return;
    var data = await resp.json();
    document.getElementById('log').textContent = data.log.join('\n');
    var logEl = document.getElementById('log');
    logEl.scrollTop = logEl.scrollHeight;
    document.getElementById('status').textContent = data.done
      ? ('Done - ' + data.processed + ' Work Items processed' + (data.error ? (' (error: ' + data.error + ')') : ''))
      : ('Running... ' + data.processed + ' processed so far');

    if (data.done) {
      clearInterval(pollTimer);
      document.getElementById('runBtn').disabled = false;
      if (data.hasFiles) {
        document.getElementById('resultsCard').style.display = 'block';
        loadTree();
      }
    }
  }, 700);
}

async function loadTree() {
  var resp = await fetch('/api/download?jobId=' + currentJobId + '&type=json');
  var data = await resp.json();
  var container = document.getElementById('tree');
  container.innerHTML = '';
  data.Roots.forEach(function (n) { container.appendChild(renderNode(n)); });
}

function renderNode(n) {
  var wrap = document.createElement('div');
  wrap.className = 'node';

  var head = document.createElement('div');
  head.className = 'node-head';

  var toggle = document.createElement('span');
  toggle.className = 'toggle';
  toggle.textContent = n.Children && n.Children.length ? '\u25be' : ' ';
  head.appendChild(toggle);

  var badge = document.createElement('span');
  badge.className = 'badge';
  badge.textContent = n.Type || '?';
  head.appendChild(badge);

  var title = document.createElement('span');
  title.className = 'title';
  title.textContent = '#' + n.Id + ' ' + (n.Title || '');
  head.appendChild(title);

  var meta = document.createElement('span');
  meta.className = 'meta';
  meta.textContent = n.State || '';
  head.appendChild(meta);

  wrap.appendChild(head);

  var body = document.createElement('div');

  if (n.Error) {
    var err = document.createElement('div');
    err.className = 'error';
    err.textContent = 'Error: ' + n.Error;
    body.appendChild(err);
  } else if (n.SystemInfo) {
    var si = document.createElement('div');
    si.className = 'sysinfo';
    si.textContent = n.SystemInfo;
    body.appendChild(si);
  } else {
    var empty = document.createElement('div');
    empty.className = 'empty';
    empty.textContent = '(no System Info)';
    body.appendChild(empty);
  }

  var childWrap = document.createElement('div');
  (n.Children || []).forEach(function (c) { childWrap.appendChild(renderNode(c)); });
  body.appendChild(childWrap);

  wrap.appendChild(body);

  head.addEventListener('click', function () { body.classList.toggle('hidden'); });

  return wrap;
}

document.getElementById('dlJson').addEventListener('click', function () {
  if (currentJobId) window.location.href = '/api/download?jobId=' + currentJobId + '&type=json';
});
document.getElementById('dlMd').addEventListener('click', function () {
  if (currentJobId) window.location.href = '/api/download?jobId=' + currentJobId + '&type=md';
});
</script>
</body>
</html>";
    }

    // =====================================================================
    // ENTRY POINT
    // =====================================================================
    internal static class Program
    {
        private static void Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("TFS System Info Extractor - local UI");
            Console.WriteLine($"Collection: {Config.TfsCollectionUrl}");

            var server = new WebServer();
            server.Start();

            Console.WriteLine("Press Ctrl+C to stop.");
            new ManualResetEvent(false).WaitOne();
        }
    }
}
