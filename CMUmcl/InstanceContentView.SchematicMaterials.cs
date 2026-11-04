using System.Windows;
using System.Windows.Controls;
using CMUmcl.Content;
using Microsoft.Win32;

namespace CMUmcl;

internal sealed partial class InstanceContentView
{
    private void ShowSchematicMaterials(string? preview=null)
    {
        if(Closed)return;StopSchematicPreview();int generation=++_generation;
        var panel=new StackPanel{Margin=new Thickness(8)};
        _body.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        panel.Children.Add(Label("预览材质包",20));
        panel.Children.Add(Label("自定义包优先级从上到下。缺失的纹理、模型和方块状态依次回退，最后使用扩展内置的基础材质。",14));
        var actions=new WrapPanel();panel.Children.Add(actions);
        bool busy=false;
        var list=new StackPanel();
        var baseInfo=Label("内置基础材质 · 默认启用 · 最低优先级\n首次预览无需选择文件，基础材质随扩展提供。",14);
        Button import=null!;
        import=Button("导入资源包",async()=>{
            if(busy)return;
            var dialog=new OpenFileDialog{Title="导入 Java 资源包（可多选）",Filter="Java 资源包 ZIP (*.zip)|*.zip",Multiselect=true};
            if(dialog.ShowDialog(Window.GetWindow(this))!=true)return;
            busy=true;actions.IsEnabled=false;_status.Text="正在保存预览材质包…";
            try {await Task.Run(()=>SchematicPreviewMaterials.Import(dialog.FileNames));if(Current()){Render();_status.Text="已导入；新包位于自定义优先级末尾，返回预览后生效。";}}
            catch(Exception ex){if(Current())_status.Text="导入失败："+ex.Message;}
            finally{busy=false;if(Current()){actions.IsEnabled=true;Render();}}
        });actions.Children.Add(import);
        actions.Children.Add(Button("检查扩展更新",ShowSchematicExtension));
        actions.Children.Add(Button(preview==null?"← 返回原理图":"应用并返回预览",()=>{if(busy)return;if(preview==null){StopSchematicPreview();_generation++;ShowSchematics();_status.Text="";}else OpenSchematicPreview(preview);}));
        panel.Children.Add(list);panel.Children.Add(baseInfo);
        bool Current()=>!Closed&&_generation==generation;
        void Change(string id,bool? enabled=null,int move=0,bool remove=false) {
            if(busy)return;
            try{SchematicPreviewMaterials.Change(id,enabled,move,remove);Render();_status.Text="已保存材质设置，返回预览后生效。";}
            catch(Exception ex){_status.Text="无法保存材质设置："+ex.Message;Render();}
        }
        void Render() {
            list.Children.Clear();
            try {
                bool supported=SchematicPreviewMaterials.SupportsStack;
                import.IsEnabled=supported;
                baseInfo.Text=supported?"内置基础材质 · 默认启用 · 最低优先级\n首次预览无需选择文件，基础材质随扩展提供。":"请先将预览扩展更新到 0.2.3，以使用内置基础材质和多包叠加。";
                var entries=SchematicPreviewMaterials.Read();
                if(entries.Count==0)list.Children.Add(Label("未添加自定义材质包，将使用内置基础材质。",14));
                for(int i=0;i<entries.Count;i++) {
                    var entry=entries[i];var row=new DockPanel{Margin=new Thickness(4),IsEnabled=supported&&!busy};
                    var controls=new WrapPanel();DockPanel.SetDock(controls,Dock.Right);row.Children.Add(controls);
                    var up=Button("上移",()=>Change(entry.Id,move:-1));up.IsEnabled=i>0;controls.Children.Add(up);
                    var down=Button("下移",()=>Change(entry.Id,move:1));down.IsEnabled=i<entries.Count-1;controls.Children.Add(down);
                    controls.Children.Add(Button("移除",()=>Change(entry.Id,remove:true)));
                    var enabled=new CheckBox{Content=$"{i+1}. {entry.Name}",IsChecked=entry.Enabled,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8)};
                    enabled.SetResourceReference(ForegroundProperty,"TextBrush");
                    enabled.Checked+=(_,_)=>Change(entry.Id,enabled:true);enabled.Unchecked+=(_,_)=>Change(entry.Id,enabled:false);row.Children.Add(enabled);list.Children.Add(row);
                }
            } catch(Exception ex){import.IsEnabled=false;_status.Text="材质列表不可用："+ex.Message;}
        }
        Render();
    }
}
