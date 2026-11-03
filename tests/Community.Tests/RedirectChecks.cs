// SPDX-License-Identifier: GPL-3.0-only
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using CMUmcl.Community;

internal static partial class Program
{
    private sealed class RedirectFixture(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public readonly List<HttpRequestMessage> Requests = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(reply(request));
        }
    }

    private static HttpResponseMessage Redirect(HttpStatusCode status, string? location)
    {
        var resp = new HttpResponseMessage(status);
        if (location != null) resp.Headers.Location = new Uri(location);
        return resp;
    }

    private static readonly Func<string, bool> AllowApi = host =>
        string.Equals(host, CommunityHosts.ModrinthApi, StringComparison.OrdinalIgnoreCase);

    private static void RedirectChecks()
    {
        // 1. Plain 200 passes through without redirect.
        {
            var fix = new RedirectFixture(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1, 2, 3 }) });
            using var h = new SafeRedirectHandler(fix, AllowApi, sameHostOnly: false);
            using var client = new HttpClient(h);
            var resp = client.GetAsync("https://api.modrinth.com/v2/x").GetAwaiter().GetResult();
            Check(resp.StatusCode == HttpStatusCode.OK && fix.Requests.Count == 1, "redirect: plain 200 passes through");
        }

        // 2. Redirect loop is rejected.
        {
            var fix = new RedirectFixture(req => req.RequestUri!.AbsolutePath == "/a"
                ? Redirect(HttpStatusCode.Found, "https://api.modrinth.com/b")
                : Redirect(HttpStatusCode.Found, "https://api.modrinth.com/a"));
            using var h = new SafeRedirectHandler(fix, AllowApi, sameHostOnly: false);
            using var client = new HttpClient(h);
            bool rejected = false;
            try { client.GetAsync("https://api.modrinth.com/a").GetAwaiter().GetResult(); }
            catch (Exception) { rejected = true; }
            Check(rejected, "redirect: loop rejected");
        }

        // 3. More than 5 hops rejected (monotonically increasing unique paths, so the
        // loop detector is never triggered and the MaxRedirects branch must be hit).
        {
            int hop = 1; // initial request is /1, so the first redirect target is /2
            var fix = new RedirectFixture(_ => Redirect(HttpStatusCode.Found, "https://api.modrinth.com/" + (++hop)));
            using var h = new SafeRedirectHandler(fix, AllowApi, sameHostOnly: false);
            using var client = new HttpClient(h);
            bool rejected = false;
            string? message = null;
            try { client.GetAsync("https://api.modrinth.com/1").GetAwaiter().GetResult(); }
            catch (Exception ex) { rejected = true; message = ex.Message; }
            Check(rejected, "redirect: more than 5 hops rejected");
            Check(fix.Requests.Count >= 6, "redirect: max-hop rejected after 6 unique hops (not a loop)");
            Check(message != null && message.Contains("重定向次数过多"), "redirect: max-hop branch (not loop)");
        }

        // 4. HTTPS→HTTP downgrade rejected.
        {
            var fix = new RedirectFixture(_ => Redirect(HttpStatusCode.Moved, "http://api.modrinth.com/x"));
            using var h = new SafeRedirectHandler(fix, AllowApi, sameHostOnly: false);
            using var client = new HttpClient(h);
            bool rejected = false;
            try { client.GetAsync("https://api.modrinth.com/a").GetAwaiter().GetResult(); }
            catch (Exception) { rejected = true; }
            Check(rejected, "redirect: HTTPS to HTTP rejected");
        }

        // 5. Non-allowlisted host rejected before following.
        {
            var fix = new RedirectFixture(_ => Redirect(HttpStatusCode.Found, "https://evil.example.com/x"));
            using var h = new SafeRedirectHandler(fix, AllowApi, sameHostOnly: false);
            using var client = new HttpClient(h);
            bool rejected = false;
            try { client.GetAsync("https://api.modrinth.com/a").GetAwaiter().GetResult(); }
            catch (Exception) { rejected = true; }
            Check(rejected && fix.Requests.Count == 1, "redirect: non-allowlisted host rejected before follow");
        }

        // 6. sameHostOnly rejects cross-host even to an allowlisted host.
        {
            var fix = new RedirectFixture(_ => Redirect(HttpStatusCode.Found, "https://cdn.modrinth.com/x"));
            using var h = new SafeRedirectHandler(fix,
                host => CommunityHosts.ModrinthCdn.Contains(host) || string.Equals(host, CommunityHosts.ModrinthApi, StringComparison.OrdinalIgnoreCase),
                sameHostOnly: true);
            using var client = new HttpClient(h);
            bool rejected = false;
            try { client.GetAsync("https://api.modrinth.com/a").GetAwaiter().GetResult(); }
            catch (Exception) { rejected = true; }
            Check(rejected, "redirect: sameHostOnly rejects cross-host");
        }

        // 7. 307/308 preserve Range + If-Range on the followed hop.
        {
            foreach (int code in new[] { 307, 308 })
            {
                var fix = new RedirectFixture(req => req.RequestUri!.AbsolutePath == "/a"
                    ? Redirect((HttpStatusCode)code, "https://api.modrinth.com/b")
                    : new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(new byte[0]) });
                using var h = new SafeRedirectHandler(fix, AllowApi, sameHostOnly: false);
                using var client = new HttpClient(h);
                var req = new HttpRequestMessage(HttpMethod.Get, "https://api.modrinth.com/a");
                req.Headers.Range = new RangeHeaderValue(5, null);
                req.Headers.IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue("\"etag1\""));
                client.SendAsync(req).GetAwaiter().GetResult();
                var second = fix.Requests[1];
                Check(second.Headers.Range != null && second.Headers.IfRange != null, "redirect: " + code + " preserves Range/If-Range");
            }
        }

        // 8. 303 switches to GET and drops body.
        {
            var fix = new RedirectFixture(req => req.RequestUri!.AbsolutePath == "/a"
                ? Redirect(HttpStatusCode.SeeOther, "https://api.modrinth.com/b")
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[0]) });
            using var h = new SafeRedirectHandler(fix, AllowApi, sameHostOnly: false);
            using var client = new HttpClient(h);
            var req = new HttpRequestMessage(HttpMethod.Post, "https://api.modrinth.com/a") { Content = new ByteArrayContent(new byte[] { 1, 2, 3 }) };
            client.SendAsync(req).GetAwaiter().GetResult();
            var second = fix.Requests[1];
            Check(second.Method == HttpMethod.Get && second.Content == null, "redirect: 303 switches to GET and drops body");
        }

        // 9. Cookie never present on any request.
        {
            var fix = new RedirectFixture(req => req.RequestUri!.AbsolutePath == "/a"
                ? Redirect(HttpStatusCode.Found, "https://api.modrinth.com/b")
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[0]) });
            using var h = new SafeRedirectHandler(fix, AllowApi, sameHostOnly: false);
            using var client = new HttpClient(h);
            client.GetAsync("https://api.modrinth.com/a").GetAwaiter().GetResult();
            foreach (var r in fix.Requests)
                Check(!r.Headers.Contains("Cookie") && !r.Headers.Contains("Set-Cookie"), "redirect: cookie absent");
        }
    }
}
