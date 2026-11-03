// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Diagnostics;

namespace CMUmcl.Content
{
    /// <summary>Snapshot exchange at game boundaries, with durable recovery and one active shared game.</summary>
    internal sealed class InstanceSharing : IDisposable
    {
        internal static readonly string[] Names={"options.txt","command_history.txt","servers.dat","hotbar.nbt","resourcepacks"};
        internal static readonly string[] Labels={"游戏设置（跨版本可能不兼容）","命令历史","多人服务器","创造模式快捷栏","资源包"};
        private static string Root=>Path.Combine(LauncherPaths.RootDirectory,"shared-instance-settings");
        private readonly FileStream _lock;
        private readonly Journal _journal;
        private bool _done;
        public sealed class Journal { public string Directory {get;set;}=""; public string[] Names {get;set;}=Array.Empty<string>();public int Pid{get;set;} public long Started{get;set;} }
        private static string JournalPath=>Path.Combine(Root,"pending.json");
        private InstanceSharing(FileStream gate,Journal journal){_lock=gate;_journal=journal;}
        private static void SafeTree(string path)
        {
            for(var p=path;!string.IsNullOrEmpty(p);p=Path.GetDirectoryName(p))
                if((File.Exists(p)||Directory.Exists(p)) && (File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("同步目录不能包含链接或重解析点。");
        }
        private static void Copy(string source,string target)
        {
            SafeTree(source);SafeTree(target);
            if(File.Exists(source)){Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(source,target,false);return;}
            if(!Directory.Exists(source))return;
            Directory.CreateDirectory(target);
            foreach(var item in Directory.EnumerateFileSystemEntries(source)){SafeTree(item);Copy(item,Path.Combine(target,Path.GetFileName(item)));}
        }
        private static void CheckBudget(string source,ref long bytes,ref int files)
        {
            SafeTree(source);
            if(File.Exists(source)){bytes+=new FileInfo(source).Length;if(++files>20000 || bytes>1024L*1024*1024)throw new IOException("共享数据超过1GiB或20000文件，请精简资源包。");}
            else if(Directory.Exists(source))foreach(var p in Directory.EnumerateFileSystemEntries(source))CheckBudget(p,ref bytes,ref files);
        }
        private static void ReplaceSnapshot(string source,string target)
        {
            SafeTree(source);SafeTree(target);long bytes=0;int files=0;CheckBudget(source,ref bytes,ref files);
            string stage=target+".stage-"+Guid.NewGuid().ToString("N"), backup=Path.Combine(Root,"backups",Guid.NewGuid().ToString("N"),Path.GetFileName(target));
            Copy(source,stage); // Nothing changes until copying has completed.
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if(File.Exists(target)||Directory.Exists(target)){Directory.CreateDirectory(Path.GetDirectoryName(backup)!);if(File.Exists(target))File.Move(target,backup);else Directory.Move(target,backup);}
            try{if(File.Exists(stage))File.Move(stage,target);else if(Directory.Exists(stage))Directory.Move(stage,target);}
            catch{if(File.Exists(backup))File.Move(backup,target);else if(Directory.Exists(backup))Directory.Move(backup,target);throw;}
        }
        private static void ValidateJournal(Journal journal)
        {
            var full=Path.GetFullPath(journal.Directory);var instances=Path.GetFullPath(LauncherPaths.InstancesDirectory).TrimEnd('\\')+"\\";
            if(!full.StartsWith(instances,StringComparison.OrdinalIgnoreCase) && !string.Equals(full,Path.GetFullPath(LauncherPaths.GameDirectory),StringComparison.OrdinalIgnoreCase))throw new IOException("共享记录目录无效。");
            if(journal.Names.Any(x=>!Names.Contains(x)))throw new IOException("共享记录项目无效。");SafeTree(full);
        }
        private static void Publish(Journal journal)
        {
            ValidateJournal(journal);
            foreach(var name in journal.Names){ReplaceSnapshot(Path.Combine(journal.Directory,name),Path.Combine(Root,"data",name));File.WriteAllText(Path.Combine(Root,name+".ready"),"1");}
        }
        private static void SaveJournal(Journal journal){var temp=JournalPath+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(journal));if(File.Exists(JournalPath))File.Replace(temp,JournalPath,null);else File.Move(temp,JournalPath);}
        internal static InstanceSharing? Begin(string directory,string[] selected)
        {
            if(selected.Length==0)return null;
            Directory.CreateDirectory(Root);SafeTree(Root);
            FileStream gate;try{gate=new FileStream(Path.Combine(Root,"session.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}catch(IOException){throw new IOException("共享设置正在被另一个游戏使用，请先退出该游戏。");}
            try
            {
                if(File.Exists(JournalPath))
                {
                    var previous=JsonSerializer.Deserialize<Journal>(File.ReadAllText(JournalPath))??throw new IOException("共享恢复记录损坏。");ValidateJournal(previous);
                    bool running=false;try{var p=Process.GetProcessById(previous.Pid);running=!p.HasExited && p.StartTime.ToUniversalTime().Ticks==previous.Started;}catch{ }
                    if(running)throw new IOException("前一个共享实例仍在运行，请退出后重试。");
                    Publish(previous);File.Delete(JournalPath);
                }
                var journal=new Journal{Directory=Path.GetFullPath(directory),Names=selected.ToArray()};ValidateJournal(journal);
                foreach(var name in selected)
                {
                    if(!File.Exists(Path.Combine(Root,name+".ready"))){ReplaceSnapshot(Path.Combine(directory,name),Path.Combine(Root,"data",name));File.WriteAllText(Path.Combine(Root,name+".ready"),"1");}
                    ReplaceSnapshot(Path.Combine(Root,"data",name),Path.Combine(directory,name));
                }
                SaveJournal(journal);return new(gate,journal);
            }
            catch{gate.Dispose();throw;}
        }
        internal void Attach(Process process)
        {
            _journal.Pid=process.Id;_journal.Started=process.StartTime.ToUniversalTime().Ticks;SaveJournal(_journal);
            process.EnableRaisingEvents=true;process.Exited+=(_,_)=>{try{Dispose();}catch{/* Durable journal is retried before the next shared launch. */}};
            if(process.HasExited)Dispose();
        }
        public void Dispose()
        {
            lock(this){if(_done)return;_done=true;try{Publish(_journal);File.Delete(JournalPath);}finally{_lock.Dispose();}}
        }
    }
}
