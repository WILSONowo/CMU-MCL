// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using CMUmcl.Content;
using CMUmcl.Launch;

namespace CMUmcl
{
    internal sealed partial class InstanceSettingsView
    {
        internal Func<bool>? IsOperationBlocked {get;set;}
        internal Func<bool>? IsMaintenanceBlocked {get;set;}
        internal bool MaintenanceBusy=>_maintenanceBusy;
        private readonly LaunchService? _launch;
        private Button? _saveButton;
        private readonly ComboBox _gameVersion=new(){IsEditable=true},_loaderKind=new(){ItemsSource=new[]{"vanilla","fabric","forge","neoforge","quilt"}},_loaderVersion=new(){IsEditable=true};
        private readonly Dictionary<string,CheckBox> _syncChecks=new();
        private bool _maintenanceBusy;
        private CancellationTokenSource? _maintenanceCts;
        private void BuildMaintenancePages()
        {
            InstanceMetadata? metadata=null;try{metadata=InstanceMaintenance.Read(_directory);}catch{ }
            AddText(_pages[4],"安装信息与依赖修复");
            AddText(_pages[4],"修改版本可能导致模组或存档不兼容，请先备份存档。新版本准备成功后才更新实例记录；不会自动替换模组。社区整合包仅支持修复，修改版本请使用导入的本地副本。");
            AddField(_pages[4],"Minecraft 版本",_gameVersion);_gameVersion.Text=metadata?.Game??"";
            _pages[4].Children.Add(Button("获取 Minecraft 版本列表",()=>_=RunMaintenance(async ct=>{using var catalog=new VersionCatalogService();var values=await catalog.GetMinecraftVersionsAsync(ct);if(values.Count==0)throw new IOException();var selected=_gameVersion.Text;_gameVersion.ItemsSource=values.Select(x=>x.Name).ToArray();_gameVersion.Text=selected;},"版本列表已更新")));
            AddField(_pages[4],"平台",_loaderKind);_loaderKind.SelectedItem=string.IsNullOrEmpty(metadata?.Loader)?"vanilla":metadata!.Loader;
            AddField(_pages[4],"加载器版本（原版留空）",_loaderVersion);_loaderVersion.Text=metadata?.LoaderVersion??"";
            _pages[4].Children.Add(Button("获取兼容加载器版本",()=>_=RunMaintenance(async ct=>{using var catalog=new VersionCatalogService();var values=await catalog.GetLoaderVersionsAsync(_loaderKind.SelectedItem as string??"vanilla",_gameVersion.Text.Trim(),ct);_loaderVersion.ItemsSource=values.Select(x=>x.Version).ToArray();_loaderVersion.Text=values.FirstOrDefault()?.Version??"";},"加载器版本列表已更新")));
            _pages[4].Children.Add(Button("准备并应用版本修改",()=>StartInstallation(false)));
            _pages[4].Children.Add(Button("检查并修复当前版本依赖",()=>StartInstallation(true)));
            _pages[4].Children.Add(Button("取消当前操作",()=>_maintenanceCts?.Cancel()));
            AddText(_pages[5],"在启用相同项目的实例间共享设置。启动前载入共享快照，退出后回传；并非实时同步。多个共享实例不能同时运行。首次启用以已有共享数据为准，原文件会保留备份。取消勾选后保留当前本地副本。");
            AddText(_pages[5],"仅同步下列五项，不同步存档、模组、登录凭据。游戏设置跨版本可能不兼容；游戏目录由全局隔离策略决定，同一游戏目录的实例本来就会共用文件。");
            string[] selected;try{selected=InstanceOptionsStore.Load(_directory).SharedSettings;}catch{selected=Array.Empty<string>();}
            for(int i=0;i<InstanceSharing.Names.Length;i++){var box=new CheckBox{Content=InstanceSharing.Labels[i],IsChecked=selected.Contains(InstanceSharing.Names[i]),Margin=new Thickness(0,5,0,9)};_syncChecks.Add(InstanceSharing.Names[i],box);_pages[5].Children.Add(box);}
            _pages[5].Children.Add(Button("立即同步已勾选项目",async ()=>{
                if(!await Confirm("将使用共享数据替换对应本地文件，原文件保留备份。首次没有共享数据时以此实例初始化。继续？","同步设置") || IsClosed)return;
                _=RunMaintenance(async ct=>{InstanceMaintenance.EnsureStopped(_directory);var meta=InstanceMaintenance.Read(_directory);var gameDir=GameDirectoryResolver.ResolveGameDirectory(_store.Load(),_directory,meta.Loader);InstanceMaintenance.EnsureStopped(gameDir);var names=_syncChecks.Where(x=>x.Value.IsChecked==true).Select(x=>x.Key).ToArray();await Task.Run(()=>{ct.ThrowIfCancellationRequested();using var sharing=InstanceSharing.Begin(gameDir,names);},ct);},"同步完成；请点击保存以启用后续自动同步");
            }));
            AddText(_pages[5],"备份位置：当前数据目录 shared-instance-settings/backups。资源包共享限1GiB和20000文件，链接目录不参与同步。");
        }
        private async void StartInstallation(bool repair)
        {
            if(_launch==null){_status.Text="当前预览未连接安装服务。";return;}
            if(!await Confirm(repair?"将校验依赖并补齐缺失或损坏文件，不修改存档和模组。继续？":"请先备份存档。将准备目标版本及加载器，成功后切换，现有模组不会自动适配。继续？","实例安装") || IsClosed)return;
            string game=_gameVersion.Text.Trim(),loader=_loaderKind.SelectedItem as string??"vanilla",version=_loaderVersion.Text.Trim();
            if(repair){try{var meta=InstanceMaintenance.Read(_directory);game=meta.Game;loader=meta.Loader;version=meta.LoaderVersion;}catch{_status.Text="实例信息无效或无法读取，请先检查实例目录。";return;}}
            _=RunMaintenance(async ct=>{var progress=new Progress<LaunchProgress>(p=>{if(IsVisible)_status.Text=p.Stage+" · "+p.Message;});await _launch.MaintainInstanceAsync(_directory,game,loader,version,repair,progress,ct);Saved=true;},repair?"依赖修复完成":"版本修改完成；原元数据备份保留，重新进入可查看当前版本");
        }
        private async Task RunMaintenance(Func<CancellationToken,Task> action,string success)
        {
            if(IsClosed || _maintenanceBusy)return;
            if(IsOperationBlocked?.Invoke()==true || IsMaintenanceBlocked?.Invoke()==true){_status.Text="实例或共享依赖正在使用，请完成当前任务后重试维护。常规设置仍可编辑。";return;}
            _maintenanceBusy=true;_maintenanceCts=new();if(_saveButton!=null)_saveButton.IsEnabled=false;_status.Text="正在处理…";
            try{await action(_maintenanceCts.Token);_status.Text=success;}
            catch(OperationCanceledException){_status.Text="已取消；原版本记录保留，已下载文件可复用。";}
            catch(Exception ex){_status.Text=ex is IOException || ex is LaunchException?ex.Message:"操作失败，原版本记录保留，请检查网络或重试。";}
            finally{_maintenanceBusy=false;_maintenanceCts.Dispose();_maintenanceCts=null;if(_saveButton!=null)_saveButton.IsEnabled=!_loadFailed;OperationFinished?.Invoke(this,EventArgs.Empty);}
        }
    }
}
