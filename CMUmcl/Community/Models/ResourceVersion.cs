// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;

namespace CMUmcl.Community
{
    internal sealed class ResourceVersion
    {
        public string Id = "";
        public string ProjectId = "";
        public IReadOnlyList<ResourceDependency> Dependencies = Array.Empty<ResourceDependency>();
        public string GameLabel => string.Join(" / ", GameVersions.Where(v => !string.IsNullOrEmpty(v) && char.IsDigit(v[0])));
        public string LoaderLabel => string.Join(" / ", Loaders);
        public string DateLabel => PublishedAt?.ToLocalTime().ToString("yyyy-MM-dd") ?? "";
        public string VersionNumber { get; set; } = "";
        public DateTimeOffset? PublishedAt;
        public IReadOnlyList<string> GameVersions = Array.Empty<string>();
        public IReadOnlyList<string> Loaders = Array.Empty<string>();
        public IReadOnlyList<ResourceFile> Files = Array.Empty<ResourceFile>();
    }
}
