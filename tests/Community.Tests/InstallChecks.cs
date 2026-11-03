// SPDX-License-Identifier: GPL-3.0-only
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using CMUmcl.Community;
using CMUmcl.Content;

internal static partial class Program
{
    // Serves a deterministic payload over an allowlisted host and counts requests
    // (so "hard-blocked before download" checks can assert zero network traffic).
    private sealed class PayloadHandler(string host, byte[] payload) : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            if (request.RequestUri!.Host != host)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
        }
    }

    private static void InstallChecks()
    {
        var root = Path.Combine(Path.GetTempPath(), "CMUmcl-install-" + Guid.NewGuid().ToString("N"));
        LauncherPaths.RootOverride = root;
        LauncherPaths.EnsureDirectories();
        var store = new InstalledStateStore();
        var targets = new ResourceTargets(store);
        var mrpack = new MrpackImportService(store);

        // Create a fabric instance (Known) so a Mod install is allowed.
        string fDir = Path.Combine(LauncherPaths.InstancesDirectory, "fabric1");
        Directory.CreateDirectory(fDir);
        File.WriteAllText(Path.Combine(fDir, "instance.json"), "{\"id\":\"local:b\",\"name\":\"Fabric\",\"game\":\"1.20.1\",\"loader\":\"fabric\"}");
        store.RecordInstall("local:b", 1, "fabric1", "Fabric");

        byte[] payload = Encoding.UTF8.GetBytes("hello mod");
        string sha1 = BitConverter.ToString(SHA1.Create().ComputeHash(payload)).Replace("-", "").ToLowerInvariant();
        var handler = new PayloadHandler("cdn.modrinth.com", payload);

        // 1. Successful Mod install writes final file, no .partial leftover.
        {
            using var svc = new ResourceInstallService(targets, mrpack, handler);
            var file = new ResourceFile { FileName = "mod.jar", Size = payload.Length, Sha1 = sha1, Url = "https://cdn.modrinth.com/mod.jar" };
            var req = new CommunityInstallRequest
            {
                Project = new ResourceProject { Source = ResourceSourceId.Modrinth },
                Version = new ResourceVersion { VersionNumber = "1.0", Loaders = new[] { "fabric" }, GameVersions = new[] { "1.20.1" } },
                File = file,
                Kind = ResourceKind.Mod,
                TargetInstanceId = "local:b",
            };
            var result = svc.InstallAsync(req, null, CancellationToken.None).GetAwaiter().GetResult();
            string modsDir = ResourceTargets.ResolveTargetDirectory(ResourceKind.Mod, fDir);
            string finalPath = Path.Combine(modsDir, "mod.jar");
            Check(result.Success && File.Exists(finalPath), "install: mod downloaded to final path");
            Check(!File.Exists(finalPath + ".partial") && !File.Exists(finalPath + ".partial.json"), "install: no .partial/.receipt leftover");
        }

        // 2. Same-hash existing file is skipped.
        {
            using var svc = new ResourceInstallService(targets, mrpack, handler);
            var file = new ResourceFile { FileName = "mod.jar", Size = payload.Length, Sha1 = sha1, Url = "https://cdn.modrinth.com/mod.jar" };
            var req = new CommunityInstallRequest
            {
                Project = new ResourceProject { Source = ResourceSourceId.Modrinth },
                Version = new ResourceVersion { VersionNumber = "1.0", Loaders = new[] { "fabric" }, GameVersions = new[] { "1.20.1" } },
                File = file, Kind = ResourceKind.Mod, TargetInstanceId = "local:b",
            };
            var result = svc.InstallAsync(req, null, CancellationToken.None).GetAwaiter().GetResult();
            Check(result.Skipped && result.Message.Contains("已存在相同版本"), "install: same hash skipped");
        }

        // 3. Mod into vanilla instance is hard-blocked (no download).
        {
            string vDir = Path.Combine(LauncherPaths.InstancesDirectory, "vanilla1");
            Directory.CreateDirectory(vDir);
            File.WriteAllText(Path.Combine(vDir, "instance.json"), "{\"id\":\"local:a\",\"name\":\"Vanilla\",\"game\":\"1.20.1\",\"loader\":\"vanilla\"}");
            store.RecordInstall("local:a", 1, "vanilla1", "Vanilla");
            using var svc = new ResourceInstallService(targets, mrpack, handler);
            var file = new ResourceFile { FileName = "mod.jar", Size = payload.Length, Sha1 = sha1, Url = "https://cdn.modrinth.com/mod.jar" };
            var req = new CommunityInstallRequest
            {
                Project = new ResourceProject { Source = ResourceSourceId.Modrinth },
                Version = new ResourceVersion { VersionNumber = "1.0", Loaders = new[] { "fabric" }, GameVersions = new[] { "1.20.1" } },
                File = file, Kind = ResourceKind.Mod, TargetInstanceId = "local:a",
            };
            var result = svc.InstallAsync(req, null, CancellationToken.None).GetAwaiter().GetResult();
            Check(result.HardBlocked && result.Message.Contains("原版"), "install: mod into vanilla hard-blocked");
        }

        // 4. Datapack into a world resolves saves\world\datapacks.
        {
            string saves = Path.Combine(LauncherPaths.GameDirectory, "saves");
            Directory.CreateDirectory(Path.Combine(saves, "w1"));
            File.WriteAllText(Path.Combine(saves, "w1", "level.dat"), "x");
            // The vanilla instance (local:a) shares gameDir under ModdedOnly, so the datapack
            // target is LauncherPaths.GameDirectory\saves\w1\datapacks. Serve the exact zip payload.
            var zipPayload = Encoding.UTF8.GetBytes("datapack");
            string zipSha1 = BitConverter.ToString(SHA1.Create().ComputeHash(zipPayload)).Replace("-", "").ToLowerInvariant();
            using var svc = new ResourceInstallService(targets, mrpack, new PayloadHandler("cdn.modrinth.com", zipPayload));
            var file = new ResourceFile { FileName = "d.zip", Size = zipPayload.Length, Sha1 = zipSha1, Url = "https://cdn.modrinth.com/d.zip" };
            var req = new CommunityInstallRequest
            {
                Project = new ResourceProject { Source = ResourceSourceId.Modrinth },
                Version = new ResourceVersion { VersionNumber = "1.0", Loaders = new[] { "datapack" }, GameVersions = new[] { "1.20.1" } },
                File = file, Kind = ResourceKind.Datapack, TargetInstanceId = "local:a", WorldName = "w1",
            };
            var result = svc.InstallAsync(req, null, CancellationToken.None).GetAwaiter().GetResult();
            string dpDir = ResourceTargets.ResolveDatapackDirectory(LauncherPaths.GameDirectory, "w1");
            Check(result.Success && File.Exists(Path.Combine(dpDir, "d.zip")), "install: datapack lands in saves\\world\\datapacks");
        }

        // 5. Hash mismatch cleans up partial/receipt and reports failure.
        {
            using var svc = new ResourceInstallService(targets, mrpack, handler);
            var file = new ResourceFile { FileName = "bad.jar", Size = payload.Length, Sha1 = new string('0', 40), Url = "https://cdn.modrinth.com/bad.jar" };
            var req = new CommunityInstallRequest
            {
                Project = new ResourceProject { Source = ResourceSourceId.Modrinth },
                Version = new ResourceVersion { VersionNumber = "1.0", Loaders = new[] { "fabric" }, GameVersions = new[] { "1.20.1" } },
                File = file, Kind = ResourceKind.Mod, TargetInstanceId = "local:b",
            };
            var result = svc.InstallAsync(req, null, CancellationToken.None).GetAwaiter().GetResult();
            string modsDir = ResourceTargets.ResolveTargetDirectory(ResourceKind.Mod, fDir);
            string bad = Path.Combine(modsDir, "bad.jar");
            Check(!result.Success && !File.Exists(bad), "install: hash mismatch leaves no 'installed' file");
        }

        // 6. CurseForge modpack (.zip) is download-only and succeeds.
        {
            byte[] packPayload = Encoding.UTF8.GetBytes("curseforge pack zip payload");
            string packSha1 = BitConverter.ToString(SHA1.Create().ComputeHash(packPayload)).Replace("-", "").ToLowerInvariant();
            using var svc = new ResourceInstallService(targets, mrpack, new PayloadHandler("mediafilez.forgecdn.net", packPayload), () => "fixture-project-key");
            string savePath = Path.Combine(Path.GetTempPath(), "CMUmcl-cfpack-" + Guid.NewGuid().ToString("N"), "pack.zip");
            var file = new ResourceFile { FileName = "pack.zip", Size = packPayload.Length, Sha1 = packSha1, Url = "https://mediafilez.forgecdn.net/files/1/2/pack.zip" };
            var req = new CommunityInstallRequest
            {
                Project = new ResourceProject { Source = ResourceSourceId.CurseForge },
                Version = new ResourceVersion { VersionNumber = "1.0" },
                File = file,
                Kind = ResourceKind.Modpack,
                ModpackSavePath = savePath,
            };
            var result = svc.InstallAsync(req, null, CancellationToken.None).GetAwaiter().GetResult();
            Check(result.Success && result.TargetPath == savePath && File.Exists(savePath), "install: curseforge modpack zip download-only succeeds");
        }

        // 7. Modrinth modpack (.zip) is hard-blocked by source-specific extension (no download).
        {
            var mrHandler = new PayloadHandler("cdn.modrinth.com", payload);
            using var svc = new ResourceInstallService(targets, mrpack, mrHandler);
            var file = new ResourceFile { FileName = "pack.zip", Size = payload.Length, Sha1 = sha1, Url = "https://cdn.modrinth.com/pack.zip" };
            var req = new CommunityInstallRequest
            {
                Project = new ResourceProject { Source = ResourceSourceId.Modrinth },
                Version = new ResourceVersion { VersionNumber = "1.0" },
                File = file,
                Kind = ResourceKind.Modpack,
            };
            var result = svc.InstallAsync(req, null, CancellationToken.None).GetAwaiter().GetResult();
            Check(result.HardBlocked && result.Message.Contains("文件类型"), "install: modrinth modpack zip hard-blocked");
            Check(mrHandler.Requests == 0, "install: modrinth modpack zip blocked before download");
        }

        // 8. Missing download URL hard-blocks both non-modpack and modpack installs.
        {
            // non-modpack: Mod with an empty URL.
            using (var svc = new ResourceInstallService(targets, mrpack, handler))
            {
                var file = new ResourceFile { FileName = "nou.jar", Size = payload.Length, Sha1 = sha1, Url = "" };
                var req = new CommunityInstallRequest
                {
                    Project = new ResourceProject { Source = ResourceSourceId.Modrinth },
                    Version = new ResourceVersion { VersionNumber = "1.0", Loaders = new[] { "fabric" }, GameVersions = new[] { "1.20.1" } },
                    File = file, Kind = ResourceKind.Mod, TargetInstanceId = "local:b",
                };
                var result = svc.InstallAsync(req, null, CancellationToken.None).GetAwaiter().GetResult();
                Check(result.HardBlocked && result.Message.Contains("下载地址"), "install: no URL hard-block (mod)");
            }

            // modpack: CurseForge with an empty URL.
            using (var svc = new ResourceInstallService(targets, mrpack, handler))
            {
                var file = new ResourceFile { FileName = "pack.zip", Size = payload.Length, Sha1 = sha1, Url = "" };
                var req = new CommunityInstallRequest
                {
                    Project = new ResourceProject { Source = ResourceSourceId.CurseForge },
                    Version = new ResourceVersion { VersionNumber = "1.0" },
                    File = file, Kind = ResourceKind.Modpack,
                };
                var result = svc.InstallAsync(req, null, CancellationToken.None).GetAwaiter().GetResult();
                Check(result.HardBlocked && result.Message.Contains("下载地址"), "install: no URL hard-block (curseforge modpack)");
            }
        }

        // 9. Unknown loader instance under ModdedOnly has no resolvable gameDir: hard-block, not exception.
        {
            string eDir = Path.Combine(LauncherPaths.InstancesDirectory, "empty1");
            Directory.CreateDirectory(eDir);
            File.WriteAllText(Path.Combine(eDir, "instance.json"), "{\"id\":\"local:c\",\"name\":\"Empty\",\"game\":\"1.20.1\",\"loader\":\"\"}");
            store.RecordInstall("local:c", 1, "empty1", "Empty");
            using var svc = new ResourceInstallService(targets, mrpack, handler);
            var file = new ResourceFile { FileName = "rp.zip", Size = payload.Length, Sha1 = sha1, Url = "https://cdn.modrinth.com/rp.zip" };
            var req = new CommunityInstallRequest
            {
                Project = new ResourceProject { Source = ResourceSourceId.Modrinth },
                Version = new ResourceVersion { VersionNumber = "1.0", GameVersions = new[] { "1.20.1" } },
                File = file, Kind = ResourceKind.ResourcePack, TargetInstanceId = "local:c",
            };
            var result = svc.InstallAsync(req, null, CancellationToken.None).GetAwaiter().GetResult();
            Check(result.HardBlocked && result.Message.Contains("无法确定"), "install: unknown loader gameDir hard-block");
        }
    }
}
