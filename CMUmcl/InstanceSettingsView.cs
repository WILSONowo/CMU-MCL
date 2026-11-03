// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CMUmcl.Content;
using Microsoft.Win32;
using System.Threading;
using System.Threading.Tasks;
using CMUmcl.Launch;

namespace CMUmcl
{
    internal sealed partial class InstanceSettingsView : UserControl
    {
        private readonly string _directory;
        private readonly bool _global;
        private readonly SettingsStore _store;
        private bool _loadFailed;
        private Button? _javaBrowse;
        private readonly TextBox _name=new(),_java=new(),_memory=new(),_width=new(),_height=new(),_arguments=new();
        private readonly ComboBox _icon=new();
        private readonly CheckBox _javaOverride=new(){Content="为此实例单独设置 Java 与内存"},_windowOverride=new(){Content="为此实例单独设置游戏窗口"},_argsOverride=new(){Content="为此实例单独设置启动参数"},_auto=new(){Content="自动分配内存"},_fullscreen=new(){Content="全屏启动"};
        private readonly TextBlock _status=new(){TextWrapping=TextWrapping.Wrap};
        private readonly Grid _content=new();
        private readonly StackPanel[] _pages={new(),new(),new(),new(),new(),new()};
        internal bool Saved {get;private set;}
        internal InstanceSettingsView(string directory,string title,SettingsStore store,bool global=false,LaunchService? launch=null)
        {
            _directory=directory;_store=store;_global=global;_launch=launch;
            var main=Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
            if(main!=null)Resources.MergedDictionaries.Add(main.Resources);
            FontFamily=new System.Windows.Media.FontFamily("Segoe UI, Microsoft YaHei UI");UseLayoutRounding=true;SnapsToDevicePixels=true;
            Title=global?"全局游戏默认设置":"实例设置 · "+title;
            SetResourceReference(BackgroundProperty,"PanelBrush");SetResourceReference(ForegroundProperty,"TextBrush");

            var root=new Grid{Margin=new Thickness(20)};root.SetResourceReference(Panel.BackgroundProperty,"PanelBrush");root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});Content=root;
            var heading=new DockPanel{Margin=new Thickness(0,0,0,16)};var back=Button("← 返回",()=>BackRequested?.Invoke());DockPanel.SetDock(back,Dock.Left);heading.Children.Add(back);heading.Children.Add(new TextBlock{Text=Title,FontSize=20,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0,0,0),TextWrapping=TextWrapping.Wrap});root.Children.Add(heading);
            var body=new Grid();body.ColumnDefinitions.Add(new(){Width=new GridLength(146)});body.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});Grid.SetRow(body,1);root.Children.Add(body);
            var nav=new ListBox{BorderThickness=new Thickness(0),Focusable=false,ItemsSource=global?new[]{"常规","Java 与内存","游戏窗口","启动参数"}:new[]{"常规","Java 与内存","游戏窗口","启动参数","安装","同步"},Margin=new Thickness(0,0,16,0)};nav.SetResourceReference(Control.BackgroundProperty,"SidebarBrush");nav.SetResourceReference(Control.ForegroundProperty,"TextBrush");
            var navStyle=new Style(typeof(ListBoxItem));navStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(12,11,8,11)));navStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));
            var navTemplate=new ControlTemplate(typeof(ListBoxItem));var surface=new FrameworkElementFactory(typeof(Border));surface.Name="Surface";surface.SetValue(Border.CornerRadiusProperty,new CornerRadius(5));surface.SetValue(Border.MarginProperty,new Thickness(0,3,0,3));surface.SetValue(Border.PaddingProperty,new Thickness(12,11,8,11));surface.SetResourceReference(Border.BackgroundProperty,"SidebarBrush");var presenter=new FrameworkElementFactory(typeof(ContentPresenter));surface.AppendChild(presenter);navTemplate.VisualTree=surface;
            var selected=new Trigger{Property=ListBoxItem.IsSelectedProperty,Value=true};selected.Setters.Add(new Setter(Border.BackgroundProperty,FindResource("AccentSoftBrush"),"Surface"));selected.Setters.Add(new Setter(Control.ForegroundProperty,FindResource("AccentBrush")));navTemplate.Triggers.Add(selected);
            var hover=new Trigger{Property=ListBoxItem.IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(Border.BackgroundProperty,FindResource("AccentSoftBrush"),"Surface"));navTemplate.Triggers.Add(hover);navStyle.Setters.Add(new Setter(Control.TemplateProperty,navTemplate));nav.ItemContainerStyle=navStyle;body.Children.Add(nav);
            var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Content=_content,Focusable=false,FocusVisualStyle=null};Grid.SetColumn(scroll,1);body.Children.Add(scroll);
            foreach(var page in _pages){page.Margin=new Thickness(2,0,12,12);page.Visibility=Visibility.Collapsed;_content.Children.Add(page);}
            nav.SelectionChanged+=(_,_)=>{for(int i=0;i<_pages.Length;i++)_pages[i].Visibility=i==nav.SelectedIndex?Visibility.Visible:Visibility.Collapsed;};nav.SelectedIndex=0;
            AddText(_pages[0],global?"全局默认供未启用单独覆盖的实例使用。":"名称和图标仅影响当前实例；留空名称或自动图标（按加载器）可恢复原始显示。");
            AddField(_pages[0],"显示名称（留空恢复原名）",_name);_icon.ItemsSource=new[]{"自动图标（按加载器）"}.Concat(InstanceOptionsStore.Icons);AddField(_pages[0],"实例图标",_icon);_name.IsEnabled=_icon.IsEnabled=!global;
            AddText(_pages[0],"实例目录："+directory);
            AddText(_pages[0],"设置在下次启动时生效。此页不会移动实例目录或修改游戏／加载器版本。");
            var globalButton=Button("编辑全局默认",()=>EditGlobalDefaultsRequested?.Invoke());globalButton.Visibility=global?Visibility.Collapsed:Visibility.Visible;_pages[0].Children.Add(globalButton);
            _pages[1].Children.Add(_javaOverride);AddField(_pages[1],"Java 路径（留空自动选择／下载）",_java);_javaBrowse=Button("选择 javaw.exe",()=>{var dialog=new OpenFileDialog{Filter="Java (javaw.exe;java.exe)|javaw.exe;java.exe"};if(dialog.ShowDialog(Window.GetWindow(this))==true)_java.Text=dialog.FileName;});_pages[1].Children.Add(_javaBrowse);_pages[1].Children.Add(_auto);AddField(_pages[1],"最大内存（MiB）",_memory);
            _pages[2].Children.Add(_windowOverride);_pages[2].Children.Add(_fullscreen);AddField(_pages[2],"窗口宽度",_width);AddField(_pages[2],"窗口高度",_height);
            _pages[3].Children.Add(_argsOverride);AddText(_pages[3],"额外 JVM 参数：每行一个完整参数，含空格时无需外层引号。覆盖模式会替换全局额外参数；留空表示不添加。内存请在“Java 与内存”设置。启动器仍保留认证所需参数。");_arguments.AcceptsReturn=true;_arguments.MinHeight=160;_arguments.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;AddField(_pages[3],"JVM 参数",_arguments);
            var footer=new Grid{Margin=new Thickness(0,12,0,0)};footer.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});footer.ColumnDefinitions.Add(new(){Width=GridLength.Auto});Grid.SetRow(footer,2);root.Children.Add(footer);footer.Children.Add(_status);var buttons=new StackPanel{Orientation=Orientation.Horizontal};Grid.SetColumn(buttons,1);footer.Children.Add(buttons);_saveButton=Button("保存",Save);buttons.Children.Add(_saveButton);
            if(!global)BuildMaintenancePages();

            try{LoadValues();}catch{_loadFailed=true;_status.Text="设置文件无效或无法读取；已禁止保存，请先恢复有效设置文件。";_saveButton.IsEnabled=false;}
            _javaOverride.Click+=(_,_)=>{if(_javaOverride.IsChecked!=true){var g=_store.Load();_java.Text=g.JavaPath??"";_auto.IsChecked=g.AutoMemory;_memory.Text=g.MaxMemoryMb.ToString();}};
            _windowOverride.Click+=(_,_)=>{if(_windowOverride.IsChecked!=true){var g=_store.Load();_width.Text=g.WindowWidth.ToString();_height.Text=g.WindowHeight.ToString();_fullscreen.IsChecked=g.FullScreen;}};
            _argsOverride.Click+=(_,_)=>{if(_argsOverride.IsChecked!=true)_arguments.Text=string.Join(Environment.NewLine,_store.Load().ExtraJvmArguments??Array.Empty<string>());};
            foreach(var check in new[]{_javaOverride,_windowOverride,_argsOverride,_auto,_fullscreen})check.Click+=(_,_)=>UpdateEnabled();UpdateEnabled();
            _savedSnapshot=CaptureSettings();
        }
        internal string Title {get;}
        internal string DirectoryPath=>_directory;
        internal bool IsGlobal=>_global;
        internal bool IsClosed {get;private set;}
        internal event EventHandler? Closed;
        internal event EventHandler? OperationFinished;
        internal event Action? BackRequested;
        internal event Action? EditGlobalDefaultsRequested;
        internal Func<string,string,Task<bool>>? ConfirmAsync {get;set;}
        private string _savedSnapshot="";
        internal bool HasUnsavedChanges=>!IsClosed && CaptureSettings()!=_savedSnapshot;
        private string CaptureSettings()=>System.Text.Json.JsonSerializer.Serialize(new object?[]{
            _global?null:_name.Text,_global?null:_icon.SelectedIndex,
            _javaOverride.IsChecked,(_global||_javaOverride.IsChecked==true)?new object?[]{_java.Text,_auto.IsChecked,_memory.Text}:null,
            _windowOverride.IsChecked,(_global||_windowOverride.IsChecked==true)?new object?[]{_width.Text,_height.Text,_fullscreen.IsChecked}:null,
            _argsOverride.IsChecked,(_global||_argsOverride.IsChecked==true)?_arguments.Text:null,
            _syncChecks.Where(x=>x.Value.IsChecked==true).Select(x=>x.Key).ToArray()});
        internal void SaveAndClose()=>Save();
        internal void Close()
        {
            if(IsClosed)return;IsClosed=true;_maintenanceCts?.Cancel();Closed?.Invoke(this,EventArgs.Empty);
        }
        private Task<bool> Confirm(string message,string title)=>ConfirmAsync?.Invoke(message,title)??Task.FromResult(false);
        private static void AddText(Panel panel,string text)=>panel.Children.Add(new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
        private static void AddField(Panel panel,string label,Control field){AddText(panel,label);field.Margin=new Thickness(0,0,0,16);field.MinHeight=34;panel.Children.Add(field);}
        private static Button Button(string text,Action action){var b=new Button{Content=text,Margin=new Thickness(4),Padding=new Thickness(12,7,12,7)};b.SetResourceReference(StyleProperty,"SecondaryButton");b.Click+=(_,_)=>action();return b;}
        private void LoadValues()
        {
            var global=_store.Load();var local=_global?new InstanceOptions():InstanceOptionsStore.Load(_directory);var effective=InstanceOptionsStore.Resolve(global,local);
            _name.Text=local.Name??"";_icon.SelectedIndex=local.Icon==null?0:Array.IndexOf(InstanceOptionsStore.Icons,local.Icon)+1;
            _javaOverride.IsChecked=local.Java!=null;_windowOverride.IsChecked=local.Window!=null;_argsOverride.IsChecked=local.JvmArguments!=null;
            foreach(var box in new[]{_javaOverride,_windowOverride,_argsOverride})box.Visibility=_global?Visibility.Collapsed:Visibility.Visible;
            _java.Text=effective.JavaPath??"";_auto.IsChecked=effective.AutoMemory;_memory.Text=effective.MaxMemoryMb.ToString();_width.Text=effective.WindowWidth.ToString();_height.Text=effective.WindowHeight.ToString();_fullscreen.IsChecked=effective.FullScreen;_arguments.Text=string.Join(Environment.NewLine,effective.ExtraJvmArguments??Array.Empty<string>());
        }
        internal void RefreshInherited()
        {
            var g=_store.Load();
            if(_javaOverride.IsChecked!=true){_java.Text=g.JavaPath??"";_auto.IsChecked=g.AutoMemory;_memory.Text=g.MaxMemoryMb.ToString();}
            if(_windowOverride.IsChecked!=true){_width.Text=g.WindowWidth.ToString();_height.Text=g.WindowHeight.ToString();_fullscreen.IsChecked=g.FullScreen;}
            if(_argsOverride.IsChecked!=true)_arguments.Text=string.Join(Environment.NewLine,g.ExtraJvmArguments??Array.Empty<string>());
            UpdateEnabled();
        }
        private void UpdateEnabled()
        {
            bool java=_global||_javaOverride.IsChecked==true,window=_global||_windowOverride.IsChecked==true;
            if(_javaBrowse!=null)_javaBrowse.IsEnabled=java;
            _java.IsEnabled=_auto.IsEnabled=java;_memory.IsEnabled=java&&_auto.IsChecked!=true;_fullscreen.IsEnabled=window;_width.IsEnabled=_height.IsEnabled=window&&_fullscreen.IsChecked!=true;_arguments.IsEnabled=_global||_argsOverride.IsChecked==true;
        }
        private void Save()
        {
            if(IsClosed || _loadFailed || _maintenanceBusy)return;
            if(!_global && IsOperationBlocked?.Invoke()==true){_status.Text="此实例正在处理任务，请完成后保存。";return;}
            try
            {
                var local=new InstanceOptions{SharedSettings=_global?Array.Empty<string>():_syncChecks.Where(x=>x.Value.IsChecked==true).Select(x=>x.Key).ToArray(),Name=string.IsNullOrWhiteSpace(_name.Text)?null:_name.Text.Trim(),Icon=_icon.SelectedIndex>0?InstanceOptionsStore.Icons[_icon.SelectedIndex-1]:null};
                if(_global||_javaOverride.IsChecked==true){if(!int.TryParse(_memory.Text,out int memory))throw new InvalidDataException("请输入有效内存数值。");var path=_java.Text.Trim();if(path.Length>0 && Launch.JavaLocator.DetectExecutable(path)==null)throw new InvalidDataException("Java 路径无效或无法识别版本。");local.Java=new(){Path=path,AutoMemory=_auto.IsChecked==true,MemoryMb=memory};}
                if(_global||_windowOverride.IsChecked==true){if(!int.TryParse(_width.Text,out int width)||!int.TryParse(_height.Text,out int height))throw new InvalidDataException("请输入有效窗口尺寸。");local.Window=new(){Width=width,Height=height,FullScreen=_fullscreen.IsChecked==true};}
                if(_global||_argsOverride.IsChecked==true)local.JvmArguments=_arguments.Text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Trim()).Where(x=>x.Length>0).ToArray();
                InstanceOptionsStore.Validate(local);
                if(_global){var settings=_store.Load();settings.JavaPath=local.Java!.Path;settings.AutoMemory=local.Java.AutoMemory;settings.MaxMemoryMb=local.Java.MemoryMb;settings.WindowWidth=local.Window!.Width;settings.WindowHeight=local.Window.Height;settings.FullScreen=local.Window.FullScreen;settings.ExtraJvmArguments=local.JvmArguments!;if(!_store.Save(settings))throw new IOException();}
                else InstanceOptionsStore.Save(_directory,local);
                Saved=true;Close();
            }
            catch(InvalidDataException ex){_status.Text=ex.Message;}
            catch{_status.Text="保存失败，原设置已保留，请检查目录权限。";}
        }
    }
}
