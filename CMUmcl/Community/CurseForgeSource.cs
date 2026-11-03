// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CMUmcl.Content;
using System.Globalization;

namespace CMUmcl.Community
{
    internal sealed class CurseForgeSource : IResourceSource, IDisposable
    {
        private const string DirectApiBase = "https://api.curseforge.com/v1";
        private const int GameId = 432;
        private static readonly HashSet<string> ApiHosts = new(StringComparer.OrdinalIgnoreCase) { CommunityHosts.CurseForgeApi };

        private readonly Func<string?> _keyProvider;
        private readonly bool _enabled;
        private readonly HttpClient _http;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly string _apiBase = DirectApiBase;

        public ResourceSourceId SourceId => ResourceSourceId.CurseForge;
        public bool IsAvailable => _enabled && !string.IsNullOrWhiteSpace(_keyProvider());

        public CurseForgeSource() : this(() => CommunityFeatures.EnabledCurseForgeKey,
            CMUmcl.Network.LauncherProxy.CreateHandler(allowAutoRedirect: false, useCookies: false),
            CommunityFeatures.CurseForgeEnabled) { }

        internal CurseForgeSource(string? apiKey, HttpMessageHandler inner,
            Func<TimeSpan, CancellationToken, Task>? delay = null)
            : this(() => apiKey, inner, true, delay) { }

        internal CurseForgeSource(Func<string?> keyProvider, HttpMessageHandler inner, bool enabled,
            Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
            _enabled = enabled;
            _delay = delay ?? RetryAfterPolicy.WaitAsync;
            _http = CommunityHttp.CreateApiClient(new CurseForgeRequestHandler(inner, keyProvider, api: true),
                host => ApiHosts.Contains(host), sameHostOnly: true);
        }

        private async Task<T> GetJsonAsync<T>(string url, CancellationToken ct)
        {
            EnsureAvailable();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(CommunityHttp.ApiTimeoutSeconds));
            try
            {
                using var response = await SendAsync(url, deadline.Token).ConfigureAwait(false);
                return await CommunityHttp.ReadJsonAsync<T>(response, SourceId, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            { throw new CommunitySourceException(SourceId, "CurseForge 请求超时。", null); }
        }

        public async Task<IReadOnlyList<ResourceProject>> SearchAsync(ResourceQuery query, CancellationToken ct)
        {
            EnsureAvailable();
            Envelope<List<ModDto>> env = await GetJsonAsync<Envelope<List<ModDto>>>(BuildSearchUrl(query), ct).ConfigureAwait(false);
            return (env.Data ?? new List<ModDto>()).Select(MapProject).ToList();
        }

        public async Task<ResourceProject> GetProjectAsync(string id, CancellationToken ct)
        {
            EnsureAvailable();
            Envelope<ModDto> env = await GetJsonAsync<Envelope<ModDto>>(_apiBase + ProjectPath(id), ct).ConfigureAwait(false);
            return MapProject(env.Data ?? new ModDto());
        }

        public async Task<IReadOnlyList<ResourceVersion>> GetVersionsAsync(string id, ResourceQuery query, CancellationToken ct)
        {
            EnsureAvailable();
            string url = _apiBase + FilesPath(id);
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(query.GameVersion))
            {
                parts.Add("gameVersion=" + Uri.EscapeDataString(query.GameVersion!.Trim()));
                if (!string.IsNullOrWhiteSpace(query.Loader) && TryMapModLoaderType(query.Loader, out int mlt))
                    parts.Add("modLoaderType=" + mlt);
            }
            parts.Add("index=" + query.Index);
            parts.Add("pageSize=" + query.ClampedPageSize);
            if (parts.Count > 0) url += "?" + string.Join("&", parts);

            Envelope<List<FileDto>> env = await GetJsonAsync<Envelope<List<FileDto>>>(url, ct).ConfigureAwait(false);

            var versions = new List<ResourceVersion>();
            foreach (FileDto f in env.Data ?? new List<FileDto>())
            {
                string? sha1 = null;
                foreach (FileHashDto h in f.Hashes ?? new List<FileHashDto>())
                    if (h.Algo == 1 && !string.IsNullOrWhiteSpace(h.Value)) sha1 = h.Value!.Trim();
                versions.Add(new ResourceVersion
                {
                    Id = f.Id.ToString(), ProjectId = id,
                    Dependencies = (f.Dependencies ?? new List<DependencyDto>()).Select(d => new ResourceDependency {
                        ProjectId = d.ModId.ToString(),
                        Kind = d.RelationType switch { 3 => DependencyKind.Required, 2 => DependencyKind.Optional,
                            5 => DependencyKind.Incompatible, 1 => DependencyKind.EmbeddedLibrary, 6 => DependencyKind.Embedded,
                            4 => DependencyKind.Tool, _ => DependencyKind.Unknown }
                    }).ToList(),
                    PublishedAt = f.FileDate == default ? (DateTimeOffset?)null : f.FileDate,
                    VersionNumber = string.IsNullOrWhiteSpace(f.DisplayName) ? (f.FileName ?? f.Id.ToString()) : f.DisplayName!,
                    GameVersions = f.GameVersions ?? new List<string>(),
                    Loaders = MapLoaders(f.SortableGameVersions),
                    Files = new List<ResourceFile>
                    {
                        new ResourceFile
                        {
                            FileName = f.FileName ?? "",
                            Size = f.FileLength,
                            Sha1 = sha1,
                            Sha512 = null,
                            Primary = true,
                            Url = "", // resolved later via download-url endpoint (see ResolveDownloadAsync)
                        },
                    },
                });
            }
            return versions;
        }

