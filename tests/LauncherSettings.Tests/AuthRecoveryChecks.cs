using System.Reflection;
using CMUmcl;
using CMUmcl.Auth;
using CMUmcl.Content;
using CMUmcl.Launch;

internal static partial class Program
{
    private static async Task AuthRecoveryChecks(MainWindow window)
    {
        var field = typeof(MainWindow).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!;
        AuthSession Session() => (AuthSession)field.GetValue(window)!;
        AuthSession Fixture(string token) => new() { AccessToken = token, ClientToken = "fixture", SelectedProfileId = new string('a',32), SelectedProfileName = token };
        var old = Fixture("old"); var fresh = Fixture("fresh");
        var vm = new InstanceCardViewModel("local:auth-recovery", "fixture", "本地实例", null, _output);
        LaunchException Expired() => new("fixture auth failure", new YggdrasilException("fixture expired", true));
        LaunchResult Result(AuthSession session) => new() { Session = session, Process = null! };
        int attempts = 0, prompts = 0;
        try
        {
            field.SetValue(window, old);
            window.ReauthenticateForLaunch = () => { prompts++; return Task.FromResult<AuthSession?>(fresh); };
            window._launchOverride = (_, _) => ++attempts == 1 ? Task.FromException<LaunchResult>(Expired()) : Task.FromResult(Result(Session()));
            var result = await window.LaunchWithLoginRecoveryAsync(vm, null, default);
            Check(attempts == 2 && prompts == 1 && result.Session.AccessToken == "fresh", "auth recovery reauthenticates once and resumes original launch with fresh session");
            Check(Get<System.Windows.Controls.TextBlock>(window,"AccountNameText").Text == "fresh", "auth recovery updates displayed account");

            field.SetValue(window, old); attempts = prompts = 0;
            window.ReauthenticateForLaunch = () => { prompts++; return Task.FromResult<AuthSession?>(null); };
            window._launchOverride = (_, _) => { attempts++; return Task.FromException<LaunchResult>(Expired()); };
            try { await window.LaunchWithLoginRecoveryAsync(vm, null, default); throw new Exception("login cancel did not stop launch"); }
            catch (OperationCanceledException) { }
            Check(attempts == 1 && prompts == 1 && Session() == old, "cancelling login prevents process retry and keeps session unchanged");

            field.SetValue(window, old); attempts = prompts = 0;
            window.ReauthenticateForLaunch = () => { prompts++; return Task.FromResult<AuthSession?>(fresh); };
            var network = new LaunchException("fixture network failure", new YggdrasilException("fixture unavailable"));
            window._launchOverride = (_, _) => { attempts++; return Task.FromException<LaunchResult>(network); };
            try { await window.LaunchWithLoginRecoveryAsync(vm, null, default); throw new Exception("network failure was ignored"); }
            catch (LaunchException ex) { Check(ReferenceEquals(ex, network) && attempts == 1 && prompts == 0, "network failure never opens login or retries launch"); }

            field.SetValue(window, old); attempts = prompts = 0;
            window._launchOverride = (_, _) => { attempts++; return Task.FromException<LaunchResult>(Expired()); };
            try { await window.LaunchWithLoginRecoveryAsync(vm, null, default); throw new Exception("persistent auth failure was ignored"); }
            catch (LaunchException) { Check(attempts == 2 && prompts == 1, "persistent auth failure is bounded to one login prompt and one retry"); }

            field.SetValue(window, old); attempts = prompts = 0;
            var login = new TaskCompletionSource<AuthSession?>(TaskCreationOptions.RunContinuationsAsynchronously);
            window.ReauthenticateForLaunch = () => { prompts++; return login.Task; };
            window._launchOverride = (_, _) => { attempts++; return Session() == old ? Task.FromException<LaunchResult>(Expired()) : Task.FromResult(Result(Session())); };
            var first = window.LaunchWithLoginRecoveryAsync(vm, null, default);
            var second = window.LaunchWithLoginRecoveryAsync(vm, null, default);
            await Until(() => prompts == 1);
            Check(attempts == 2 && prompts == 1, "concurrent expired launches share a single login dialog");
            login.SetResult(fresh); var both = await Task.WhenAll(first, second);
            Check(attempts == 4 && both.All(x => x.Session == fresh), "concurrent pending launches both resume with reauthenticated session");

            field.SetValue(window, old); attempts = prompts = 0;
            var slow = new TaskCompletionSource<LaunchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            window.ReauthenticateForLaunch = () => { prompts++; return Task.FromResult<AuthSession?>(fresh); };
            window._launchOverride = (_, _) => ++attempts == 1 ? slow.Task : Session() == old ? Task.FromException<LaunchResult>(Expired()) : Task.FromResult(Result(Session()));
            var slowLaunch = window.LaunchWithLoginRecoveryAsync(vm, null, default);
            await window.LaunchWithLoginRecoveryAsync(vm, null, default);
            slow.SetResult(Result(old)); await slowLaunch;
            Check(Session() == fresh, "late older game-window result cannot overwrite reauthenticated session");

            field.SetValue(window, old); attempts = prompts = 0;
            using var cancel = new CancellationTokenSource();
            window.ReauthenticateForLaunch = () => { prompts++; cancel.Cancel(); return Task.FromResult<AuthSession?>(fresh); };
            window._launchOverride = (_, _) => { attempts++; return Task.FromException<LaunchResult>(Expired()); };
            try { await window.LaunchWithLoginRecoveryAsync(vm, null, cancel.Token); throw new Exception("cancelled launch was retried"); }
            catch (OperationCanceledException) { Check(attempts == 1 && prompts == 1, "cancel during login never starts another game attempt"); }
            Console.WriteLine("PASS authentication recovery UI checks");
        }
        finally { window._launchOverride = null; window.ReauthenticateForLaunch = null; }
    }
}
