// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CMUmcl.Content;

namespace CMUmcl.Community
{
    /// <summary>Simple install progress (reported by the VM).</summary>
    internal sealed class CommunityInstallProgress
    {
        public string Stage = "";
        public string Message = "";
        public long? BytesDownloaded;
        public long? TotalBytes;
    }

    /// <summary>One install request (single file).</summary>
    internal sealed class CommunityInstallRequest
    {
        public ResourceProject Project = null!;
        public ResourceVersion Version = null!;
        public ResourceFile File = null!;
        public ResourceKind Kind;
        public string? TargetInstanceId;    // null for Modpack
        public string? WorldName;           // Datapack only
        public string? ModpackDisplayName;  // Modrinth modpack: new instance name
        public string? ModpackSavePath;     // CurseForge modpack: final save path
        public Func<string, bool>? ConfirmOverwrite; // returns true to overwrite (conflict confirmation)
    }

    /// <summary>
    /// Installs a single resource file. Hands the FINAL target path to ResumableDownload (which owns
    /// .partial/.partial.json/verification/atomic commit). Never appends .partial itself, never calls
    /// Compat.Move after the downloader. Modrinth modpacks import via MrpackImportService; CurseForge
    /// modpacks are download-only.
    /// </summary>
    internal sealed class ResourceInstallService : IDisposable
    {
        private static readonly HashSet<string> DownloadHosts = CreateDownloadHosts();

        private static HashSet<string> CreateDownloadHosts()
        {
            var set = new HashSet<string>(CommunityHosts.ModrinthCdn, StringComparer.OrdinalIgnoreCase);
            set.UnionWith(CommunityHosts.CurseForgeCdn);
            return set;
        }

        private readonly ResourceTargets _targets;
        private readonly MrpackImportService _mrpack;
        private readonly HttpClient _download;

        public ResourceInstallService(ResourceTargets targets, MrpackImportService mrpack, Func<string?>? apiKeyProvider = null)
            : this(targets, mrpack, CMUmcl.Network.LauncherProxy.CreateHandler(allowAutoRedirect: false, useCookies: false), apiKeyProvider) { }

        internal ResourceInstallService(ResourceTargets targets, MrpackImportService mrpack, HttpMessageHandler downloadHandler, Func<string?>? apiKeyProvider = null)
        {
            _targets = targets;
            _mrpack = mrpack;
            _download = new HttpClient(new SafeRedirectHandler(new CurseForgeRequestHandler(downloadHandler, apiKeyProvider ?? (() => CommunityFeatures.EnabledCurseForgeKey), api: false), host => DownloadHosts.Contains(host), sameHostOnly: false))
            {
                Timeout = TimeSpan.FromMinutes(30),
            };
            _download.DefaultRequestHeaders.UserAgent.ParseAdd(CommunityHttp.UserAgent);
        }

