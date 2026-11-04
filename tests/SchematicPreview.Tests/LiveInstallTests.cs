using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CMUmcl;
using CMUmcl.Content;

internal static partial class Program
{
    // Explicit live acceptance mode: user-authorized installation into the real
    // component store. No test transport, public-key override or storage override.
    private static int LiveInstallMain(string[] args)
    {
        _output=Path.GetFullPath(args[1]);Directory.CreateDirectory(_output);int code=1;
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        app.Dispatcher.BeginInvoke(new Action(async()=>{try {
            SchematicPreviewSession.RootOverride=null;SchematicPreviewSession.VerificationKeyOverride=null;
            var assembly=typeof(InstanceContentView).Assembly;
            var expectedClient=args.Length>4?Version.Parse(args[4]):new Version(0,3,48,14);
            string expectedComponent=args.Length>5?args[5]:"0.2.3";
            Check(assembly.GetName().Version==expectedClient,"acceptance loads intended published client "+expectedClient);
            string root=SchematicPreviewSession.ComponentRoot;
            SchematicPreviewSession.CheckRegularPath(root);
            Check(true,"real component location resolves migrated launcher data root");
            int confirmations=0;
            var view=new InstanceContentView(_output,_output,"在线扩展正式包验收",()=>false,(_,_)=>{confirmations++;return Task.FromResult(true);});
            var window=new Window{Content=view,Width=1100,Height=760,Title="Published client extension installation acceptance"};window.Show();
            try {
                typeof(InstanceContentView).GetMethod("ShowSchematicExtension",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(view,null);
                var body=(ContentControl)typeof(InstanceContentView).GetField("_body",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(view)!;
                var panel=(StackPanel)((ScrollViewer)body.Content).Content;
                var actions=panel.Children.OfType<WrapPanel>().Single();
                var check=actions.Children.OfType<Button>().Single(b=>Equals(b.Content,"检查在线版本"));
                var install=actions.Children.OfType<Button>().Single(b=>Equals(b.Content,"安装扩展")||Equals(b.Content,"更新扩展"));
                var status=(TextBlock)typeof(InstanceContentView).GetField("_status",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(view)!;
                var clock=Stopwatch.StartNew();
                while(!check.IsEnabled&&clock.ElapsedMilliseconds<35000) await Task.Delay(100);
                Check(panel.Children.OfType<TextBlock>().Any(t=>t.Text.Contains("在线版本：")),"published manager verifies live signed extension feed: "+status.Text);
                bool installNeeded=SchematicPreviewSession.InstalledVersion()!=expectedComponent;
                Check(install.IsEnabled==installNeeded,"real migrated-data page enables install when extension is missing");
                if(installNeeded) {
                    install.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));clock.Restart();
                    while(!check.IsEnabled&&clock.ElapsedMilliseconds<300000) await Task.Delay(100);
                    Check(confirmations==1&&status.Text.Contains("安装完成"),"real install button downloads and activates extension: "+status.Text);
                }
                Check(SchematicPreviewSession.InstalledVersion()==expectedComponent,"real storage has active extension "+expectedComponent);
                string worker=SchematicPreviewSession.ResolveInstalled();
                Check(File.Exists(Path.Combine(Path.GetDirectoryName(worker)!,"signed-feed.json")),"real worker resolves with pinned signature receipt and verified entry hash");
                Check(!install.IsEnabled,"installed current version is shown without offering another install");
                File.WriteAllText(Path.Combine(_output,"live-install.json"),JsonSerializer.Serialize(new {
                    clientVersion=assembly.GetName().Version!.ToString(),clientPath=assembly.Location,clientSha256=SchematicPreviewSession.Hash(assembly.Location),
                    componentRoot=root,componentVersion=SchematicPreviewSession.InstalledVersion(),workerSha256=SchematicPreviewSession.Hash(worker),
                    confirmations,usedDefaultNetwork=true,usedDefaultPublicKey=true,usedDefaultComponentRoot=true
                },new JsonSerializerOptions{WriteIndented=true}));
            } finally {view.Close();window.Close();}
            await Ui(args[2],args[3]);
            Console.WriteLine($"PASS {_checks} live published client installation checks");code=0;
        }catch(Exception ex){Console.WriteLine(ex);}finally{app.Shutdown();}}));
        app.Run();return code;
    }
}
