using System.Windows;
using CMUmcl;

// Only component tests need a temporary host. Product code embeds this
// UserControl directly in MainWindow and never creates this host Window.
internal static class InstanceEditorTestHost
{
    internal static void Show(this InstanceSettingsView view)
    {
        var host=new Window{Content=view,Width=double.IsNaN(view.Width)?850:view.Width,Height=double.IsNaN(view.Height)?650:view.Height,
            WindowStyle=WindowStyle.None,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=-18000,Top=-18000};
        view.Closed+=(_,_)=>host.Close();host.Show();
    }
}
