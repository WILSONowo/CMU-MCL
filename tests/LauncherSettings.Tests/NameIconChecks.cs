// SPDX-License-Identifier: GPL-3.0-only
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using CMUmcl;
using CMUmcl.Content;
internal static partial class Program
{
    private static async Task NameIconChecks(MainWindow window,InstalledStateStore installed,SettingsStore store)
    {
        string id="local:name-icon-fixture",folder="name-icon-fixture",dir=Path.Combine(LauncherPaths.InstancesDirectory,folder);Directory.CreateDirectory(dir);
        var metadata=new InstanceMetadata{Id=id,Name="原始名称",Game="1.21.1",Loader="fabric",LoaderVersion="0.16.10"};File.WriteAllText(Path.Combine(dir,"instance.json"),JsonSerializer.Serialize(metadata));installed.RecordInstall(id,1,folder,metadata.Name);
        Click(window,"InstancesNavButton");window.UpdateLayout();await Task.Delay(50);
        var list=Get<ItemsControl>(window,"InstancesList");var card=list.Items.OfType<InstanceCardViewModel>().Single(x=>x.InstanceId==id);
        Check(card.DisplayName==metadata.Name && card.IconSource!.EndsWith("Fabric.png"),"name/icon: original instance automatically uses Fabric icon");
        foreach(var pair in new[]{("forge","Forge.png"),("neoforge","NeoForge.png"),("quilt","Quilt.png"),("optifine","OptiFine.png"),("vanilla","grass-block.png"),(" FABRIC ","Fabric.png")})
        {
            metadata.Loader=pair.Item1;File.WriteAllText(Path.Combine(dir,"instance.json"),JsonSerializer.Serialize(metadata));Call(window,"RefreshInstances");window.UpdateLayout();await Task.Delay(30);
            Check(ReferenceEquals(card,list.Items.OfType<InstanceCardViewModel>().Single(x=>x.InstanceId==id))&&card.IconSource!.EndsWith(pair.Item2),"loader icon: existing card follows "+pair.Item1);
            var item=(DependencyObject)list.ItemContainerGenerator.ContainerFromItem(card);Check(Descendants(item).OfType<Image>().Any(x=>x.Source!=null&&x.Source.Width>0),"loader icon: embedded asset renders for "+pair.Item1);
        }
        metadata.Loader="unrecognized-loader";File.WriteAllText(Path.Combine(dir,"instance.json"),JsonSerializer.Serialize(metadata));Call(window,"RefreshInstances");window.UpdateLayout();Check(card.IconSource==null,"loader icon: unknown loader uses default placeholder");
        Check(new InstanceCardViewModel(id,"missing","",null,dir).IconSource==null,"loader icon: missing metadata uses default placeholder");
        metadata.Loader="fabric";File.WriteAllText(Path.Combine(dir,"instance.json"),JsonSerializer.Serialize(metadata));Call(window,"RefreshInstances");
        foreach(string icon in new[]{"Fabric.png","Quilt.png"})
        {
            var dialog=new InstanceSettingsView(dir,card.DisplayName,store){};dialog.Show();await Task.Delay(30);
            var field=typeof(InstanceSettingsView).GetField("_name",BindingFlags.Instance|BindingFlags.NonPublic)!;((TextBox)field.GetValue(dialog)!).Text="新名称-"+icon;
            var iconBox=(ComboBox)typeof(InstanceSettingsView).GetField("_icon",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(dialog)!;iconBox.SelectedIndex=Array.IndexOf(InstanceOptionsStore.Icons,icon)+1;
            typeof(InstanceSettingsView).GetMethod("Save",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(dialog,null);
            Check(dialog.Saved,"name/icon: normal settings form saved");
            Call(window,"RefreshInstances");window.UpdateLayout();await Task.Delay(50);
            var updated=list.Items.OfType<InstanceCardViewModel>().Single(x=>x.InstanceId==id);
            Check(ReferenceEquals(card,updated) && updated.DisplayName=="新名称-"+icon && updated.IconSource!.EndsWith(icon),"name/icon: existing instance card immediately refreshed");
            var container=(DependencyObject)list.ItemContainerGenerator.ContainerFromItem(updated);var children=Descendants(container).ToArray();
            Check(children.OfType<TextBlock>().Any(x=>x.Text==updated.DisplayName),"name/icon: new name is actually rendered in instance template");
            Check(children.OfType<Image>().Any(x=>x.Source!=null && x.Source.Width>0),"name/icon: actual instance template loads selected icon");
        }
        card.BeginLaunch();string status=card.StatusText;InstanceOptionsStore.Save(dir,new(){Name="任务中改名",Icon="Forge.png"});Call(window,"RefreshInstances");Check(ReferenceEquals(card,list.Items.OfType<InstanceCardViewModel>().Single(x=>x.InstanceId==id)) && card.StatusText==status && !card.ButtonEnabled && card.IsProgressVisible,"name/icon: refresh preserves active launch progress");card.LaunchFail("fixture ended");
        InstanceOptionsStore.Save(dir,new());Call(window,"RefreshInstances");window.UpdateLayout();await Task.Delay(30);
        Check(card.DisplayName==metadata.Name && card.IconSource!.EndsWith("Fabric.png"),"name/icon: clearing overrides restores original name and automatic loader icon");
        var restored=(DependencyObject)list.ItemContainerGenerator.ContainerFromItem(card);Check(Descendants(restored).OfType<Image>().Any(x=>x.Source!=null&&x.IsVisible),"name/icon: automatic image visible after reset");
        InstanceOptionsStore.Save(dir,new(){Icon="Quilt.png"});metadata.Loader="forge";File.WriteAllText(Path.Combine(dir,"instance.json"),JsonSerializer.Serialize(metadata));Call(window,"RefreshInstances");Check(card.IconSource!.EndsWith("Quilt.png"),"loader icon: explicit user choice survives loader change");InstanceOptionsStore.Save(dir,new());Call(window,"RefreshInstances");Check(card.IconSource!.EndsWith("Forge.png"),"loader icon: clearing user choice immediately resumes loader mapping");window.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var f=File.Create(Path.Combine(_output,"restored-instance-list.png")))png.Save(f);
        File.WriteAllText(Path.Combine(_output,"result.txt"),$"PASS {_checks} name/icon checks");
    }
}
