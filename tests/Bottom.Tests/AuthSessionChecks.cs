using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using CMUmcl.Auth;
using CMUmcl.Content;

internal static partial class Program
{
    private sealed class AuthHandler(Func<string, JsonElement, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            return await send(request.RequestUri!.AbsolutePath.Split('/').Last(), body.RootElement, ct);
        }
    }
    private static HttpResponseMessage AuthResponse(HttpStatusCode status, object? body = null)
        => new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
    private static AuthSession AuthFixture(string token = "old")
        => new() { AccessToken = token, ClientToken = "client", SelectedProfileId = new string('a', 32), SelectedProfileName = "fixture" };
    private static object RefreshFixture(string token = "new", bool profile = true)
        => new { accessToken = token, clientToken = "client", selectedProfile = profile ? new { id = new string('a', 32), name = "fixture" } : null };
    private static async Task AuthRejects(Func<Task> action, string name)
    {
        try { await action(); }
        catch (YggdrasilException) { Check(true, name); return; }
        throw new Exception("FAIL " + name);
    }

    private static async Task AuthSessionChecks()
    {
        var authStore = new AuthStore();
        authStore.ClearSession();
        var session = AuthFixture();
        using (var client = new YggdrasilClient(new AuthHandler((path, body, ct) =>
        {
            Check(path == "validate", "valid session does not call refresh");
            return Task.FromResult(AuthResponse(HttpStatusCode.NoContent));
        })))
        {
            var gate = new AuthSessionGate(client, authStore);
            Check(await gate.RunAsync(session, current => gate.StartAsync(current, () => current.AccessToken == "old", default), default),
                "valid session reaches process handoff with the validated token");
        }
        int refreshes = 0;
        using (var client = new YggdrasilClient(new AuthHandler((path, body, ct) =>
        {
            if (path == "refresh") { refreshes++; return Task.FromResult(AuthResponse(HttpStatusCode.OK, RefreshFixture())); }
            return Task.FromResult(AuthResponse(body.GetProperty("accessToken").GetString() == "old" ? HttpStatusCode.Forbidden : HttpStatusCode.NoContent));
        })))
        {
            var gate = new AuthSessionGate(client, authStore);
            var firstEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finishFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var first = gate.RunAsync(session, async current => { firstEntered.SetResult(true); await finishFirst.Task; return current; }, default);
            await firstEntered.Task;
            bool secondEntered = false;
            var second = gate.RunAsync(session, current => { secondEntered = true; return Task.FromResult(current); }, default);
            Check(!secondEntered, "concurrent launch waits until first token handoff finishes");
            using (var cancel = new CancellationTokenSource())
            {
                var waiting = gate.RunAsync(session, Task.FromResult, cancel.Token);
                cancel.Cancel();
                try { await waiting; throw new Exception("waiting launch was not cancelled"); }
                catch (OperationCanceledException) { Check(true, "waiting launch cancellation preserves cancellation semantics"); }
            }
            finishFirst.SetResult(true);
            var results = await Task.WhenAll(first, second);
            Check(refreshes == 1 && results.All(x => x.AccessToken == "new"), "concurrent stale sessions reuse one verified replacement");
            Check(authStore.LoadSession() == null, "refresh respects do-not-remember login");
            try { await gate.RunAsync(session, _ => Task.FromException<bool>(new IOException("fixture")), default); }
            catch (IOException) { }
            Check((await gate.RunAsync(session, Task.FromResult, default)).AccessToken == "new" && refreshes == 1,
                "failed game preparation cannot lose refreshed in-memory token");
        }

        authStore.SaveSession(session);
        using (var client = new YggdrasilClient(new AuthHandler((path, body, ct) => Task.FromResult(path == "refresh"
            ? AuthResponse(HttpStatusCode.OK, RefreshFixture())
            : AuthResponse(body.GetProperty("accessToken").GetString() == "old" ? HttpStatusCode.Forbidden : HttpStatusCode.NoContent)))))
        {
            var gate = new AuthSessionGate(client, authStore);
            await gate.RunAsync(session, Task.FromResult, default);
            Check(authStore.LoadSession()?.AccessToken == "new", "remembered session saves verified replacement");
        }

        foreach (var fixture in new[] { RefreshFixture(), RefreshFixture(""), RefreshFixture("new", false),
            new { accessToken = "new", clientToken = "client", selectedProfile = new { id = new string('b',32), name = "other" } } })
        {
            authStore.SaveSession(session);
            bool started = false;
            using var client = new YggdrasilClient(new AuthHandler((path, body, ct) => Task.FromResult(path == "refresh"
                ? AuthResponse(HttpStatusCode.OK, fixture) : AuthResponse(HttpStatusCode.Forbidden))));
            var gate = new AuthSessionGate(client, authStore);
            await AuthRejects(() => gate.RunAsync(session, _ => { started = true; return Task.FromResult(true); }, default),
                "invalid, empty, unbound or mismatched refresh response rejected");
            Check(!started && authStore.LoadSession()?.AccessToken == "old", "rejected refresh cannot start game or overwrite saved session");
        }

        int validations = 0;
        authStore.SaveSession(session);
        using (var cancel = new CancellationTokenSource())
        {
            bool cancelVerification = true;
            int rotations = 0;
            using var client = new YggdrasilClient(new AuthHandler((path, body, ct) =>
            {
                if (path == "refresh") { rotations++; return Task.FromResult(AuthResponse(HttpStatusCode.OK, RefreshFixture())); }
                if (body.GetProperty("accessToken").GetString() == "old") return Task.FromResult(AuthResponse(HttpStatusCode.Forbidden));
                if (cancelVerification) { cancel.Cancel(); return Task.FromCanceled<HttpResponseMessage>(ct); }
                return Task.FromResult(AuthResponse(HttpStatusCode.NoContent));
            }));
            var gate = new AuthSessionGate(client, authStore);
            try { await gate.RunAsync(session, Task.FromResult, cancel.Token); throw new Exception("verification was not cancelled"); }
            catch (OperationCanceledException) { }
            Check(authStore.LoadSession()?.AccessToken == "old", "cancelled verification does not persist an unverified token");
            cancelVerification = false;
            Check((await gate.RunAsync(session, Task.FromResult, default)).AccessToken == "new" && rotations == 1,
                "retry verifies retained refresh response instead of refreshing revoked old token");
            Check(authStore.LoadSession()?.AccessToken == "new", "retry persists replacement only after successful verification");
        }
        foreach (bool logout in new[] { true, false })
        {
            authStore.SaveSession(session);
            using var client = new YggdrasilClient(new AuthHandler((path, body, ct) =>
            {
                if (path == "refresh")
                {
                    if (logout) authStore.ClearSession(); else authStore.SaveSession(AuthFixture("other-login"));
                    return Task.FromResult(AuthResponse(HttpStatusCode.OK, RefreshFixture()));
                }
                return Task.FromResult(AuthResponse(body.GetProperty("accessToken").GetString() == "old" ? HttpStatusCode.Forbidden : HttpStatusCode.NoContent));
            }));
            await new AuthSessionGate(client, authStore).RunAsync(session, Task.FromResult, default);
            Check(logout ? authStore.LoadSession() == null : authStore.LoadSession()?.AccessToken == "other-login",
                "late refresh cannot restore logged-out session or overwrite another login");
        }
        using (var client = new YggdrasilClient(new AuthHandler((path, body, ct) => Task.FromResult(
            AuthResponse(++validations == 1 ? HttpStatusCode.NoContent : HttpStatusCode.Forbidden)))))
        {
            var gate = new AuthSessionGate(client, authStore);
            bool started = false;
            await AuthRejects(() => gate.RunAsync(session, current => gate.StartAsync(current, () => started = true, default), default),
                "token invalidated during process construction rejected at final start gate");
            Check(!started, "final validation failure never invokes process start");
        }

        int requests = 0;
        using (var client = new YggdrasilClient(new AuthHandler((path, body, ct) => { requests++; return Task.FromResult(AuthResponse(HttpStatusCode.ServiceUnavailable)); })))
        {
            await AuthRejects(() => new AuthSessionGate(client, authStore).RunAsync(session, Task.FromResult, default), "network failure stops launch");
            Check(requests == 1, "unavailable validation does not rotate token");
        }
        using (var cancel = new CancellationTokenSource())
        using (var client = new YggdrasilClient(new AuthHandler(async (path, body, ct) => { cancel.Cancel(); await Task.Delay(Timeout.Infinite, ct); return AuthResponse(HttpStatusCode.NoContent); })))
        {
            try { await client.ValidateAsync("fixture", "client", cancel.Token); throw new Exception("validation was not cancelled"); }
            catch (OperationCanceledException) { Check(true, "HTTP validation cancellation is not reported as a timeout"); }
        }
        using (var client = new YggdrasilClient(new AuthHandler((path, body, ct) =>
        {
            Check(body.GetProperty("selectedProfile").GetProperty("id").GetString() == session.SelectedProfileId,
                "multi-profile login sends selected profile to refresh endpoint");
            return Task.FromResult(AuthResponse(HttpStatusCode.OK, RefreshFixture()));
        })))
            await client.RefreshAsync("fixture", "client", selectedProfile: new YggdrasilProfile { Id = session.SelectedProfileId, Name = session.SelectedProfileName });

        var selected = new YggdrasilProfile { Id = session.SelectedProfileId, Name = session.SelectedProfileName };
        foreach (bool boundInitially in new[] { true, false })
        {
            int loginRefreshes = 0;
            using var client = new YggdrasilClient(new AuthHandler((path, body, ct) =>
            {
                if (path == "refresh")
                {
                    loginRefreshes++;
                    Check(body.GetProperty("selectedProfile").GetProperty("id").GetString() == selected.Id, "login binds the user's selected role");
                    return Task.FromResult(AuthResponse(HttpStatusCode.OK, RefreshFixture()));
                }
                Check(body.GetProperty("accessToken").GetString() == (boundInitially ? "authenticated" : "new"), "login validates the actual server-selected token");
                return Task.FromResult(AuthResponse(HttpStatusCode.NoContent));
            }));
            var login = await AuthSessionGate.CompleteLoginAsync(client,
                new AuthenticateResult { AccessToken = "authenticated", ClientToken = "client", SelectedProfile = boundInitially ? selected : null },
                selected, "fallback-client", "fixture");
            Check(login.AccessToken == (boundInitially ? "authenticated" : "new") && loginRefreshes == (boundInitially ? 0 : 1), "single-role and multi-role login use verified session completion");
        }
        using (var client = new YggdrasilClient(new AuthHandler((path, body, ct) => Task.FromResult(AuthResponse(HttpStatusCode.Forbidden)))))
            await AuthRejects(() => AuthSessionGate.CompleteLoginAsync(client,
                new AuthenticateResult { AccessToken = "invalid", ClientToken = "client", SelectedProfile = selected }, selected, "client", "fixture"),
                "invalid authenticated token cannot complete login");
    }
}
