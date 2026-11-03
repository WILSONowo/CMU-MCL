using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using CMUmcl;
using CMUmcl.Update;

internal static partial class Program
{
    private static async Task LauncherUpdatePopupChecks(MainWindow window)
    {
        int windows = Application.Current.Windows.Count;
        var release = new UpdateRelease(2, "windows-x86_64", "9.9.9",
            "https://csumcl.mcapcs.cn/update/windows-x86_64/net48/9.9.9.zip", 1234,
            new string('a', 64), new UpdateSignature("ECDSA-P256-SHA256", "ieee-p1363-base64",
                Convert.ToBase64String(new byte[64])), "popup fixture");
        var method = typeof(MainWindow).GetMethod("PromptForLauncherUpdateAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task)method.Invoke(window, new object[] { (release, Array.Empty<byte>()) })!;
        await Until(() => Get<Grid>(window, "InlinePromptOverlay").Visibility == Visibility.Visible);
        Check(Get<TextBlock>(window, "InlinePromptTitle").Text == "\u53D1\u73B0\u5BA2\u6237\u7AEF\u66F4\u65B0",
            "update popup uses the in-window inline prompt");
        Check(Get<TextBlock>(window, "InlinePromptMessage").Text.Contains("9.9.9"),
            "update popup names the available version");
        Check(Get<Button>(window, "InlinePromptContinue").Content is string primary &&
              primary == "\u4E0B\u8F7D\u5E76\u5B89\u88C5",
            "update popup offers an explicit download action");
        Check(Application.Current.Windows.Count == windows,
            "update popup does not open a separate window");
        Get<Button>(window, "InlinePromptCancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await task;
        Check(Get<Grid>(window, "InlinePromptOverlay").Visibility == Visibility.Collapsed,
            "update popup can be dismissed without forced installation");
        Check(Get<TextBlock>(window, "LauncherUpdateStatus").Text.Contains("9.9.9"),
            "dismissed update remains visible in the settings status");
    }
}
