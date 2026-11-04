using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Reflection;
using CMUmcl;
using CMUmcl.Content;
using CMUmcl.Update;

internal static partial class Program
{
    private static int VerifyPublishedClient(string[] args)
    {
        try {
            string root=Path.GetFullPath(args[1]);Directory.CreateDirectory(root);
            using var http=new HttpClient(CMUmcl.Network.LauncherProxy.CreateHandler(allowAutoRedirect:false)){Timeout=TimeSpan.FromMinutes(3)};
            byte[] feed=http.GetByteArrayAsync(UpdateProtocol.FeedUrl).GetAwaiter().GetResult();
            var release=UpdateProtocol.ReadRelease(feed);
            string expected=args.Length>2?args[2]:SchematicPreviewProtocol.CurrentLauncher.ToString();
            Check(release.Version==expected&&UpdateProtocol.ParseVersion(release.Version)>new Version(0,3,48,11),"published signed client feed offers intended newer version");
            File.WriteAllBytes(Path.Combine(root,"latest.json"),feed);
            string package=Path.Combine(root,Path.GetFileName(new Uri(release.Url).AbsolutePath));
            File.WriteAllBytes(package,http.GetByteArrayAsync(release.Url).GetAwaiter().GetResult());
            UpdateProtocol.VerifyArchive(package,release);
            Check(true,"published client archive verifies with existing client update public key");
            string extracted=Path.Combine(root,"extracted-"+Guid.NewGuid().ToString("N"));
            string[] files=UpdateProtocol.ExtractArchive(package,extracted);
            Check(AssemblyName.GetAssemblyName(Path.Combine(extracted,"CMUmcl.exe")).Version==UpdateProtocol.ParseVersion(expected),"published client payload version matches signed feed");
            Check(files.All(name=>!name.EndsWith("SchematicPreviewWorker.exe",StringComparison.OrdinalIgnoreCase)),"published client update keeps preview extension optional");
            var assembly=Assembly.LoadFile(Path.Combine(extracted,"CMUmcl.exe"));
            Check(assembly.GetManifestResourceNames().Contains("CMUmcl.CurseForge.ProjectKey"),"server channel preserves existing keyed client distribution");
            string pin=(string)assembly.GetType("CMUmcl.Content.SchematicPreviewProtocol")!.GetField("PublicKey",BindingFlags.Static|BindingFlags.NonPublic)!.GetRawConstantValue()!;
            Check(pin==SchematicPreviewProtocol.PublicKey,"published client carries verified component channel key");
            Console.WriteLine($"PASS {_checks} published client checks");return 0;
        } catch(Exception ex) {Console.WriteLine(ex);return 1;}
    }
    private static int VerifyClientPackage(string[] args)
    {
        try {
            string directory=Path.Combine(Path.GetFullPath(args[2]),"extracted-"+Guid.NewGuid().ToString("N"));
            string[] files=UpdateProtocol.ExtractArchive(args[1],directory);
            var assembly=Assembly.LoadFile(Path.Combine(directory,"CMUmcl.exe"));
            string expected=args.Length>3?args[3]:SchematicPreviewProtocol.CurrentLauncher.ToString();
            Check(assembly.GetName().Version!.ToString()==expected,"prepared client package has intended release version");
            Check(!assembly.GetManifestResourceNames().Contains("CMUmcl.CurseForge.ProjectKey"),"prepared public client has no injected CurseForge key");
            Check(files.All(name=>!name.EndsWith("SchematicPreviewWorker.exe",StringComparison.OrdinalIgnoreCase)),"optional worker is absent from client update package");
            string pin=(string)assembly.GetType("CMUmcl.Content.SchematicPreviewProtocol")!.GetField("PublicKey",BindingFlags.Static|BindingFlags.NonPublic)!.GetRawConstantValue()!;
            Check(pin==SchematicPreviewProtocol.PublicKey,"prepared client embeds verified independent public key");
            Console.WriteLine($"PASS {_checks} client package checks");return 0;
        } catch(Exception ex) {Console.WriteLine(ex);return 1;}
    }
    private static int VerifyPreparedChannel(string[] args)
    {
        _output=Path.GetFullPath(args[1]);Directory.CreateDirectory(_output);int code=1;
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        app.Dispatcher.BeginInvoke(new Action(async()=>{try{
            SchematicPreviewSession.RootOverride=Path.Combine(_output,"installed-"+Guid.NewGuid().ToString("N"));SchematicPreviewSession.VerificationKeyOverride=null;
            byte[] feed=File.ReadAllBytes(Path.Combine(args[2],"latest.json"));var release=SchematicPreviewProtocol.Read(feed);
            string package=Path.Combine(args[2],Path.GetFileName(new Uri(release.Url).AbsolutePath));SchematicPreviewProtocol.VerifyArchive(package,release,SchematicPreviewProtocol.PublicKey);
            var transport=new ComponentTransport{Feed=feed,Zip=File.ReadAllBytes(package)};
            bool live=args[0]=="--verify-public-channel";
            using var client=live?new SchematicPreviewUpdateClient():new SchematicPreviewUpdateClient(new HttpClient(transport),SchematicPreviewProtocol.PublicKey);
            var offer=await client.CheckAsync(CancellationToken.None);
            Check(offer.Release==release,"channel matches prepared signed release");
            if(live) {
                Check(SchematicPreviewSession.InstalledVersion()==null,"public version check leaves optional extension uninstalled");
                using var cancel=new CancellationTokenSource();
                await Fails(()=>client.InstallAsync(offer,cancel.Token,new CancelDownloadProgress(cancel)),"public download can be cancelled before activation");
                Check(cancel.IsCancellationRequested&&SchematicPreviewSession.InstalledVersion()==null,"cancelled public install leaves extension uninstalled");
            }
            await client.InstallAsync(offer,CancellationToken.None);
            Check(File.Exists(SchematicPreviewSession.ResolveInstalled()),"prepared production-key package verifies and installs with client pin");
            Check(File.Exists(Path.Combine(SchematicPreviewSession.ComponentRoot,"versions",release.Version,"Source.zip")),"prepared signed package includes source archive");
            using(var sources=ZipFile.OpenRead(Path.Combine(SchematicPreviewSession.ComponentRoot,"versions",release.Version,"Source.zip"))) {
                Check(sources.GetEntry("components/schematic-preview/src/main.rs")!=null&&sources.GetEntry("scripts/build-schematic-preview.ps1")!=null,"source archive preserves buildable repository layout");
                Check(sources.Entries.All(e=>!e.FullName.EndsWith(".pem",StringComparison.OrdinalIgnoreCase)&&!e.FullName.Contains("pack.zip")),"source archive excludes signing keys and Minecraft texture pack");
            }
            if(live&&args.Length>5) {
                SchematicPreviewSession.RootOverride=Path.Combine(_output,"upgrade-"+Guid.NewGuid().ToString("N"));
                SchematicPreviewSession.InstallLocal(Path.Combine(args[5],"component.json"));
                string previous=File.ReadAllText(Path.Combine(SchematicPreviewSession.ComponentRoot,"current.json"));
                using var cancel=new CancellationTokenSource();
                await Fails(()=>client.InstallAsync(offer,cancel.Token,new CancelDownloadProgress(cancel)),"public upgrade download can be cancelled");
                Check(previous==File.ReadAllText(Path.Combine(SchematicPreviewSession.ComponentRoot,"current.json")),"cancelled public upgrade preserves old activation");
                await client.InstallAsync(offer,CancellationToken.None);
                Check(SchematicPreviewSession.InstalledVersion()==release.Version,"public download upgrades previous component");
            }
            await Ui(args[3],args[4]);code=0;Console.WriteLine($"PASS {_checks} {(live?"public":"prepared")} channel checks");
        }catch(Exception ex){Console.WriteLine(ex);}finally{app.Shutdown();}}));app.Run();return code;
    }
    private sealed class CancelDownloadProgress(CancellationTokenSource source) : IProgress<string>
    {
        public void Report(string value) { if(value.StartsWith("下载预览扩展")) source.Cancel(); }
    }
    private static int OnlineMain(string[] args)
    {
        _output=Path.GetFullPath(args[1]);Directory.CreateDirectory(_output);
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};int code=1;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        app.Dispatcher.BeginInvoke(new Action(async()=>{try{await Online(args[2],args[3],args[4]);code=0;Console.WriteLine($"PASS {_checks} online checks");}catch(Exception ex){Console.WriteLine(ex);}finally{SchematicPreviewSession.RootOverride=null;SchematicPreviewSession.VerificationKeyOverride=null;app.Shutdown();}}));app.Run();return code;
    }
    private sealed class ComponentFixture : IDisposable
    {
        internal readonly ECDsaCng Key=new(256);
        internal string PublicKey {get;}
        internal byte[] Worker {get;}
        internal ComponentFixture(string component)
        {
            var prefix=Convert.FromBase64String("MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE");
            var raw=Key.Key.Export(CngKeyBlobFormat.EccPublicBlob).Skip(8).ToArray();
            PublicKey="-----BEGIN PUBLIC KEY-----\n"+Convert.ToBase64String(prefix.Concat(raw).ToArray())+"\n-----END PUBLIC KEY-----";
            Worker=File.ReadAllBytes(Path.Combine(component,"SchematicPreviewWorker.exe"));
        }
        internal byte[] Zip(string version,Action<ZipArchive>? extra=null)
        {
            using var stream=new MemoryStream();
            using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true)) {
                Put(zip,"component.json",Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{component="schematic-preview",apiVersion=2,version,entrySha256=Hex(Worker)})));
                Put(zip,"SchematicPreviewWorker.exe",Worker);
                foreach(string name in new[]{"LICENSE","NOTICE","SOURCE.txt"})Put(zip,name,Encoding.UTF8.GetBytes("Local test fixture"));
                extra?.Invoke(zip);
            }
            return stream.ToArray();
        }
        internal static void Put(ZipArchive zip,string name,byte[] bytes,int attributes=0)
        {var entry=zip.CreateEntry(name);entry.ExternalAttributes=attributes;using var output=entry.Open();output.Write(bytes,0,bytes.Length);}
        internal byte[] Feed(byte[] zip,string version,Action<Dictionary<string,object>>? modify=null)
        {
            object Signature(byte[] data)=>new{algorithm="ECDSA-P256-SHA256",format="ieee-p1363-base64",value=Convert.ToBase64String(Key.SignData(data,HashAlgorithmName.SHA256))};
            var payload=new Dictionary<string,object>{["formatVersion"]=1,["component"]="schematic-preview",["apiVersion"]=2,["version"]=version,["url"]=SchematicPreviewProtocol.UrlRoot,["size"]=(long)zip.Length,["sha256"]=Hex(zip),["entrySha256"]=Hex(Worker),["minLauncherVersion"]="0.3.48.12",["archiveSignature"]=Signature(zip),["notes"]="可选测试扩展"};
            payload["url"]="https://csumcl.mcapcs.cn/components/schematic-preview/SchematicPreview-"+version+".zip";
            modify?.Invoke(payload);byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(payload);
            return JsonSerializer.SerializeToUtf8Bytes(new{payload=Convert.ToBase64String(bytes),signature=Signature(bytes)});
        }
        public void Dispose()=>Key.Dispose();
    }
    private static string Hex(byte[] bytes) {using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    private sealed class ComponentTransport : HttpMessageHandler
    {
        internal byte[] Feed=Array.Empty<byte>(),Zip=Array.Empty<byte>();internal int FeedRequests,ArchiveRequests;
        internal HttpStatusCode Code=HttpStatusCode.OK;internal bool Slow;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            bool feed=request.RequestUri!.AbsoluteUri==SchematicPreviewProtocol.FeedUrl;if(feed)FeedRequests++;else ArchiveRequests++;
            var response=new HttpResponseMessage(Code){RequestMessage=request};
            if((int)Code>=300&&(int)Code<400)response.Headers.Location=new Uri("https://example.com/component.zip");
            response.Content=!feed&&Slow?new StreamContent(new SlowDownload(Zip)):new ByteArrayContent(feed?Feed:Zip);
            return Task.FromResult(response);
        }
    }
    private sealed class SlowDownload : Stream
    {
        private readonly byte[] _bytes;private bool _read;
        internal SlowDownload(byte[] bytes)=>_bytes=bytes;
        public override async Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken ct)
        {if(_read){await Task.Delay(10000,ct);return 0;}_read=true;int n=Math.Min(4096,count);Array.Copy(_bytes,0,buffer,offset,n);return n;}
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException();public override void Flush(){}public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long n)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
    }
    private static async Task Online(string component,string input,string pack)
    {
        using var fixture=new ComponentFixture(component);
        SchematicPreviewSession.RootOverride=Path.Combine(_output,"installed-"+Guid.NewGuid().ToString("N"));SchematicPreviewSession.VerificationKeyOverride=fixture.PublicKey;
        byte[] zip=fixture.Zip("0.2.2");var transport=new ComponentTransport{Zip=zip,Feed=fixture.Feed(zip,"0.2.2")};
        SchematicPreviewUpdateClient Client()=>new(new HttpClient(transport,false),fixture.PublicKey);
        using(var client=Client()) {
            var offer=await client.CheckAsync(CancellationToken.None);
            Check(offer.Release.Version=="0.2.2","signed independent feed is accepted");
            Check(transport.ArchiveRequests==0 && !Directory.Exists(SchematicPreviewSession.ComponentRoot),"checking never downloads or installs optional component");
            Check(SchematicPreviewProtocol.PublicKey!=UpdateProtocol.PublicKey,"component signing key is independent from launcher update key");
            await Fails(()=>Task.Run(()=>SchematicPreviewProtocol.Read(transport.Feed)),"production pin rejects test signer");
            await Fails(()=>Task.Run(()=>UpdateProtocol.ReadRelease(transport.Feed)),"client update channel rejects component envelope");
            var corrupt=(byte[])transport.Feed.Clone();corrupt[corrupt.Length/2]^=1;
            await Fails(()=>Task.Run(()=>SchematicPreviewProtocol.Read(corrupt,fixture.PublicKey)),"tampered envelope rejected");
            foreach(var change in new Action<Dictionary<string,object>>[]{p=>p["component"]="launcher",p=>p["apiVersion"]=1,p=>p["formatVersion"]=2,p=>p["version"]="../bad",p=>p["size"]=0L,p=>p["sha256"]="bad",p=>p["entrySha256"]="bad",p=>p["minLauncherVersion"]="99.0.0"})
                await Fails(()=>Task.Run(()=>SchematicPreviewProtocol.Read(fixture.Feed(zip,"0.2.2",change),fixture.PublicKey)),"invalid signed release field rejected");
            foreach(string url in new[]{"http://csumcl.mcapcs.cn/components/schematic-preview/a.zip","https://evil.example/components/schematic-preview/a.zip","https://csumcl.mcapcs.cn/update/windows-x86_64/net48/a.zip","https://csumcl.mcapcs.cn/components/schematic-preview/a.zip?token=x","https://user:pass@csumcl.mcapcs.cn/components/schematic-preview/a.zip","https://csumcl.mcapcs.cn:444/components/schematic-preview/a.zip","https://csumcl.mcapcs.cn/components/schematic-preview/%61.zip"})
                await Fails(()=>Task.Run(()=>SchematicPreviewProtocol.Read(fixture.Feed(zip,"0.2.2",p=>p["url"]=url),fixture.PublicKey)),"foreign credential or encoded URL rejected");
            await client.InstallAsync(offer,CancellationToken.None);
            string installed=SchematicPreviewSession.ResolveInstalled();
            Check(File.Exists(installed)&&SchematicPreviewSession.InstalledVersion()=="0.2.2","download signature hash extraction and atomic activation succeed");
            Check(File.Exists(Path.Combine(Path.GetDirectoryName(installed)!,"signed-feed.json")),"signed receipt persists for launch verification");
            string receipt=Path.Combine(Path.GetDirectoryName(installed)!,"signed-feed.json");byte[] saved=File.ReadAllBytes(receipt);File.Delete(receipt);
            await Fails(()=>Task.Run(()=>SchematicPreviewSession.ResolveInstalled()),"missing online receipt blocks launch");File.WriteAllBytes(receipt,saved);
        }
        string before=File.ReadAllText(Path.Combine(SchematicPreviewSession.ComponentRoot,"current.json"));
        async Task Rejected(byte[] archive,byte[] feed,string name)
        {
            transport.Zip=archive;transport.Feed=feed;transport.Code=HttpStatusCode.OK;
            using var client=Client();var offer=await client.CheckAsync(CancellationToken.None);
            await Fails(()=>client.InstallAsync(offer,CancellationToken.None),name);
            Check(before==File.ReadAllText(Path.Combine(SchematicPreviewSession.ComponentRoot,"current.json")),"failed operation preserves previous activation");
            Check(Directory.GetDirectories(SchematicPreviewSession.ComponentRoot,"staging-*").Length==0,"failed operation cleans owned staging directory");
        }
        byte[] newer=fixture.Zip("0.2.3"),validFeed=fixture.Feed(newer,"0.2.3");
        var changed=(byte[])newer.Clone();changed[20]^=1;await Rejected(changed,validFeed,"tampered package rejected");
        await Rejected(newer,fixture.Feed(newer,"0.2.3",p=>p["size"]=(long)newer.Length+1),"truncated package rejected");
        await Rejected(newer,fixture.Feed(newer,"0.2.3",p=>p["entrySha256"]=new string('0',64)),"wrong signed entry hash rejected");
        await Rejected(newer,fixture.Feed(newer,"0.2.4"),"package version does not match signed release");
        await Rejected(zip,fixture.Feed(zip,"0.2.1"),"downgrade rejected");
        await Rejected(newer,fixture.Feed(newer,"0.2.3",p=>p["archiveSignature"]=new{algorithm="ECDSA-P256-SHA256",format="ieee-p1363-base64",value=Convert.ToBase64String(new byte[64])}),"wrong archive signature rejected even with correct hash");
        foreach(string name in new[]{"../escape.exe","/escape.exe","Licenses/CON.txt","Licenses/file.","Licenses/file ","Licenses/a:b","component.JSON","Assets/pack.zip"}) {
            byte[] dangerous=fixture.Zip("0.2.3",archive=>ComponentFixture.Put(archive,name,new byte[]{1}));
            await Rejected(dangerous,fixture.Feed(dangerous,"0.2.3"),"unsafe ZIP path rejected: "+name);
        }
        byte[] link=fixture.Zip("0.2.3",archive=>ComponentFixture.Put(archive,"Licenses/link",new byte[]{1},unchecked((int)0xA0000000)));
        await Rejected(link,fixture.Feed(link,"0.2.3"),"ZIP symlink rejected");
        byte[] many=fixture.Zip("0.2.3",archive=>{for(int i=0;i<2048;i++)ComponentFixture.Put(archive,"Licenses/"+i+".txt",new byte[]{1});});
        await Rejected(many,fixture.Feed(many,"0.2.3"),"ZIP entry count limit enforced");
        byte[] oversized=(byte[])newer.Clone();int central=(int)BitConverter.ToUInt32(oversized,oversized.Length-6);
        Array.Copy(BitConverter.GetBytes((uint)SchematicPreviewProtocol.MaxArchive+1),0,oversized,central+24,4);
        await Rejected(oversized,fixture.Feed(oversized,"0.2.3"),"ZIP per-file expansion limit enforced before extraction");
        foreach(var status in new[]{HttpStatusCode.Redirect,HttpStatusCode.NotFound}) {
            transport.Code=status;using var client=Client();await Fails(()=>client.CheckAsync(CancellationToken.None),"feed redirect or 404 fails closed");
        }
        transport.Code=HttpStatusCode.OK;transport.Zip=newer;transport.Feed=validFeed;
        using(var client=Client()) {
            var offer=await client.CheckAsync(CancellationToken.None);transport.Code=HttpStatusCode.Redirect;
            await Fails(()=>client.InstallAsync(offer,CancellationToken.None),"archive redirect fails closed");transport.Code=HttpStatusCode.OK;
            transport.Slow=true;using var cancel=new CancellationTokenSource(60);
            await Fails(()=>client.InstallAsync(offer,cancel.Token),"cancel during streaming removes partial package");transport.Slow=false;
            Check(before==File.ReadAllText(Path.Combine(SchematicPreviewSession.ComponentRoot,"current.json")),"stream cancellation preserves previous activation");
            using(var held=SchematicPreviewSession.AcquireInstallLock())await Fails(()=>client.InstallAsync(offer,CancellationToken.None),"concurrent installers cannot overwrite each other");
            using(var held=File.OpenRead(Path.Combine(SchematicPreviewSession.ComponentRoot,"current.json")))await Fails(()=>client.InstallAsync(offer,CancellationToken.None),"activation failure leaves old current file intact");
            Check(before==File.ReadAllText(Path.Combine(SchematicPreviewSession.ComponentRoot,"current.json")),"activation failure retains previous version");
            await client.InstallAsync(offer,CancellationToken.None);
            Check(SchematicPreviewSession.InstalledVersion()=="0.2.3","signed upgrade atomically switches to new version");
            Check(Directory.Exists(Path.Combine(SchematicPreviewSession.ComponentRoot,"versions","0.2.2")),"upgrade retains previous version for recovery");
            SchematicPreviewSession.ResolveInstalled();
        }
        await Ui(input,pack);
        await OptionalOnlineUi(fixture,zip);
    }
    private static async Task OptionalOnlineUi(ComponentFixture fixture,byte[] zip,string? root=null)
    {
        SchematicPreviewSession.RootOverride=root??Path.Combine(_output,"optional-ui-"+Guid.NewGuid().ToString("N"));
        var transport=new ComponentTransport{Zip=zip,Feed=fixture.Feed(zip,"0.2.2")};int confirmations=0;
        var view=new InstanceContentView(_output,_output,"可选扩展测试",()=>false,(_,_)=>{confirmations++;return Task.FromResult(false);});
        view.PreviewUpdatesFactory=()=>new SchematicPreviewUpdateClient(new HttpClient(transport,false),fixture.PublicKey);
        var window=new Window{Content=view,Width=1100,Height=760};window.Show();
        typeof(InstanceContentView).GetMethod("ShowSchematicExtension",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(view,null);
        await Task.Delay(200);
        var body=(ContentControl)typeof(InstanceContentView).GetField("_body",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(view)!;
        var panel=(StackPanel)((ScrollViewer)body.Content).Content;
        var actions=panel.Children.OfType<WrapPanel>().Single();var install=actions.Children.OfType<Button>().First(b=>Equals(b.Content,"安装扩展"));
        Check(install.IsEnabled && transport.ArchiveRequests==0,"extension page shows verified offer without automatic install");
        install.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);
        Check(confirmations==1&&transport.ArchiveRequests==0&&!Directory.Exists(SchematicPreviewSession.ComponentRoot),"declining install leaves all component files unchanged");
        view.Close();window.Close();
    }
}
