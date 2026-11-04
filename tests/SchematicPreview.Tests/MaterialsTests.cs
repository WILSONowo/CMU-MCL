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
    private static int MaterialsMain(string[] args)
    {
        _output=Path.GetFullPath(args[1]);Directory.CreateDirectory(_output);int code=1;
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        app.Dispatcher.BeginInvoke(new Action(async()=>{try{
            SchematicPreviewSession.RootOverride=Path.Combine(_output,"components");
            SchematicPreviewSession.InstallLocal(Path.Combine(args[2],"component.json"));
            Check(SchematicPreviewMaterials.SupportsStack,"component 0.2.3 advertises bundled base and pack stack support");
            Check(SchematicPreviewMaterials.Read().Count==0&&SchematicPreviewMaterials.Enabled().Count==0,"fresh preview needs no local custom pack");
            await Ui(args[3],Path.Combine(_output,"no-local-pack.zip"));
            File.Copy(Path.Combine(_output,"page-dark.png"),Path.Combine(_output,"base-only.png"));
            string low=Path.Combine(args[4],"low-blue.zip"),high=Path.Combine(args[4],"high-red.zip");
            string lowHash=SchematicPreviewSession.Hash(low),highHash=SchematicPreviewSession.Hash(high);
            await Task.Run(()=>SchematicPreviewMaterials.Import(new[]{low,high}));
            var saved=SchematicPreviewMaterials.Read();
            Check(saved.Count==2&&saved[0].Id==lowHash&&saved[1].Id==highHash,"imports keep upstream lowest-custom-priority insertion order");
            Check(SchematicPreviewSession.Hash(low)==lowHash&&SchematicPreviewSession.Hash(high)==highHash,"imports preserve original ZIP files");
            await Task.Run(()=>SchematicPreviewMaterials.Import(new[]{low}));
            Check(SchematicPreviewMaterials.Read().Count==2,"duplicate content does not add duplicate layers");
            var enabled=SchematicPreviewMaterials.Enabled();
            Check(enabled.Count==2&&enabled[0].Name=="low-blue"&&enabled[1].Name=="high-red","enabled request order is highest priority first");
            Check(enabled.All(p=>p.Path.StartsWith(SchematicPreviewSession.ComponentRoot,StringComparison.OrdinalIgnoreCase)),"preview uses managed immutable copies");
            await Ui(args[3],Path.Combine(_output,"no-local-pack.zip"));
            File.Copy(Path.Combine(_output,"page-dark.png"),Path.Combine(_output,"low-blue-first.png"));
            Check(Dominant(Path.Combine(_output,"low-blue-first.png"),false)>500,"GPU scene uses highest-priority blue texture-only pack with base geometry");
            SchematicPreviewMaterials.Change(highHash,move:-1);
            Check(SchematicPreviewMaterials.Read()[0].Id==highHash,"priority changes persist to disk");
            var restart=new System.Diagnostics.ProcessStartInfo(Assembly.GetExecutingAssembly().Location){UseShellExecute=false,RedirectStandardOutput=true,CreateNoWindow=true};
            Compat.AddArgument(restart,"--material-state");Compat.AddArgument(restart,SchematicPreviewSession.ComponentRoot);
            using(var process=System.Diagnostics.Process.Start(restart)!) {
                string restored=await process.StandardOutput.ReadToEndAsync();process.WaitForExit();
                var restoredEntries=JsonSerializer.Deserialize<List<SchematicPreviewPackEntry>>(restored)!;
                Check(process.ExitCode==0&&restoredEntries[0].Id==highHash&&restoredEntries[1].Id==lowHash&&restoredEntries.All(p=>p.Enabled),"a fresh process restores enabled packs and priority from disk");
            }
            await Ui(args[3],Path.Combine(_output,"no-local-pack.zip"));
            File.Copy(Path.Combine(_output,"page-dark.png"),Path.Combine(_output,"high-red-first.png"));
            Check(Dominant(Path.Combine(_output,"high-red-first.png"),true)>500,"GPU scene changes to red when pack priority is reversed");
            SchematicPreviewMaterials.Change(highHash,enabled:false);
            Check(SchematicPreviewMaterials.Enabled().Count==1&&SchematicPreviewMaterials.Enabled()[0].Name=="low-blue","disabled layers are omitted from worker request");
            await MaterialsUi();
            SchematicPreviewMaterials.Change(highHash,enabled:false);
            string snapshot=File.ReadAllText(Path.Combine(SchematicPreviewSession.ComponentRoot,"resource-packs","packs.json"));
            string broken=Path.Combine(args[4],"broken.zip");
            await Fails(()=>Task.Run(()=>SchematicPreviewMaterials.Import(new[]{broken})),"invalid ZIP is rejected before replacing saved state");
            Check(snapshot==File.ReadAllText(Path.Combine(SchematicPreviewSession.ComponentRoot,"resource-packs","packs.json")),"failed import preserves prior enabled state and ordering");
            string rollback=Path.Combine(_output,"rollback.zip");
            using(var archive=System.IO.Compression.ZipFile.Open(rollback,System.IO.Compression.ZipArchiveMode.Create)) {
                using var writer=new StreamWriter(archive.CreateEntry("pack.mcmeta").Open());writer.Write("{\"pack\":{\"pack_format\":71,\"description\":\"rollback fixture\"}}");
            }
            string rollbackId=SchematicPreviewSession.Hash(rollback);
            await Fails(()=>Task.Run(()=>SchematicPreviewMaterials.Import(new[]{rollback,broken})),"multi-file import failure rolls back the whole selection");
            Check(!File.Exists(Path.Combine(SchematicPreviewSession.ComponentRoot,"resource-packs",rollbackId+".zip"))&&snapshot==File.ReadAllText(Path.Combine(SchematicPreviewSession.ComponentRoot,"resource-packs","packs.json")),"failed multi-file import leaves no copied orphan or partial priority state");
            SchematicPreviewMaterials.Change(lowHash,remove:true);
            Check(File.Exists(low),"removing a saved pack does not delete original ZIP");
            Check(SchematicPreviewMaterials.Read().Count==1&&SchematicPreviewMaterials.Enabled().Count==0,"removal and disabled pack leave bundled base available");
            SchematicPreviewMaterials.Change(highHash,remove:true);
            Check(SchematicPreviewMaterials.Read().Count==0,"empty saved stack remains valid");
            bool rejectedCount=false;
            try{await SchematicPreviewSession.OpenAsync(args[3],"",IntPtr.Zero,CancellationToken.None,packs:Enumerable.Repeat(new SchematicPreviewPack("x",low),17).ToArray());}
            catch(IOException ex){rejectedCount=ex.Message=="材质包请求无效。";}
            Check(rejectedCount,"session rejects oversized layer count before starting a worker");
            Console.WriteLine($"PASS {_checks} material stack checks");code=0;
        }catch(Exception ex){Console.WriteLine(ex);}finally{SchematicPreviewSession.RootOverride=null;app.Shutdown();}}));app.Run();return code;
    }

    private static int Dominant(string path,bool red)
    {
        using var bitmap=new System.Drawing.Bitmap(path);int count=0;
        for(int y=0;y<bitmap.Height;y+=4)for(int x=0;x<bitmap.Width;x+=4) {
            var pixel=bitmap.GetPixel(x,y);
            if(red?pixel.R>pixel.B*2&&pixel.R>pixel.G*2&&pixel.R>50:pixel.B>pixel.R*2&&pixel.B>pixel.G*2&&pixel.B>50)count++;
        }
        return count;
    }

    private static async Task MaterialsUi()
    {
        var view=new InstanceContentView(_output,_output,"材质管理验收",()=>false,(_,_)=>Task.FromResult(true));
        var window=new Window{Content=view,Width=1100,Height=760};window.Show();
        try {
            typeof(InstanceContentView).GetMethod("ShowSchematicMaterials",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(view,new object?[]{null});
            await Task.Delay(100);
            var body=(ContentControl)typeof(InstanceContentView).GetField("_body",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(view)!;
            var panel=(StackPanel)((ScrollViewer)body.Content).Content;
            Check(panel.Children.OfType<TextBlock>().Any(t=>t.Text.Contains("内置基础材质")),"material page exposes persistent default base pack");
            var list=panel.Children.OfType<StackPanel>().Single();
            var rows=list.Children.OfType<DockPanel>().ToArray();
            Check(rows.Length==2,"material page lists all saved packs");
            var disabled=rows[0].Children.OfType<CheckBox>().Single();
            Check(disabled.IsChecked==false,"disabled checkbox state survives opening a fresh manager");
            disabled.IsChecked=true;
            Check(SchematicPreviewMaterials.Read()[0].Enabled,"checkbox enables and persists selected layer");
            rows=list.Children.OfType<DockPanel>().ToArray();
            var controls=rows[0].Children.OfType<WrapPanel>().Single();
            controls.Children.OfType<Button>().Single(b=>Equals(b.Content,"下移")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(SchematicPreviewMaterials.Read()[0].Name=="low-blue","manager priority button updates persisted order");
        } finally {view.Close();window.Close();}
    }
}
