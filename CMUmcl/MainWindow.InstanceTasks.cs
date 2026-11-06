using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using CMUmcl.Launch;
using CMUmcl.Content;

namespace CMUmcl;

public partial class MainWindow
{
    private readonly Dictionary<string, CancellationTokenSource> _launchRequests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Process> _gameProcesses = new(StringComparer.Ordinal);
    private readonly Dictionary<string, InstanceSettingsView> _settingsWindows = new(StringComparer.Ordinal);
    internal Func<InstanceCardViewModel, CancellationToken, Task<LaunchResult>>? _launchOverride;

    internal bool CanInstallPack(string id)
        => !_launchRequests.ContainsKey(id) && !_gameProcesses.ContainsKey(id)
        && !(_settingsWindows.TryGetValue(id,out var editor) && editor.MaintenanceBusy);

    private bool IsInstanceBusy(InstanceCardViewModel card)
        => _launchRequests.ContainsKey(card.InstanceId) || _gameProcesses.ContainsKey(card.InstanceId)
        || _cards.Any(x => x.InstanceId == card.InstanceId && x.IsProgressVisible);

    private InstanceSettingsView? _instanceEditor,_parentEditor;
    private InstanceContentView? _instanceContent;
    private void OpenInstanceContent(InstanceCardViewModel card)
        =>ShowInstanceContent(card,false);
    private void OpenInstanceDirectories(InstanceCardViewModel card)
        =>ShowInstanceContent(card,true);
    private void ShowInstanceContent(InstanceCardViewModel card,bool directoriesOnly)
    {
        CloseCreateInstanceView();CloseEmbeddedUiForNavigation();
        var metadata=InstanceMaintenance.Read(card.InstallPath);
        string gameDirectory=GameDirectoryResolver.ResolveGameDirectory(_settingsStore.Load(),card.InstallPath,metadata.Loader);
        _instanceContent=new InstanceContentView(card.InstallPath,gameDirectory,card.DisplayName,
            ()=>IsInstanceBusy(card) || !CanInstallPack(card.InstanceId)
                || (gameDirectory!=card.InstallPath && (_gameProcesses.Count!=0 || _launchRequests.Count!=0 || _activeOperations!=0 || CreateInstanceView.HasActiveOperation || _settingsWindows.Values.Any(x=>x.MaintenanceBusy))),
            async (title,message)=>await ShowInlinePromptAsync(title,message)==InlineChoice.Continue,directoriesOnly:directoriesOnly);
        _instanceContent.BackRequested+=ShowInstancesView;
        _currentPage=MainPage.InstanceContent;UpdateNavStyles();
        HeaderTitleText.Text=directoriesOnly?"实例目录":"实例内容";HeaderStatusText.Text=card.DisplayName;
        NewInstanceButton.Visibility=RefreshButton.Visibility=Visibility.Collapsed;
        CardsScroll.Visibility=EmptyStateText.Visibility=LoadingState.Visibility=ErrorState.Visibility=InstancesScroll.Visibility=InstancesEmptyStateText.Visibility=SettingsScroll.Visibility=CommunityResourceView.Visibility=Visibility.Collapsed;
        InstanceSettingsHost.Content=_instanceContent;InstanceSettingsHost.Visibility=Visibility.Visible;
    }
    private bool _leavingEditor;
    private void OpenInstanceSettings(InstanceCardViewModel card)
    {
        CloseCreateInstanceView();
        if(_settingsWindows.TryGetValue(card.InstanceId,out var existing))
        { if(!existing.IsClosed)ShowInstanceEditor(existing);return; }
        CloseEmbeddedUiForNavigation();
        var editor = new InstanceSettingsView(card.InstallPath,card.DisplayName,_settingsStore,launch:_launchService)
        {
            IsOperationBlocked=()=>IsInstanceBusy(card),
            IsMaintenanceBlocked=()=>_gameProcesses.Count!=0 || _launchRequests.Count!=0 || _activeOperations!=0 || CreateInstanceView.HasActiveOperation || _settingsWindows.Values.Any(x=>x.MaintenanceBusy),
        };
        _settingsWindows.Add(card.InstanceId,editor);
        editor.OperationFinished+=(_,_)=>{if(editor.IsClosed)_settingsWindows.Remove(card.InstanceId);};
        editor.Closed+=(_,_)=>{if(!editor.MaintenanceBusy)_settingsWindows.Remove(card.InstanceId);EmbeddedEditorClosed(editor);};
        editor.EditGlobalDefaultsRequested+=()=>
        {
            CancelInlinePrompt();_parentEditor=editor;
            var global=new InstanceSettingsView(editor.DirectoryPath,editor.Title,_settingsStore,true);
            global.Closed+=(_,_)=>EmbeddedEditorClosed(global);ShowInstanceEditor(global);
        };
        ShowInstanceEditor(editor);
    }

