using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using CMUmcl.Community;
using CMUmcl.Content;

internal static partial class Program
{
    private sealed class DependencyHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public readonly List<string> Paths = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Paths.Add(request.RequestUri!.AbsolutePath); return Task.FromResult(respond(request)); }
    }
    private static HttpResponseMessage DependencyJson(object data) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json") };
    private static async Task DependencyChecks()
    {
        var h = new DependencyHttp(_ => DependencyJson(Enumerable.Range(0, 1).Select(n => new {
            id = "parent", version_number = "1", project_id = "iris",
            dependencies = new[] {
                new { project_id = "sodium", version_id = (string?)null, file_name = (string?)null, dependency_type = "required" },
                new { project_id = (string?)null, version_id = "pinned", file_name = (string?)null, dependency_type = "optional" },
                new { project_id = "bad", version_id = (string?)null, file_name = (string?)null, dependency_type = "incompatible" },
                new { project_id = "included", version_id = (string?)null, file_name = (string?)null, dependency_type = "embedded" },
                new { project_id = (string?)null, version_id = (string?)null, file_name = "external.jar", dependency_type = "future-type" }
            }, files = Array.Empty<object>()
        }).ToArray()));
        using (var source = new ModrinthSource(h))
        {
            var v = (await source.GetVersionsAsync("iris", new ResourceQuery(), default)).Single();
            Check(v.Dependencies.Select(d => d.Kind).SequenceEqual(new[] { DependencyKind.Required, DependencyKind.Optional, DependencyKind.Incompatible, DependencyKind.Embedded, DependencyKind.Unknown }), "dependencies: Modrinth preserves each relation kind");
            Check(v.Dependencies[1].ProjectId == null && v.Dependencies[1].VersionId == "pinned", "dependencies: version-only references retain exact version");
            Check(v.Dependencies[4].FileName == "external.jar", "dependencies: filename-only unknown references are not discarded");
        }
        using (var source = new CurseForgeSource("fixture-key", new DependencyHttp(_ => DependencyJson(new { data = new[] { new { id = 1, dependencies = Enumerable.Range(1, 7).Select(n => new { modId = n + 1, relationType = n }).ToArray() } } }))))
        {
            var v = (await source.GetVersionsAsync("1", new ResourceQuery(), default)).Single();
            Check(v.Dependencies.Select(d => d.Kind).SequenceEqual(new[] { DependencyKind.EmbeddedLibrary, DependencyKind.Optional, DependencyKind.Required, DependencyKind.Tool, DependencyKind.Incompatible, DependencyKind.Embedded, DependencyKind.Unknown }), "dependencies: CurseForge maps all relation enums without treating optional as required");
        }
        var pins = new DependencyHttp(r => r.RequestUri!.AbsolutePath.Contains("/version/")
            ? DependencyJson(new { id = "pin", project_id = "sodium-id", version_number = "exact-old", files = Array.Empty<object>() })
            : DependencyJson(new { id = "sodium-id", slug = "sodium", title = "Sodium" }));
        using (var source = new ModrinthSource(pins))
        {
            var catalog = new ResourceCatalog(source);
            var resolved = await catalog.ResolveDependencyAsync(ResourceSourceId.Modrinth, new ResourceDependency { VersionId = "pin" }, default);
            Check(resolved.Project.Title == "Sodium" && resolved.PinnedVersion?.Id == "pin" && pins.Paths.SequenceEqual(new[] { "/v2/version/pin", "/v2/project/sodium-id" }), "dependencies: resolves version-only reference without replacing pin with latest");
            bool rejected = false; int before = pins.Paths.Count;
            try { await catalog.ResolveDependencyAsync(ResourceSourceId.Modrinth, new ResourceDependency { ProjectId = "different-project", VersionId = "pin" }, default); } catch (CommunitySourceException) { rejected = true; }
            Check(rejected && pins.Paths.Count == before + 1, "dependencies: mismatched pinned project is rejected before project lookup");
        }

        SetupTempRoot();
        var store = new InstalledStateStore();
        new LocalInstanceService(store).Create(new CreateLocalInstanceRequest { DisplayName = "dependency-fixture", GameVersion = "1.21.1", Loader = "fabric", LoaderVersion = "0.16.10" });
        var targets = new ResourceTargets(store);
        using var installer = new ResourceInstallService(targets, new MrpackImportService(store));
        var detail = new ResourceDetailViewModel(new ResourceProject { Source = ResourceSourceId.Modrinth, Title = "Iris" }, installer, targets);
        ResourceVersion Version(string id, params ResourceDependency[] refs) => new() { Id = id, VersionNumber = id, GameVersions = new[] { "1.21.1" }, Loaders = new[] { "fabric" }, Dependencies = refs, Files = new[] { new ResourceFile { FileName = "iris.jar", Url = "https://cdn.modrinth.com/iris.jar", Sha1 = new string('a', 40), Size = 10, Primary = true } } };
        var required = new ResourceDependency { ProjectId = "sodium", Kind = DependencyKind.Required };
        var version = Version("v1", required, required, new ResourceDependency { ProjectId = "optional", Kind = DependencyKind.Optional });
        detail.LoadInstances(ResourceKind.Mod, null); detail.LoadVersions(new[] { version }, ResourceKind.Mod, detail.SelectedInstance);
        detail.BindInstall(ResourceKind.Mod, () => detail.SelectedVersion!.Files[0]);
        int calls = 0;
        detail.ConfigureDependencies((d, ct) => { calls++; return Task.FromResult(new ResolvedDependency(new ResourceProject { Title = d.ProjectId! }, null)); }, (_, _) => Task.FromResult<System.Windows.Media.Imaging.BitmapSource?>(null));
        Check(!detail.CanInstall, "dependencies: declared dependencies are checked before parent install becomes available");
        await detail.EnsureDependenciesAsync();
        Check(detail.Dependencies.Count == 2 && detail.Dependencies[0].Reference.Kind == DependencyKind.Required && detail.CanInstall, "dependencies: deduplicates rows and displays required before optional");
        await detail.EnsureDependenciesAsync();
        Check(calls == 2, "dependencies: reopening same version reuses checked metadata");
        detail.SelectedVersion = Version("none"); await detail.EnsureDependenciesAsync();
        Check(!detail.HasDependencies && detail.Dependencies.Count == 0 && detail.CanInstall, "dependencies: version change clears old dependency rows");

        var late = new TaskCompletionSource<ResolvedDependency>();
        detail.ConfigureDependencies((d, ct) => d.ProjectId == "old" ? late.Task : Task.FromResult(new ResolvedDependency(new ResourceProject { Title = "New" }, null)), (_, _) => Task.FromResult<System.Windows.Media.Imaging.BitmapSource?>(null));
        detail.SelectedVersion = Version("old", new ResourceDependency { ProjectId = "old", Kind = DependencyKind.Required });
        Task pending = detail.EnsureDependenciesAsync();
        detail.SelectedVersion = Version("new", new ResourceDependency { ProjectId = "new", Kind = DependencyKind.Required });
        Check(detail.Dependencies.Any(d => d.Title == "New"), "dependencies: changing selected version refreshes open dependency view without reopening");
        await detail.EnsureDependenciesAsync(); late.SetResult(new ResolvedDependency(new ResourceProject { Title = "Old" }, null)); await pending;
        Check(detail.Dependencies.Single().Title == "New" && detail.CanInstall, "dependencies: late old-version response cannot overwrite new selection");

        detail.ConfigureDependencies((_, _) => throw new CommunitySourceException(ResourceSourceId.Modrinth, "fixture unavailable"), (_, _) => Task.FromResult<System.Windows.Media.Imaging.BitmapSource?>(null));
        detail.SelectedVersion = Version("external", new ResourceDependency { FileName = "external-required.jar", Kind = DependencyKind.Required });
        await detail.EnsureDependenciesAsync();
        Check(detail.HasDependencyError && detail.Dependencies.Single().HasError && detail.Dependencies.Single().Title == "external-required.jar", "dependencies: failed or unresolvable required reference stays visible as an error");
        Check(detail.DependencyNotice.Contains("只安装当前模组"), "dependencies: parent-only action is explicit rather than claiming automatic installation");
        SetupTempRoot();
    }
}
