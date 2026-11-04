using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Threading;
using System.Windows.Markup;
using System.Xml.Linq;
using CMUmcl;
using CMUmcl.Content;

internal static partial class Program
{
    private static int _checks;
    private static string _output = "";
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); _checks++; }
    [STAThread] private static int Main(string[] args)
    {
        if(args[0]=="--verify-client-package") return VerifyClientPackage(args);
        if(args[0]=="--junction") return JunctionMain(args);
        if(args[0]=="--verify-live-install") return LiveInstallMain(args);
        if(args[0]=="--verify-published-client") return VerifyPublishedClient(args);
        if(args[0]=="--verify-channel" || args[0]=="--verify-public-channel") return VerifyPreparedChannel(args);
        if(args[0]=="--online") return OnlineMain(args);
        if(args[0]=="--interaction") return InteractionMain(args);
        if(args[0]=="--benchmark") return BenchmarkMain(args);
        _output = Path.GetFullPath(args[0]); Directory.CreateDirectory(_output);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        int result = 1;
        app.Dispatcher.BeginInvoke(new Action(async () => {
            try { await Run(args[1], args[2], args[3], args.Length > 4 ? args[4] : null); result = 0; Console.WriteLine($"PASS {_checks} preview checks"); }
            catch (Exception ex) { Console.WriteLine(ex); }
            finally { app.Shutdown(); }
        }));
        app.Run(); return result;
    }
    private static async Task Fails(Func<Task> action, string name)
    { bool failed = false; try { await action(); } catch { failed = true; } Check(failed, name); }
    private static async Task Run(string component, string input, string pack, string? large)
    {
        SchematicPreviewSession.RootOverride = Path.Combine(_output, "installed-" + Guid.NewGuid().ToString("N"));
        var temporaryBefore = Directory.GetDirectories(Path.GetTempPath(), "CMUmcl-preview-*").ToArray();
        await Fails(() => Task.Run(() => SchematicPreviewSession.ResolveInstalled()), "missing component is actionable");
        await Task.Run(() => SchematicPreviewSession.InstallLocal(Path.Combine(component, "component.json")));
        string installed = SchematicPreviewSession.ResolveInstalled();
        Check(File.Exists(installed), "local install activates verified worker");
        string active = File.ReadAllText(Path.Combine(SchematicPreviewSession.ComponentRoot, "current.json"));
        string invalid = Path.Combine(_output, "invalid-component"); Directory.CreateDirectory(invalid);
        File.WriteAllText(Path.Combine(invalid, "component.json"), "{\"component\":\"other\",\"apiVersion\":1}");
        await Fails(() => Task.Run(() => SchematicPreviewSession.InstallLocal(Path.Combine(invalid, "component.json"))), "invalid replacement component rejected");
        Check(active == File.ReadAllText(Path.Combine(SchematicPreviewSession.ComponentRoot, "current.json")), "failed install preserves previous activation");
        Check(!File.Exists(Path.Combine(Path.GetDirectoryName(installed)!, "pack.zip")), "component contains no Minecraft resource pack");
        using var host = new SchematicPreviewHost();
        var testWindow = new Window { Content = host, Width = 960, Height = 700, Left = 30, Top = 30, Topmost = true, Title = "Native GPU preview acceptance" };
        testWindow.Show();
        IntPtr parent = await host.WaitForHandleAsync(CancellationToken.None);
        string before = SchematicPreviewSession.Hash(input);
        using (var session = await SchematicPreviewSession.OpenAsync(input, pack, parent, CancellationToken.None))
        {
            int id = session.WorkerId; string temp = session.TemporaryDirectory;
            Check(session.NativeWindow != IntPtr.Zero && GetParent(session.NativeWindow) == parent, "GPU viewport is created as a real launcher child");
            GetWindowThreadProcessId(session.NativeWindow, out uint worker);
            Check(worker == session.WorkerId, "isolated worker owns its native swapchain window");
            Check(GetAncestor(session.NativeWindow, 2) == new System.Windows.Interop.WindowInteropHelper(testWindow).Handle, "viewport shares launcher top-level window");
            Check(session.Summary.Contains("方块"), "real decoded metadata returned"); Console.WriteLine(session.Summary);
            Check(!Directory.GetFiles(temp, "*.png").Any(), "no image file transport exists");
            string first = await Capture(session.NativeWindow, "initial.png");
            await session.CommandAsync(new { command = "orbit", x = 190, y = 35 }, CancellationToken.None);
            Check(first != await Capture(session.NativeWindow, "rotated.png"), "native swapchain orbit changes screen pixels");
            string rotation = await Capture(session.NativeWindow, "before-zoom.png");
            await session.CommandAsync(new { command = "zoom", factor = 0.7 }, CancellationToken.None);
            Check(rotation != await Capture(session.NativeWindow, "zoomed.png"), "native swapchain zoom changes screen pixels");
            long inputs = session.NativeInputEvents;
            PostMessage(session.NativeWindow, 0x201, new IntPtr(1), Point(200,200));
            for (int i = 1; i <= 50; i++) PostMessage(session.NativeWindow, 0x200, new IntPtr(1), Point(200+i*3,200+i));
            PostMessage(session.NativeWindow, 0x202, IntPtr.Zero, Point(350,250));
            PostMessage(session.NativeWindow, 0x20A, new IntPtr(120 << 16), IntPtr.Zero);
            PostMessage(session.NativeWindow, 0x100, new IntPtr(0x27), IntPtr.Zero);
            await Task.Delay(150); await session.CommandAsync(new {command="status"}, CancellationToken.None);
            Check(session.NativeInputEvents >= inputs+3, "native pointer drag wheel and arrow key reach renderer without IPC");
            long startFrames=session.PresentedFrames, startInputs=session.NativeInputEvents;
            PostMessage(session.NativeWindow,0x201,new IntPtr(1),Point(180,180));
            for(int i=0;i<180;i++) { PostMessage(session.NativeWindow,0x200,new IntPtr(1),Point(180+i,180+i%30)); await Task.Delay(6); }
            PostMessage(session.NativeWindow,0x202,IntPtr.Zero,Point(360,180));
            await Task.Delay(100); await session.CommandAsync(new {command="status"},CancellationToken.None);
            Console.WriteLine($"Native drag: {session.NativeInputEvents-startInputs} input updates, {session.PresentedFrames-startFrames} presentations");
            Check(session.NativeInputEvents-startInputs>30 && session.PresentedFrames-startFrames>10,"sustained native drag presents live frames");
            long frames = session.PresentedFrames;
            await Task.Delay(500); await session.CommandAsync(new {command="status"}, CancellationToken.None);
            Check(frames == session.PresentedFrames, "idle viewport does not render continuously");
            foreach (int width in new[] { 800, 1200, 960 }) {
                testWindow.Width=width; testWindow.Height=width*0.7; await Task.Delay(100);
                await session.CommandAsync(new {command="resize"}, CancellationToken.None); await Task.Delay(50);
                await session.CommandAsync(new {command="status"}, CancellationToken.None);
                GetClientRect(parent,out RectNative rect);
                Check(session.SurfaceWidth == rect.Right && session.SurfaceHeight == rect.Bottom, "swapchain follows physical host size "+width);
            }
            testWindow.WindowState=WindowState.Minimized; await Task.Delay(200);
            await session.CommandAsync(new {command="status"}, CancellationToken.None);
            Check(!session.SurfaceVisible, "minimized launcher suspends native presentation");
            testWindow.WindowState=WindowState.Normal; await Task.Delay(200);
            await session.CommandAsync(new {command="resize"}, CancellationToken.None);
            Check(session.SurfaceVisible, "restoring launcher resumes native presentation");
            var popup=new Window { Owner=testWindow,Width=200,Height=160,Left=200,Top=180,Topmost=true,WindowStyle=WindowStyle.None,Background=new SolidColorBrush(System.Windows.Media.Color.FromRgb(200,30,180)) };
            popup.Show(); await Task.Delay(200);
            using(var sample=new Bitmap(1,1)) { using(var graphics=Graphics.FromImage(sample)) graphics.CopyFromScreen(250,230,0,0,new System.Drawing.Size(1,1)); var pixel=sample.GetPixel(0,0); Check(pixel.R==200 && pixel.G==30 && pixel.B==180,"owned popup correctly occludes native GPU child"); }
            popup.Close(); await Task.Delay(100);
            await session.CommandAsync(new { command = "fit" }, CancellationToken.None);
            await Capture(session.NativeWindow, "restored.png");
            IntPtr child = session.NativeWindow;
            using var cleanupLock=new FileStream(Path.Combine(temp,"cleanup-lock"),FileMode.Create,FileAccess.ReadWrite,FileShare.None);
            var closing=Stopwatch.StartNew(); session.Dispose(); Check(closing.ElapsedMilliseconds<100,"closing does not wait for cleanup on WPF thread"); await Task.Delay(150); Check(!session.CleanupTask.IsCompleted,"locked-file cleanup retries run in background"); cleanupLock.Dispose(); await session.CleanupTask;
            Check(Exited(id), "closing kills worker"); Check(!Directory.Exists(temp), "closing removes input snapshots");
            Check(!IsWindow(child), "closing destroys native swapchain window");
        }
        Check(before == SchematicPreviewSession.Hash(input), "source schematic remains unchanged");
        string bad = Path.Combine(_output, "bad.litematic"); File.WriteAllText(bad, "not NBT");
        await Fails(async () => { using var s = await SchematicPreviewSession.OpenAsync(bad, pack, parent, CancellationToken.None); }, "malformed schematic reports worker error");
        string huge = Path.Combine(_output, "oversized.litematic"); using (var file = File.Create(huge)) file.SetLength(64L * 1024 * 1024 + 1);
        await Fails(async () => { using var s = await SchematicPreviewSession.OpenAsync(huge, pack, parent, CancellationToken.None); }, "oversized schematic rejected before parsing"); File.Delete(huge);
        if (large != null) await Fails(async () => { using var s = await SchematicPreviewSession.OpenAsync(large, pack, parent, CancellationToken.None); }, "real upstream 67-million-block fixture rejected within decode limits");
        using (var cancel = new CancellationTokenSource())
        {
            cancel.CancelAfter(20);
            await Fails(async () => { using var s = await SchematicPreviewSession.OpenAsync(input, pack, parent, cancel.Token); }, "cancel during real load terminates process");
        }
        await Fails(async () => { using var s = await SchematicPreviewSession.OpenAsync(input, pack, parent, CancellationToken.None, 1); }, "timeout during real load terminates process");
        using (var session = await SchematicPreviewSession.OpenAsync(input, pack, parent, CancellationToken.None))
        {
            int id = session.WorkerId; Process.GetProcessById(id).Kill(); await Task.Delay(150);
            await Fails(async () => await session.CommandAsync(new { command = "fit" }, CancellationToken.None), "crashed worker cannot hang launcher");
        }
        string workerHash = SchematicPreviewSession.Hash(installed); File.AppendAllText(installed, "tamper");
        await Fails(() => Task.Run(() => SchematicPreviewSession.ResolveInstalled()), "tampered entry rejected before launch");
        File.Copy(Path.Combine(component, "SchematicPreviewWorker.exe"), installed, true);
        Check(workerHash == SchematicPreviewSession.Hash(installed), "test worker restored");
        testWindow.Close();
        await Ui(input, pack);
        var leaked = Directory.GetDirectories(Path.GetTempPath(), "CMUmcl-preview-*").Except(temporaryBefore).ToArray();
        Check(leaked.Length == 0, "success failure cancel timeout and crash leave no snapshots");
    }
    private static async Task Ui(string input, string pack)
    {
        string game = Path.Combine(_output, "game"); Directory.CreateDirectory(Path.Combine(game, "schematics"));
        string schematic = Path.Combine(game, "schematics", "中文 空格 Cottage.litematic"); File.Copy(input, schematic, true);
        var view = new InstanceContentView(game, game, "本地预览测试", () => false, (_, _) => Task.FromResult(true));
        foreach (string key in new[] { "PanelBrush", "SidebarBrush", "TextBrush", "MutedTextBrush", "AccentSoftBrush", "PanelBorderBrush", "AccentBrush" })
            view.Resources[key] = key.Contains("Text") ? System.Windows.Media.Brushes.WhiteSmoke : key == "AccentBrush" ? System.Windows.Media.Brushes.CornflowerBlue : new SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 37, 46));
        view.Resources["PanelBrush"] = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2E2E32"));
        view.Resources["SidebarBrush"] = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#232326"));
        view.Resources["PanelBorderBrush"] = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#45454B"));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var source = XDocument.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MainWindow.source.xaml"));
        var appSource = XDocument.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App.source.xaml"));
        var resources = new XElement(wpf + "ResourceDictionary", new XAttribute(XNamespace.Xmlns + "x", xaml));
        foreach (string name in new[] { "ModernButtonTemplate", "SecondaryButton" })
            resources.Add(new XElement((name == "ModernButtonTemplate" ? appSource : source).Descendants().First(e => (string?)e.Attribute(xaml + "Key") == name)));
        view.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(resources.ToString()));
        var nav = (ListBox)typeof(InstanceContentView).GetField("_navigation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!; nav.SelectedIndex=3;
        var window = new Window { Content = view, Width = 1100, Height = 760, Background = System.Windows.Media.Brushes.Black, Title = "Native GPU preview acceptance", Left=30, Top=30, Topmost=true }; window.Show();
        typeof(InstanceContentView).GetField("_previewPack", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, pack);
        typeof(InstanceContentView).GetMethod("OpenSchematicPreview", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, new object[] { schematic });
        var sessionField = typeof(InstanceContentView).GetField("_schematicPreview", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var timer = Stopwatch.StartNew();
        while (sessionField.GetValue(view) == null && timer.ElapsedMilliseconds < 100000) await Task.Delay(100);
        Check(sessionField.GetValue(view) is SchematicPreviewSession, "WPF page loads real schematic with Chinese/spaced path");
        var session = (SchematicPreviewSession)sessionField.GetValue(view)!; int id = session.WorkerId;
        var viewport=(SchematicPreviewHost)typeof(InstanceContentView).GetField("_previewViewport",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(view)!;
        Check(Math.Abs(session.DpiScale-GetDpiForWindow(viewport.ParentHandle)/96.0)<0.01,"native viewport matches launcher DPI context");
        PostMessage(session.NativeWindow,0x201,new IntPtr(1),Point(50,50)); PostMessage(session.NativeWindow,0x202,IntPtr.Zero,Point(50,50)); await Task.Delay(100);
        uint thread=GetWindowThreadProcessId(session.NativeWindow,out _);
        var focus=new GuiThreadInfo {Size=Marshal.SizeOf(typeof(GuiThreadInfo))};
        Check(GetGUIThreadInfo(thread,ref focus) && focus.Focus==session.NativeWindow,"pointer interaction focuses native canvas");
        PostMessage(session.NativeWindow,0x100,new IntPtr(9),IntPtr.Zero); await Task.Delay(100);
        focus=new GuiThreadInfo {Size=Marshal.SizeOf(typeof(GuiThreadInfo))};
        Check(GetGUIThreadInfo(thread,ref focus) && focus.Focus!=session.NativeWindow,"Tab leaves native canvas for launcher controls");
        Check(((System.Windows.Interop.IKeyboardInputSink)viewport).TabInto(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First)),"WPF tab navigation can enter native canvas");
        await Capture(new System.Windows.Interop.WindowInteropHelper(window).Handle, "page-dark.png");
        int initialWidth=session.SurfaceWidth;
        window.Width=1250; window.Height=820; await Task.Delay(250);
        await session.CommandAsync(new {command="status"}, CancellationToken.None);
        Check(session.SurfaceWidth != initialWidth, "production WPF page automatically resizes native GPU surface");
        view.Resources["PanelBrush"] = System.Windows.Media.Brushes.White; view.Resources["SidebarBrush"] = System.Windows.Media.Brushes.WhiteSmoke;
        view.Resources["TextBrush"] = System.Windows.Media.Brushes.Black; view.Resources["MutedTextBrush"] = System.Windows.Media.Brushes.DimGray;
        view.Resources["AccentSoftBrush"] = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFF1E6"));
        view.Resources["PanelBorderBrush"] = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E4E4E9"));
        await Task.Delay(200); view.UpdateLayout();
        await Capture(new System.Windows.Interop.WindowInteropHelper(window).Handle, "page-light.png");
        Check(!Directory.GetFiles(session.TemporaryDirectory,"*.png").Any(), "production page never requests PNG transport");
        var navigation = (ListBox)typeof(InstanceContentView).GetField("_navigation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
        navigation.SelectedIndex = 1; await Task.Delay(100);
        await session.CleanupTask; Check(Exited(id), "page switch releases real worker");
        navigation.SelectedIndex=3;
        typeof(InstanceContentView).GetMethod("OpenSchematicPreview",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(view,new object[]{schematic});
        await Task.Delay(30); navigation.SelectedIndex=1; await Task.Delay(500);
        Check(sessionField.GetValue(view)==null,"switching pages during native load cancels the pending session");
        view.Close(); window.Close();
    }
    private static bool Exited(int id) { try { using var p = Process.GetProcessById(id); return p.HasExited; } catch (ArgumentException) { return true; } }
    private static IntPtr Point(int x,int y) => new IntPtr((y<<16)|(x&65535));
    private static async Task<string> Capture(IntPtr hwnd,string name)
    {
        await Task.Delay(180); DwmFlush(); GetWindowRect(hwnd,out RectNative rect);
        int width=rect.Right-rect.Left, height=rect.Bottom-rect.Top;
        using var bitmap=new Bitmap(width,height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using (var graphics=Graphics.FromImage(bitmap)) graphics.CopyFromScreen(rect.Left,rect.Top,0,0,new System.Drawing.Size(width,height));
        string path=Path.Combine(_output,name); bitmap.Save(path,ImageFormat.Png);
        int colors=0; var seen=new HashSet<int>();
        for(int y=0;y<height;y+=8) for(int x=0;x<width;x+=8) {if(seen.Add(bitmap.GetPixel(x,y).ToArgb()))colors++;}
        Check(colors>32, "visible GPU scene has real textured pixels: "+name);
        return SchematicPreviewSession.Hash(path);
    }
    [StructLayout(LayoutKind.Sequential)] private struct RectNative {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint process);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd,out RectNative rect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd,out RectNative rect);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd,uint message,IntPtr w,IntPtr l);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [StructLayout(LayoutKind.Sequential)] private struct GuiThreadInfo {public int Size;public uint Flags;public IntPtr Active,Focus,Capture,MenuOwner,MoveSize,Caret; public RectNative CaretRect;}
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread,ref GuiThreadInfo info);
}
