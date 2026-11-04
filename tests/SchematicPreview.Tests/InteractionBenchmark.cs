using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using CMUmcl.Content;

internal static partial class Program
{
    private static int InteractionMain(string[] args)
    {
        var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());int code=1;
        app.Dispatcher.BeginInvoke(new Action(async()=>{try{await Interactions(args);code=0;}catch(Exception e){Console.WriteLine(e);}finally{app.Shutdown();}}));app.Run();return code;
    }
    private static async Task Interactions(string[] args)
    {
        string output=args[1],component=args[2],small=args[3],large=args[4],pack=args[5],official=args[6],presentmon=args[7];
        Directory.CreateDirectory(output);
        if(SchematicPreviewSession.Hash(pack)!=SchematicPreviewSession.Hash(Path.Combine(Path.GetDirectoryName(official)!,"Assets","pack.zip")))throw new Exception("Different materials");
        SchematicPreviewSession.RootOverride=Path.Combine(output,"components-"+Guid.NewGuid().ToString("N"));SchematicPreviewSession.InstallLocal(Path.Combine(component,"component.json"));
        TimeBeginPeriod(1);
        try {
            foreach(var model in new[]{small,large}) for(int round=0;round<3;round++) {
                string label=Path.GetFileNameWithoutExtension(model)+"-"+round;
                Console.WriteLine("START "+label);
                var start=new ProcessStartInfo(official,"\""+model+"\""){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(official)!};
                start.EnvironmentVariables["WEBVIEW2_USER_DATA_FOLDER"]=Path.Combine(output,"profiles",label);
                using var process=Process.Start(start)!;using var job=new PreviewProcessJob(process.Handle);
                Rect stage=Rect.Empty;IntPtr owner=IntPtr.Zero;string[] names=Array.Empty<string>();
                var timeout=Stopwatch.StartNew();
                try {
                    while(timeout.ElapsedMilliseconds<90000) {
                        owner=FindUpstream(process.Id);
                        if(owner!=IntPtr.Zero) {
                            try {
                                var root=AutomationElement.FromHandle(owner);
                                var nodes=root.FindAll(TreeScope.Descendants,System.Windows.Automation.Condition.TrueCondition).Cast<AutomationElement>().ToArray();
                                names=nodes.Select(n=>n.Current.Name).ToArray();
                                var canvas=nodes.FirstOrDefault(n=>n.Current.Name.StartsWith("3D preview of "));
                                if(canvas!=null && names.Any(n=>n.Contains("model data loaded in"))) {
                                    stage=canvas.Current.BoundingRectangle;GetWindowRect(owner,out RectNative outer);
                                    int dx=800-(int)Math.Round(stage.Width),dy=480-(int)Math.Round(stage.Height);
                                    if(dx==0 && dy==0)break;
                                    SetWindowPos(owner,new IntPtr(-1),30,30,outer.Right-outer.Left+dx,outer.Bottom-outer.Top+dy,0x40);
                                }
                            } catch(ElementNotAvailableException) { }
                        }
                        if(process.HasExited)throw new Exception("Official program exited before loading");
                        await Task.Delay(300);
                    }
                    if(stage.IsEmpty || Math.Round(stage.Width)!=800 || Math.Round(stage.Height)!=480) {
                        File.WriteAllText(Path.Combine(output,label+"-uia.txt"),string.Join("\n",names));throw new Exception("Cannot verify loaded 800x480 upstream viewport");
                    }
                    GetClientRect(owner,out RectNative client);var origin=new ScreenPoint();ClientToScreen(owner,ref origin);
                    double left=stage.Left-origin.X,top=stage.Top-origin.Y;
                    File.WriteAllText(Path.Combine(output,label+"-settings.json"),JsonSerializer.Serialize(new{modelSha256=SchematicPreviewSession.Hash(model),packSha256=SchematicPreviewSession.Hash(pack),backingWidth=client.Right,backingHeight=client.Bottom,stageLeft=left,stageTop=top,stageWidth=stage.Width,stageHeight=stage.Height,sourceSettings="Fresh WebView2 profile; chunk 32 / threads 4; same unmodified wgpu renderer: FIFO, latency 2, BGRA8 sRGB, Depth24Plus, automatic 4x MSAA when supported",allAccessibleNames=names,footer=names.Where(n=>n.Contains("triangles")||n.Contains("blocks")||n.Contains("model data loaded"))},new JsonSerializerOptions{WriteIndented=true}));
                    if(args.Length>8 && args[8]=="probe") {Console.WriteLine("UIA stage verified: "+stage);return;}
                    await MeasureInteraction(output,label+"-upstream",process.Id,owner,stage,presentmon);
                    process.Kill();await Task.Run(()=>process.WaitForExit(5000));job.Dispose();
                    using var host=new SchematicPreviewHost();var window=new Window {Content=host,Left=30,Top=30,Width=client.Right+16,Height=client.Bottom+39,Topmost=true};window.Show();
                    try {
                        IntPtr parent=await host.WaitForHandleAsync(CancellationToken.None);
                        var rootHandle=new System.Windows.Interop.WindowInteropHelper(window).Handle;
                        for(int attempt=0;attempt<4;attempt++) {GetClientRect(parent,out RectNative current);GetWindowRect(rootHandle,out RectNative outer);if(current.Right==client.Right && current.Bottom==client.Bottom)break;SetWindowPos(rootHandle,new IntPtr(-1),30,30,outer.Right-outer.Left+client.Right-current.Right,outer.Bottom-outer.Top+client.Bottom-current.Bottom,0x40);await Task.Delay(100);}
                        using var session=await SchematicPreviewSession.OpenAsync(model,pack,parent,CancellationToken.None,100000,4);
                        try {
                            await session.CommandAsync(new {command="stage",left,top,width=800,height=480},CancellationToken.None);
                            await session.CommandAsync(new {command="fit"},CancellationToken.None);
                            GetClientRect(session.NativeWindow,out RectNative own);
                            if(own.Right!=client.Right||own.Bottom!=client.Bottom)throw new Exception("Swapchain dimensions differ");
                            var ownOrigin=new ScreenPoint();ClientToScreen(session.NativeWindow,ref ownOrigin);
                            await MeasureInteraction(output,label+"-adapted",session.WorkerId,rootHandle,new Rect(ownOrigin.X+left,ownOrigin.Y+top,800,480),presentmon);
                        } finally {session.Dispose();await session.CleanupTask;}
                    } finally {window.Close();}
                    Console.WriteLine("DONE "+label);
                } finally {if(!process.HasExited){process.Kill();await Task.Run(()=>process.WaitForExit(5000));}}
            }
        } finally {TimeEndPeriod(1);}
    }
    private static async Task MeasureInteraction(string output,string label,int pid,IntPtr owner,Rect stage,string presentmon)
    {
        SetForegroundWindow(owner);SetCursorPos((int)(stage.Left+stage.Width/2),(int)(stage.Top+stage.Height/2));
        KeyEvent(0x24,0,0,UIntPtr.Zero);KeyEvent(0x24,0,2,UIntPtr.Zero);await Task.Delay(300);
        string csv=Path.Combine(output,label+".csv");
        using var capture=Process.Start(new ProcessStartInfo(presentmon,$"--process_id {pid} --session_name CMUmclBench-{pid} --timed 10 --terminate_after_timed --no_console_stats --v2_metrics --qpc_time_ms --output_file \"{csv}\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})!;
        var stdout=capture.StandardOutput.ReadToEndAsync();var stderr=capture.StandardError.ReadToEndAsync();
        await Task.Delay(800);
        MouseEvent(2,0,0,0,UIntPtr.Zero);
        double begin=0,end=0;int sent=0;var clock=Stopwatch.StartNew();
        try {
            for(int step=0;step<840;step++) {
                double due=step*(1000.0/120);double delay=due-clock.Elapsed.TotalMilliseconds;if(delay>1)await Task.Delay((int)delay);
                double t=step/120.0;
                SetCursorPos((int)(stage.Left+400+90*Math.Sin(2*Math.PI*t/4)),(int)(stage.Top+240+25*Math.Sin(2*Math.PI*t/3)));
                if(step==120)begin=Stopwatch.GetTimestamp()*1000.0/Stopwatch.Frequency;
                sent++;
            }
            end=Stopwatch.GetTimestamp()*1000.0/Stopwatch.Frequency;
        } finally {MouseEvent(4,0,0,0,UIntPtr.Zero);}
        await Task.Run(()=>capture.WaitForExit(10000));
        if(!capture.HasExited){capture.Kill();throw new Exception("PresentMon failed to stop");}
        File.WriteAllText(Path.Combine(output,label+"-capture.log"),(await stdout)+"\n"+(await stderr));
        File.WriteAllText(Path.Combine(output,label+"-sample.json"),JsonSerializer.Serialize(new{pid,startQpcMs=begin,endQpcMs=end,sent,viewport=new{stage.X,stage.Y,stage.Width,stage.Height},inputHz=120,sampleSeconds=(end-begin)/1000}));
        if(capture.ExitCode!=0||!File.Exists(csv))throw new Exception("PresentMon capture failed");
        Console.WriteLine("CAPTURED "+label);
    }
    [StructLayout(LayoutKind.Sequential)]private struct ScreenPoint{public int X,Y;}
    [DllImport("user32.dll")]private static extern bool ClientToScreen(IntPtr hwnd,ref ScreenPoint point);
    [DllImport("user32.dll")]private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")]private static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll",EntryPoint="mouse_event")]private static extern void MouseEvent(uint flags,uint x,uint y,uint data,UIntPtr extra);
    [DllImport("user32.dll",EntryPoint="keybd_event")]private static extern void KeyEvent(byte key,byte scan,uint flags,UIntPtr extra);
    [DllImport("winmm.dll",EntryPoint="timeBeginPeriod")]private static extern uint TimeBeginPeriod(uint period);
    [DllImport("winmm.dll",EntryPoint="timeEndPeriod")]private static extern uint TimeEndPeriod(uint period);
}