        /// <summary>Use only the official grant, after fresh distribution/file metadata validation.</summary>
        public async Task<ResourceFile?> ResolveDownloadAsync(string modId, long fileId, ResourceFile template, CancellationToken ct)
        {
            EnsureAvailable();
            string projectPath = ProjectPath(modId);
            if (fileId <= 0) throw new CommunitySourceException(SourceId, "无效的 CurseForge 文件编号。", null);
            Envelope<ModDto> project = await GetJsonAsync<Envelope<ModDto>>(_apiBase + projectPath, ct).ConfigureAwait(false);
            if (project.Data == null || project.Data.Id.ToString(CultureInfo.InvariantCulture) != modId
                || project.Data.AllowModDistribution == false) return null;
            Envelope<FileDto> metadata = await GetJsonAsync<Envelope<FileDto>>(
                _apiBase + projectPath + "/files/" + fileId.ToString(CultureInfo.InvariantCulture), ct).ConfigureAwait(false);
            FileDto f = metadata.Data;
            string? sha1 = f?.Hashes?.FirstOrDefault(h => h.Algo == 1 && IsSha1(h.Value))?.Value;
            if (f == null || f.Id != fileId || f.ModId.ToString(CultureInfo.InvariantCulture) != modId
                || !f.IsAvailable || f.FileLength <= 0 || f.FileLength > 2L * 1024 * 1024 * 1024
                || sha1 == null || string.IsNullOrWhiteSpace(f.FileName)) return null;
            DownloadUrlDto dto = await GetJsonAsync<DownloadUrlDto>(
                _apiBase + projectPath + "/files/" + fileId.ToString(CultureInfo.InvariantCulture) + "/download-url", ct).ConfigureAwait(false);
            if (!Uri.TryCreate(dto.Data, UriKind.Absolute, out Uri? uri) || !CurseForgeRequestHandler.IsOfficialFile(uri)) return null;
            return new ResourceFile
            {
                FileName = f.FileName!, Size = f.FileLength, Sha1 = sha1, Sha512 = null,
                Primary = template.Primary, Url = uri.AbsoluteUri,
            };
        }

        private static bool IsSha1(string? value) => value != null && value.Length == 40 && value.All(Uri.IsHexDigit);
        public void Dispose() => _http.Dispose();
        private void EnsureAvailable()
        {
            if (IsAvailable) return;
            throw new CommunitySourceException(SourceId, "CurseForge 来源尚未启用或访问凭据未配置。", null);
        }
        private const string SearchPath = "/mods/search";
        private static string ProjectPath(string id)
        {
            if (!long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out long n) || n <= 0)
                throw new CommunitySourceException(ResourceSourceId.CurseForge, "无效的 CurseForge 项目编号。", null);
            return "/mods/" + n.ToString(CultureInfo.InvariantCulture);
        }
        private static string FilesPath(string id) => ProjectPath(id) + "/files";

