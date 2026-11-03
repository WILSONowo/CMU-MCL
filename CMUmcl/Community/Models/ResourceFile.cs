// SPDX-License-Identifier: GPL-3.0-only
using CMUmcl.Content;

namespace CMUmcl.Community
{
    internal sealed class ResourceFile
    {
        public string FileName = "";
        public long Size;                 // <=0 表示未知
        public string Url = "";
        public string? Sha1;
        public string? Sha512;            // Modrinth 提供；CurseForge 为 null
        public bool Primary;

        /// <summary>True when at least SHA-1 or SHA-512 is present (secure hash required to install).</summary>
        public bool HasSecureHash => !string.IsNullOrWhiteSpace(Sha1) || !string.IsNullOrWhiteSpace(Sha512);

        /// <summary>Plan alias: true when any install-verifiable hash is present.</summary>
        public bool HasAnyHash => HasSecureHash;

        public DownloadExpectation ToExpectation()
            => new DownloadExpectation(Size > 0 ? Size : null, Sha512, null, Sha1);
    }
}