    private void ShowInstanceEditor(InstanceSettingsView editor)
    {
        _instanceEditor=editor;
        editor.ConfirmAsync=async (message,title)=>await ShowInlinePromptAsync(title,message)==InlineChoice.Continue;
        editor.BackRequested-=EditorBackRequested;editor.BackRequested+=EditorBackRequested;
        _currentPage=MainPage.InstanceSettings;UpdateNavStyles();
        HeaderTitleText.Text=editor.IsGlobal?"游戏默认设置":"实例管理";HeaderStatusText.Text=editor.Title;
        NewInstanceButton.Visibility=RefreshButton.Visibility=Visibility.Collapsed;
        CardsScroll.Visibility=EmptyStateText.Visibility=LoadingState.Visibility=ErrorState.Visibility=InstancesScroll.Visibility=InstancesEmptyStateText.Visibility=SettingsScroll.Visibility=CommunityResourceView.Visibility=Visibility.Collapsed;
        InstanceSettingsHost.Content=editor;InstanceSettingsHost.Visibility=Visibility.Visible;
    }

    private async void EditorBackRequested()
    {
        var editor=_instanceEditor;if(editor==null)return;
        if(editor.HasUnsavedChanges)
        {
            var choice=await ShowInlinePromptAsync(editor.IsGlobal?"保存游戏默认设置":"保存实例设置","有尚未保存的修改，是否保存后返回？","保存",true);
            if(!ReferenceEquals(editor,_instanceEditor) || editor.IsClosed || choice==InlineChoice.Cancel)return;
            if(choice==InlineChoice.Continue){editor.SaveAndClose();return;}
        }
        editor.Close();
    }

    private void EmbeddedEditorClosed(InstanceSettingsView editor)
    {
        if(_leavingEditor || !ReferenceEquals(editor,_instanceEditor))return;
        if(editor.IsGlobal && _parentEditor!=null)
        {var parent=_parentEditor;_parentEditor=null;if(editor.Saved)parent.RefreshInherited();ShowInstanceEditor(parent);return;}
        _instanceEditor=null;InstanceSettingsHost.Content=null;InstanceSettingsHost.Visibility=Visibility.Collapsed;
        if(!_windowClosed){RefreshAfterInstanceSettings();ShowInstancesView();}
    }

    private void CloseEmbeddedUiForNavigation()
    {
        CancelInlinePrompt();
        if(_instanceContent!=null){_instanceContent.Close();_instanceContent=null;InstanceSettingsHost.Content=null;InstanceSettingsHost.Visibility=Visibility.Collapsed;}
        if(_instanceEditor==null && _parentEditor==null)return;
        _leavingEditor=true;
        try { _instanceEditor?.Close();_parentEditor?.Close();_instanceEditor=_parentEditor=null;InstanceSettingsHost.Content=null;InstanceSettingsHost.Visibility=Visibility.Collapsed; }
        finally { _leavingEditor=false; }
        if(!_windowClosed)RefreshAfterInstanceSettings();
    }

    private readonly HashSet<Process> _stoppedByUser = new();
    private void TrackGame(InstanceCardViewModel card, Process process, GameProcessDiagnostics? diagnostics = null)
    {
        _gameProcesses[card.InstanceId] = process;
        void Finished()
        {
            if (_gameProcesses.TryGetValue(card.InstanceId,out var current) && ReferenceEquals(current,process))
            {
                _gameProcesses.Remove(card.InstanceId);
                int? code=null;try{code=process.ExitCode;}catch{ }
                bool stopped=_stoppedByUser.Remove(process);
                card.GameExited(code,code is null or 0?null:diagnostics?.DescribeHint(),stopped);
            }
        }
        process.Exited += (_,_) => { if(!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(new Action(Finished)); };
        process.EnableRaisingEvents = true;
        if(process.HasExited)Dispatcher.BeginInvoke(new Action(Finished));
    }

    private async void LaunchAction_Click(object sender, RoutedEventArgs e)
    {
        if(sender is not Button {DataContext:InstanceCardViewModel card})return;
        if(_launchRequests.TryGetValue(card.InstanceId,out var request)){request.Cancel();return;}
        if(!_gameProcesses.TryGetValue(card.InstanceId,out var process))return;
        if(process.HasExited){_gameProcesses.Remove(card.InstanceId);card.GameExited();return;}
        if(await ShowInlinePromptAsync("结束游戏","将结束此实例的游戏进程，未保存的游戏进度可能丢失。继续？")!=InlineChoice.Continue)return;
        // The process may exit while the embedded confirmation is open.
        if(!_gameProcesses.TryGetValue(card.InstanceId,out var current) || !ReferenceEquals(process,current))return;
        try { _stoppedByUser.Add(process); TerminateTrackedGame(process); }
        catch { _stoppedByUser.Remove(process); card.LaunchFail("无法结束游戏进程，请检查游戏是否已退出。"); }
    }

    // Operates only on the Process object returned by this launch. Never finds or
    // terminates unrelated Java processes by name or an unverified PID.
    internal static void TerminateTrackedGame(Process process)
    { if(!process.HasExited)process.Kill(); }
}