        private static ResourceProject MapProject(ModDto m)
        {
            return new ResourceProject
            {
                Source = ResourceSourceId.CurseForge,
                Id = m.Id.ToString(),
                Title = m.Name ?? "",
                Description = m.Summary ?? "",
                IconUrl = !string.IsNullOrWhiteSpace(m.Logo?.ThumbnailUrl) ? m.Logo!.ThumbnailUrl : (string.IsNullOrWhiteSpace(m.Logo?.Url)?null:m.Logo!.Url),
                Author = string.Empty,
                Downloads = m.DownloadCount,
                UpdatedAt = m.DateModified == default ? (DateTimeOffset?)null : m.DateModified,
                GameVersions = (m.LatestFiles ?? new List<FileDto>()).SelectMany(f => f.GameVersions ?? new List<string>()).Distinct().ToList(),
                Loaders = MapLoaders((m.LatestFiles ?? new List<FileDto>()).SelectMany(f => f.SortableGameVersions ?? new List<SortableGameVersionDto>()).ToList()),
                ProjectUrl = string.IsNullOrWhiteSpace(m.Links?.WebsiteUrl) ? null : m.Links!.WebsiteUrl,
            };
        }

        /// <summary>Extracts known loaders from sortable game versions via case-insensitive name matching.</summary>
        private static List<string> MapLoaders(List<SortableGameVersionDto>? versions)
        {
            var loaders = new List<string>();
            foreach (SortableGameVersionDto v in versions ?? new List<SortableGameVersionDto>())
            {
                string? loader = MatchLoader(v);
                if (loader != null && !loaders.Contains(loader)) loaders.Add(loader);
            }
            return loaders;
        }

        private static string? MatchLoader(SortableGameVersionDto v)
        {
            string name = ((v.GameVersionName ?? "") + " " + (v.GameVersion ?? "")).ToLowerInvariant();
            if (name.Contains("fabric")) return "fabric";
            if (name.Contains("neoforge")) return "neoforge";
            if (name.Contains("forge")) return "forge";
            if (name.Contains("quilt")) return "quilt";
            return null;
        }

        private string BuildSearchUrl(ResourceQuery query)
        {
            string url = _apiBase + SearchPath + "?gameId=" + GameId + "&classId=" + ResourceKindMaps.CurseForgeClassId(query.Kind);
            if (!string.IsNullOrWhiteSpace(query.SearchText))
                url += "&searchFilter=" + Uri.EscapeDataString(query.SearchText!.Trim());
            if (!string.IsNullOrWhiteSpace(query.GameVersion))
            {
                url += "&gameVersion=" + Uri.EscapeDataString(query.GameVersion!.Trim());
                if (!string.IsNullOrWhiteSpace(query.Loader) && TryMapModLoaderType(query.Loader, out int mlt))
                    url += "&modLoaderType=" + mlt;
            }
            (int field, string order) = SortField(query.Sort);
            url += "&sortField=" + field + "&sortOrder=" + order + "&index=" + query.Index + "&pageSize=" + query.ClampedPageSize;
            return url;
        }

        private static (int Field, string Order) SortField(ResourceSort sort) => sort switch
        {
            ResourceSort.Downloads => (6, "desc"),
            ResourceSort.LastUpdated => (3, "desc"),
            ResourceSort.Name => (4, "asc"),
            _ => (1, "desc"), // Recommended = Featured, desc
        };

        private static bool TryMapModLoaderType(string? loader, out int modLoaderType)
        {
            modLoaderType = 0;
            switch ((loader ?? "").Trim().ToLowerInvariant())
            {
                case "forge": modLoaderType = 1; return true;
                case "fabric": modLoaderType = 4; return true;
                case "quilt": modLoaderType = 5; return true;
                case "neoforge": modLoaderType = 6; return true;
                default: return false;
            }
        }

        private async Task<HttpResponseMessage> SendAsync(string url, CancellationToken ct)
        {
            for (int attempt = 0; ; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

                if (response.StatusCode != (HttpStatusCode)429)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        int code = (int)response.StatusCode;
                        response.Dispose();
                        string reason = code switch
                        {
                            401 => "CurseForge 访问凭据无效，请联系启动器维护者。",
                            403 => "CurseForge 拒绝访问（403）。",
                            404 => "项目/文件不存在。",
                            429 => "CurseForge 限流。",
                            _ when code >= 500 => "CurseForge 服务暂时不可用。",
                            _ => "CurseForge API 返回 HTTP " + code + "。",
                        };
                        throw new CommunitySourceException(SourceId, reason, code);
                    }
                    return response;
                }

