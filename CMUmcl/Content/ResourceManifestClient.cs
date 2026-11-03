using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CMUmcl.Content
{
    /// <summary>
    /// Fetches and verifies the signed server-pack manifest from the resource site.
    /// Fails closed: the manifest is only parsed after its detached ECDSA P-256/SHA-256
    /// signature verifies against the embedded public key.
    /// </summary>
    public sealed class ResourceManifestClient : IDisposable
    {
        public const string BaseUrl = "https://csumcl.mcapcs.cn";
        public const string AllowedPackHost = "csumcl.mcapcs.cn";
        private const string LegacyAllowedPackHost = "103.24.216.15";
        private const string UserAgent = "CMUmcl/0.1.0";
        private const string SignatureAlgorithm = "ECDSA-P256-SHA256";
        private const long MaximumPackSize = 8L * 1024 * 1024 * 1024;

        // Embedded manifest-signing public key (SPKI PEM). Out-of-band; never fetched.
        private const string PublicKeyPem =
            "-----BEGIN PUBLIC KEY-----\n" +
            "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE4oPSKR6LHNu1iifTLCpw8NGfzTiw\n" +
            "anmgQl/6+zHvodFc6NNGbKkL2GcilT0DdpZQAu/1Dy3iomuoGLrS5QhDvw==\n" +
            "-----END PUBLIC KEY-----";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        private readonly HttpClient _http;
        private readonly string _publicKeyPem;

        public ResourceManifestClient() : this(CMUmcl.Network.LauncherProxy.CreateHandler(), new Uri(BaseUrl + "/"), PublicKeyPem) { }

        internal ResourceManifestClient(HttpMessageHandler handler, Uri baseAddress, string publicKeyPem)
        {
            _publicKeyPem = publicKeyPem;
            _http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(20),
                BaseAddress = baseAddress,
                MaxResponseContentBufferSize = 4 * 1024 * 1024,
            };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }

        /// <summary>
        /// Downloads the manifest and its detached signature, verifies the signature
        /// over the exact bytes, then parses and allowlist-checks the result.
        /// </summary>
        public async Task<ResourceManifest> GetManifestAsync(CancellationToken ct = default)
        {
            // A detached signature and its manifest are two independently cached
            // URLs. A publication/cache transition may expose a mismatched pair.
            // Retry fresh pairs, but never accept unverified data.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var result = await GetVerifiedManifestAsync(attempt, ct).ConfigureAwait(false);
                if (result is not null) return result;
                if (attempt < 2) await Task.Delay(200 * (attempt + 1), ct).ConfigureAwait(false);
            }
            throw new ResourceException("清单签名验证失败，已拒绝加载。请稍后重试。");
        }

        private async Task<ResourceManifest?> GetVerifiedManifestAsync(int attempt, CancellationToken ct)
        {
            byte[] manifestBytes;
            byte[] sigBytes;

            try
            {
                var suffix = attempt == 0 ? string.Empty : "?refresh=" + Guid.NewGuid().ToString("N");
                var manifestTask = GetBytesAsync("manifest.json" + suffix, attempt > 0, ct);
                var signatureTask = GetBytesAsync("manifest.json.sig" + suffix, attempt > 0, ct);
                await Task.WhenAll(manifestTask, signatureTask).ConfigureAwait(false);
                manifestBytes = await manifestTask.ConfigureAwait(false);
                sigBytes = await signatureTask.ConfigureAwait(false);
                if (sigBytes.Length > 65536) throw new ResourceException("签名文件过大，已拒绝加载。");
            }
            catch (HttpRequestException ex)
            {
                throw new ResourceException("网络错误，无法连接资源站，请检查网络连接。", ex);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new ResourceException("连接资源站超时，请稍后再试。", ex);
            }

            ManifestSignature signature;
            try
            {
                signature = JsonSerializer.Deserialize<ManifestSignature>(sigBytes, JsonOptions)
                    ?? throw new ResourceException("签名文件为空或格式无效。");
            }
            catch (JsonException ex)
            {
                throw new ResourceException("签名文件格式无效。", ex);
            }

            if (!string.Equals(signature.Algorithm, SignatureAlgorithm, StringComparison.OrdinalIgnoreCase))
            {
                throw new ResourceException("不支持的签名算法，已拒绝加载。");
            }

            if (!VerifySignature(manifestBytes, signature.Signature))
            {
                return null;
            }

            ResourceManifest manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<ResourceManifest>(manifestBytes, JsonOptions)
                    ?? throw new ResourceException("清单内容为空或格式无效。");
            }
            catch (JsonException ex)
            {
                throw new ResourceException("清单内容格式无效。", ex);
            }

            ValidateManifest(manifest);
            return manifest;
        }

        /// <summary>
        /// Validates the signed manifest's structure before any card or download uses it.
        /// A malformed signed manifest is a publisher error; fail closed instead of
        /// rendering unusable entries or accepting a partially trusted download target.
        /// </summary>
        private static void ValidateManifest(ResourceManifest manifest)
        {
            if (manifest.FormatVersion != 1)
                throw new ResourceException($"不支持的资源清单格式版本：{manifest.FormatVersion}。");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (ManifestInstance instance in manifest.Instances)
            {
                if (instance is null || string.IsNullOrWhiteSpace(instance.Id) || instance.Id.Length > 64)
                    throw new ResourceException("资源清单包含无效的实例 ID，已拒绝加载。");
                if (!ids.Add(instance.Id))
                    throw new ResourceException($"资源清单包含重复的实例 ID：{instance.Id}，已拒绝加载。");
                if (string.IsNullOrWhiteSpace(instance.Name))
                    throw new ResourceException($"资源清单实例 {instance.Id} 缺少名称，已拒绝加载。");
                if (instance.Revision < 0)
                    throw new ResourceException($"资源清单实例 {instance.Id} 的修订号无效，已拒绝加载。");
                if (instance.Server is not null && (instance.Server.Port < 0 || instance.Server.Port > 65535))
                    throw new ResourceException($"资源清单实例 {instance.Id} 的服务器端口无效，已拒绝加载。");

                ManifestPack? pack = instance.Pack;
                if (pack is null || !string.Equals(pack.Kind, "mrpack", StringComparison.OrdinalIgnoreCase))
                    throw new ResourceException($"资源清单实例 {instance.Id} 的整合包类型不受支持，已拒绝加载。");
                if (!IsAllowedResourceUrl(pack.Url))
                    throw new ResourceException($"整合包下载地址不被允许，已拒绝加载（实例：{instance.Id}）。");
                if (pack.Size <= 0 || pack.Size > MaximumPackSize)
                    throw new ResourceException($"资源清单实例 {instance.Id} 的整合包大小无效，已拒绝加载。");

                bool hasHash = false;
                hasHash |= ValidateHash(pack.Sha1, 40, instance.Id, "SHA-1");
                hasHash |= ValidateHash(pack.Sha256, 64, instance.Id, "SHA-256");
                hasHash |= ValidateHash(pack.Sha512, 128, instance.Id, "SHA-512");
                if (!hasHash)
                    throw new ResourceException($"资源清单实例 {instance.Id} 的整合包缺少校验哈希，已拒绝加载。");

                bool hasFilesUrl = !string.IsNullOrWhiteSpace(pack.FilesUrl);
                bool hasFilesHash = !string.IsNullOrWhiteSpace(pack.FilesSha256);
                if (hasFilesUrl != hasFilesHash)
                    throw new ResourceException($"资源清单实例 {instance.Id} 的增量索引字段不完整，已拒绝加载。");
                if (hasFilesUrl)
                {
                    if (!IsAllowedResourceUrl(pack.FilesUrl))
                        throw new ResourceException($"增量索引地址不被允许，已拒绝加载（实例：{instance.Id}）。");
                    if (!IsHex(pack.FilesSha256!, 64))
                        throw new ResourceException($"资源清单实例 {instance.Id} 的增量索引哈希无效，已拒绝加载。");
                }
            }
        }

        private static bool ValidateHash(string? value, int length, string instanceId, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            if (!IsHex(value, length))
                throw new ResourceException($"资源清单实例 {instanceId} 的 {name} 校验值无效，已拒绝加载。");
            return true;
        }

        private static bool IsHex(string value, int length)
        {
            if (value.Length != length)
                return false;
            foreach (char c in value)
                if (!Compat.IsAsciiHexDigit(c))
                    return false;
            return true;
        }

        private async Task<byte[]> GetBytesAsync(string path, bool refresh, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            if (refresh)
                request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        }

        private bool VerifySignature(byte[] data, string signatureBase64)
        {
            try
            {
                byte[] signature = Convert.FromBase64String(signatureBase64);
                using var ecdsa = Compat.CreateEcdsa(_publicKeyPem);
                return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Absolute HTTPS URL on the resource host. Used for pack artifacts and the
        /// incremental index; per-file Modrinth download URLs keep their own HTTPS rule.
        /// </summary>
        public static bool IsAllowedResourceUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return false;
            if (uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
                return false;
            return string.Equals(uri.Host, AllowedPackHost, StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Host, LegacyAllowedPackHost, StringComparison.OrdinalIgnoreCase);
        }

        public void Dispose() => _http.Dispose();
    }
}
