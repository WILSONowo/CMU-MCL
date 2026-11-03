using System.IO;
// SPDX-License-Identifier: GPL-3.0-only
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CMUmcl;
using CMUmcl.Auth;

internal static partial class Program
{
    private sealed class AvatarHandler : HttpMessageHandler
    {
        internal int Calls; internal bool Offline, BadId, BadBase64, MissingSkin, Redirect, Oversized;
        internal string Url = "https://csu-mc.magicalsheep.cn/textures/fixture";
        internal byte[] Skin = Array.Empty<byte>();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            await Task.Delay(15,ct).ConfigureAwait(false);
            Calls++; ct.ThrowIfCancellationRequested();
            Check(!r.Headers.Contains("Authorization") && !r.Headers.Contains("Cookie") && !r.Headers.Contains("x-api-key") && !r.Headers.Contains("X-Client-Token"), "avatar request carries no credentials");
            if (Offline) throw new HttpRequestException();
            if (Redirect) return new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://example.org/") } };
            string text;
            if (r.RequestUri!.AbsolutePath.EndsWith("yggdrasil/")) text = "{\"skinDomains\":[\"csu-mc.magicalsheep.cn\"]}";
            else if (r.RequestUri.AbsolutePath.Contains("/profile/"))
            {
                var value = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { textures = MissingSkin ? new { SKIN = (object?)null } : new { SKIN = (object?)new { url = Url } } })));
                text = JsonSerializer.Serialize(new { id = BadId ? new string('2',32) : new string('1',32), properties = new[] { new { name="textures",value=BadBase64?"invalid":value } } });
            }
            else return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Oversized ? new byte[2097153] : Skin) };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text) };
        }
    }
    private static byte[] AvatarSkin(int height = 64)
    {
        var pixels = new byte[64*height*4];
        for (int y=8;y<16;y++) for (int x=8;x<16;x++) { int at=(y*64+x)*4; pixels[at+2]=255; pixels[at+3]=255; }
        // One opaque blue hat pixel over red skin; remaining hat pixels transparent.
        int hat=(8*64+40)*4; pixels[hat]=255; pixels[hat+3]=255;
        var bitmap = BitmapSource.Create(64,height,96,96,PixelFormats.Bgra32,null,pixels,256);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=new MemoryStream();png.Save(stream);return stream.ToArray();
    }
    private static async Task AvatarChecks(MainWindow window)
    {
        string id=new string('1',32), cache=Path.Combine(_output,"avatar-cache");
        var handler = new AvatarHandler { Skin=AvatarSkin() };
        using var service = new SkinAvatarService(handler,cache);
        Check(SkinAvatarService.NormalizeId("../auth.dat")==null && SkinAvatarService.NormalizeId(new string('0',32))==null,"invalid/empty UUID rejected without paths or network");
        Check(await service.GetAsync("invalid",CancellationToken.None)==null && handler.Calls==0,"no request for invalid profile");
        var head=await service.GetAsync(id,CancellationToken.None);
        Check(head!=null && head.IsFrozen && head.PixelWidth==8 && head.PixelHeight==8,"profile UUID resolves into frozen 8x8 head");
        var pixels=new byte[256];head!.CopyPixels(pixels,32,0);
        Check(pixels[0]==255 && pixels[2]==0 && pixels[6]==255 && pixels[4]==0,"hat overlays face and transparent hat preserves face");
        Check(Directory.GetFiles(cache).Length==2 && new FileInfo(Path.Combine(cache,id+".png")).Length<65536,"cache contains bounded head and skin PNGs");
        var skin=service.Skin;
        var asyncPreview=new SkinPreview();
        for(int repeat=0;repeat<4;repeat++){await service.GetAsync(id,CancellationToken.None,true);asyncPreview.SetSkin(service.Skin,service.IsSlim);}
        Check(asyncPreview.Parts==12,"real asynchronous decode survives first render and repeated refresh");
        Check(skin!=null,"full skin available for preview");
        var preview=new SkinPreview();var host=Get<System.Windows.Controls.Grid>(window,"SkinPreviewHost");host.Children.Clear();host.Children.Add(preview);
        var modelPixels=new byte[64*64*4];for(int n=0;n<modelPixels.Length;n+=4){modelPixels[n]=100;modelPixels[n+1]=120;modelPixels[n+2]=160;modelPixels[n+3]=255;}
        var modelSkin=BitmapSource.Create(64,64,96,96,PixelFormats.Bgra32,null,modelPixels,256);
        preview.SetSkin(modelSkin,false);
        Check(preview.Parts==12,"modern model renders six body parts and six outer layers");
        double angle=preview.Rotation;preview.RotateBy(30);Check(preview.Rotation!=angle,"drag delta rotates model");
        window.UpdateLayout();await Task.Delay(150);
        Check(preview.ActualHeight>100 && preview.ActualWidth>100,"sidebar preview has usable responsive area");
        var skinRender=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);skinRender.Render(window);
        var skinEncoder=new PngBitmapEncoder();skinEncoder.Frames.Add(BitmapFrame.Create(skinRender));using(var f=File.Create(Path.Combine(_output,"skin-preview.png")))skinEncoder.Save(f);
        preview.SetSkin(null,false);Check(preview.Parts==0,"missing skin clears model to placeholder");preview.SetSkin(skin,true);Check(preview.Parts==12,"slim model supported");
        int calls=handler.Calls; var fresh=await service.GetAsync(id,CancellationToken.None);
        Check(fresh!=null && handler.Calls==calls,"fresh cache skips network");
        await service.GetAsync(id,CancellationToken.None,true);Check(handler.Calls>calls,"refresh bypasses fresh cache");
        handler.Offline=true;File.SetLastWriteTimeUtc(Path.Combine(cache,id+".png"),DateTime.UtcNow.AddDays(-1));
        Check(await service.GetAsync(id,CancellationToken.None)!=null,"offline retains stale cached avatar");
        File.WriteAllText(Path.Combine(cache,id+".png"),"corrupt");
        Check(await service.GetAsync(id,CancellationToken.None)==null,"corrupt cache and offline fall back");
        window.SetAccountAvatar(head);
        Check(Get<Image>(window,"AccountAvatarImage").Visibility==Visibility.Visible && Get<TextBlock>(window,"AccountAvatarFallback").Visibility==Visibility.Collapsed,"sidebar switches default icon to skin head");
        window.UpdateLayout(); await Task.Delay(100);
        var avatar=Get<Image>(window,"AccountAvatarImage");
        Check(Math.Abs(avatar.ActualWidth-36)<0.1 && Math.Abs(avatar.ActualHeight-36)<0.1,"avatar fills square frame without inset strip");
        Check(avatar.Stretch==Stretch.Fill && avatar.SnapsToDevicePixels,"avatar scales with pixel aligned frame");
        var render=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);render.Render(window);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(render));using(var f=File.Create(Path.Combine(_output,"avatar-sidebar.png")))encoder.Save(f);
        window.SetAccountAvatar(null);
        Check(Get<Image>(window,"AccountAvatarImage").Source==null && Get<TextBlock>(window,"AccountAvatarFallback").Visibility==Visibility.Visible,"default icon restored without stale image");
        foreach(var url in new[]{"https://evil.example/skin.png","http://csu-mc.magicalsheep.cn/skin","https://csu-mc.magicalsheep.cn:444/skin","https://user@csu-mc.magicalsheep.cn/skin","https://csu-mc.magicalsheep.cn/skin?token=fixture"})
        {
            var h=new AvatarHandler { Skin=AvatarSkin(),Url=url };using var s=new SkinAvatarService(h,Path.Combine(_output,Guid.NewGuid().ToString("N")));
            Check(await s.GetAsync(id,CancellationToken.None)==null && h.Calls==2,"untrusted texture address rejected before request");
        }
        foreach(var mode in new[]{"id","base64","missing","redirect","size","png"})
        {
            var h=new AvatarHandler { Skin=mode=="png"?new byte[32]:AvatarSkin(),BadId=mode=="id",BadBase64=mode=="base64",MissingSkin=mode=="missing",Redirect=mode=="redirect",Oversized=mode=="size" };
            using var s=new SkinAvatarService(h,Path.Combine(_output,Guid.NewGuid().ToString("N")));
            Check(await s.GetAsync(id,CancellationToken.None)==null,"malformed/unsafe response falls back: "+mode);
        }
        var cancel=new CancellationTokenSource();cancel.Cancel();
        using(var s=new SkinAvatarService(new AvatarHandler(),Path.Combine(_output,"cancel")))
        { bool cancelled=false;try{await s.GetAsync(id,cancel.Token);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"window cancellation stops avatar request"); }
        foreach(var h in new[]{32,64}) Check(SkinAvatarService.RenderHead(new PngBitmapDecoder(new MemoryStream(AvatarSkin(h)),BitmapCreateOptions.None,BitmapCacheOption.OnLoad).Frames[0]).PixelWidth==8,"legacy/modern skin supported "+h);
        File.WriteAllText(Path.Combine(_output,"result.txt"),"PASS "+_checks+" avatar checks");
    }
}
