// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CMUmcl.Network;

namespace CMUmcl.Community
{
    /// <summary>Builds SafeRedirectHandler-wrapped clients for all community sources.</summary>
    internal static class CommunityHttp
    {
        public const string UserAgent = "CMUmcl/0.3.48.7 (https://github.com/yuhui258036/CMU-MCL)";
        public const int ApiTimeoutSeconds = 30;
        public const int MaxJsonBytes = 8 * 1024 * 1024; // 8 MiB

        public static HttpClient CreateApiClient(Func<string, bool> hostAllowed, bool sameHostOnly)
            => CreateApiClient(LauncherProxy.CreateHandler(allowAutoRedirect: false, useCookies: false), hostAllowed, sameHostOnly);

        public static HttpClient CreateApiClient(HttpMessageHandler inner, Func<string, bool> hostAllowed, bool sameHostOnly)
        {
            var client = new HttpClient(new SafeRedirectHandler(inner, hostAllowed, sameHostOnly))
            {
                Timeout = TimeSpan.FromSeconds(ApiTimeoutSeconds),
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            return client;
        }

        /// <summary>Reads a JSON response into a bounded buffer (8 MiB) before deserialization.</summary>
        public static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, ResourceSourceId sourceId, CancellationToken ct)
        {
            using var ms = new MemoryStream();
            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            {
                var buffer = new byte[65536];
                int read;
                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                {
                    if (ms.Length + read > MaxJsonBytes)
                        throw new CommunitySourceException(sourceId, "接口响应过大。", (int)response.StatusCode);
                    await ms.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                }
            }
            ms.Position = 0;
            return JsonSerializer.Deserialize<T>(ms.ToArray(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        }
    }
}
