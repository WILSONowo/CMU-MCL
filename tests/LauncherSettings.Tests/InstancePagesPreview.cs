using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using CMUmcl;
using CMUmcl.Content;
using CMUmcl.Launch;

internal static partial class Program
{
    // Uses the existing fixture transport and isolated data root; never a saved account.
    private static void ShowInstancePagesPreview(MainWindow window, InstalledStateStore installed, SettingsStore store)
    {
        const string id = "local:style-preview";
        string directory = Path.Combine(LauncherPaths.InstancesDirectory, "style-preview");
        Directory.CreateDirectory(directory);
        var metadata = new InstanceMetadata { Id = id, Name = "本地示例实例", Game = "1.21.1", Loader = "fabric", LoaderVersion = "0.16.10" };
        File.WriteAllText(Path.Combine(directory, "instance.json"), JsonSerializer.Serialize(metadata));
        installed.RecordInstall(id, 1, "style-preview", metadata.Name);
        var settings = store.Load(); settings.VersionIsolation = VersionIsolation.All; store.Save(settings);
        string mods = Path.Combine(directory, "mods"); Directory.CreateDirectory(mods);
        string example = Path.Combine(mods, "style-preview.jar");
        if (!File.Exists(example))
        {
            using var zip = ZipFile.Open(example, ZipArchiveMode.Create);
            using var writer = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open());
            writer.Write("{\"id\":\"cmumcl-style-preview\",\"name\":\"界面预览示例\",\"description\":\"用于查看资源列表样式的本地示例文件。\"}");
        }
        window._launchOverride = (_, _) => Task.FromException<LaunchResult>(new IOException("此示例仅用于界面预览。"));
        window.Title = "CMUmcl — NET48 HMCL 设置交互预览";
        typeof(MainWindow).GetMethod("RefreshInstances", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
        var card = new InstanceCardViewModel(id, metadata.Name, "本地预览", metadata, directory);
        if(Environment.GetCommandLineArgs().Contains("--copy-review"))
        {
            window.Title="CMUmcl — NET48 文案预览";
            typeof(MainWindow).GetMethod("ShowSettingsView",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,null);
            Click(window,"ProxyTab");return;
        }
        bool directories=Environment.GetCommandLineArgs().Contains("--directories");
        if(directories)window.Title="CMUmcl — NET48 目录卡片预览";
        typeof(MainWindow).GetMethod(directories?"OpenInstanceDirectories":"OpenInstanceSettings", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, new object[] { card });
    }
}
