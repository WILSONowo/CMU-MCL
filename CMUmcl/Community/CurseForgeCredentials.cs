// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using System.Text;

namespace CMUmcl.Community
{
    internal static class CurseForgeCredentials
    {
        private const string ResourceName = "CMUmcl.CurseForge.ProjectKey";
        internal static string? GetApiKey()
        {
            string? value = Environment.GetEnvironmentVariable("CMUMCL_CURSEFORGE_API_KEY");
            return string.IsNullOrWhiteSpace(value) ? GetEmbeddedApiKey() : value!.Trim();
        }

        // Reads only an optional build resource, never a credential file or server environment.
        // Ordinary source builds have no resource. No key text is generated as C# source.
        internal static string? GetEmbeddedApiKey()
        {
            using Stream? stream = typeof(CurseForgeCredentials).Assembly.GetManifestResourceStream(ResourceName);
            if (stream == null) return null;
            using var buffer = new MemoryStream();
            var bytes = new byte[4097];
            int count;
            while ((count = stream.Read(bytes, 0, bytes.Length)) > 0)
            {
                if (buffer.Length + count > 4096) return null;
                buffer.Write(bytes, 0, count);
            }
            string key;
            try { key = new UTF8Encoding(false, true).GetString(buffer.ToArray()).Trim(); }
            catch (DecoderFallbackException) { return null; }
            if (key.Length == 0 || key.IndexOfAny(new[] { '\r', '\n' }) >= 0) return null;
            return key;
        }
    }
}
