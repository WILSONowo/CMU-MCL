// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Diagnostics;
using System.Security.Cryptography;

namespace CMUmcl.Content
{
    internal static class InstanceMaintenance
    {
        internal static void EnsureStopped(string directory)
        {
            string file=Path.Combine(directory,".cmumcl-running.json");if(!File.Exists(file))return;
            var data=JsonSerializer.Deserialize<InstanceSharing.Journal>(File.ReadAllText(file));if(data==null)throw new IOException("运行记录无效，请先确认游戏已退出。");
            bool running=false;try{var p=Process.GetProcessById(data.Pid);running=!p.HasExited && p.StartTime.ToUniversalTime().Ticks==data.Started;}catch{ }
            if(running)throw new IOException("此实例正在运行，请先退出游戏。");
        }
        internal static void RecordRunning(string directory,Process process)=>File.WriteAllText(Path.Combine(directory,".cmumcl-running.json"),JsonSerializer.Serialize(new InstanceSharing.Journal{Pid=process.Id,Started=process.StartTime.ToUniversalTime().Ticks}));
        internal static InstanceMetadata Read(string directory)=>JsonSerializer.Deserialize<InstanceMetadata>(File.ReadAllText(Path.Combine(directory,"instance.json")))??throw new InvalidDataException("实例信息无效。");
        internal static void Commit(string directory,InstanceMetadata metadata)
        {
            string path=Path.Combine(directory,"instance.json"),temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            string backup=Path.Combine(directory,".cmumcl-maintenance-backups",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(backup);File.Copy(path,Path.Combine(backup,"instance.json"));
            var marker=Path.Combine(directory,ProvisioningMarker.FileName);if(File.Exists(marker))File.Copy(marker,Path.Combine(backup,ProvisioningMarker.FileName));
            try{File.WriteAllText(temp,JsonSerializer.Serialize(metadata,new JsonSerializerOptions{WriteIndented=true}));File.Replace(temp,path,null);}finally{if(File.Exists(temp))File.Delete(temp);}
        }
        internal static int QuarantineCorruptDependencies(string game)
        {
            string root=LauncherPaths.MinecraftDirectory;int count=0;
            string backup=Path.Combine(root,".repair-quarantine",Guid.NewGuid().ToString("N"));
            void Check(string relative,JsonElement entry)
            {
                var path=PackFileSystem.ResolveUnder(root,PackFileSystem.SanitizeRelativePath(relative));if(!File.Exists(path))return;
                bool invalid=entry.TryGetProperty("size",out var size) && new FileInfo(path).Length!=size.GetInt64();
                if(entry.TryGetProperty("sha1",out var hash) && hash.GetString() is string expected){using var sha=SHA1.Create();using var file=File.OpenRead(path);invalid|=!BitConverter.ToString(sha.ComputeHash(file)).Replace("-","").Equals(expected,StringComparison.OrdinalIgnoreCase);}
                if(invalid){var target=PackFileSystem.ResolveUnder(backup,PackFileSystem.SanitizeRelativePath(relative));Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Move(path,target);count++;}
            }
            var visited=new System.Collections.Generic.HashSet<string>();
            void Version(string id)
            {
                if(!visited.Add(id))return;
                string rel="versions/"+id+"/"+id+".json";var path=PackFileSystem.ResolveUnder(root,PackFileSystem.SanitizeRelativePath(rel));if(!File.Exists(path))return;
                JsonDocument json;
                try{json=JsonDocument.Parse(File.ReadAllText(path));}
                catch(JsonException){var target=PackFileSystem.ResolveUnder(backup,PackFileSystem.SanitizeRelativePath(rel));Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Move(path,target);count++;return;}
                using var document=json;var data=json.RootElement;
                if(data.TryGetProperty("inheritsFrom",out var parent))Version(parent.GetString()!);
                if(data.TryGetProperty("downloads",out var downloads)&&downloads.TryGetProperty("client",out var client))Check("versions/"+id+"/"+id+".jar",client);
                if(data.TryGetProperty("libraries",out var libs))foreach(var lib in libs.EnumerateArray())if(lib.TryGetProperty("downloads",out var dl))
                {
                    if(dl.TryGetProperty("artifact",out var artifact)&&artifact.TryGetProperty("path",out var ap))Check("libraries/"+ap.GetString(),artifact);
                    if(dl.TryGetProperty("classifiers",out var classifiers))foreach(var item in classifiers.EnumerateObject())if(item.Value.TryGetProperty("path",out var cp))Check("libraries/"+cp.GetString(),item.Value);
                }
                if(data.TryGetProperty("assetIndex",out var asset)&&asset.TryGetProperty("id",out var index))
                {
                    string indexPath="assets/indexes/"+index.GetString()+".json";Check(indexPath,asset);var full=PackFileSystem.ResolveUnder(root,PackFileSystem.SanitizeRelativePath(indexPath));
                    if(File.Exists(full)){using var doc=JsonDocument.Parse(File.ReadAllText(full));foreach(var item in doc.RootElement.GetProperty("objects").EnumerateObject()){var value=item.Value;string hash=value.GetProperty("hash").GetString()!;if(hash.Length!=40 || !hash.All(Uri.IsHexDigit))throw new InvalidDataException("资源哈希无效。");using var expected=JsonDocument.Parse("{\"sha1\":\""+hash+"\",\"size\":"+value.GetProperty("size").GetInt64()+"}");Check("assets/objects/"+hash.Substring(0,2)+"/"+hash,expected.RootElement);}}
                }
            }
            Version(game);return count;
        }
    }
}
