using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TfsSystemInfoExtractor.Core.Abstractions;
using TfsSystemInfoExtractor.Core.Model;
using TfsSystemInfoExtractor.Core.Model.SourceControl;
using TfsSystemInfoExtractor.Infrastructure.Configuration;

namespace TfsSystemInfoExtractor.Infrastructure.Tfs
{
    /// <summary>
    /// Resolves the source-control activity behind a work item's <c>ArtifactLink</c>
    /// relations against the TFS Git / TFVC REST APIs:
    /// <list type="bullet">
    ///   <item><c>vstfs:///Git/Commit/{project}/{repo}/{sha}</c> -> Git commit + repository (+ changed components)</item>
    ///   <item><c>vstfs:///VersionControl/Changeset/{id}</c> -> TFVC changeset (+ changed components)</item>
    /// </list>
    /// Pull-request and branch links are recognised but not yet expanded. Every call is
    /// best-effort: a link that will not resolve is logged and skipped, never thrown.
    /// </summary>
    public sealed class TfsGitSourceControlProvider : IWorkItemSourceControlProvider
    {
        private const string GitCommitPrefix = "vstfs:///Git/Commit/";
        private const string ChangesetPrefix = "vstfs:///VersionControl/Changeset/";

        private readonly TfsRestClient _client;
        private readonly TfsSourceControlOptions _options;
        private readonly ILogger<TfsGitSourceControlProvider> _logger;

        // repository id -> repository (or null when it could not be read). Cached for the
        // lifetime of one extraction so a repo shared by many work items is read once.
        private readonly ConcurrentDictionary<string, SourceRepository?> _repoCache =
            new ConcurrentDictionary<string, SourceRepository?>(StringComparer.OrdinalIgnoreCase);

        public TfsGitSourceControlProvider(
            TfsRestClient client,
            IOptions<TfsSourceControlOptions> options,
            ILogger<TfsGitSourceControlProvider> logger)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<SourceControlInfo> GetAsync(
            int workItemId,
            IReadOnlyList<WorkItemArtifactLink> artifactLinks,
            CancellationToken cancellationToken)
        {
            if (!_options.Enabled || artifactLinks == null || artifactLinks.Count == 0)
            {
                return SourceControlInfo.Empty;
            }

            var gitCommits = new List<(string RepoId, string Sha)>();
            var changesetIds = new List<int>();

            foreach (var link in artifactLinks)
            {
                var uri = link.Uri ?? string.Empty;
                if (TryParseGitCommit(uri, out var repoId, out var sha))
                {
                    gitCommits.Add((repoId, sha));
                }
                else if (uri.StartsWith(ChangesetPrefix, StringComparison.OrdinalIgnoreCase) &&
                         int.TryParse(uri.Substring(ChangesetPrefix.Length).Trim('/'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var csId))
                {
                    changesetIds.Add(csId);
                }
                else
                {
                    // pull requests / branches / builds - recognised but not expanded yet
                    _logger.LogDebug("Work item {WorkItemId}: artifact link not a commit or changeset: {Uri}", workItemId, uri);
                }
            }

            var budget = Math.Max(0, _options.MaxCommitsPerWorkItem);
            var distinctGit = gitCommits.Distinct().ToList();
            var distinctCs = changesetIds.Distinct().ToList();
            if (budget > 0 && distinctGit.Count + distinctCs.Count > budget)
            {
                _logger.LogInformation(
                    "Work item {WorkItemId} links {Count} commits/changesets; resolving the first {Budget}.",
                    workItemId, distinctGit.Count + distinctCs.Count, budget);
                distinctGit = distinctGit.Take(budget).ToList();
                distinctCs = distinctCs.Take(Math.Max(0, budget - distinctGit.Count)).ToList();
            }

            var repositories = new Dictionary<string, SourceRepository>(StringComparer.OrdinalIgnoreCase);
            var commits = new List<Commit>();
            var contributors = new Dictionary<string, Developer>(StringComparer.OrdinalIgnoreCase);
            var components = new Dictionary<string, SourceComponent>(StringComparer.OrdinalIgnoreCase);

            foreach (var (repoId, sha) in distinctGit)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var repo = await ResolveRepositoryAsync(repoId, cancellationToken).ConfigureAwait(false);
                    if (repo != null)
                    {
                        repositories[repo.Id] = repo;
                    }

                    var commit = await ReadGitCommitAsync(repoId, sha, cancellationToken).ConfigureAwait(false);
                    if (commit == null)
                    {
                        continue;
                    }

                    var changed = _options.IncludeComponents
                        ? await ReadGitCommitChangesAsync(repoId, sha, cancellationToken).ConfigureAwait(false)
                        : default;

                    commits.Add(new Commit(commit.Value.Sha, commit.Value.Message, commit.Value.Author,
                        commit.Value.Date, repoId, commit.Value.Url, changed.Components, changed.Paths));
                    Add(contributors, commit.Value.Author);
                    foreach (var name in changed.Components ?? Array.Empty<string>())
                    {
                        components[name] = new SourceComponent(name, repoId);
                    }
                }
                catch (Exception ex) when (!(ex is OperationCanceledException) && !(ex is TfsSystemInfoExtractor.Core.Exceptions.TfsAuthenticationRequiredException))
                {
                    _logger.LogWarning(ex, "Could not resolve Git commit {Sha} in repo {RepoId} for work item {WorkItemId}.", sha, repoId, workItemId);
                }
            }

