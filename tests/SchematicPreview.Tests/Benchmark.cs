using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using CMUmcl.Content;

internal static partial class Program
{
    private static int BenchmarkMain(string[] args)
    {
        var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext()); int code=1;
        app.Dispatcher.BeginInvoke(new Action(async()=>{try{await Benchmark(args);code=0;}catch(Exception ex){Console.WriteLine(ex);}finally{app.Shutdown();}}));app.Run();return code;
    }
    private static async Task Benchmark(string[] args)
    {
        string output=args[1],component=args[2],model=args[3],pack=args[4],upstream=args[5];Directory.CreateDirectory(output);
        if(SchematicPreviewSession.Hash(pack)!=SchematicPreviewSession.Hash(Path.Combine(Path.GetDirectoryName(upstream)!,"Assets","pack.zip")))throw new Exception("Resource packs differ");
        SchematicPreviewSession.RootOverride=Path.Combine(output,"installed-"+Guid.NewGuid().ToString("N"));SchematicPreviewSession.InstallLocal(Path.Combine(component,"component.json"));
        var rows=new List<object>();
        for(int repeat=0;repeat<1;repeat++)
        {
            var clock=Stopwatch.StartNew();
            using(var process=Process.Start(new ProcessStartInfo(upstream,"\""+model+"\""){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(upstream)!})!)
            {
                using var job=new PreviewProcessJob(process.Handle);
                IntPtr hwnd=IntPtr.Zero;
                try {
                    while(hwnd==IntPtr.Zero && clock.ElapsedMilliseconds<30000){process.Refresh();hwnd=FindUpstream(process.Id);await Task.Delay(10);}
                    SetWindowPos(hwnd,new IntPtr(-1),30,30,960,700,0x40);
                    await WaitForTexture(()=>{process.Refresh();var active=FindUpstream(process.Id);if(active!=IntPtr.Zero)SetWindowPos(active,new IntPtr(-1),30,30,960,700,0x40);return active;},clock,Path.Combine(output,$"upstream-{repeat}.png")); double ms=clock.Elapsed.TotalMilliseconds;
                    rows.Add(new{implementation="upstream-v0.5.2",repeat,firstTextureMs=ms});Console.WriteLine($"upstream {repeat}: {ms:F1} ms");
                } finally {if(!process.HasExited){process.Kill();await Task.Run(()=>process.WaitForExit(5000));}}
            }
            using var host=new SchematicPreviewHost();var window=new Window {Content=host,Width=960,Height=700,Left=30,Top=30,Topmost=true};window.Show();
            IntPtr parent=await host.WaitForHandleAsync(CancellationToken.None);await Task.Delay(100);clock.Restart();
            var opening=SchematicPreviewSession.OpenAsync(model,pack,parent,CancellationToken.None);
            SchematicPreviewSession? session=null;
            try {
                await WaitForTexture(()=>GetWindow(parent,5),clock,Path.Combine(output,$"adapted-{repeat}.png"));double ms=clock.Elapsed.TotalMilliseconds;
                session=await opening;rows.Add(new{implementation="adapted-native",repeat,firstTextureMs=ms});Console.WriteLine($"adapted {repeat}: {ms:F1} ms");
            } finally {if(session==null){try{session=await opening;}catch{}}if(session!=null){session.Dispose();await session.CleanupTask;}window.Close();}
        }
        File.WriteAllText(Path.Combine(output,"comparison.json"),JsonSerializer.Serialize(new{modelSha256=SchematicPreviewSession.Hash(model),packSha256=SchematicPreviewSession.Hash(pack),windowWidth=960,windowHeight=700,scope="First visible textured geometry, includes startup; upstream and adapted UI viewport areas differ; not a GPU frame-time comparison",rows},new JsonSerializerOptions{WriteIndented=true}));
    }
    private static async Task WaitForTexture(Func<IntPtr> getWindow,Stopwatch clock,string evidence)
    {
        while(clock.ElapsedMilliseconds<30000){IntPtr hwnd=getWindow();if(hwnd!=IntPtr.Zero && GetWindowRect(hwnd,out RectNative r)){
            int width=r.Right-r.Left,height=r.Bottom-r.Top;
            if(width>100 && height>100){using var bitmap=new Bitmap(width,height);using(var g=Graphics.FromImage(bitmap))g.CopyFromScreen(r.Left,r.Top,0,0,new System.Drawing.Size(width,height));
                int brown=0,dark=0,total=0;for(int y=height/4;y<height*9/10;y+=4)for(int x=width/6;x<width*5/6;x+=4){var c=bitmap.GetPixel(x,y);total++;if(c.R<25 && c.G<30 && c.B<40)dark++;if(c.R>30 && c.R<210 && c.R>c.G*1.12 && c.G>c.B*1.3)brown++;}if(brown>200 && dark>total*0.5){bitmap.Save(evidence,System.Drawing.Imaging.ImageFormat.Png);return;}
            }}await Task.Delay(20);
        }throw new TimeoutException("No textured geometry observed");
    }
    private static IntPtr FindUpstream(int pid) {
        IntPtr found=IntPtr.Zero; EnumWindows((h,p)=>{GetWindowThreadProcessId(h,out uint owner);if(owner==pid && IsWindowVisible(h)){var title=new System.Text.StringBuilder(512);GetWindowText(h,title,512);if(title.ToString().Contains("Litematica Preview"))found=h;}return true;},IntPtr.Zero);return found;
    }
    private delegate bool EnumCallback(IntPtr hwnd,IntPtr parameter);
    [DllImport("user32.dll")]private static extern bool EnumWindows(EnumCallback callback,IntPtr parameter);
    [DllImport("user32.dll")]private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetWindowText(IntPtr hwnd,System.Text.StringBuilder text,int size);
    [DllImport("user32.dll")]private static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")]private static extern IntPtr GetWindow(IntPtr hwnd,uint command);
}