        public async Task<CommunityInstallResult> InstallAsync(CommunityInstallRequest request, IProgress<CommunityInstallProgress>? progress, CancellationToken ct)
        {
            var result = new CommunityInstallResult();

            // Modpack preflight: source-specific extension + hash only. This runs before
            // EvaluateInstall so CurseForge modpacks (".zip", download-only) are not hard-blocked
            // by the generic Modpack -> ".mrpack" rule, and no target-instance rules
            // (LoaderState / gameDir / datapack world) are applied to modpacks.
            if (request.Kind == ResourceKind.Modpack)
            {
                string expectedExt = request.Project.Source == ResourceSourceId.Modrinth ? ".mrpack" : ".zip";
                string ext = Path.GetExtension(request.File.FileName ?? "").ToLowerInvariant();
                if (ext != expectedExt)
                {
                    result.HardBlocked = true;
                    result.Message = "文件类型与所选分类不符。";
                    return result;
                }
                if (!request.File.HasAnyHash)
                {
                    result.HardBlocked = true;
                    result.Message = "来源未提供 SHA-1/SHA-512 哈希，无法校验，禁止安装。";
                    return result;
                }
                if (string.IsNullOrWhiteSpace(request.File.Url))
                {
                    result.HardBlocked = true;
                    result.Message = "来源未提供可用的文件下载地址，请稍后重试。";
                    return result;
                }

                try
                {
                    string target = await InstallModpackAsync(request, progress, ct).ConfigureAwait(false);
                    result.Success = true;
                    result.TargetPath = target;
                    result.Message = request.Project.Source == ResourceSourceId.Modrinth
                        ? "整合包已导入。" : "整合包已下载。";
                }
                catch (OperationCanceledException)
                {
                    result.Cancelled = true;
                    result.Message = "安装已取消。";
                }
                catch (Exception ex) when (ex is ResourceException or InstanceImportException or IOException or HttpRequestException)
                {
                    result.Message = ex.Message;
                }
                catch (Exception ex)
                {
                    result.Message = "安装失败：" + ex.Message;
                }
                return result;
            }

            // Hard-block preflight for non-modpack kinds.
            CompatibilityEvaluator.InstallDecision decision = CompatibilityEvaluator.EvaluateInstall(
                request.Kind,
                ResolveTargetState(request),
                ResolveInstanceLoader(request),
                ResolveInstanceGame(request),
                request.Version.Loaders,
                request.Version.GameVersions,
                request.File,
                datapackWorldSelected: request.Kind != ResourceKind.Datapack || !string.IsNullOrWhiteSpace(request.WorldName));

            if (decision.HardBlocks.Count > 0)
            {
                result.HardBlocked = true;
                result.Message = decision.HardBlocks[0];
                return result;
            }
            result.SoftWarnings = decision.SoftWarnings;

            // No download URL -> hard-block (CurseForge version files carry an empty URL until resolved).
            if (string.IsNullOrWhiteSpace(request.File.Url))
            {
                result.HardBlocked = true;
                result.Message = "来源未提供可用的文件下载地址，请稍后重试。";
                return result;
            }

            string targetDir;
            try { targetDir = ResolveTargetDirectory(request); }
            catch (ResourceException ex) { result.HardBlocked = true; result.Message = ex.Message; return result; }

            Directory.CreateDirectory(targetDir);
            string finalPath = Path.Combine(targetDir, SafeFileName(request.File.FileName, request.Version.VersionNumber));

            // Conflict: skip when same hash; confirm overwrite otherwise (before downloading).
            if (File.Exists(finalPath) && FileMatches(finalPath, request.File))
            {
                result.Skipped = true;
                result.Message = "已存在相同版本，已跳过。";
                return result;
            }
            if (File.Exists(finalPath) && !(request.ConfirmOverwrite?.Invoke(finalPath) ?? false))
            {
                result.Cancelled = true;
                result.Message = "已取消（未覆盖）。";
                return result;
            }

            try
            {
                await ResumableDownload.DownloadAsync(
                    _download, request.File.Url, finalPath, request.File.ToExpectation(),
                    (downloaded, total) => progress?.Report(new CommunityInstallProgress
                    {
                        Stage = "下载",
                        Message = Path.GetFileName(finalPath),
                        BytesDownloaded = downloaded,
                        TotalBytes = total,
                    }),
                    ct).ConfigureAwait(false);
                result.Success = true;
                result.TargetPath = finalPath;
                return result;
            }
            catch (OperationCanceledException)
            {
                TryDelete(finalPath + ".partial");
                TryDelete(finalPath + ".partial.json");
                result.Cancelled = true;
                result.Message = "安装已取消。";
                return result;
            }
            catch (InvalidDataException)
            {
                TryDelete(finalPath + ".partial");
                TryDelete(finalPath + ".partial.json");
                result.Message = "文件校验失败（哈希或大小不匹配），已取消。";
                return result;
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException)
            {
                // Do not expose internal exception/status details; keep a clear user-facing message.
                result.Message = "下载服务暂时不可用，请稍后重试。";
                return result;
            }
        }

        public void Dispose() => _download.Dispose();

        // ---------------------------------------------------------------- internals

