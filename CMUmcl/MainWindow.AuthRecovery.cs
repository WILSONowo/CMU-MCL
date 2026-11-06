using System;
using System.Threading;
using System.Threading.Tasks;
using CMUmcl.Auth;
using CMUmcl.Content;
using CMUmcl.Launch;

namespace CMUmcl
{
    public partial class MainWindow
    {
        internal Func<Task<AuthSession?>>? ReauthenticateForLaunch { get; set; }
        private Task<AuthSession?>? _reauthentication;

        // A failed refresh can require credentials, rather than another refresh.
        // Recover once and resume the original instance; network failures do not
        // open a login dialog. Concurrent launches share the same dialog.
        internal async Task<LaunchResult> LaunchWithLoginRecoveryAsync(InstanceCardViewModel vm,
            IProgress<LaunchProgress>? progress, CancellationToken ct)
        {
            for (int attempt = 0; ; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                var attemptedSession = _session;
                try
                {
                    var result = await (_launchOverride != null ? _launchOverride(vm, ct)
                        : _launchService.LaunchLocalAsync(vm.InstanceId, attemptedSession, progress, ct));
                    // Another launch may have reauthenticated while this game was
                    // opening its window. Its newer session must not be replaced.
                    if (ReferenceEquals(_session, attemptedSession)) _session = result.Session;
                    return result;
                }
                catch (LaunchException ex) when (ex.RequiresLogin && attempt == 0 && ReauthenticateForLaunch != null)
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Report(new LaunchProgress { Stage = "重新登录", Message = "登录状态需要恢复，请重新登录；完成后将继续启动。" });
                    if (ReferenceEquals(_session, attemptedSession))
                    {
                        _reauthentication ??= PromptForLaunchLoginAsync();
                        var pending = _reauthentication;
                        AuthSession? restored;
                        try { restored = await pending; }
                        finally { if (ReferenceEquals(_reauthentication, pending) && pending.IsCompleted) _reauthentication = null; }
                        ct.ThrowIfCancellationRequested();
                        if (restored == null) throw new OperationCanceledException();
                    }
                }
            }
        }

        private async Task<AuthSession?> PromptForLaunchLoginAsync()
        {
            // Yield before opening a modal so the shared task is assigned first.
            await Task.Yield();
            if (_windowClosed) return null;
            var restored = await ReauthenticateForLaunch!();
            if (restored != null && !_windowClosed)
            {
                _session = restored;
                AccountNameText.Text = restored.SelectedProfileName;
                _ = LoadAccountAvatarAsync(true);
            }
            return restored;
        }
    }
}
