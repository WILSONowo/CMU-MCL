using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using CMUmcl.Content;

internal static partial class Program
{
    private static int JunctionMain(string[] args)
    {
        _output=Path.GetFullPath(args[1]);Directory.CreateDirectory(_output);int code=1;
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        app.Dispatcher.BeginInvoke(new Action(async()=>{try {
            SchematicPreviewSession.RootOverride=null;
            SchematicPreviewSession.CheckRegularPath(SchematicPreviewSession.ComponentRoot);
            Check(true,"real data-root storage resolves without traversing junction");
            _=SchematicPreviewSession.InstalledVersion();
            Check(true,"real installation state can be read without traversing junction");
            string resolved=SchematicPreviewStorage.UnderDataRoot(args[2]);
            string expected=Path.Combine(Path.GetFullPath(args[3]),"components","schematic-preview");
            Check(resolved==expected,"test data-root junction resolves before appending component paths");
            Check(SchematicPreviewStorage.UnderDataRoot(args[3])==expected,"normal data-root location is unchanged");
            string missing=Path.Combine(args[3],"not-created");
            Check(SchematicPreviewStorage.UnderDataRoot(missing)==Path.Combine(missing,"components","schematic-preview"),"missing ordinary data root remains optional and uncreated");
            Check(!Directory.Exists(missing),"checking an uncreated data root does not create it");
            using var fixture=new ComponentFixture(args[4]);
            byte[] zip=fixture.Zip("0.2.2");
            SchematicPreviewSession.VerificationKeyOverride=fixture.PublicKey;
            await OptionalOnlineUi(fixture,zip,resolved);
            var transport=new ComponentTransport{Feed=fixture.Feed(zip,"0.2.2"),Zip=zip};
            using var client=new SchematicPreviewUpdateClient(new HttpClient(transport),fixture.PublicKey);
            var offer=await client.CheckAsync(CancellationToken.None);
            await client.InstallAsync(offer,CancellationToken.None);
            Check(File.Exists(Path.Combine(expected,"current.json")),"signed online install activates under physical data root");
            Check(File.Exists(SchematicPreviewSession.ResolveInstalled()),"installed signed worker resolves and verifies after activation");
            Check(File.Exists(Path.Combine(args[2],"components","schematic-preview","current.json")),"existing data-root alias still points to same installation");
            string unsafeRoot=SchematicPreviewStorage.UnderDataRoot(args[7]);
            await Fails(()=>Task.Run(()=>SchematicPreviewSession.CheckRegularPath(Path.Combine(unsafeRoot,"current.json"))),"links below component storage remain rejected");
            await Fails(()=>Task.Run(()=>SchematicPreviewSession.CheckRegularPath(args[2])),"general file-input path checks still reject junctions");
            await Ui(args[5],args[6]);
            Console.WriteLine($"PASS {_checks} junction checks");code=0;
        } catch(Exception ex){Console.WriteLine(ex);}finally{SchematicPreviewSession.RootOverride=null;SchematicPreviewSession.VerificationKeyOverride=null;app.Shutdown();}}));
        app.Run();return code;
    }
}