            foreach (var changesetId in distinctCs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var cs = await ReadChangesetAsync(changesetId, cancellationToken).ConfigureAwait(false);
                    if (cs == null)
                    {
                        continue;
                    }

                    var changed = _options.IncludeComponents
                        ? await ReadChangesetChangesAsync(changesetId, cancellationToken).ConfigureAwait(false)
                        : default;

                    var repoId = "tfvc:" + (changed.Project ?? "root");
                    repositories[repoId] = new SourceRepository(repoId, changed.Project ?? "TFVC", null, "TfsVersionControl");

                    commits.Add(new Commit("C" + changesetId.ToString(CultureInfo.InvariantCulture), cs.Value.Message,
                        cs.Value.Author, cs.Value.Date, repoId, cs.Value.Url, changed.Components, changed.Paths));
                    Add(contributors, cs.Value.Author);
                    foreach (var name in changed.Components ?? Array.Empty<string>())
                    {
                        components[name] = new SourceComponent(name, repoId);
                    }
                }
                catch (Exception ex) when (!(ex is OperationCanceledException) && !(ex is TfsSystemInfoExtractor.Core.Exceptions.TfsAuthenticationRequiredException))
                {
                    _logger.LogWarning(ex, "Could not resolve TFVC changeset {ChangesetId} for work item {WorkItemId}.", changesetId, workItemId);
                }
            }

            if (repositories.Count == 0 && commits.Count == 0 && contributors.Count == 0 && components.Count == 0)
            {
                return SourceControlInfo.Empty;
            }

