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
  a { color:var(--accent-2); }
  .topbar { background:linear-gradient(120deg, #312e81, #4f46e5); color:#fff; padding:18px 24px; }
  .brand { display:flex; align-items:center; gap:14px; max-width:1140px; margin:0 auto; }
  .logo { width:40px; height:40px; border-radius:10px; display:grid; place-items:center;
    background:rgba(255,255,255,.15); font-weight:700; letter-spacing:.5px; font-size:13px; }
  .brand-title { font-size:17px; font-weight:650; }
  .brand-sub { font-size:12.5px; opacity:.82; margin-top:2px; }
  main { max-width:1140px; margin:24px auto; padding:0 20px; display:flex; flex-direction:column; gap:18px; }
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
  textarea:focus, .btn:focus-visible, .vbtn:focus-visible { outline:2px solid var(--accent); outline-offset:1px; }
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

  /* results toolbar */
  .results-top { display:flex; flex-direction:column; gap:12px; margin-bottom:14px; }
  .switch { display:flex; gap:4px; flex-wrap:wrap; background:var(--surface-2); border:1px solid var(--border);
    border-radius:10px; padding:4px; }
  .vbtn { border:none; background:none; color:var(--muted); font:inherit; font-weight:600; font-size:12.5px;
    padding:6px 12px; border-radius:7px; cursor:pointer; }
  .vbtn:hover { color:var(--text); }
  .vbtn.active { background:var(--surface); color:var(--accent-2); box-shadow:0 1px 2px rgba(0,0,0,.08); }
  .bar2 { display:flex; gap:8px; align-items:center; flex-wrap:wrap; }
  .stats { display:flex; gap:6px; flex-wrap:wrap; margin-right:auto; }
  .stat { font-size:12px; color:var(--muted); background:var(--surface-2); border:1px solid var(--border);
    padding:4px 9px; border-radius:999px; }
  .stat b { color:var(--text); }
  .stat.ok b { color:var(--ok); }
  .stat.bad b { color:var(--bad); }

  .badge { font-size:11px; font-weight:650; padding:2px 8px; border-radius:6px; white-space:nowrap; flex:none;
    background:hsl(var(--h) 60% 92%); color:hsl(var(--h) 55% 32%); }
  @media (prefers-color-scheme: dark) { .badge { background:hsl(var(--h) 38% 24%); color:hsl(var(--h) 70% 78%); } }
  .wid { font-size:12.5px; font-weight:600; color:var(--accent-2); text-decoration:none; white-space:nowrap; flex:none; }
  .wid:hover { text-decoration:underline; }
  .state { font-size:11px; color:var(--muted); border:1px solid var(--border); padding:1px 7px;
    border-radius:999px; white-space:nowrap; flex:none; }
  .sysinfo-text { white-space:pre-wrap; word-break:break-word; }
  .empty { color:var(--muted); font-style:italic; padding:20px; text-align:center; }

  /* view: compact */
  .view-compact .crow { display:flex; align-items:center; gap:8px; padding:3px 8px; border-radius:6px;
    font-size:12.5px; min-height:24px; }
  .view-compact .crow:hover { background:var(--surface-2); }
  .view-compact .dot { width:8px; height:8px; border-radius:50%; flex:none; }
  .view-compact .cid { color:var(--accent-2); font-weight:600; flex:none; }
  .view-compact .ctitle { overflow:hidden; text-overflow:ellipsis; white-space:nowrap; flex:1; min-width:0; }
  .view-compact .cflag { flex:none; font-size:10px; font-weight:700; width:15px; height:15px; border-radius:4px;
    display:grid; place-items:center; }
  .view-compact .cflag.ok { background:rgba(5,150,105,.16); color:var(--ok); }
  .view-compact .cflag.err { background:rgba(220,38,38,.16); color:var(--bad); }

  /* view: tree */
  .view-tree .node-head { display:flex; align-items:center; gap:8px; padding:6px 8px; border-radius:8px; }
  .view-tree .node.has-kids > .node-head { cursor:pointer; }
  .view-tree .node-head:hover { background:var(--surface-2); }
  .view-tree .twist { flex:none; width:14px; height:14px; padding:0; border:none; background:none;
    cursor:pointer; position:relative; }
  .view-tree .node.has-kids > .node-head > .twist::before { content:''; position:absolute; top:50%; left:50%;
    width:0; height:0; border-left:5px solid var(--muted); border-top:4px solid transparent;
    border-bottom:4px solid transparent; transform:translate(-50%,-50%); transition:transform .12s; }
  .view-tree .node.has-kids.open > .node-head > .twist::before { transform:translate(-50%,-50%) rotate(90deg); }
  .view-tree .ntitle { flex:1; min-width:0; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
  .view-tree .kidcount { font-size:11px; font-weight:600; color:var(--muted); background:var(--surface-2);
    border:1px solid var(--border); min-width:20px; text-align:center; border-radius:999px; padding:1px 5px; flex:none; }
  .view-tree .node-body { padding-left:22px; margin-left:7px; border-left:1px solid var(--border); }
  .view-tree .node.has-kids:not(.open) > .node-body { display:none; }
  .view-tree .sysinfo { position:relative; background:var(--si-bg); border:1px solid var(--si-border);
    color:var(--si-text); border-radius:8px; padding:10px 40px 10px 12px; margin:4px 0 8px; font-size:13px; }
  .view-tree .sysinfo.err { background:rgba(220,38,38,.10); border-color:var(--bad); color:var(--bad); }
  .view-tree .noinfo { font-size:12px; color:var(--muted); font-style:italic; margin:2px 0 6px; }
  .copy { position:absolute; top:6px; right:8px; font-size:11px; font-weight:600; border:1px solid var(--si-border);
    background:var(--surface); color:var(--muted); border-radius:6px; padding:2px 8px; cursor:pointer; }
  .copy:hover { color:var(--text); }

  /* view: cards */
  .view-cards .grid { display:grid; grid-template-columns:repeat(auto-fill, minmax(280px, 1fr)); gap:12px; }
  .view-cards .wcard { border:1px solid var(--border); border-radius:10px; padding:12px 14px; background:var(--surface-2); }
  .view-cards .wcard.err { border-color:var(--bad); }
  .view-cards .wc-head { display:flex; align-items:center; gap:8px; flex-wrap:wrap; margin-bottom:6px; }
  .view-cards .wc-title { font-weight:600; margin-bottom:6px; }
  .view-cards .wc-si { font-size:12.5px; color:var(--si-text); background:var(--si-bg); border:1px solid var(--si-border);
    border-radius:8px; padding:8px 10px; white-space:pre-wrap; word-break:break-word;
    display:-webkit-box; -webkit-line-clamp:5; -webkit-box-orient:vertical; overflow:hidden; }
  .view-cards .wc-si.err { color:var(--bad); background:rgba(220,38,38,.10); border-color:var(--bad); }
  .view-cards .wc-none { font-size:12px; color:var(--muted); font-style:italic; }
  .view-cards .wc-foot { margin-top:8px; font-size:11px; color:var(--muted); display:flex; gap:10px; }

  /* view: table */
  .view-table .twrap { overflow-x:auto; }
  .view-table table { width:100%; border-collapse:collapse; font-size:12.5px; }
  .view-table th, .view-table td { text-align:left; padding:7px 10px; border-bottom:1px solid var(--border);
    vertical-align:top; }
  .view-table th { color:var(--muted); font-size:11px; text-transform:uppercase; letter-spacing:.5px; position:sticky; top:0;
    background:var(--surface); cursor:pointer; white-space:nowrap; }
  .view-table tr:hover td { background:var(--surface-2); }
  .view-table td.t-title { max-width:340px; }
  .view-table td.t-si { max-width:420px; color:var(--muted); }
  .view-table .clip { overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
  .view-table .yes { color:var(--ok); font-weight:600; }
  .view-table .no { color:var(--muted); }
  .view-table .er { color:var(--bad); font-weight:600; }

  /* view: detailed */
  .view-detailed .drow { border:1px solid var(--border); border-radius:10px; padding:16px 18px; margin-bottom:14px;
    background:var(--surface-2); }
  .view-detailed .drow.err { border-color:var(--bad); }
  .view-detailed .d-head { display:flex; align-items:center; gap:10px; flex-wrap:wrap; margin-bottom:10px; }
  .view-detailed .d-head .d-title { font-size:15px; font-weight:650; }
  .view-detailed dl { display:grid; grid-template-columns:130px 1fr; gap:6px 14px; margin:0 0 12px; font-size:13px; }
  .view-detailed dt { color:var(--muted); font-weight:600; }
  .view-detailed dd { margin:0; word-break:break-word; }
  .view-detailed .d-si { position:relative; background:var(--si-bg); border:1px solid var(--si-border); color:var(--si-text);
    border-radius:8px; padding:12px 40px 12px 14px; white-space:pre-wrap; word-break:break-word; font-size:13px; }
  .view-detailed .d-si.err { background:rgba(220,38,38,.10); border-color:var(--bad); color:var(--bad); }
  .view-detailed .d-si.none { color:var(--muted); font-style:italic; background:none; border-style:dashed; }

  /* view: outline */
  .view-outline .o-item { margin-bottom:16px; }
  .view-outline .o-h { font-weight:650; }
  .view-outline .o-h a { text-decoration:none; }
  .view-outline .o-meta { font-size:11.5px; color:var(--muted); margin:2px 0 6px; }
  .view-outline .o-si { white-space:pre-wrap; word-break:break-word; font-size:13px; padding-left:2px;
    border-left:2px solid var(--si-border); padding-left:12px; }
  .view-outline .o-si.err { border-color:var(--bad); color:var(--bad); }
  .view-outline .o-si.none { color:var(--muted); font-style:italic; border-color:var(--border); }

  /* view: focus */
  .view-focus .f-item { border:1px solid var(--si-border); border-radius:10px; margin-bottom:14px; overflow:hidden; }
  .view-focus .f-head { background:var(--si-bg); padding:10px 14px; display:flex; align-items:center; gap:10px;
    flex-wrap:wrap; border-bottom:1px solid var(--si-border); }
  .view-focus .f-title { font-weight:650; color:var(--si-text); }
  .view-focus .f-path { font-size:11px; color:var(--muted); width:100%; }
  .view-focus .f-si { position:relative; padding:14px 44px 14px 16px; white-space:pre-wrap; word-break:break-word;
    font-size:14px; line-height:1.6; }
  .view-focus .f-copy { position:absolute; top:10px; right:12px; }

  @media (max-width:640px) {
    .view-detailed dl { grid-template-columns:1fr; }
    .view-table td.t-si { display:none; }
    .view-table th.h-si { display:none; }
  }
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
    <div class='card-head'><h2>Results</h2></div>
    <div class='results-top'>
      <div class='switch' id='viewSwitch'></div>
      <div class='bar2'>
        <div class='stats'>
          <span class='stat'><b id='statTotal'>0</b> items</span>
          <span class='stat ok'><b id='statInfo'>0</b> with info</span>
          <span class='stat bad' id='statErrWrap' hidden><b id='statErr'>0</b> errors</span>
          <span class='stat'>depth <b id='statDepth'>0</b></span>
        </div>
        <button class='btn ghost sm' id='expandAll' type='button' hidden>Expand all</button>
        <button class='btn ghost sm' id='collapseAll' type='button' hidden>Collapse all</button>
        <button class='btn ok sm' id='dlJson' type='button'>JSON</button>
        <button class='btn ok sm' id='dlMd' type='button'>Markdown</button>
      </div>
    </div>
    <div class='viewport' id='viewport'></div>
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
  if (data.hasFiles) { $('#resultsCard').hidden = false; loadResults(); }
}
function setStatus(kind, text) {
  const el = $('#status');
  el.className = 'status status-' + kind;
  el.textContent = text;
}

/* ---------- results + views ---------- */
const VIEWS = ['compact', 'tree', 'cards', 'table', 'detailed', 'outline', 'focus'];
const LABELS = { compact:'Compact', tree:'Tree', cards:'Cards', table:'Table', detailed:'Detailed', outline:'Outline', focus:'Info Focus' };
let currentView = 'tree';
try { const s = localStorage.getItem('tfsView'); if (s && VIEWS.indexOf(s) >= 0) currentView = s; } catch (e) {}
let roots = null, flat = [], byId = {}, stats = null;

const sw = $('#viewSwitch');
VIEWS.forEach(v => {
  const b = document.createElement('button');
  b.className = 'vbtn';
  b.type = 'button';
  b.dataset.view = v;
  b.textContent = LABELS[v];
  b.addEventListener('click', () => setView(v));
  sw.appendChild(b);
});
function setView(v) {
  currentView = v;
  try { localStorage.setItem('tfsView', v); } catch (e) {}
  document.querySelectorAll('#viewSwitch .vbtn').forEach(b => b.classList.toggle('active', b.dataset.view === v));
  const isTree = v === 'tree';
  $('#expandAll').hidden = !isTree;
  $('#collapseAll').hidden = !isTree;
  if (roots) render();
}

async function loadResults() {
  const r = await fetch('/api/download?jobId=' + jobId + '&type=json');
  const data = await r.json();
  roots = data.Roots || [];
  flat = [];
  byId = {};
  (function rec(nodes, depth, parent) {
    nodes.forEach(n => {
      n._depth = depth;
      n._parent = parent;
      byId[n.Id] = n;
      flat.push(n);
      if (n.Children && n.Children.length) rec(n.Children, depth + 1, n.Id);
    });
  })(roots, 0, null);

  stats = { total: flat.length, withInfo: 0, errors: 0, depth: 0 };
  flat.forEach(n => {
    if (n.Error) stats.errors++;
    else if (hasInfo(n)) stats.withInfo++;
    if (n._depth > stats.depth) stats.depth = n._depth;
  });
  $('#statTotal').textContent = stats.total;
  $('#statInfo').textContent = stats.withInfo;
  $('#statErr').textContent = stats.errors;
  $('#statErrWrap').hidden = stats.errors === 0;
  $('#statDepth').textContent = stats.depth;

  setView(currentView);
}

function hasInfo(n) { return !n.Error && n.SystemInfo != null && String(n.SystemInfo).trim() !== ''; }
function hue(t) { let h = 0; for (const c of (t || '?')) h = (h * 31 + c.charCodeAt(0)) % 360; return h; }
function el(tag, cls, text) {
  const e = document.createElement(tag);
  if (cls) e.className = cls;
  if (text != null) e.textContent = text;
  return e;
}
function badge(n) { const b = el('span', 'badge', n.Type || '?'); b.style.setProperty('--h', hue(n.Type)); return b; }
function widLink(n, cls) {
  const a = el('a', cls || 'wid', '#' + n.Id);
  if (n.Url) { a.href = n.Url; a.target = '_blank'; a.rel = 'noopener'; }
  return a;
}
function copyBtn(text, cls) {
  const b = el('button', (cls || 'copy'), 'Copy');
  b.type = 'button';
  b.addEventListener('click', ev => {
    ev.stopPropagation();
    if (navigator.clipboard) navigator.clipboard.writeText(text);
    b.textContent = 'Copied';
    setTimeout(() => { b.textContent = 'Copy'; }, 1200);
  });
  return b;
}
function pathOf(n) {
  const parts = [];
  let cur = byId[n._parent];
  let guard = 0;
  while (cur && guard++ < 50) { parts.unshift('#' + cur.Id + ' ' + (cur.Title || '')); cur = byId[cur._parent]; }
  return parts.join('  >  ');
}

function render() {
  const vp = $('#viewport');
  vp.className = 'viewport view-' + currentView;
  vp.innerHTML = '';
  const fn = { compact: vCompact, tree: vTree, cards: vCards, table: vTable, detailed: vDetailed, outline: vOutline, focus: vFocus }[currentView];
  fn(vp);
}

/* 1. compact */
function vCompact(vp) {
  flat.forEach(n => {
    const row = el('div', 'crow');
    row.style.paddingLeft = (8 + n._depth * 16) + 'px';
    const d = el('span', 'dot');
    d.style.background = 'hsl(' + hue(n.Type) + ' 60% 55%)';
    row.appendChild(d);
    row.appendChild(el('span', 'cid', '#' + n.Id));
    const t = el('span', 'ctitle', n.Title || '(untitled)');
    t.dir = 'auto';
    t.title = n.Type + ' - ' + (n.State || '');
    row.appendChild(t);
    if (n.Error) row.appendChild(el('span', 'cflag err', '!'));
    else if (hasInfo(n)) row.appendChild(el('span', 'cflag ok', 'i'));
    vp.appendChild(row);
  });
}

/* 2. tree */
function vTree(vp) { roots.forEach(n => vp.appendChild(treeNode(n))); }
function treeNode(n) {
  const hasKids = !!(n.Children && n.Children.length);
  const node = el('div', 'node' + (hasKids ? ' has-kids' : ''));
  const head = el('div', 'node-head');
  const tw = el('button', 'twist'); tw.type = 'button'; tw.style.visibility = hasKids ? 'visible' : 'hidden';
  head.appendChild(tw);
  head.appendChild(badge(n));
  head.appendChild(widLink(n));
  const t = el('span', 'ntitle', n.Title || ''); t.dir = 'auto'; head.appendChild(t);
  if (n.State) head.appendChild(el('span', 'state', n.State));
  if (hasKids) head.appendChild(el('span', 'kidcount', n.Children.length));
  node.appendChild(head);

  const body = el('div', 'node-body');
  if (n.Error) {
    const e = el('div', 'sysinfo err'); e.appendChild(el('div', 'sysinfo-text', n.Error)); body.appendChild(e);
  } else if (hasInfo(n)) {
    const w = el('div', 'sysinfo');
    w.appendChild(copyBtn(n.SystemInfo));
    const tx = el('div', 'sysinfo-text', n.SystemInfo); tx.dir = 'auto';
    w.appendChild(tx); body.appendChild(w);
  } else {
    body.appendChild(el('div', 'noinfo', 'No System Info'));
  }
  if (hasKids) { const k = el('div'); n.Children.forEach(c => k.appendChild(treeNode(c))); body.appendChild(k); }
  node.appendChild(body);

  if (hasKids) {
    head.addEventListener('click', e => { if (!e.target.closest('a')) node.classList.toggle('open'); });
    if (n._depth < 1) node.classList.add('open');
  }
  return node;
}
$('#expandAll').addEventListener('click', () => document.querySelectorAll('#viewport .node.has-kids').forEach(n => n.classList.add('open')));
$('#collapseAll').addEventListener('click', () => document.querySelectorAll('#viewport .node.has-kids').forEach(n => n.classList.remove('open')));

/* 3. cards */
function vCards(vp) {
  const grid = el('div', 'grid');
  flat.forEach(n => {
    const c = el('div', 'wcard' + (n.Error ? ' err' : ''));
    const h = el('div', 'wc-head');
    h.appendChild(badge(n));
    h.appendChild(widLink(n));
    if (n.State) h.appendChild(el('span', 'state', n.State));
    c.appendChild(h);
    const t = el('div', 'wc-title', n.Title || ''); t.dir = 'auto'; c.appendChild(t);
    if (n.Error) { const s = el('div', 'wc-si err', n.Error); c.appendChild(s); }
    else if (hasInfo(n)) { const s = el('div', 'wc-si', n.SystemInfo); s.dir = 'auto'; c.appendChild(s); }
    else c.appendChild(el('div', 'wc-none', 'No System Info'));
    const f = el('div', 'wc-foot');
    f.appendChild(el('span', null, 'depth ' + n._depth));
    f.appendChild(el('span', null, (n.Children ? n.Children.length : 0) + ' children'));
    c.appendChild(f);
    grid.appendChild(c);
  });
  vp.appendChild(grid);
}

/* 4. table */
function vTable(vp) {
  const wrap = el('div', 'twrap');
  const tbl = el('table');
  const thead = el('thead');
  const htr = el('tr');
  ['ID', 'Type', 'Title', 'State', 'Info', 'System Info'].forEach((h, i) => {
    const th = el('th', i === 5 ? 'h-si' : null, h);
    th.addEventListener('click', () => sortTable(i));
    htr.appendChild(th);
  });
  thead.appendChild(htr); tbl.appendChild(thead);
  const tb = el('tbody');
  tableRows().forEach(n => tb.appendChild(tableRow(n)));
  tbl.appendChild(tb);
  wrap.appendChild(tbl);
  vp.appendChild(wrap);
}
let sortKey = null, sortDir = 1;
function tableRows() {
  if (sortKey == null) return flat;
  const key = ['Id', 'Type', 'Title', 'State', 'info', 'SystemInfo'][sortKey];
  return flat.slice().sort((a, b) => {
    const va = key === 'info' ? (a.Error ? 2 : hasInfo(a) ? 1 : 0) : String(a[key] || '').toLowerCase();
    const vb = key === 'info' ? (b.Error ? 2 : hasInfo(b) ? 1 : 0) : String(b[key] || '').toLowerCase();
    return (va < vb ? -1 : va > vb ? 1 : 0) * sortDir;
  });
}
function sortTable(i) { if (sortKey === i) sortDir = -sortDir; else { sortKey = i; sortDir = 1; } render(); }
function tableRow(n) {
  const tr = el('tr');
  const c1 = el('td'); c1.appendChild(widLink(n)); tr.appendChild(c1);
  tr.appendChild(el('td', null, n.Type || ''));
  const c3 = el('td', 't-title');
  const inner = el('div', 'clip', n.Title || ''); inner.dir = 'auto';
  inner.style.paddingLeft = (n._depth * 14) + 'px';
  c3.appendChild(inner); tr.appendChild(c3);
  tr.appendChild(el('td', null, n.State || ''));
  const c5 = el('td');
  c5.appendChild(n.Error ? el('span', 'er', 'ERR') : hasInfo(n) ? el('span', 'yes', 'Yes') : el('span', 'no', 'No'));
  tr.appendChild(c5);
  const c6 = el('td', 't-si');
  const si = el('div', 'clip', n.Error ? n.Error : (n.SystemInfo || '').replace(/\s+/g, ' ').trim());
  si.title = n.Error || n.SystemInfo || '';
  c6.appendChild(si); tr.appendChild(c6);
  return tr;
}

/* 5. detailed */
function vDetailed(vp) {
  flat.forEach(n => {
    const d = el('div', 'drow' + (n.Error ? ' err' : ''));
    const h = el('div', 'd-head');
    h.appendChild(badge(n));
    h.appendChild(widLink(n));
    const t = el('span', 'd-title', n.Title || ''); t.dir = 'auto'; h.appendChild(t);
    if (n.State) h.appendChild(el('span', 'state', n.State));
    d.appendChild(h);

    const dl = el('dl');
    const add = (k, v, isLink) => {
      dl.appendChild(el('dt', null, k));
      if (isLink && v) { const dd = el('dd'); const a = el('a', null, v); a.href = v; a.target = '_blank'; a.rel = 'noopener'; dd.appendChild(a); dl.appendChild(dd); }
      else dl.appendChild(el('dd', null, v || '-'));
    };
    add('Depth', String(n._depth));
    add('Parent', n._parent != null ? ('#' + n._parent) : '(root)');
    add('Type', n.Type);
    add('State', n.State);
    add('Children', String(n.Children ? n.Children.length : 0));
    add('URL', n.Url, true);
    d.appendChild(dl);

    let si;
    if (n.Error) si = el('div', 'd-si err', n.Error);
    else if (hasInfo(n)) { si = el('div', 'd-si'); si.appendChild(copyBtn(n.SystemInfo)); const tx = el('div', 'sysinfo-text', n.SystemInfo); tx.dir = 'auto'; si.appendChild(tx); }
    else si = el('div', 'd-si none', 'No System Info recorded on this work item.');
    d.appendChild(si);
    vp.appendChild(d);
  });
}

/* 6. outline */
function vOutline(vp) {
  flat.forEach(n => {
    const item = el('div', 'o-item');
    item.style.marginLeft = (n._depth * 18) + 'px';
    const lvl = Math.min(n._depth + 2, 6);
    const h = el('h' + lvl, 'o-h');
    h.appendChild(widLink(n));
    h.appendChild(document.createTextNode(' ' + (n.Title || '')));
    item.appendChild(h);
    item.appendChild(el('div', 'o-meta', (n.Type || '?') + (n.State ? (' - ' + n.State) : '')));
    let si;
    if (n.Error) si = el('div', 'o-si err', n.Error);
    else if (hasInfo(n)) { si = el('div', 'o-si'); si.dir = 'auto'; si.textContent = n.SystemInfo; }
    else si = el('div', 'o-si none', '(no system info)');
    item.appendChild(si);
    vp.appendChild(item);
  });
}

/* 7. focus */
function vFocus(vp) {
  const items = flat.filter(hasInfo);
  if (!items.length) { vp.appendChild(el('div', 'empty', 'No work items had a System Info value.')); return; }
  items.forEach(n => {
    const box = el('div', 'f-item');
    const h = el('div', 'f-head');
    h.appendChild(badge(n));
    h.appendChild(widLink(n));
    const t = el('span', 'f-title', n.Title || ''); t.dir = 'auto'; h.appendChild(t);
    if (n.State) h.appendChild(el('span', 'state', n.State));
    const p = pathOf(n);
    if (p) h.appendChild(el('div', 'f-path', p));
    box.appendChild(h);
    const si = el('div', 'f-si');
    si.appendChild(copyBtn(n.SystemInfo, 'copy f-copy'));
    const tx = el('div', 'sysinfo-text', n.SystemInfo); tx.dir = 'auto';
    si.appendChild(tx);
    box.appendChild(si);
    vp.appendChild(box);
  });
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