                TimeSpan wait = RetryAfterPolicy.Delay(response, TimeSpan.FromMilliseconds(300 * (int)Math.Pow(3, attempt)), DateTimeOffset.UtcNow);
                response.Dispose();
                if (attempt >= 3)
                    throw new CommunitySourceException(SourceId, "CurseForge 限流。", 429);

                await _delay(wait, ct).ConfigureAwait(false);
            }
        }

        private sealed class Envelope<T>
        {
            [JsonPropertyName("data")] public T Data { get; set; } = default!;
            [JsonPropertyName("pagination")] public PaginationDto Pagination { get; set; } = new();
        }

        private sealed class PaginationDto
        {
            [JsonPropertyName("index")] public int Index { get; set; }
            [JsonPropertyName("pageSize")] public int PageSize { get; set; }
            [JsonPropertyName("resultCount")] public int ResultCount { get; set; }
            [JsonPropertyName("totalCount")] public int TotalCount { get; set; }
        }

        private sealed class ModDto
        {
            [JsonPropertyName("id")] public long Id { get; set; }
            [JsonPropertyName("name")] public string? Name { get; set; }
            [JsonPropertyName("summary")] public string? Summary { get; set; }
            [JsonPropertyName("downloadCount")] public long DownloadCount { get; set; }
            [JsonPropertyName("dateModified")] public DateTimeOffset DateModified { get; set; }
            [JsonPropertyName("allowModDistribution")] public bool? AllowModDistribution { get; set; }
            [JsonPropertyName("links")] public LinksDto? Links { get; set; }
            [JsonPropertyName("logo")] public LogoDto? Logo { get; set; }
            [JsonPropertyName("latestFiles")] public List<FileDto>? LatestFiles { get; set; }
        }

        private sealed class LinksDto { [JsonPropertyName("websiteUrl")] public string? WebsiteUrl { get; set; } }
        private sealed class LogoDto { [JsonPropertyName("thumbnailUrl")] public string? ThumbnailUrl { get; set; } [JsonPropertyName("url")] public string? Url {get;set;} }

        private sealed class DependencyDto
        {
            [JsonPropertyName("modId")] public long ModId { get; set; }
            [JsonPropertyName("relationType")] public int RelationType { get; set; }
        }
        private sealed class FileDto
        {
            [JsonPropertyName("dependencies")] public List<DependencyDto>? Dependencies { get; set; }
            [JsonPropertyName("id")] public long Id { get; set; }
            [JsonPropertyName("fileDate")] public DateTimeOffset FileDate { get; set; }
            [JsonPropertyName("fileName")] public string? FileName { get; set; }
            [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
            [JsonPropertyName("fileLength")] public long FileLength { get; set; }
            [JsonPropertyName("hashes")] public List<FileHashDto>? Hashes { get; set; }
            [JsonPropertyName("downloadUrl")] public string? DownloadUrl { get; set; }
            [JsonPropertyName("gameVersions")] public List<string>? GameVersions { get; set; }
            [JsonPropertyName("sortableGameVersions")] public List<SortableGameVersionDto>? SortableGameVersions { get; set; }
            [JsonPropertyName("modId")] public long ModId { get; set; }
            [JsonPropertyName("isAvailable")] public bool IsAvailable { get; set; }
        }

        private sealed class SortableGameVersionDto
        {
            [JsonPropertyName("gameVersionName")] public string? GameVersionName { get; set; }
            [JsonPropertyName("gameVersionPadded")] public string? GameVersionPadded { get; set; }
            [JsonPropertyName("gameVersion")] public string? GameVersion { get; set; }
            [JsonPropertyName("gameVersionReleaseDate")] public string? GameVersionReleaseDate { get; set; }
            [JsonPropertyName("gameVersionTypeId")] public int GameVersionTypeId { get; set; }
        }

        private sealed class FileHashDto
        {
            [JsonPropertyName("value")] public string? Value { get; set; }
            [JsonPropertyName("algo")] public int Algo { get; set; }
        }

        private sealed class DownloadUrlDto { [JsonPropertyName("data")] public string? Data { get; set; } }
    }
}
