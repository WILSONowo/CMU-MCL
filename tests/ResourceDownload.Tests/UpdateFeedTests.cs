using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using CMUmcl.Update;

internal static class UpdateFeedTests
{
    public static async Task Run(Action<bool, string> check)
    {
        using var key = new ECDsaCng(256);
        string publicKey = Compat.ExportPem((ECDsaCng)key);
        UpdateSignature Sign(byte[] data) => new("ECDSA-P256-SHA256", "ieee-p1363-base64",
            Convert.ToBase64String(key.SignData(data, HashAlgorithmName.SHA256)));
        byte[] Envelope(UpdateRelease release)
        {
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(release, UpdateProtocol.JsonOptions);
            return JsonSerializer.SerializeToUtf8Bytes(
                new UpdateEnvelope(Convert.ToBase64String(payload), Sign(payload)), UpdateProtocol.JsonOptions);
        }

        var net48 = new UpdateRelease(2, "windows-x86_64", "9.0.0",
            "https://csumcl.mcapcs.cn/update/windows-x86_64/net48/9.0.0.zip",
            1234, new string('a', 64), Sign([1, 2, 3]), "net48 fixture");
        var legacy = new UpdateRelease(2, "windows-x86_64", "9.0.0",
            "https://csumcl.mcapcs.cn/update/windows-x86_64/CMUmcl-9.0.0-test.zip",
            1234, new string('a', 64), Sign([1, 2, 3]), "legacy fixture");

        var preferred = new Transport(request =>
            request.RequestUri!.AbsolutePath == "/update/windows-x86_64/net48/latest.json"
                ? Json(Envelope(net48))
                : Json(Envelope(legacy)));
        using (var client = new LauncherUpdateClient(new HttpClient(preferred), publicKey))
        {
            var result = await client.CheckAsync(default);
            check(result is { } r && r.Release.Version == "9.0.0" &&
                  r.Release.Url.Contains("/net48/", StringComparison.Ordinal),
                "net48 feed is preferred when both channels exist");
            check(preferred.Paths.Count == 1 &&
                  preferred.Paths[0] == "/update/windows-x86_64/net48/latest.json",
                "preferred feed does not request the legacy channel");
        }

        var fallback = new Transport(request =>
            request.RequestUri!.AbsolutePath == "/update/windows-x86_64/net48/latest.json"
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : Json(Envelope(legacy)));
        using (var client = new LauncherUpdateClient(new HttpClient(fallback), publicKey))
        {
            var result = await client.CheckAsync(default);
            check(result is { } r && r.Release.Version == "9.0.0" &&
                  r.Release.Url.Contains("/CMUmcl-9.0.0-test.zip", StringComparison.Ordinal),
                "legacy flat feed remains readable when net48 feed is absent");
            check(fallback.Paths.SequenceEqual(new[]
                  {
                      "/update/windows-x86_64/net48/latest.json",
                      "/update/windows-x86_64/latest.json",
                  }),
                "legacy fallback is used only after the net48 404");
        }

        var missing = new Transport(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using (var client = new LauncherUpdateClient(new HttpClient(missing), publicKey))
        {
            bool rejected = false;
            try { await client.CheckAsync(default); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "missing net48 and legacy feeds fail closed");
        }

        check(IsValid("https://csumcl.mcapcs.cn/update/windows-x86_64/net48/9.0.0.zip") &&
              IsValid("https://csumcl.mcapcs.cn/update/windows-x86_64/CMUmcl-0.2.0-test.zip"),
            "net48 and legacy archive URLs are both allowlisted");
        check(!IsValid("https://csumcl.mcapcs.cn/update/windows-x86_64/other/9.0.0.zip") &&
              !IsValid("https://csumcl.mcapcs.cn/update/windows-x86_64/net48/nested/9.0.0.zip") &&
              !IsValid("https://evil.example/update/windows-x86_64/net48/9.0.0.zip") &&
              !IsValid("https://csumcl.mcapcs.cn/update/windows-x86_64/net48/9.0.0.zip?x=1"),
            "arbitrary update paths and hosts stay rejected");
    }

    private static bool IsValid(string url)
    {
        try
        {
            UpdateProtocol.ValidateUrl(new Uri(url, UriKind.Absolute));
            return true;
        }
        catch (InvalidDataException) { return false; }
    }

    private static HttpResponseMessage Json(byte[] bytes) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(bytes),
    };

    private sealed class Transport(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public List<string> Paths { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(reply(request));
        }
    }
}
