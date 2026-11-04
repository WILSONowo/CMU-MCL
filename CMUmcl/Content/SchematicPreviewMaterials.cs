using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace CMUmcl.Content;

internal sealed record SchematicPreviewPack(string Name, string Path);
internal sealed record SchematicPreviewPackEntry(string Id, string Name, bool Enabled);
internal sealed record SchematicPreviewPackState(int FormatVersion, List<SchematicPreviewPackEntry> Packs);

internal static class SchematicPreviewMaterials
{
    private const long MaximumBytes = 256L * 1024 * 1024;
    private static string Root => Path.Combine(SchematicPreviewSession.ComponentRoot, "resource-packs");
    private static string StatePath => Path.Combine(Root, "packs.json");
    internal static bool SupportsStack => Version.TryParse(SchematicPreviewSession.InstalledVersion(), out var version) && version >= new Version(0,2,3);
    private static string PackPath(string id) => Path.Combine(Root, id + ".zip");

    internal static List<SchematicPreviewPackEntry> Read()
    {
        SchematicPreviewSession.CheckRegularPath(StatePath);
        if (!File.Exists(StatePath)) return new();
        if (new FileInfo(StatePath).Length > 32768) throw new IOException("材质包列表过大。");
        var state=JsonSerializer.Deserialize<SchematicPreviewPackState>(File.ReadAllText(StatePath));
        if (state==null || state.FormatVersion!=1 || state.Packs==null || state.Packs.Count>16 ||
            state.Packs.Any(p=>p==null || !SchematicPreviewProtocol.IsHash(p.Id) || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length>128 || p.Name.Any(char.IsControl)) ||
            state.Packs.Select(p=>p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=state.Packs.Count)
            throw new IOException("材质包列表无效，请重新导入。");
        return state.Packs.Select(p=>p with {Id=p.Id.ToLowerInvariant()}).ToList();
    }

    internal static IReadOnlyList<SchematicPreviewPack> Enabled()
    {
        var result=new List<SchematicPreviewPack>();long total=0;
        foreach(var entry in Read().Where(p=>p.Enabled)) {
            string path=PackPath(entry.Id);SchematicPreviewSession.CheckRegularPath(path);
            total=checked(total+new FileInfo(path).Length);
            if(total>MaximumBytes)throw new IOException("已启用的自定义材质包合计不能超过 256 MiB。");
            if(SchematicPreviewSession.Hash(path)!=entry.Id.ToLowerInvariant())throw new IOException("材质包文件已变化，请重新导入："+entry.Name);
            result.Add(new(entry.Name,path));
        }
        return result;
    }

    internal static void Import(IEnumerable<string> sources)
    {
        using var held=SchematicPreviewSession.AcquireInstallLock();
        var entries=Read();SchematicPreviewSession.CheckRegularPath(Root);Directory.CreateDirectory(Root);
        var created=new List<string>();
        try {
        foreach(string source in sources) {
            SchematicPreviewSession.CheckRegularPath(source);
            string temporary=Path.Combine(Root,"import-"+Guid.NewGuid().ToString("N")+".tmp");
            try {
                using(var input=File.OpenRead(source))using(var output=File.Create(temporary)) {
                    if(input.Length==0||input.Length>MaximumBytes)throw new IOException("请选择不超过 256 MiB 的资源包 ZIP。");
                    byte[] buffer=new byte[65536];long total=0;int count;
                    while((count=input.Read(buffer,0,buffer.Length))!=0) { total+=count;if(total>MaximumBytes)throw new IOException("材质包超过大小限制。");output.Write(buffer,0,count); }
                }
                Validate(temporary);
                string id=SchematicPreviewSession.Hash(temporary);
                if(entries.Any(p=>p.Id==id))continue;
                if(entries.Count>=16)throw new IOException("最多保存 16 个自定义材质包。");
                string name=Path.GetFileNameWithoutExtension(source);
                name=new string(name.Where(c=>!char.IsControl(c)).Take(128).ToArray());if(string.IsNullOrWhiteSpace(name))name="材质包";
                string target=PackPath(id);SchematicPreviewSession.CheckRegularPath(target);
                if(File.Exists(target)) { if(SchematicPreviewSession.Hash(target)!=id)throw new IOException("已有材质文件校验失败。"); }
                else {File.Move(temporary,target);created.Add(target);}
                // Like upstream, newly imported packs start at the lowest custom priority.
                entries.Add(new(id,name,true));
            } finally {if(File.Exists(temporary))File.Delete(temporary);}
        }
        Save(entries);
        } catch {
            foreach(string path in created)try{SchematicPreviewSession.CheckRegularPath(path);if(File.Exists(path))File.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}
            throw;
        }
    }

    internal static void Change(string id, bool? enabled=null, int move=0, bool remove=false)
    {
        using var held=SchematicPreviewSession.AcquireInstallLock();var entries=Read();
        int index=entries.FindIndex(p=>p.Id==id);if(index<0)throw new IOException("材质包已被移除，请刷新。");
        if(remove)entries.RemoveAt(index);
        else if(enabled.HasValue)entries[index]=entries[index] with {Enabled=enabled.Value};
        else if(move!=0) {int next=Math.Max(0,Math.Min(entries.Count-1,index+Math.Sign(move)));var entry=entries[index];entries.RemoveAt(index);entries.Insert(next,entry);}
        Save(entries);
        if(remove) {string path=PackPath(id);SchematicPreviewSession.CheckRegularPath(path);if(File.Exists(path))File.Delete(path);}
    }

    private static void Save(List<SchematicPreviewPackEntry> entries)
    {
        SchematicPreviewSession.CheckRegularPath(StatePath);Directory.CreateDirectory(Root);
        long total=entries.Where(p=>p.Enabled).Sum(p=>new FileInfo(PackPath(p.Id)).Length);
        if(total>MaximumBytes)throw new IOException("已启用的自定义材质包合计不能超过 256 MiB，可先禁用部分包。");
        string temporary=Path.Combine(Root,"state-"+Guid.NewGuid().ToString("N")+".tmp");
        try {File.WriteAllText(temporary,JsonSerializer.Serialize(new SchematicPreviewPackState(1,entries)));if(File.Exists(StatePath))File.Replace(temporary,StatePath,null);else File.Move(temporary,StatePath);}
        finally {if(File.Exists(temporary))File.Delete(temporary);}
    }

    private static void Validate(string path)
    {
        using var zip=ZipFile.OpenRead(path);long expanded=0;
        if(zip.Entries.Count>100000||zip.GetEntry("pack.mcmeta")==null)throw new IOException("请选择根目录包含 pack.mcmeta 的 Java 资源包 ZIP。");
        foreach(var entry in zip.Entries) {expanded=checked(expanded+entry.Length);if(entry.Length>64L*1024*1024||expanded>512L*1024*1024)throw new IOException("材质包展开大小超限。");}
        var metadata=zip.GetEntry("pack.mcmeta")!;if(metadata.Length>65536)throw new IOException("材质包说明过大。");
        using var input=metadata.Open();using var reader=new StreamReader(input);var text=new char[65537];int count=reader.ReadBlock(text,0,text.Length);
        if(count>65536)throw new IOException("材质包说明过大。");using var json=JsonDocument.Parse(new string(text,0,count));
        if(!json.RootElement.TryGetProperty("pack",out _))throw new IOException("材质包说明无效。");
    }
}
