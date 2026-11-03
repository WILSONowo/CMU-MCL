// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CMUmcl.Community
{
    internal interface IResourceSource
    {
        ResourceSourceId SourceId { get; }
        bool IsAvailable { get; }
        Task<IReadOnlyList<ResourceProject>> SearchAsync(ResourceQuery query, CancellationToken ct);
        Task<ResourceProject> GetProjectAsync(string id, CancellationToken ct);
        Task<IReadOnlyList<ResourceVersion>> GetVersionsAsync(string id, ResourceQuery query, CancellationToken ct);
    }
}
