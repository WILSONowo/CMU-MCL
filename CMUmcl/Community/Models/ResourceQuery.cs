// SPDX-License-Identifier: GPL-3.0-only
namespace CMUmcl.Community
{
    internal sealed class ResourceQuery
    {
        public string? SearchText;          // 名称关键字（可为空）
        public ResourceKind Kind;           // 当前分类
        public string? GameVersion;         // 手动输入的 Minecraft 版本（可为空）
        public string? Loader;              // ""/"vanilla"/"fabric"/"forge"/"neoforge"/"quilt"/null
        public ResourceSourceId? Source;    // null = 全部可用源；否则单源
        public ResourceSort Sort;           // 推荐/下载量/更新时间/名称
        public int Index;                   // 0 起
        public int PageSize = 20;           // 1..50

        public int ClampedPageSize => Compat.Clamp(PageSize, 1, 50);
        public static int ClampPageSize(int pageSize) => Compat.Clamp(pageSize, 1, 50);

        /// <summary>Returns a normalized copy: paging clamped, strings trimmed, whitespace to null, loader lower-cased.</summary>
        public ResourceQuery Normalize()
        {
            return new ResourceQuery
            {
                SearchText = NormalizeText(SearchText),
                Kind = Kind,
                GameVersion = NormalizeText(GameVersion),
                Loader = NormalizeLoader(Loader),
                Source = Source,
                Sort = Sort,
                Index = Compat.Clamp(Index, 0, int.MaxValue),
                PageSize = Compat.Clamp(PageSize, 1, 50),
            };
        }

        private static string? NormalizeText(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string? NormalizeLoader(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
    }
}
