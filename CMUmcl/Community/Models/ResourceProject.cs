// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;

namespace CMUmcl.Community
{
    internal sealed class ResourceProject
    {
        public ResourceSourceId Source { get; set; }
        public string Id { get; set; } = "";                 // 项目 ID（Modrinth id/slug；CurseForge 数字 id 的字符串形式）
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string? IconUrl { get; set; }
        public string? Author { get; set; }
        public long Downloads { get; set; } = -1;            // -1 表示未知
        public DateTimeOffset? UpdatedAt { get; set; }
        public IReadOnlyList<string> GameVersions { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> Loaders { get; set; } = Array.Empty<string>();
        public string? ProjectUrl { get; set; }
        public CompatibilityHint Hint { get; set; } = CompatibilityHint.Unknown;
        public string SourceLabel => ResourceSourceLabels.ToDisplayName(Source);
    }
}
