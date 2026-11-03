using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;
using System.Windows.Media;

namespace CMUmcl;

internal static class LauncherWindowChrome
{
    internal static void Apply(Window window)
    {
        if(window.Tag is string tag && tag=="launcher-chrome")return;
        window.Tag="launcher-chrome";
        window.WindowStyle=WindowStyle.None;
        var chrome=new WindowChrome{CaptionHeight=38,ResizeBorderThickness=new Thickness(6),GlassFrameThickness=new Thickness(0),CornerRadius=new CornerRadius(0),UseAeroCaptionButtons=false};
        WindowChrome.SetWindowChrome(window,chrome);
        window.StateChanged+=(_,_)=>chrome.ResizeBorderThickness=new Thickness(window.WindowState==WindowState.Maximized?0:6);
        var content=window.Content;window.Content=null;
        var root=new Grid();root.RowDefinitions.Add(new(){Height=new GridLength(38)});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        root.SetResourceReference(Panel.BackgroundProperty,"PanelBrush");
        var bar=new DockPanel{LastChildFill=true};
        var controls=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(controls,Dock.Right);bar.Children.Add(controls);
        void Add(string label,Action action)
        {
            var button=new Button{Content=label,Width=42,Height=30,Padding=new Thickness(0),Margin=new Thickness(2)};
            button.SetResourceReference(FrameworkElement.StyleProperty,"SecondaryButton");
            WindowChrome.SetIsHitTestVisibleInChrome(button,true);button.Click+=(_,_)=>action();controls.Children.Add(button);
        }
        Add("—",()=>window.WindowState=WindowState.Minimized);
        if(window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip)Add("□",()=>window.WindowState=window.WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized);
        Add("×",window.Close);
        bar.Children.Add(new TextBlock{Text=window.Title,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(14,0,4,0),TextTrimming=TextTrimming.CharacterEllipsis,FontSize=12});
        root.Children.Add(bar);var presenter=new ContentPresenter{Content=content};Grid.SetRow(presenter,1);root.Children.Add(presenter);window.Content=root;
    }
}

internal static class LauncherPrompt
{
    internal static Func<Window,string,string,MessageBoxButton,MessageBoxResult>? ShowOverride;
    internal static MessageBoxResult Show(Window owner,string message,string title="提示",MessageBoxButton buttons=MessageBoxButton.OK,MessageBoxImage image=MessageBoxImage.None)
    {
        if(ShowOverride!=null)return ShowOverride(owner,message,title,buttons);
        var dialog=new Window{Title=title,Owner=owner,FontFamily=owner.FontFamily,Width=480,SizeToContent=SizeToContent.Height,MinHeight=160,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false};
        dialog.Resources.MergedDictionaries.Add(owner.Resources);
        dialog.SetResourceReference(Control.ForegroundProperty,"TextBrush");dialog.SetResourceReference(Control.BackgroundProperty,"PanelBrush");
        var root=new StackPanel{Margin=new Thickness(24)};
        root.Children.Add(new TextBlock{Text=message,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,20)});
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};root.Children.Add(actions);
        MessageBoxResult result=buttons==MessageBoxButton.YesNo?MessageBoxResult.No:MessageBoxResult.Cancel;
        void Add(string label,MessageBoxResult value,bool defaultButton)
        {
            var b=new Button{Content=label,MinWidth=80,Margin=new Thickness(4),IsDefault=defaultButton};b.SetResourceReference(FrameworkElement.StyleProperty,"SecondaryButton");
            b.Click+=(_,_)=>{result=value;dialog.Close();};actions.Children.Add(b);
        }
        if(buttons==MessageBoxButton.YesNo){Add("继续",MessageBoxResult.Yes,false);Add("取消",MessageBoxResult.No,true);}
        else {Add("确定",MessageBoxResult.OK,true);if(buttons==MessageBoxButton.OKCancel)Add("取消",MessageBoxResult.Cancel,false);}
        dialog.Content=root;LauncherWindowChrome.Apply(dialog);dialog.ShowDialog();return result;
    }
}