        private LoaderState ResolveTargetState(CommunityInstallRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.TargetInstanceId)) return LoaderState.Unknown;
            ResourceTarget? t = _targets.EnumerateInstances().FirstOrDefault(x => x.Id == request.TargetInstanceId);
            return t?.LoaderState ?? LoaderState.Unknown;
        }

        private string ResolveInstanceLoader(CommunityInstallRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.TargetInstanceId)) return "";
            ResourceTarget? t = _targets.EnumerateInstances().FirstOrDefault(x => x.Id == request.TargetInstanceId);
            return t?.Loader ?? "";
        }

        private string ResolveInstanceGame(CommunityInstallRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.TargetInstanceId)) return "";
            ResourceTarget? t = _targets.EnumerateInstances().FirstOrDefault(x => x.Id == request.TargetInstanceId);
            return t?.Game ?? "";
        }

        private string ResolveTargetDirectory(CommunityInstallRequest request)
        {
            ResourceTarget? t = _targets.EnumerateInstances().FirstOrDefault(x => x.Id == request.TargetInstanceId);
            if (t == null || string.IsNullOrWhiteSpace(t.GameDir))
                throw new ResourceException("无法确定资源写入位置（实例信息缺失/损坏，请修复 instance.json）。");
            if (request.Kind == ResourceKind.Datapack)
            {
                if (string.IsNullOrWhiteSpace(request.WorldName)) throw new ResourceException("未选择世界。");
                return ResourceTargets.ResolveDatapackDirectory(t.GameDir!, request.WorldName);
            }
            return ResourceTargets.ResolveTargetDirectory(request.Kind, t.GameDir!);
        }

        private async Task<string> InstallModpackAsync(CommunityInstallRequest request, IProgress<CommunityInstallProgress>? progress, CancellationToken ct)
        {
            if (request.Project.Source == ResourceSourceId.Modrinth)
            {
                string dir = Path.Combine(LauncherPaths.CacheDirectory, "community", "modpacks");
                Directory.CreateDirectory(dir);
                string mrpack = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".mrpack");
                try
                {
                    await ResumableDownload.DownloadAsync(
                        _download, request.File.Url, mrpack, request.File.ToExpectation(),
                        (d, t) => progress?.Report(new CommunityInstallProgress { Stage = "下载整合包", Message = "下载整合包", BytesDownloaded = d, TotalBytes = t }),
                        ct).ConfigureAwait(false);
                    var importProgress = new Progress<ImportProgress>(p => progress?.Report(new CommunityInstallProgress { Stage = "导入", Message = p.Message }));
                    InstanceImportResult imported = await _mrpack.ImportAsync(mrpack, request.ModpackDisplayName, importProgress, ct).ConfigureAwait(false);
                    return imported.InstanceDirectory;
                }
                finally
                {
                    TryDelete(mrpack);
                    TryDelete(mrpack + ".partial");
                    TryDelete(mrpack + ".partial.json");
                }
            }

            // CurseForge modpack: download-only to the user save path.
            if (string.IsNullOrWhiteSpace(request.ModpackSavePath))
                throw new ResourceException("请选择整合包保存位置。");
            string savePath = Path.GetFullPath(request.ModpackSavePath);
            string? saveDir = Path.GetDirectoryName(savePath);
            if (string.IsNullOrWhiteSpace(saveDir))
                throw new ResourceException("整合包保存位置无效。");
            Directory.CreateDirectory(saveDir);
            if (File.Exists(savePath) && !(request.ConfirmOverwrite?.Invoke(savePath) ?? false))
                throw new ResourceException("已取消（未覆盖）。");
            try
            {
                await ResumableDownload.DownloadAsync(
                    _download, request.File.Url, savePath, request.File.ToExpectation(),
                    (d, t) => progress?.Report(new CommunityInstallProgress { Stage = "下载整合包", Message = Path.GetFileName(savePath), BytesDownloaded = d, TotalBytes = t }),
                    ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryDelete(savePath + ".partial");
                TryDelete(savePath + ".partial.json");
                throw;
            }
            catch (InvalidDataException)
            {
                TryDelete(savePath + ".partial");
                TryDelete(savePath + ".partial.json");
                throw;
            }
            return savePath;
        }

        private static bool FileMatches(string path, ResourceFile file)
        {
            try
            {
                var info = new FileInfo(path);
                if (file.Size > 0 && info.Length != file.Size) return false;

                bool hasSha1 = !string.IsNullOrWhiteSpace(file.Sha1);
                bool hasSha512 = !string.IsNullOrWhiteSpace(file.Sha512);

                if (hasSha1 || hasSha512)
                {
                    using var sha1 = hasSha1 ? IncrementalHash.CreateHash(HashAlgorithmName.SHA1) : null;
                    using var sha512 = hasSha512 ? IncrementalHash.CreateHash(HashAlgorithmName.SHA512) : null;

                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072);
                    var buffer = new byte[131072];
                    int read;
                    while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (hasSha1) sha1!.AppendData(buffer, 0, read);
                        if (hasSha512) sha512!.AppendData(buffer, 0, read);
                    }

                    if (hasSha1 && !string.Equals(Compat.Hex(sha1!.GetHashAndReset()), file.Sha1!.Trim(), StringComparison.OrdinalIgnoreCase))
                        return false;
                    if (hasSha512 && !string.Equals(Compat.Hex(sha512!.GetHashAndReset()), file.Sha512!.Trim(), StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                return file.HasAnyHash;
            }
            catch { return false; }
        }

        private static string SafeFileName(string? fileName, string? versionNumber)
        {
            string name = Path.GetFileName(fileName ?? "").Trim();
            if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                string token = (versionNumber ?? "resource").Trim();
                token = new string(token.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
                return token.Length == 0 ? "资源" : "资源-" + token;
            }
            return name;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
        }
    }

    internal sealed class CommunityInstallResult
    {
        public bool Success;
        public bool Skipped;
        public bool Cancelled;
        public bool HardBlocked;
        public string Message = "";
        public string? TargetPath;
        public List<string> SoftWarnings = new();
    }
}
