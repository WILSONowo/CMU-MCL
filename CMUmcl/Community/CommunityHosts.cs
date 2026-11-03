// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;

namespace CMUmcl.Community
{
    /// <summary>Central host allowlists for the community-resource network layer.</summary>
    internal static class CommunityHosts
    {
        public const string ModrinthApi = "api.modrinth.com";
        public const string CurseForgeApi = "api.curseforge.com";

        public static readonly HashSet<string> ModrinthCdn = new(StringComparer.OrdinalIgnoreCase)
        { "cdn.modrinth.com" };

        public static readonly HashSet<string> CurseForgeCdn = new(StringComparer.OrdinalIgnoreCase)
        { "mediafilez.forgecdn.net", "edge.forgecdn.net", "media.forgecdn.net" };

        /// <summary>Own resource-site hosts used by server-pack icon_url and icons/&lt;sha256&gt; assets.</summary>
        public static readonly HashSet<string> ResourceCdn = new(StringComparer.OrdinalIgnoreCase)
        { CMUmcl.Content.ResourceManifestClient.AllowedPackHost, "103.24.216.15" };

        public static readonly HashSet<string> ProjectLinks = new(StringComparer.OrdinalIgnoreCase)
        { "modrinth.com", "www.curseforge.com", "legacy.curseforge.com" };

        /// <summary>Extracts the host of an absolute HTTPS URL; returns false otherwise.</summary>
        public static bool TryGetHttpsHost(string? url, out string? host)
        {
            host = null;
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) return false;
            if (!IsSafeHttpsUri(uri)) return false;
            host = uri.Host;
            return true;
        }

        internal static bool IsSafeHttpsUri(Uri uri)
            => uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443
                && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0
                && !Uri.UnescapeDataString(uri.Query).Contains("api-key", StringComparison.OrdinalIgnoreCase);

        /// <summary>True when url is an absolute HTTPS URL whose host is in <paramref name="hosts"/>.</summary>
        public static bool IsAllowedUrl(string? url, HashSet<string> hosts)
            => TryGetHttpsHost(url, out string? host) && hosts.Contains(host);

        /// <summary>True when the project page link host is allowlisted for opening externally.</summary>
        public static bool IsAllowedProjectLink(string? url) => IsAllowedUrl(url, ProjectLinks);
    }
}
