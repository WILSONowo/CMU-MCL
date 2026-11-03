// SPDX-License-Identifier: GPL-3.0-only
using CMUmcl.Community;

internal static partial class Program
{
    private sealed class FakeSource : IResourceSource
    {
        private readonly ResourceSourceId _id;
        private readonly Func<ResourceQuery, IReadOnlyList<ResourceProject>> _search;
        private readonly bool _available;

        public FakeSource(ResourceSourceId id, Func<ResourceQuery, IReadOnlyList<ResourceProject>> search, bool available = true)
        {
            _id = id;
            _search = search;
            _available = available;
        }

        public ResourceSourceId SourceId => _id;
        public bool IsAvailable => _available;
        public Task<IReadOnlyList<ResourceProject>> SearchAsync(ResourceQuery query, CancellationToken ct) => Task.FromResult(_search(query));
        public Task<ResourceProject> GetProjectAsync(string id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ResourceVersion>> GetVersionsAsync(string id, ResourceQuery query, CancellationToken ct) => throw new NotSupportedException();
    }

    private static ResourceProject P(ResourceSourceId src, string id, string title, long downloads = 0) =>
        new ResourceProject { Source = src, Id = id, Title = title, Downloads = downloads };

    private static IReadOnlyList<ResourceProject> Gen(ResourceSourceId src, int count)
    {
        var list = new List<ResourceProject>();
        for (int i = 0; i < count; i++) list.Add(P(src, i.ToString(), "T" + i));
        return list;
    }

