// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace CMUmcl.Community
{
    internal sealed class ModrinthSource : IResourceSource, IDisposable
    {
        private const string ApiBase = "https://api.modrinth.com/v2";
        private static readonly HashSet<string> ApiHosts = new(StringComparer.OrdinalIgnoreCase) { CommunityHosts.ModrinthApi };
        private static readonly HashSet<string> KnownLoaders = new(StringComparer.OrdinalIgnoreCase) { "fabric", "forge", "neoforge", "quilt" };

        private readonly HttpClient _http;
        public ResourceSourceId SourceId => ResourceSourceId.Modrinth;
        public bool IsAvailable => true;

        public ModrinthSource()
            : this(CMUmcl.Network.LauncherProxy.CreateHandler(allowAutoRedirect: false, useCookies: false)) { }

        internal ModrinthSource(HttpMessageHandler inner)
        {
            _http = CommunityHttp.CreateApiClient(inner, host => ApiHosts.Contains(host), sameHostOnly: true);
        }

        public async Task<IReadOnlyList<ResourceProject>> SearchAsync(ResourceQuery query, CancellationToken ct)
        {
            string url = BuildSearchUrl(query);
            using var response = await SendAsync(url, ct).ConfigureAwait(false);
            SearchResponse data = await CommunityHttp.ReadJsonAsync<SearchResponse>(response, SourceId, ct).ConfigureAwait(false);
            var projects = new List<ResourceProject>();
            foreach (Hit hit in data.Hits)
            {
                projects.Add(new ResourceProject
                {
                    Source = ResourceSourceId.Modrinth,
                    Id = string.IsNullOrWhiteSpace(hit.Slug) ? hit.ProjectId : hit.Slug,
                    Title = hit.Title ?? "",
                    Description = hit.Description ?? "",
                    IconUrl = string.IsNullOrWhiteSpace(hit.IconUrl) ? null : hit.IconUrl,
                    Author = hit.Author ?? "",
                    Downloads = hit.Downloads,
                    UpdatedAt = hit.DateModified == default ? (DateTimeOffset?)null : hit.DateModified,
                    GameVersions = hit.Versions ?? new List<string>(),
                    Loaders = (hit.Categories ?? new List<string>()).Where(c => KnownLoaders.Contains(c)).ToList(),
                    ProjectUrl = ProjectUrl(query.Kind, string.IsNullOrWhiteSpace(hit.Slug) ? hit.ProjectId : hit.Slug),
                });
            }
            return projects;
        }

        public async Task<ResourceProject> GetProjectAsync(string id, CancellationToken ct)
        {
            string url = ApiBase + "/project/" + Uri.EscapeDataString(id);
            using var response = await SendAsync(url, ct).ConfigureAwait(false);
            ProjectDto dto = await CommunityHttp.ReadJsonAsync<ProjectDto>(response, SourceId, ct).ConfigureAwait(false);
            return new ResourceProject
            {
                Source = ResourceSourceId.Modrinth,
                Id = string.IsNullOrWhiteSpace(dto.Slug) ? dto.Id : dto.Slug,
                Title = dto.Title ?? "",
                Description = dto.Description ?? "",
                IconUrl = string.IsNullOrWhiteSpace(dto.IconUrl) ? null : dto.IconUrl,
                GameVersions = dto.GameVersions ?? new List<string>(),
                Loaders = (dto.Loaders ?? new List<string>()).Where(l => KnownLoaders.Contains(l)).ToList(),
                ProjectUrl = ProjectUrl(ProjectTypeToKind(dto.ProjectType), string.IsNullOrWhiteSpace(dto.Slug) ? dto.Id : dto.Slug),
            };
        }

        public async Task<IReadOnlyList<ResourceVersion>> GetVersionsAsync(string id, ResourceQuery query, CancellationToken ct)
        {
            // Omit an absent loader filter. Modrinth treats loaders=[] as matching
            // no loaders, rather than as an unfiltered version request.
            var parts = new List<string>();
            string loaderFilter = query.Loader?.Trim() ?? "";
            if (query.Kind == ResourceKind.Datapack)
                parts.Add("loaders=" + Uri.EscapeDataString("[\"datapack\"]"));
            else if (loaderFilter.Length > 0 && !string.Equals(loaderFilter, "vanilla", StringComparison.OrdinalIgnoreCase))
                parts.Add("loaders=" + BuildLoaderJson(loaderFilter));
            if (!string.IsNullOrWhiteSpace(query.GameVersion))
                parts.Add("game_versions=" + Uri.EscapeDataString("[\"" + query.GameVersion!.Trim() + "\"]"));
            string url = ApiBase + "/project/" + Uri.EscapeDataString(id) + "/version";
            if (parts.Count > 0) url += "?" + string.Join("&", parts);

            using var response = await SendAsync(url, ct).ConfigureAwait(false);
            List<VersionDto> data = await CommunityHttp.ReadJsonAsync<List<VersionDto>>(response, SourceId, ct).ConfigureAwait(false);
            return data.OrderByDescending(v => v.DatePublished).Select(MapVersion).ToList();
        }

        internal async Task<ResourceVersion> GetVersionAsync(string id, CancellationToken ct)
        {
            using var response = await SendAsync(ApiBase + "/version/" + Uri.EscapeDataString(id), ct).ConfigureAwait(false);
            var dto = await CommunityHttp.ReadJsonAsync<VersionDto>(response, SourceId, ct).ConfigureAwait(false);
            if (dto.Id != id || string.IsNullOrWhiteSpace(dto.ProjectId))
                throw new CommunitySourceException(SourceId, "前置版本信息与来源记录不一致。");
            return MapVersion(dto);
        }

        private static ResourceVersion MapVersion(VersionDto v) => new ResourceVersion
        {
            Id = v.Id ?? "", ProjectId = v.ProjectId ?? "",
            Dependencies = (v.Dependencies ?? new List<DependencyDto>()).Select(d => new ResourceDependency {
                ProjectId = d.ProjectId, VersionId = d.VersionId, FileName = d.FileName,
                Kind = d.DependencyType switch { "required" => DependencyKind.Required, "optional" => DependencyKind.Optional,
                    "incompatible" => DependencyKind.Incompatible, "embedded" => DependencyKind.Embedded, _ => DependencyKind.Unknown }
            }).ToList(),
            VersionNumber = v.VersionNumber ?? "",
            PublishedAt = v.DatePublished == default ? (DateTimeOffset?)null : v.DatePublished,
            GameVersions = v.GameVersions ?? new List<string>(), Loaders = v.Loaders ?? new List<string>(),
            Files = (v.Files ?? new List<FileDto>())
                .Where(f => !string.IsNullOrWhiteSpace(f.Url) && CommunityHosts.IsAllowedUrl(f.Url, CommunityHosts.ModrinthCdn))
                .OrderByDescending(f => f.Primary).Select(f => new ResourceFile {
                    FileName = f.FileName ?? "", Size = f.Size, Url = f.Url!, Sha1 = f.Hashes?.Sha1, Sha512 = f.Hashes?.Sha512, Primary = f.Primary
                }).ToList(),
        };

        public void Dispose() => _http.Dispose();

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
                        throw new CommunitySourceException(SourceId, "Modrinth API 返回 HTTP " + code + "。", code);
                    }
                    return response;
                }

                response.Dispose();
                if (attempt >= 3)
                    throw new CommunitySourceException(SourceId, "Modrinth API 返回 HTTP 429。", 429);

                await Task.Delay(300 * (int)Math.Pow(3, attempt), ct).ConfigureAwait(false);
            }
        }

        private static string BuildSearchUrl(ResourceQuery query)
        {
            var facets = new List<List<string>>
            {
                new List<string> { ResourceKindMaps.ModrinthFacet(query.Kind) },
            };
            if (query.Kind == ResourceKind.Mod && !string.IsNullOrWhiteSpace(query.Loader))
            {
                string loader = query.Loader.Trim().ToLowerInvariant();
                if (loader == "quilt")
                    facets.Add(new List<string> { "categories:quilt", "categories:fabric" });
                else
                    facets.Add(new List<string> { "categories:" + loader });
            }
            if (!string.IsNullOrWhiteSpace(query.GameVersion))
                facets.Add(new List<string> { "versions:" + query.GameVersion.Trim() });

            string sortParam = query.Sort switch
            {
                ResourceSort.Downloads => "downloads",
                ResourceSort.LastUpdated => "updated",
                _ => "relevance", // Recommended and Name both fetch relevance; Name is client-sorted on the merged page.
            };

            string facetsJson = Uri.EscapeDataString(JsonSerializer.Serialize(facets));
            return ApiBase + "/search?query=" + Uri.EscapeDataString(query.SearchText ?? "")
                + "&facets=" + facetsJson + "&offset=" + query.Index + "&limit=" + query.ClampedPageSize + "&index=" + sortParam;
        }

        private static string BuildLoaderJson(string? loader)
        {
            if (string.IsNullOrWhiteSpace(loader)) return Uri.EscapeDataString("[]");
            string l = loader.Trim().ToLowerInvariant();
            if (l == "quilt") return Uri.EscapeDataString("[\"quilt\",\"fabric\"]");
            if (l == "vanilla") return Uri.EscapeDataString("[]");
            return Uri.EscapeDataString("[\"" + l + "\"]");
        }

        private static string ProjectUrl(ResourceKind kind, string slugOrId)
        {
            string segment = kind switch
            {
                ResourceKind.Modpack => "modpack",
                ResourceKind.ResourcePack => "resourcepack",
                ResourceKind.Shader => "shader",
                ResourceKind.Datapack => "datapack",
                _ => "mod",
            };
            return "https://modrinth.com/" + segment + "/" + slugOrId;
        }

        private static ResourceKind ProjectTypeToKind(string? projectType) => projectType?.ToLowerInvariant() switch
        {
            "modpack" => ResourceKind.Modpack,
            "resourcepack" => ResourceKind.ResourcePack,
            "shader" => ResourceKind.Shader,
            "datapack" => ResourceKind.Datapack,
            _ => ResourceKind.Mod,
        };

        // ---------------------------------------------------------------- DTOs

        private sealed class SearchResponse
        {
            [JsonPropertyName("hits")] public List<Hit> Hits { get; set; } = new();
            [JsonPropertyName("total_hits")] public int TotalHits { get; set; }
        }

        private sealed class Hit
        {
            [JsonPropertyName("project_id")] public string ProjectId { get; set; } = "";
            [JsonPropertyName("slug")] public string Slug { get; set; } = "";
            [JsonPropertyName("title")] public string Title { get; set; } = "";
            [JsonPropertyName("description")] public string Description { get; set; } = "";
            [JsonPropertyName("author")] public string Author { get; set; } = "";
            [JsonPropertyName("downloads")] public long Downloads { get; set; }
            [JsonPropertyName("date_modified")] public DateTimeOffset DateModified { get; set; }
            [JsonPropertyName("icon_url")] public string? IconUrl { get; set; }
            [JsonPropertyName("versions")] public List<string>? Versions { get; set; }
            [JsonPropertyName("categories")] public List<string>? Categories { get; set; }
        }

        private sealed class ProjectDto
        {
            [JsonPropertyName("id")] public string Id { get; set; } = "";
            [JsonPropertyName("slug")] public string Slug { get; set; } = "";
            [JsonPropertyName("title")] public string Title { get; set; } = "";
            [JsonPropertyName("description")] public string Description { get; set; } = "";
            [JsonPropertyName("project_type")] public string? ProjectType { get; set; }
            [JsonPropertyName("icon_url")] public string? IconUrl { get; set; }
            [JsonPropertyName("game_versions")] public List<string>? GameVersions { get; set; }
            [JsonPropertyName("loaders")] public List<string>? Loaders { get; set; }
        }

        private sealed class DependencyDto
        {
            [JsonPropertyName("project_id")] public string? ProjectId { get; set; }
            [JsonPropertyName("version_id")] public string? VersionId { get; set; }
            [JsonPropertyName("file_name")] public string? FileName { get; set; }
            [JsonPropertyName("dependency_type")] public string? DependencyType { get; set; }
        }
        private sealed class VersionDto
        {
            [JsonPropertyName("project_id")] public string? ProjectId { get; set; }
            [JsonPropertyName("dependencies")] public List<DependencyDto>? Dependencies { get; set; }
            [JsonPropertyName("id")] public string Id { get; set; } = "";
            [JsonPropertyName("version_number")] public string VersionNumber { get; set; } = "";
            [JsonPropertyName("loaders")] public List<string>? Loaders { get; set; }
            [JsonPropertyName("game_versions")] public List<string>? GameVersions { get; set; }
            [JsonPropertyName("date_published")] public DateTimeOffset DatePublished { get; set; }
            [JsonPropertyName("files")] public List<FileDto>? Files { get; set; }
        }

        private sealed class FileDto
        {
            [JsonPropertyName("url")] public string? Url { get; set; }
            [JsonPropertyName("filename")] public string? FileName { get; set; }
            [JsonPropertyName("size")] public long Size { get; set; }
            [JsonPropertyName("primary")] public bool Primary { get; set; }
            [JsonPropertyName("hashes")] public HashDto? Hashes { get; set; }
        }

        private sealed class HashDto
        {
            [JsonPropertyName("sha1")] public string? Sha1 { get; set; }
            [JsonPropertyName("sha512")] public string? Sha512 { get; set; }
        }
    }
}
