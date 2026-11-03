// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace CMUmcl.Community
{
    public sealed class ResourceKindEqualsConverter : System.Windows.Data.IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => values.Length == 2 && Equals(values[0], values[1]);
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }

    public partial class CommunityResourceView : UserControl
    {
        public CommunityResourceView()
        {
            InitializeComponent();
        }

        private ContextMenu? _websiteMenu;
        private bool _closedByWebsiteClick;
        private void OpenProjectUrl_Click(object sender, RoutedEventArgs e)
        {
            var detail = (DataContext as CommunityResourceViewModel)?.Detail;
            if (sender is not Button button || detail == null) return;
            if(_websiteMenu?.IsOpen==true){_websiteMenu.IsOpen=false;return;}
            if(_closedByWebsiteClick){_closedByWebsiteClick=false;return;}
            var menu = CreateProjectWebsiteMenu(detail);
            _websiteMenu=menu;
            menu.Closed+=(_,_)=>{_closedByWebsiteClick=button.IsMouseOver && System.Windows.Input.Mouse.LeftButton==System.Windows.Input.MouseButtonState.Pressed;};
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        internal static ContextMenu CreateProjectWebsiteMenu(ResourceDetailViewModel detail)
        {
            var menu = new ContextMenu { UseLayoutRounding = true, SnapsToDevicePixels = true, Padding = new Thickness(4), BorderThickness = new Thickness(1) };
            menu.SetResourceReference(Control.BorderBrushProperty, "PanelBorderBrush");
            menu.SetResourceReference(Control.BackgroundProperty, "PanelBrush");
            menu.SetResourceReference(Control.ForegroundProperty, "TextBrush");
            var outer = new ControlTemplate(typeof(ContextMenu));
            var frame = new FrameworkElementFactory(typeof(Border));frame.SetValue(Border.CornerRadiusProperty,new CornerRadius(6));frame.SetValue(Border.PaddingProperty,new Thickness(4));frame.SetValue(Border.BorderThicknessProperty,new Thickness(1));
            frame.SetResourceReference(Border.BackgroundProperty,"PanelBrush");frame.SetResourceReference(Border.BorderBrushProperty,"PanelBorderBrush");
            frame.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));outer.VisualTree=frame;menu.Template=outer;
            foreach (var source in new[] { ResourceSourceId.Modrinth, ResourceSourceId.CurseForge })
            {
                string url = detail.GetPlatformUrl(source);
                var item = new MenuItem { Header = source + (detail.HasPlatformProjectUrl(source) ? " 项目页 ↗" : " 搜索 ↗"), Tag = url };
                var template = new ControlTemplate(typeof(MenuItem));
                var border = new FrameworkElementFactory(typeof(Border));
                border.Name="ItemSurface";
                border.SetValue(Border.CornerRadiusProperty,new CornerRadius(4));
                border.SetValue(Border.PaddingProperty,new Thickness(12,9,12,9));
                border.SetResourceReference(Border.BackgroundProperty,"PanelBrush");
                var content = new FrameworkElementFactory(typeof(ContentPresenter));
                content.SetValue(ContentPresenter.ContentSourceProperty,"Header");border.AppendChild(content);template.VisualTree=border;
                var hover = new Trigger { Property=MenuItem.IsHighlightedProperty, Value=true };
                hover.Setters.Add(new Setter(Border.BackgroundProperty, FindThemeBrush("AccentSoftBrush"),"ItemSurface"));

                template.Triggers.Add(hover);item.SetResourceReference(Control.BackgroundProperty,"PanelBrush");item.Template=template;
                item.Click += (_, _) => OpenExternalProjectUrl(url);
                menu.Items.Add(item);
            }
            return menu;
        }

        private static object FindThemeBrush(string name) => Application.Current.FindResource(name);
        private static void OpenExternalProjectUrl(string url)
        {
            if (!CommunityHosts.IsAllowedProjectLink(url)) return;
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                // External browser launch failures are non-fatal; ignore.
            }
        }

        private void BrowseModpackSavePath_Click(object sender, RoutedEventArgs e)
        {
            ResourceDetailViewModel? detail = (DataContext as CommunityResourceViewModel)?.Detail;
            if (detail == null) return;
            var dialog = new SaveFileDialog
            {
                Filter = "ZIP 整合包 (*.zip)|*.zip",
                DefaultExt = ".zip",
                AddExtension = true,
                Title = "选择整合包保存位置",
            };
            if (!string.IsNullOrWhiteSpace(detail.ModpackSavePath))
            {
                try
                {
                    string? dir = Path.GetDirectoryName(detail.ModpackSavePath);
                    if (!string.IsNullOrWhiteSpace(dir)) dialog.InitialDirectory = dir;
                    dialog.FileName = Path.GetFileName(detail.ModpackSavePath);
                }
                catch
                {
                    // Invalid existing path: leave the dialog defaults.
                }
            }
            if (dialog.ShowDialog() == true)
                detail.ModpackSavePath = dialog.FileName;
        }
    }
}