    private static void CatalogChecks()
    {
        // 1. Source=All merges both sources' pages; (Source,Id) dedupe keeps distinct-source same-title.
        {
            var a = new FakeSource(ResourceSourceId.Modrinth, q => new[] { P(ResourceSourceId.Modrinth, "1", "Same", 10) });
            var b = new FakeSource(ResourceSourceId.CurseForge, q => new[] { P(ResourceSourceId.CurseForge, "1", "Same", 20) });
            var cat = new ResourceCatalog(a, b);
            var page = cat.SearchAsync(new ResourceQuery { Source = null, Sort = ResourceSort.Recommended }, null, null, CancellationToken.None).GetAwaiter().GetResult();
            Check(page.Projects.Count == 2, "catalog: all-sources merge keeps distinct-source same-title entries");
        }

        // 2. Name sort is page-local client sort (ascending title).
        {
            var a = new FakeSource(ResourceSourceId.Modrinth, q => new[] { P(ResourceSourceId.Modrinth, "1", "Zebra"), P(ResourceSourceId.Modrinth, "2", "Apple") });
            var b = new FakeSource(ResourceSourceId.CurseForge, q => new[] { P(ResourceSourceId.CurseForge, "3", "Mango") });
            var cat = new ResourceCatalog(a, b);
            var page = cat.SearchAsync(new ResourceQuery { Source = null, Sort = ResourceSort.Name }, null, null, CancellationToken.None).GetAwaiter().GetResult();
            Check(page.Projects[0].Title == "Apple" && page.Projects[1].Title == "Mango" && page.Projects[2].Title == "Zebra", "catalog: Name sort is page-local ascending");
        }

        // 3. Downloads sort is page-local descending.
        {
            var a = new FakeSource(ResourceSourceId.Modrinth, q => new[] { P(ResourceSourceId.Modrinth, "1", "low", 5) });
            var b = new FakeSource(ResourceSourceId.CurseForge, q => new[] { P(ResourceSourceId.CurseForge, "2", "high", 50) });
            var cat = new ResourceCatalog(a, b);
            var page = cat.SearchAsync(new ResourceQuery { Source = null, Sort = ResourceSort.Downloads }, null, null, CancellationToken.None).GetAwaiter().GetResult();
            Check(page.Projects[0].Id == "2" && page.Projects[1].Id == "1", "catalog: Downloads sort descending");
        }

        // 4. Single-source failure does not kill the other source.
        {
            var fail = new FakeSource(ResourceSourceId.Modrinth, q => throw new CommunitySourceException(ResourceSourceId.Modrinth, "down"));
            var ok = new FakeSource(ResourceSourceId.CurseForge, q => new[] { P(ResourceSourceId.CurseForge, "9", "OK") });
            var cat = new ResourceCatalog(fail, ok);
            var page = cat.SearchAsync(new ResourceQuery { Source = null }, null, null, CancellationToken.None).GetAwaiter().GetResult();
            Check(page.Projects.Count == 1 && page.Projects[0].Id == "9" && page.Failures.Count == 1, "catalog: one source failure isolates the other");
        }

        // 5. Single-source query uses pageSize 50; all-sources uses 25 per source.
        {
            int? seenPageSize = null;
            var a = new FakeSource(ResourceSourceId.Modrinth, q => { seenPageSize = q.PageSize; return new[] { P(ResourceSourceId.Modrinth, "1", "x") }; });
            var cat = new ResourceCatalog(a);
            cat.SearchAsync(new ResourceQuery { Source = ResourceSourceId.Modrinth }, null, null, CancellationToken.None).GetAwaiter().GetResult();
            Check(seenPageSize == 50, "catalog: single source pageSize 50");

            seenPageSize = null;
            var b = new FakeSource(ResourceSourceId.Modrinth, q => { seenPageSize = q.PageSize; return Array.Empty<ResourceProject>(); });
            var c = new FakeSource(ResourceSourceId.CurseForge, q => Array.Empty<ResourceProject>());
            var cat2 = new ResourceCatalog(b, c);
            cat2.SearchAsync(new ResourceQuery { Source = null }, null, null, CancellationToken.None).GetAwaiter().GetResult();
            Check(seenPageSize == 25, "catalog: all-sources pageSize 25 per source");
        }

        // 6. HasMore: full page (== page size) => true; short page => false (25 all-sources, 50 single-source).
        {
            var full = new FakeSource(ResourceSourceId.Modrinth, q => Gen(ResourceSourceId.Modrinth, 25));
            var catFull = new ResourceCatalog(full);
            var pFull = catFull.SearchAsync(new ResourceQuery { Source = null }, null, null, CancellationToken.None).GetAwaiter().GetResult();
            Check(pFull.HasMore, "catalog: HasMore true when a source returns exactly 25 (all-sources)");

            var shortPage = new FakeSource(ResourceSourceId.Modrinth, q => Gen(ResourceSourceId.Modrinth, 24));
            var catShort = new ResourceCatalog(shortPage);
            var pShort = catShort.SearchAsync(new ResourceQuery { Source = null }, null, null, CancellationToken.None).GetAwaiter().GetResult();
            Check(!pShort.HasMore, "catalog: HasMore false when a source returns fewer than 25 (all-sources)");

            var full50 = new FakeSource(ResourceSourceId.Modrinth, q => Gen(ResourceSourceId.Modrinth, 50));
            var catFull50 = new ResourceCatalog(full50);
            var pFull50 = catFull50.SearchAsync(new ResourceQuery { Source = ResourceSourceId.Modrinth }, null, null, CancellationToken.None).GetAwaiter().GetResult();
            Check(pFull50.HasMore, "catalog: HasMore true when a source returns exactly 50 (single-source)");

            var short50 = new FakeSource(ResourceSourceId.Modrinth, q => Gen(ResourceSourceId.Modrinth, 49));
            var catShort50 = new ResourceCatalog(short50);
            var pShort50 = catShort50.SearchAsync(new ResourceQuery { Source = ResourceSourceId.Modrinth }, null, null, CancellationToken.None).GetAwaiter().GetResult();
            Check(!pShort50.HasMore, "catalog: HasMore false when a source returns fewer than 50 (single-source)");
        }

        // 7. Hint is set via CompatibilityEvaluator.ProjectHint.
        {
            var proj = new ResourceProject
            {
                Source = ResourceSourceId.Modrinth,
                Id = "hint",
                Title = "HintMod",
                Loaders = new[] { "fabric" },
                GameVersions = new[] { "1.20.1" },
            };
            var cat = new ResourceCatalog(new FakeSource(ResourceSourceId.Modrinth, q => new[] { proj }));

            var compatible = cat.SearchAsync(new ResourceQuery { Kind = ResourceKind.Mod, Source = null }, "fabric", "1.20.1", CancellationToken.None).GetAwaiter().GetResult();
            Check(compatible.Projects[0].Hint == CompatibilityHint.Compatible, "catalog: Hint Compatible when target loader+game match");

            var incompatible = cat.SearchAsync(new ResourceQuery { Kind = ResourceKind.Mod, Source = null }, "forge", "1.20.1", CancellationToken.None).GetAwaiter().GetResult();
            Check(incompatible.Projects[0].Hint == CompatibilityHint.Incompatible, "catalog: Hint Incompatible when target loader mismatches");

            var unknown = cat.SearchAsync(new ResourceQuery { Kind = ResourceKind.Mod, Source = null }, null, null, CancellationToken.None).GetAwaiter().GetResult();
            Check(unknown.Projects[0].Hint == CompatibilityHint.Unknown, "catalog: Hint Unknown when target loader/game null");
        }

        // 8. Selected-but-unavailable source reports a failure and no projects.
        {
            var unavailable = new FakeSource(ResourceSourceId.Modrinth, q => new[] { P(ResourceSourceId.Modrinth, "1", "never") }, available: false);
            var cat = new ResourceCatalog(unavailable);
            var page = cat.SearchAsync(new ResourceQuery { Source = ResourceSourceId.Modrinth }, null, null, CancellationToken.None).GetAwaiter().GetResult();
            Check(page.Projects.Count == 0 && page.Failures.Count >= 1, "catalog: selected-but-unavailable source reports a failure and no projects");
        }
    }
}
