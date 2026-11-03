// SPDX-License-Identifier: GPL-3.0-only
namespace CMUmcl.Community
{
    internal static class CommunityFeatures
    {
        // Direct-client feature; availability additionally requires a separately configured key.
        internal const bool CurseForgeEnabled = true;
        internal static string? EnabledCurseForgeKey => CurseForgeEnabled ? CurseForgeCredentials.GetApiKey() : null;
    }
}
