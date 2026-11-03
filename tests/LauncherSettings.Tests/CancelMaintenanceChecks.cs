// SPDX-License-Identifier: GPL-3.0-only
using System.IO;
using System.Reflection;
using CMUmcl;
using CMUmcl.Content;
using CMUmcl.Launch;
internal static partial class Program
{
    private static async Task CancelMaintenanceChecks(MainWindow owner,LaunchService launch)
    {
        var method=typeof(VersionCatalogService).GetMethod("WithTimeoutAsync",BindingFlags.Static|BindingFlags.NonPublic)!.MakeGenericMethod(typeof(string));
        Task<string?> Race(Func<Task<string>> factory,CancellationToken ct)=>(Task<string?>)method.Invoke(null,new object[]{factory,ct})!;
        using(var cancel=new CancellationTokenSource()){cancel.Cancel();bool called=false,cancelled=false;try{await Race(()=>{called=true;return Task.FromResult("bad");},cancel.Token);}catch(OperationCanceledException){cancelled=true;}Check(cancelled&&!called,"cancel: pre-cancel skips catalog network factory");}
        using(var cancel=new CancellationTokenSource()){var pending=new TaskCompletionSource<string>();var waiting=Race(()=>pending.Task,cancel.Token);cancel.Cancel();bool cancelled=false;try{await waiting;}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"cancel: cancellation winning timeout race propagates instead of empty result");pending.SetException(new IOException("fixture observed abandoned failure"));}
        Check(await Race(()=>Task.FromResult("ok"),CancellationToken.None)=="ok","cancel: successful catalog response unaffected");
        using(var cancel=new CancellationTokenSource()){cancel.Cancel();bool validation=false,cancelled=false;launch.MaintenanceValidateOverride=(_,_,_,_)=>{validation=true;return Task.CompletedTask;};try{await launch.MaintainInstanceAsync(Path.Combine(_output,"nonexistent"),"26.3","vanilla","",false,null,cancel.Token);}catch(OperationCanceledException){cancelled=true;}finally{launch.MaintenanceValidateOverride=null;}Check(cancelled&&!validation,"cancel: maintenance cancellation precedes file access and validation");}
        var dialog=new InstanceSettingsView(Path.Combine(_output,"missing-ui-instance"),"cancel fixture",new SettingsStore()){};dialog.Show();
        var run=typeof(InstanceSettingsView).GetMethod("RunMaintenance",BindingFlags.Instance|BindingFlags.NonPublic)!;
        Func<CancellationToken,Task> action=async ct=>{await Task.Delay(5000,ct);};var task=(Task)run.Invoke(dialog,new object[]{action,"must not succeed"})!;
        var cts=(CancellationTokenSource)typeof(InstanceSettingsView).GetField("_maintenanceCts",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(dialog)!;cts.Cancel();await task;
        var status=(System.Windows.Controls.TextBlock)typeof(InstanceSettingsView).GetField("_status",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(dialog)!;
        Check(status.Text.StartsWith("已取消")&&!status.Text.Contains("网络"),"cancel: actual maintenance UI shows cancelled message");dialog.Close();
        File.WriteAllText(Path.Combine(_output,"result.txt"),$"PASS {_checks} cancellation checks");
    }
}
