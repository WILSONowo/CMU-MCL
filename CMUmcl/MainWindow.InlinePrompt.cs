using System.Windows;
namespace CMUmcl;
public partial class MainWindow
{
    internal enum InlineChoice {Cancel,Continue,Discard}
    private TaskCompletionSource<InlineChoice>? _inlinePrompt;
    internal Task<InlineChoice> ShowInlinePromptAsync(string title,string message,string primary="继续",bool allowDiscard=false)
    {
        CancelInlinePrompt();
        var pending=new TaskCompletionSource<InlineChoice>(TaskCreationOptions.RunContinuationsAsynchronously);_inlinePrompt=pending;
        InlinePromptTitle.Text=title;InlinePromptMessage.Text=message;InlinePromptContinue.Content=primary;
        InlinePromptDiscard.Visibility=allowDiscard?Visibility.Visible:Visibility.Collapsed;
        InlinePromptOverlay.Visibility=Visibility.Visible;InlinePromptCancel.Focus();return pending.Task;
    }
    private void CompleteInlinePrompt(InlineChoice choice)
    {
        var pending=_inlinePrompt;_inlinePrompt=null;InlinePromptOverlay.Visibility=Visibility.Collapsed;pending?.TrySetResult(choice);
    }
    private void CancelInlinePrompt()=>CompleteInlinePrompt(InlineChoice.Cancel);
    private void InlinePromptContinue_Click(object sender,RoutedEventArgs e)=>CompleteInlinePrompt(InlineChoice.Continue);
    private void InlinePromptCancel_Click(object sender,RoutedEventArgs e)=>CancelInlinePrompt();
    private void InlinePromptDiscard_Click(object sender,RoutedEventArgs e)=>CompleteInlinePrompt(InlineChoice.Discard);
}
