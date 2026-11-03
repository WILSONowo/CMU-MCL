using System.IO;
using System.Reflection;
using System.Text.Json;
using CMUmcl.Content;

internal static partial class Program
{
    private sealed class InlineImportProgress(Action<ImportProgress> callback):IProgress<ImportProgress>{public void Report(ImportProgress value)=>callback(value);}
    private static async Task FolderImportChecks(InstalledStateStore installed)
    {
        string source=Path.Combine(_output,"source","26.3-Fabric 0.19.5");Directory.CreateDirectory(source);
        string json="{\"id\":\"26.3-Fabric 0.19.5\",\"clientVersion\":\"26.3\",\"type\":\"release\",\"mainClass\":\"net.fabricmc.loader.impl.launch.knot.KnotClient\",\"libraries\":[{\"name\":\"net.fabricmc:fabric-loader:0.19.5\"}]}";
        string versionFile=Path.Combine(source,"26.3-Fabric 0.19.5.json");File.WriteAllText(versionFile,json);
        foreach(string name in new[]{"mods/test.jar","saves/temporary/level.dat","options.txt"}){string path=Path.Combine(source,name);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,"fixture "+name);}
        var service=new InstanceImportService(installed);var result=await service.ImportAsync(source);
        Check(result.Game=="26.3"&&result.Loader=="fabric"&&result.LoaderVersion=="0.19.5","folder import: merged PCL JSON resolves exact game and Fabric version");
        Check(File.ReadAllText(Path.Combine(result.InstanceDirectory,"saves/temporary/level.dat"))=="fixture saves/temporary/level.dat"&&File.Exists(Path.Combine(result.InstanceDirectory,"mods/test.jar")),"folder import: world and mod files preserved in copied target");
        Check(File.ReadAllText(versionFile)==json&&!File.Exists(Path.Combine(source,"instance.json")),"folder import: source is never rewritten");
        var metadata=JsonSerializer.Deserialize<InstanceMetadata>(File.ReadAllText(Path.Combine(result.InstanceDirectory,"instance.json")))!;Check(metadata.Game=="26.3"&&metadata.LoaderVersion=="0.19.5","folder import: persisted metadata retains exact versions");
        var second=await service.ImportAsync(source);Check(second.InstanceDirectory!=result.InstanceDirectory&&File.Exists(Path.Combine(result.InstanceDirectory,"options.txt")),"folder import: repeated import avoids overwriting first instance");
        int count=Directory.GetDirectories(LauncherPaths.InstancesDirectory).Length;using var cancel=new CancellationTokenSource();bool canceled=false;
        try{await service.ImportAsync(source,"cancel-mid-copy",new InlineImportProgress(p=>{if(p.BytesCopied>0)cancel.Cancel();}),cancel.Token);}catch(OperationCanceledException){canceled=true;}
        Check(canceled&&Directory.GetDirectories(LauncherPaths.InstancesDirectory).Length==count,"folder import: cancellation removes partial target");
        File.WriteAllText(versionFile,json.Replace("\"clientVersion\":\"26.3\"","\"clientVersion\":\"invalid/version\""));bool rejected=false;try{await service.ImportAsync(source);}catch(InstanceImportException){rejected=true;}Check(rejected&&Directory.GetDirectories(LauncherPaths.InstancesDirectory).Length==count,"folder import: invalid base version does not register an instance");
        File.WriteAllText(versionFile,"{\"id\":\"custom\",\"inheritsFrom\":\"1.21.1\",\"clientVersion\":\"26.3\",\"type\":\"release\"}");var inherited=await service.ImportAsync(source);Check(inherited.Game=="1.21.1","folder import: existing explicit inheritance keeps precedence");
        File.WriteAllText(versionFile,"{\"id\":\"1.21.1\",\"type\":\"release\"}");Check((await service.ImportAsync(source)).Game=="1.21.1","folder import: vanilla id detection remains supported");
        foreach(var pair in new[]{("fabric","0.20.1"),("quilt","0.29.2"),("forge","1.21.1-52.0.1"),("neoforge","21.1.1")})
        {
            string patchJson=JsonSerializer.Serialize(new{id="HMCL custom name",patches=new[]{new{id="game",version="26.1.2"},new{id=pair.Item1,version=pair.Item2}}});File.WriteAllText(versionFile,patchJson);
            var hmcl=await service.ImportAsync(source);Check(hmcl.Game=="26.1.2"&&hmcl.Loader==pair.Item1&&hmcl.LoaderVersion==pair.Item2,"folder import: HMCL nested patches "+pair.Item1);
        }
        File.WriteAllText(versionFile,"{\"id\":\"HMCL renamed\",\"patches\":[{\"id\":\"game\",\"version\":\"26.1.2\"}]}");var hmclVanilla=await service.ImportAsync(source);Check(hmclVanilla.Game=="26.1.2"&&hmclVanilla.Loader=="vanilla","folder import: HMCL vanilla game patch");
        File.WriteAllText(Path.Combine(_output,"result.txt"),$"PASS {_checks} folder import checks");
    }
}
