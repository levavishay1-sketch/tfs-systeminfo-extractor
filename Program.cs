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
        public const string TfsCollectionUrl = "http://192.168.160.17:8080/tfs/Altshuler%20Shaham%20IT";

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
            var values = (object[])parsed["value"];

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

            var relations = (object[])raw["relations"];
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
<html lang='en'>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width, initial-scale=1'>
<title>TFS System Info Extractor</title>
<style>
  * { box-sizing: border-box; }
  :root {
    --bg:#f5f6f8; --surface:#ffffff; --surface-2:#f9fafb; --border:#e5e7eb;
    --text:#1a1d23; --muted:#6b7280;
    --accent:#4f46e5; --accent-2:#4338ca;
    --ok:#059669; --bad:#dc2626;
    --si-bg:#fffbeb; --si-border:#f5e2a8; --si-text:#6b4d10;
    --radius:12px; color-scheme: light;
  }
  @media (prefers-color-scheme: dark) {
    :root {
      --bg:#0e1014; --surface:#161922; --surface-2:#1c202b; --border:#2a2f3c;
      --text:#e6e8ec; --muted:#9aa3b2;
      --accent:#6366f1; --accent-2:#a5b4fc;
      --ok:#34d399; --bad:#f87171;
      --si-bg:#241f10; --si-border:#4a3f22; --si-text:#ecdcab;
      color-scheme: dark;
    }
  }
  body { margin:0; background:var(--bg); color:var(--text);
    font:14px/1.55 'Segoe UI', system-ui, -apple-system, Roboto, Arial, sans-serif; }
  .topbar { background:linear-gradient(120deg, #312e81, #4f46e5); color:#fff; padding:18px 24px; }
  .brand { display:flex; align-items:center; gap:14px; max-width:1080px; margin:0 auto; }
  .logo { width:40px; height:40px; border-radius:10px; display:grid; place-items:center;
    background:rgba(255,255,255,.15); font-weight:700; letter-spacing:.5px; font-size:13px; }
  .brand-title { font-size:17px; font-weight:650; }
  .brand-sub { font-size:12.5px; opacity:.82; margin-top:2px; }
  main { max-width:1080px; margin:24px auto; padding:0 20px; display:flex; flex-direction:column; gap:18px; }
  .card { background:var(--surface); border:1px solid var(--border); border-radius:var(--radius);
    padding:18px 20px; box-shadow:0 1px 2px rgba(0,0,0,.04); }
  .card-head { display:flex; align-items:center; justify-content:space-between; gap:12px;
    margin-bottom:12px; flex-wrap:wrap; }
  .card-head h2 { margin:0; font-size:12.5px; font-weight:700; text-transform:uppercase;
    letter-spacing:.7px; color:var(--muted); }
  .hint { font-size:12px; color:var(--muted); }
  textarea { width:100%; min-height:96px; resize:vertical; padding:12px 14px; border:1px solid var(--border);
    border-radius:10px; background:var(--surface-2); color:var(--text);
    font:13px/1.5 'Cascadia Code', Consolas, monospace; }
  textarea:focus, .btn:focus-visible, .file-btn input:focus-visible + span {
    outline:2px solid var(--accent); outline-offset:1px; }
  .actions { display:flex; gap:10px; margin-top:12px; flex-wrap:wrap; align-items:center; }
  .btn { border:1px solid transparent; border-radius:9px; padding:9px 18px; font:inherit;
    font-weight:600; cursor:pointer; transition:background .12s, border-color .12s, opacity .12s; }
  .btn.sm { padding:6px 12px; font-size:12.5px; }
  .btn.primary { background:var(--accent); color:#fff; }
  .btn.primary:hover { background:var(--accent-2); }
  .btn.primary:disabled { opacity:.55; cursor:default; }
  .btn.ok { background:var(--ok); color:#fff; }
  .btn.ok:hover { filter:brightness(1.06); }
  .btn.ghost { background:var(--surface-2); border-color:var(--border); color:var(--text); }
  .btn.ghost:hover { border-color:var(--muted); }
  .file-btn { position:relative; overflow:hidden; display:inline-block; }
  .file-btn input { position:absolute; inset:0; opacity:0; cursor:pointer; width:100%; height:100%; }
  .file-btn span { display:inline-block; padding:9px 18px; border-radius:9px; background:var(--surface-2);
    border:1px solid var(--border); font-weight:600; }
  .status { font-size:12.5px; font-weight:600; padding:4px 11px; border-radius:999px; }
  .status-idle { color:var(--muted); }
  .status-running { color:var(--accent-2); background:rgba(99,102,241,.14); }
  .status-done { color:var(--ok); background:rgba(5,150,105,.15); }
  .status-error { color:var(--bad); background:rgba(220,38,38,.13); }
  .log { margin:0; background:#0b0e14; color:#c9d5c3; border-radius:10px; padding:14px; height:260px;
    overflow:auto; white-space:pre-wrap; word-break:break-word;
    font:12px/1.55 'Cascadia Code', Consolas, monospace; }
  .toolbar { display:flex; gap:8px; align-items:center; flex-wrap:wrap; }
  .stats { display:flex; gap:6px; margin-right:6px; flex-wrap:wrap; }
  .stat { font-size:12px; color:var(--muted); background:var(--surface-2); border:1px solid var(--border);
    padding:4px 9px; border-radius:999px; }
  .stat b { color:var(--text); }
  .stat.ok b { color:var(--ok); }
  .stat.bad b { color:var(--bad); }
  .tree { margin-top:4px; }
  .node-head { display:flex; align-items:center; gap:8px; padding:6px 8px; border-radius:8px; }
  .node.has-kids > .node-head { cursor:pointer; }
  .node-head:hover { background:var(--surface-2); }
  .twist { flex:none; width:14px; height:14px; padding:0; border:none; background:none; cursor:pointer;
    position:relative; }
  .node.has-kids > .node-head > .twist::before { content:''; position:absolute; top:50%; left:50%;
    width:0; height:0; border-left:5px solid var(--muted); border-top:4px solid transparent;
    border-bottom:4px solid transparent; transform:translate(-50%,-50%); transition:transform .12s; }
  .node.has-kids.open > .node-head > .twist::before { transform:translate(-50%,-50%) rotate(90deg); }
  .badge { font-size:11px; font-weight:650; padding:2px 8px; border-radius:6px; white-space:nowrap; flex:none;
    background:hsl(var(--h) 60% 92%); color:hsl(var(--h) 55% 32%); }
  @media (prefers-color-scheme: dark) {
    .badge { background:hsl(var(--h) 38% 24%); color:hsl(var(--h) 70% 78%); }
  }
  .wid { font-size:12.5px; font-weight:600; color:var(--accent-2); text-decoration:none; white-space:nowrap; flex:none; }
  .wid:hover { text-decoration:underline; }
  .ntitle { flex:1; min-width:0; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
  .state { font-size:11px; color:var(--muted); border:1px solid var(--border); padding:1px 7px;
    border-radius:999px; white-space:nowrap; flex:none; }
  .kidcount { font-size:11px; font-weight:600; color:var(--muted); background:var(--surface-2);
    border:1px solid var(--border); min-width:20px; text-align:center; border-radius:999px; padding:1px 5px; flex:none; }
  .node-body { padding-left:22px; margin-left:7px; border-left:1px solid var(--border); }
  .node.has-kids:not(.open) > .node-body { display:none; }
  .sysinfo { position:relative; background:var(--si-bg); border:1px solid var(--si-border); color:var(--si-text);
    border-radius:8px; padding:10px 40px 10px 12px; margin:4px 0 8px; }
  .sysinfo.err { background:rgba(220,38,38,.10); border-color:var(--bad); color:var(--bad); }
  .sysinfo-text { white-space:pre-wrap; word-break:break-word; font-size:13px; }
  .copy { position:absolute; top:6px; right:8px; font-size:11px; font-weight:600; border:1px solid var(--si-border);
    background:var(--surface); color:var(--muted); border-radius:6px; padding:2px 8px; cursor:pointer; }
  .copy:hover { color:var(--text); }
  .noinfo { font-size:12px; color:var(--muted); font-style:italic; margin:2px 0 6px; }
  .placeholder { color:var(--muted); font-size:13px; padding:8px 0; }
  @media (max-width:640px) { .ntitle { white-space:normal; } }
</style>
</head>
<body>
<header class='topbar'>
  <div class='brand'>
    <div class='logo'>TFS</div>
    <div>
      <div class='brand-title'>System Info Extractor</div>
      <div class='brand-sub'>Walks every Work Item child recursively and extracts the System Info field</div>
    </div>
  </div>
</header>
<main>
  <section class='card'>
    <div class='card-head'>
      <h2>Work Item IDs</h2>
      <span class='hint' id='idCount'></span>
    </div>
    <textarea id='ids' spellcheck='false' placeholder='46269, 44065, 42629 - or one per line'></textarea>
    <div class='actions'>
      <label class='file-btn'><input type='file' id='file' accept='.txt,.csv'><span>Upload file</span></label>
      <button class='btn ghost' id='clearBtn' type='button'>Clear</button>
      <button class='btn primary' id='runBtn' type='button'>Run</button>
    </div>
  </section>

  <section class='card' id='progressCard' hidden>
    <div class='card-head'>
      <h2>Progress</h2>
      <span class='status status-idle' id='status'></span>
    </div>
    <pre class='log' id='log'></pre>
  </section>

  <section class='card' id='resultsCard' hidden>
    <div class='card-head'>
      <h2>Results</h2>
      <div class='toolbar'>
        <div class='stats'>
          <span class='stat'><b id='statTotal'>0</b> items</span>
          <span class='stat ok'><b id='statInfo'>0</b> with info</span>
          <span class='stat bad' id='statErrWrap' hidden><b id='statErr'>0</b> errors</span>
        </div>
        <button class='btn ghost sm' id='expandAll' type='button'>Expand all</button>
        <button class='btn ghost sm' id='collapseAll' type='button'>Collapse all</button>
        <button class='btn ok sm' id='dlJson' type='button'>JSON</button>
        <button class='btn ok sm' id='dlMd' type='button'>Markdown</button>
      </div>
    </div>
    <div class='tree' id='tree'></div>
  </section>
</main>
<script>
const $ = s => document.querySelector(s);
let jobId = null, timer = null;

$('#file').addEventListener('change', e => {
  const f = e.target.files[0]; if (!f) return;
  const r = new FileReader();
  r.onload = () => { $('#ids').value = r.result; updateCount(); };
  r.readAsText(f, 'utf-8');
});
$('#ids').addEventListener('input', updateCount);
function updateCount() {
  const n = ($('#ids').value.match(/\d+/g) || []).length;
  $('#idCount').textContent = n ? (n + (n === 1 ? ' id' : ' ids')) : '';
}
$('#clearBtn').addEventListener('click', () => { $('#ids').value = ''; $('#file').value = ''; updateCount(); $('#ids').focus(); });
$('#runBtn').addEventListener('click', run);

async function run() {
  const ids = $('#ids').value;
  if (!ids.trim()) { $('#ids').focus(); return; }
  $('#runBtn').disabled = true;
  $('#runBtn').textContent = 'Running...';
  setStatus('running', 'Starting');
  $('#progressCard').hidden = false;
  $('#resultsCard').hidden = true;
  $('#log').textContent = '';
  let resp;
  try { resp = await fetch('/api/run', { method: 'POST', body: ids }); }
  catch (e) { fail('Cannot reach the local server.'); return; }
  if (!resp.ok) { fail(await resp.text()); return; }
  jobId = (await resp.json()).jobId;
  timer = setInterval(poll, 700);
}
function fail(msg) {
  setStatus('error', msg || 'Error');
  $('#runBtn').disabled = false;
  $('#runBtn').textContent = 'Run';
}
async function poll() {
  let data;
  try {
    const r = await fetch('/api/status?jobId=' + jobId);
    if (!r.ok) return;
    data = await r.json();
  } catch (e) { return; }
  const log = $('#log');
  const atBottom = log.scrollHeight - log.scrollTop - log.clientHeight < 40;
  log.textContent = data.log.join('\n');
  if (atBottom) log.scrollTop = log.scrollHeight;
  if (!data.done) { setStatus('running', data.processed + ' work items processed'); return; }
  clearInterval(timer);
  $('#runBtn').disabled = false;
  $('#runBtn').textContent = 'Run';
  if (data.error) setStatus('error', data.error);
  else setStatus('done', 'Done - ' + data.processed + ' work items');
  if (data.hasFiles) { $('#resultsCard').hidden = false; loadTree(); }
}
function setStatus(kind, text) {
  const el = $('#status');
  el.className = 'status status-' + kind;
  el.textContent = text;
}

let stats;
async function loadTree() {
  const tree = $('#tree');
  const r = await fetch('/api/download?jobId=' + jobId + '&type=json');
  const data = await r.json();
  stats = { total: 0, withInfo: 0, errors: 0 };
  tree.innerHTML = '';
  data.Roots.forEach(n => tree.appendChild(renderNode(n, 0)));
  $('#statTotal').textContent = stats.total;
  $('#statInfo').textContent = stats.withInfo;
  $('#statErr').textContent = stats.errors;
  $('#statErrWrap').hidden = stats.errors === 0;
}
function hueOf(type) {
  let h = 0;
  for (const c of (type || '?')) h = (h * 31 + c.charCodeAt(0)) % 360;
  return h;
}
function renderNode(n, depth) {
  stats.total++;
  if (n.Error) stats.errors++;
  else if (n.SystemInfo) stats.withInfo++;

  const hasKids = !!(n.Children && n.Children.length);
  const node = document.createElement('div');
  node.className = 'node' + (hasKids ? ' has-kids' : '');

  const head = document.createElement('div');
  head.className = 'node-head';

  const tw = document.createElement('button');
  tw.className = 'twist';
  tw.type = 'button';
  tw.setAttribute('aria-label', 'toggle');
  tw.style.visibility = hasKids ? 'visible' : 'hidden';
  head.appendChild(tw);

  const badge = document.createElement('span');
  badge.className = 'badge';
  badge.style.setProperty('--h', hueOf(n.Type));
  badge.textContent = n.Type || '?';
  head.appendChild(badge);

  const id = document.createElement('a');
  id.className = 'wid';
  id.textContent = '#' + n.Id;
  if (n.Url) { id.href = n.Url; id.target = '_blank'; id.rel = 'noopener'; }
  head.appendChild(id);

  const title = document.createElement('span');
  title.className = 'ntitle';
  title.dir = 'auto';
  title.textContent = n.Title || '';
  head.appendChild(title);

  if (n.State) {
    const st = document.createElement('span');
    st.className = 'state';
    st.textContent = n.State;
    head.appendChild(st);
  }
  if (hasKids) {
    const cc = document.createElement('span');
    cc.className = 'kidcount';
    cc.textContent = n.Children.length;
    head.appendChild(cc);
  }
  node.appendChild(head);

  const body = document.createElement('div');
  body.className = 'node-body';

  if (n.Error) {
    const e = document.createElement('div');
    e.className = 'sysinfo err';
    const t = document.createElement('div');
    t.className = 'sysinfo-text';
    t.textContent = n.Error;
    e.appendChild(t);
    body.appendChild(e);
  } else if (n.SystemInfo) {
    const wrap = document.createElement('div');
    wrap.className = 'sysinfo';
    const cp = document.createElement('button');
    cp.className = 'copy';
    cp.type = 'button';
    cp.textContent = 'Copy';
    cp.addEventListener('click', ev => {
      ev.stopPropagation();
      if (navigator.clipboard) navigator.clipboard.writeText(n.SystemInfo);
      cp.textContent = 'Copied';
      setTimeout(() => { cp.textContent = 'Copy'; }, 1200);
    });
    const t = document.createElement('div');
    t.className = 'sysinfo-text';
    t.dir = 'auto';
    t.textContent = n.SystemInfo;
    wrap.appendChild(cp);
    wrap.appendChild(t);
    body.appendChild(wrap);
  } else {
    const em = document.createElement('div');
    em.className = 'noinfo';
    em.textContent = 'No System Info';
    body.appendChild(em);
  }

  if (hasKids) {
    const kids = document.createElement('div');
    n.Children.forEach(c => kids.appendChild(renderNode(c, depth + 1)));
    body.appendChild(kids);
  }
  node.appendChild(body);

  if (hasKids) {
    head.addEventListener('click', e => {
      if (e.target.closest('a')) return;
      node.classList.toggle('open');
    });
    if (depth < 1) node.classList.add('open');
  }
  return node;
}
$('#expandAll').addEventListener('click', () => setAll(true));
$('#collapseAll').addEventListener('click', () => setAll(false));
function setAll(open) {
  document.querySelectorAll('#tree .node.has-kids').forEach(n => n.classList.toggle('open', open));
}
$('#dlJson').addEventListener('click', () => { if (jobId) location.href = '/api/download?jobId=' + jobId + '&type=json'; });
$('#dlMd').addEventListener('click', () => { if (jobId) location.href = '/api/download?jobId=' + jobId + '&type=md'; });
updateCount();
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
