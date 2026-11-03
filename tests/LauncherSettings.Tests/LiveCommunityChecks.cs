using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CMUmcl;
using CMUmcl.Community;
using CMUmcl.Content;

internal static partial class Program
{
    // Opt-in real-network acceptance. Uses the production view/default factory and
    // command bindings; all state and installed files remain under RootOverride.
    private static async Task LiveCommunityChecks(Window window, SettingsStore settings, InstalledStateStore installed)
    {
        var rows = new List<object>();
        string stage = "Prepare";
        bool success = false;
        try
        {
            Check(CommunityFeatures.CurseForgeEnabled && !string.IsNullOrEmpty(CurseForgeCredentials.GetEmbeddedApiKey()), "local candidate enabled with embedded resource");
            Check(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CMUMCL_CURSEFORGE_API_KEY")), "no runtime credential override");
            var config = settings.Load(); config.VersionIsolation = VersionIsolation.All; settings.Save(config);
            Click(window, "CommunityNavButton");
            await Task.Delay(200); window.UpdateLayout();
            var view = Get<UserControl>(window, "CommunityResourceView");
            var vm = (CommunityResourceViewModel)view.DataContext;
            Check(view.IsVisible && vm.SourceOptions.Count == 3, "formal community page exposes both sources");
            await LiveUntil(() => !vm.IsLoading, TimeSpan.FromMinutes(3));
            Check(vm.Results.Count > 0 && !vm.HasError, "formal first entry automatically displays real resources without clicking search");
            Screenshot(window, "initial-resources.png");
            foreach (var source in new[] { ResourceSourceId.CurseForge, ResourceSourceId.Modrinth })
            {
                vm.BackToResultsCommand.Execute(null);
                stage = source + "Search"; Console.WriteLine("STEP " + stage);
                vm.SelectedTarget = null; vm.OnlyCompatible = false;
                vm.SelectedSourceOption = vm.SourceOptions.Single(o => o.Value == source);
                vm.SearchText = "sodium"; vm.Kind = ResourceKind.Mod;
                Check(Descendants(view).OfType<Button>().Any(b => ReferenceEquals(b.Command, vm.SearchCommand)), "formal search command bound");
                vm.SearchCommand.Execute(null);
                await LiveUntil(() => !vm.IsLoading, TimeSpan.FromMinutes(3));
                Check(!vm.HasError && !vm.HasPartialMessage && vm.Results.Count > 0 && vm.Results.All(c => c.Project.Source == source), "real search returns selected source");
                int searchCount = vm.Results.Count;
                var card = vm.Results.First(c => c.Project.Title.IndexOf("sodium", StringComparison.OrdinalIgnoreCase) >= 0);
                Screenshot(window, source + "-search.png");
                stage = source + "Versions";
                var versions = await vm.LoadVersionsForAsync(card, default);
                var chosen = versions.First(v => v.Loaders.Contains("fabric") && v.GameVersions.Any(g => g.StartsWith("1.")) && v.Files.Any(f => f.Size > 0 && f.Size <= 32L * 1024 * 1024 && f.HasSecureHash));
                string game = chosen.GameVersions.First(g => g.StartsWith("1."));
                string id = "live-" + source.ToString().ToLowerInvariant();
                string dir = Path.Combine(LauncherPaths.InstancesDirectory, id);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "instance.json"), JsonSerializer.Serialize(new InstanceMetadata { Id = id, Name = source + " 本地验收", Game = game, Loader = "fabric", LoaderVersion = "acceptance-only" }));
                installed.RecordInstall(id, 1, id, source + " 本地验收");
                vm.RefreshTargets(); vm.SelectedTarget = vm.TargetInstances.Single(t => t.Id == id);
                stage = source + "Detail"; Console.WriteLine("STEP " + stage);
                await vm.OpenDetailAsync(card);
                var detail = vm.Detail;
                Check(detail != null && !vm.HasError, "formal detail opens with real versions and grants");
                detail!.SelectedVersion = detail.Versions.Single(v => v.Id == chosen.Id);
                vm.OpenVersionCommand.Execute(detail.SelectedVersion);
                await detail.EnsureDependenciesAsync();
                await Task.Delay(100);
                var file = detail.SelectedVersion.Files.OrderByDescending(f => f.Primary).First();
                Check(file.Size > 0 && file.Size <= 32L * 1024 * 1024 && file.HasSecureHash && (source == ResourceSourceId.CurseForge || !string.IsNullOrEmpty(file.Url)), "selected download has verifiable metadata and resolves its official grant at install");
                Check(detail.CanInstall && detail.InstallCommand.CanExecute(null), "formal install command enabled for compatible isolated instance");
                await Task.Delay(150); window.UpdateLayout();
                Check(Descendants(view).OfType<Button>().Any(b => ReferenceEquals(b.Command, detail.InstallCommand)), "formal install button bound");
                Screenshot(window, source + "-detail.png");
                string target = Path.Combine(dir, "mods", file.FileName);
                Check(Path.GetFullPath(target).StartsWith(Path.GetFullPath(dir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && !File.Exists(target), "fresh isolated install target");
                stage = source + "Install"; Console.WriteLine("STEP " + stage);
                detail.InstallCommand.Execute(null);
                await LiveUntil(() => !detail.IsInstalling, TimeSpan.FromMinutes(5));
                Check(detail.Status == "安装完成" && !detail.HasHardBlock && File.Exists(target), "formal page reports installation complete");
                string sha1, sha512;
                using (var stream = File.OpenRead(target)) using (var hash = SHA1.Create()) sha1 = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                using (var stream = File.OpenRead(target)) using (var hash = SHA512.Create()) sha512 = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                bool hashes = (string.IsNullOrEmpty(file.Sha1) || string.Equals(file.Sha1, sha1, StringComparison.OrdinalIgnoreCase)) && (string.IsNullOrEmpty(file.Sha512) || string.Equals(file.Sha512, sha512, StringComparison.OrdinalIgnoreCase));
                Check(new FileInfo(target).Length == file.Size && hashes, "installed bytes match upstream size and every provided hash");
                Check(Directory.GetFiles(Path.Combine(dir, "mods")).Length == 1, "only committed mod remains in target directory");
                await Task.Delay(250); Screenshot(window, source + "-installed.png");
                rows.Add(new { source = source.ToString(), search_count = searchCount, game, installed_bytes = new FileInfo(target).Length, expected_bytes = file.Size, sha1, sha512, size_and_hashes_match = hashes, page_status_complete = detail.Status == "安装完成", isolated_target = id });
            }
            success = true; stage = "Complete";
        }
        finally
        {
            // No URLs, HTTP headers, exception messages, session data or credentials.
            File.WriteAllText(Path.Combine(_output, "live-community-report.json"), JsonSerializer.Serialize(new { schema = 1, success, stage, local_candidate_enabled = CommunityFeatures.CurseForgeEnabled, embedded_resource_present = !string.IsNullOrEmpty(CurseForgeCredentials.GetEmbeddedApiKey()), runtime_key_override_used = false, formal_view_and_default_factory = true, interaction = "bound-commands", isolated_data = true, game_launched = false, sources = rows }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static void CommunityDetailBindingChecks(Window window, InstalledStateStore installed)
    {
        var view = Get<UserControl>(window, "CommunityResourceView");
        var vm = (CommunityResourceViewModel)view.DataContext;
        var targets = new ResourceTargets(installed);
        using var service = new ResourceInstallService(targets, new MrpackImportService(installed));
        var detail = new ResourceDetailViewModel(new ResourceProject(), service, targets);
        detail.LoadInstances(ResourceKind.Mod, null);
        detail.LoadVersions(Array.Empty<ResourceVersion>(), ResourceKind.Mod, null);
        detail.BindInstall(ResourceKind.Mod, () => null);
        typeof(CommunityResourceViewModel).GetProperty("IsDetailPage")!.SetValue(vm, true);
        typeof(CommunityResourceViewModel).GetProperty("Detail")!.SetValue(vm, detail);
        typeof(CommunityResourceViewModel).GetProperty("IsVersionDialogOpen")!.SetValue(vm, true);
        window.UpdateLayout();
        var bar = Descendants(view).OfType<ProgressBar>().Single(b => System.Windows.Data.BindingOperations.GetBinding(b, ProgressBar.ValueProperty)?.Path.Path == "ProgressValue");
        Check(System.Windows.Data.BindingOperations.GetBinding(bar, ProgressBar.ValueProperty)!.Mode == System.Windows.Data.BindingMode.OneWay, "community: readonly detail progress uses one-way binding");
        detail.ReportProgress(new CommunityInstallProgress { Stage = "下载", BytesDownloaded = 5, TotalBytes = 10 });
        window.UpdateLayout();
        Check(bar.Value == 50, "community: actual detail view renders readonly install progress");
    }

    private static async Task LiveUntil(Func<bool> done, TimeSpan timeout)
    {
        DateTime limit = DateTime.UtcNow + timeout;
        while (!done()) { if (DateTime.UtcNow > limit) throw new TimeoutException(); await Task.Delay(100); }
    }
}