            return new SourceControlInfo(
                repositories.Values.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
                commits.OrderByDescending(c => c.CommittedOn ?? DateTimeOffset.MinValue),
                contributors.Values.OrderBy(d => d.DisplayName ?? d.Name, StringComparer.OrdinalIgnoreCase),
                components.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase));
        }

        // ---- Git -------------------------------------------------------------------

        private async Task<SourceRepository?> ResolveRepositoryAsync(string repoId, CancellationToken cancellationToken)
        {
            if (_repoCache.TryGetValue(repoId, out var cached))
            {
                return cached;
            }

            SourceRepository? repo = null;
            try
            {
                using var doc = await _client.GetJsonAsync($"_apis/git/repositories/{repoId}", null, cancellationToken).ConfigureAwait(false);
                var root = doc.RootElement;
                var name = GetString(root, "name") ?? repoId;
                var url = GetString(root, "remoteUrl") ?? GetString(root, "webUrl") ?? GetString(root, "url");
                repo = new SourceRepository(repoId, name, url, "TfsGit");
            }
            catch (Exception ex) when (!(ex is OperationCanceledException) && !(ex is TfsSystemInfoExtractor.Core.Exceptions.TfsAuthenticationRequiredException))
            {
                _logger.LogWarning(ex, "Could not read Git repository {RepoId}.", repoId);
            }

            _repoCache[repoId] = repo;
            return repo;
        }

        private async Task<(string Sha, string? Message, Developer Author, DateTimeOffset? Date, string? Url)?> ReadGitCommitAsync(
            string repoId, string sha, CancellationToken cancellationToken)
        {
            using var doc = await _client
                .GetJsonAsync($"_apis/git/repositories/{repoId}/commits/{sha}", null, cancellationToken)
                .ConfigureAwait(false);
            var root = doc.RootElement;

            var id = GetString(root, "commitId") ?? sha;
            var message = GetString(root, "comment");
            var url = GetString(root, "remoteUrl") ?? GetString(root, "url");

            var identity = root.TryGetProperty("author", out var a) && a.ValueKind == JsonValueKind.Object ? a
                : root.TryGetProperty("committer", out var c) && c.ValueKind == JsonValueKind.Object ? c : default;
            var author = ReadGitIdentity(identity);
            var date = ReadDate(identity, "date");

            return (id, message, author, date, url);
        }

        private async Task<ChangedItems> ReadGitCommitChangesAsync(string repoId, string sha, CancellationToken cancellationToken)
        {
            var raw = new List<(string Path, bool IsFolder)>();
            try
            {
                using var doc = await _client
                    .GetJsonAsync($"_apis/git/repositories/{repoId}/commits/{sha}/changes", "top=1000", cancellationToken)
                    .ConfigureAwait(false);

                if (doc.RootElement.TryGetProperty("changes", out var changes) && changes.ValueKind == JsonValueKind.Array)
                {
                    foreach (var change in changes.EnumerateArray())
                    {
                        if (!change.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        var path = GetString(item, "path");
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            raw.Add((path!.Replace('\\', '/'),
                                string.Equals(GetString(item, "gitObjectType"), "tree", StringComparison.OrdinalIgnoreCase)));
                        }
                    }
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException) && !(ex is TfsSystemInfoExtractor.Core.Exceptions.TfsAuthenticationRequiredException))
            {
                _logger.LogWarning(ex, "Could not read changed items for Git commit {Sha} in repo {RepoId}.", sha, repoId);
            }

            return BuildChangedItems(LeafChanges(raw), tfvc: false, project: null);
        }

        // ---- TFVC ----------------------------------------------------------------

        private async Task<(string? Message, Developer Author, DateTimeOffset? Date, string? Url)?> ReadChangesetAsync(
            int changesetId, CancellationToken cancellationToken)
        {
            using var doc = await _client
                .GetJsonAsync($"_apis/tfvc/changesets/{changesetId.ToString(CultureInfo.InvariantCulture)}", null, cancellationToken)
                .ConfigureAwait(false);
            var root = doc.RootElement;

            var message = GetString(root, "comment");
            var url = GetString(root, "url");
            var identity = root.TryGetProperty("author", out var a) && a.ValueKind == JsonValueKind.Object ? a
                : root.TryGetProperty("checkedInBy", out var c) && c.ValueKind == JsonValueKind.Object ? c : default;
            var author = ReadIdentityRef(identity);
            var date = ReadDate(root, "createdDate");

            return (message, author, date, url);
        }

        private async Task<ChangedItems> ReadChangesetChangesAsync(int changesetId, CancellationToken cancellationToken)
        {
            var raw = new List<(string Path, bool IsFolder)>();
            string? project = null;
            try
            {
                using var doc = await _client
                    .GetJsonAsync($"_apis/tfvc/changesets/{changesetId.ToString(CultureInfo.InvariantCulture)}/changes", "$top=1000", cancellationToken)
                    .ConfigureAwait(false);

                if (doc.RootElement.TryGetProperty("value", out var changes) && changes.ValueKind == JsonValueKind.Array)
                {
                    foreach (var change in changes.EnumerateArray())
                    {
                        if (!change.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        var path = GetString(item, "path");
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            project = project ?? TfvcProject(path);
                            raw.Add((path!.Replace('\\', '/'),
                                item.TryGetProperty("isFolder", out var f) && f.ValueKind == JsonValueKind.True));
                        }
                    }
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException) && !(ex is TfsSystemInfoExtractor.Core.Exceptions.TfsAuthenticationRequiredException))
            {
                _logger.LogWarning(ex, "Could not read changed items for TFVC changeset {ChangesetId}.", changesetId);
            }

            return BuildChangedItems(LeafChanges(raw), tfvc: true, project: project);
        }

        private readonly struct ChangedItems
        {
            public ChangedItems(IReadOnlyList<string> components, IReadOnlyList<string> paths, string? project)
            {
                Components = components;
                Paths = paths;
                Project = project;
            }

            public IReadOnlyList<string> Components { get; }

            public IReadOnlyList<string> Paths { get; }

            public string? Project { get; }
        }

        /// <summary>
        /// Keep only the actual changed items: a path that is an ancestor directory of
        /// another changed path (the folder-add entries Git/TFVC also report) is dropped,
        /// so <c>X/Y/Z/file.js</c> stays but <c>X</c>, <c>X/Y</c>, <c>X/Y/Z</c> do not.
        /// </summary>
        private static List<(string Path, bool IsFolder)> LeafChanges(IReadOnlyList<(string Path, bool IsFolder)> raw)
        {
            var all = raw.Select(x => x.Path.TrimEnd('/')).ToList();
            return raw
                .Where(x =>
                {
                    var prefix = x.Path.TrimEnd('/') + "/";
                    return !all.Any(other => other.Length > prefix.Length &&
                                             other.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                })
                .ToList();
        }

        private ChangedItems BuildChangedItems(List<(string Path, bool IsFolder)> leaves, bool tfvc, string? project)
        {
            var components = new List<string>();
            var paths = new List<string>();

            foreach (var (path, isFolder) in leaves)
            {
                paths.Add(tfvc ? path : path.TrimStart('/'));   // display the full file path, git without the leading '/'
                var component = ComponentFromPath(NormalizeSegments(path, tfvc), isFolder);
                if (component != null)
                {
                    components.Add(component);
                }
            }

            var capped = paths.Where(p => !string.IsNullOrWhiteSpace(p))
                              .Distinct(StringComparer.OrdinalIgnoreCase)
                              .Take(Math.Max(0, _options.MaxChangedPathsPerCommit))
                              .ToArray();

            return new ChangedItems(Dedupe(components), capped, project);
        }

        // ---- parsing helpers ---------------------------------------------------

        private static bool TryParseGitCommit(string uri, out string repoId, out string sha)
        {
            repoId = string.Empty;
            sha = string.Empty;
            if (!uri.StartsWith(GitCommitPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var rest = Uri.UnescapeDataString(uri.Substring(GitCommitPrefix.Length));
            var parts = rest.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                return false;
            }

            sha = parts[parts.Length - 1];
            repoId = parts[parts.Length - 2];
            return sha.Length > 0 && repoId.Length > 0;
        }

        /// <summary>
        /// The specific component a changed item belongs to. First choice: the segment
        /// directly beneath a configured container folder (e.g.
        /// <c>Folder/Components/ComponentA/File.cs</c> -&gt; <c>ComponentA</c>). Otherwise
        /// the folder the change was actually made in (the item's own directory, or the
        /// folder itself when a whole directory changed) - never a truncated ancestor.
        /// </summary>
        private string? ComponentFromPath(string[] segments, bool isFolder)
        {
            if (segments.Length == 0)
            {
                return null;
            }

            var markers = _options.ComponentContainerFolders;
            if (markers != null)
            {
                for (var i = segments.Length - 1; i >= 0; i--)
                {
                    if (i + 1 >= segments.Length)
                    {
                        continue;
                    }

                    if (!markers.Any(m => string.Equals(m, segments[i], StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    // the candidate must be a directory: something below it, or the change is that directory
                    if (i + 1 < segments.Length - 1 || isFolder)
                    {
                        return segments[i + 1];
                    }
                }
            }

            if (isFolder)
            {
                return segments[segments.Length - 1];         // a whole directory was added / deleted / renamed
            }

            return segments.Length >= 2 ? segments[segments.Length - 2] : null;   // the file's own folder
        }

        /// <summary>Repo-relative path segments: leading '/' stripped for Git; '$/' and the project stripped for TFVC.</summary>
        private static string[] NormalizeSegments(string? path, bool tfvc)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return Array.Empty<string>();
            }

            var s = path!.Replace('\\', '/').Trim('/');
            if (tfvc && s.StartsWith("$/", StringComparison.Ordinal))
            {
                s = s.Substring(2);
            }

            var segments = s.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            return tfvc && segments.Length > 0 ? segments.Skip(1).ToArray() : segments;
        }

        private static string? TfvcProject(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var s = path!.Trim('/');
            if (s.StartsWith("$/", StringComparison.Ordinal))
            {
                s = s.Substring(2);
            }

            var seg = s.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            return seg.Length > 0 ? seg[0] : null;
        }

        private static Developer ReadGitIdentity(JsonElement identity)
        {
            if (identity.ValueKind != JsonValueKind.Object)
            {
                return new Developer("Unknown");
            }

            var name = GetString(identity, "name");
            var email = GetString(identity, "email");
            return new Developer(
                name: !string.IsNullOrWhiteSpace(name) ? name! : !string.IsNullOrWhiteSpace(email) ? email! : "Unknown",
                uniqueName: email,
                displayName: name);
        }

        private static Developer ReadIdentityRef(JsonElement identity)
        {
            if (identity.ValueKind != JsonValueKind.Object)
            {
                return new Developer("Unknown");
            }

            var display = GetString(identity, "displayName");
            var unique = GetString(identity, "uniqueName");
            return new Developer(
                name: !string.IsNullOrWhiteSpace(unique) ? unique! : !string.IsNullOrWhiteSpace(display) ? display! : "Unknown",
                uniqueName: unique,
                displayName: display);
        }

        private static void Add(IDictionary<string, Developer> map, Developer developer)
        {
            var key = developer.UniqueName ?? developer.DisplayName ?? developer.Name;
            if (!map.ContainsKey(key))
            {
                map[key] = developer;
            }
        }

        private static DateTimeOffset? ReadDate(JsonElement element, string property)
        {
            if (element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(property, out var value) &&
                value.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            {
                return parsed;
            }

            return null;
        }

        private static IReadOnlyList<string> Dedupe(IEnumerable<string> values) =>
            values.Where(v => !string.IsNullOrWhiteSpace(v))
                  .Distinct(StringComparer.OrdinalIgnoreCase)
                  .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                  .ToArray();

        private static string? GetString(JsonElement element, string property) =>
            element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(property, out var value) &&
            value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }
}
