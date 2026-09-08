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
                        ? await ReadGitCommitComponentsAsync(repoId, sha, cancellationToken).ConfigureAwait(false)
                        : Array.Empty<string>();

                    commits.Add(new Commit(commit.Value.Sha, commit.Value.Message, commit.Value.Author,
                        commit.Value.Date, repoId, commit.Value.Url, changed));
                    Add(contributors, commit.Value.Author);
                    foreach (var name in changed)
                    {
                        components[name] = new SourceComponent(name, repoId);
                    }
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
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

                    var (changed, project) = _options.IncludeComponents
                        ? await ReadChangesetComponentsAsync(changesetId, cancellationToken).ConfigureAwait(false)
                        : (Array.Empty<string>(), null);

                    var repoId = "tfvc:" + (project ?? "root");
                    repositories[repoId] = new SourceRepository(repoId, project ?? "TFVC", null, "TfsVersionControl");

                    commits.Add(new Commit("C" + changesetId.ToString(CultureInfo.InvariantCulture), cs.Value.Message,
                        cs.Value.Author, cs.Value.Date, repoId, cs.Value.Url, changed));
                    Add(contributors, cs.Value.Author);
                    foreach (var name in changed)
                    {
                        components[name] = new SourceComponent(name, repoId);
                    }
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
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
            catch (Exception ex) when (!(ex is OperationCanceledException))
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

        private async Task<IReadOnlyList<string>> ReadGitCommitComponentsAsync(string repoId, string sha, CancellationToken cancellationToken)
        {
            var found = new List<string>();
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

                        var isFolder = string.Equals(GetString(item, "gitObjectType"), "tree", StringComparison.OrdinalIgnoreCase);
                        var component = ComponentFromPath(GetString(item, "path"), tfvc: false, isFolder: isFolder);
                        if (component != null)
                        {
                            found.Add(component);
                        }
                    }
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger.LogWarning(ex, "Could not read changed files for Git commit {Sha} in repo {RepoId}.", sha, repoId);
            }

            return Dedupe(found);
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

        private async Task<(IReadOnlyList<string> Components, string? Project)> ReadChangesetComponentsAsync(
            int changesetId, CancellationToken cancellationToken)
        {
            var found = new List<string>();
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
                        project ??= TfvcProject(path);
                        var isFolder = change.TryGetProperty("item", out var it) && it.TryGetProperty("isFolder", out var f) && f.ValueKind == JsonValueKind.True;
                        var component = ComponentFromPath(path, tfvc: true, isFolder: isFolder);
                        if (component != null)
                        {
                            found.Add(component);
                        }
                    }
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _logger.LogWarning(ex, "Could not read changed files for TFVC changeset {ChangesetId}.", changesetId);
            }

            return (Dedupe(found), project);
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

        private string? ComponentFromPath(string? path, bool tfvc, bool isFolder)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var normalized = path!.Replace('\\', '/').Trim('/');
            if (tfvc && normalized.StartsWith("$/", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(2);
            }

            var segments = normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var index = (tfvc ? 1 : 0) + Math.Max(1, _options.ComponentPathDepth) - 1;

            if (segments.Length > index + 1)
            {
                return segments[index];               // there's a file/folder below -> this segment is a folder
            }

            if (isFolder && segments.Length == index + 1)
            {
                return segments[index];               // the change is that folder itself
            }

            return null;
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
