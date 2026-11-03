using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using CMUmcl.Content;

namespace CMUmcl.Network;

public static class LauncherProxy
{
    // Capture the OS/environment proxy before installing the process-local fallback.
    private static readonly IWebProxy SystemProxy = WebRequest.DefaultWebProxy;
    private static readonly IWebProxy DynamicProxy = new FollowingProxy();
    public static void Initialize() => WebRequest.DefaultWebProxy = DynamicProxy;
    public static HttpMessageHandler CreateHandler(bool allowAutoRedirect = true, bool useCookies = true,
        DecompressionMethods decompression = DecompressionMethods.GZip | DecompressionMethods.Deflate)
        => new ProxyRoutingHandler(allowAutoRedirect, useCookies, decompression);

    internal sealed record Configuration(LauncherProxyMode Mode, string Host, int Port, bool Authentication, string Username, string PasswordProtected);
    internal static Configuration Snapshot()
    {
        var s = SettingsStore.Current;
        // Inactive saved fields do not force replacement of live connection pools.
        return s.ProxyMode is LauncherProxyMode.System or LauncherProxyMode.Direct
            ? new(s.ProxyMode, "", 0, false, "", "")
            : new(s.ProxyMode, s.ProxyHost, s.ProxyPort, s.ProxyAuthentication,
                s.ProxyAuthentication ? s.ProxyUsername : "", s.ProxyAuthentication ? s.ProxyPasswordProtected : "");
    }
    public static bool TryAddress(LauncherProxyMode mode, string host, int port, out Uri? address)
    {
        address = null;
        if (mode is not (LauncherProxyMode.Http or LauncherProxyMode.Socks5) || port is < 1 or > 65535) return false;
        host = host.Trim();
        if (host.StartsWith('[') && host.EndsWith(']')) host = host.Substring(1,host.Length-2);
        if (Uri.CheckHostName(host) == UriHostNameType.Unknown || host.IndexOfAny(['/', '\\', '@', '?', '#']) >= 0) return false;
        try { address = new UriBuilder(mode == LauncherProxyMode.Http ? "http" : "socks5", host, port).Uri; return true; }
        catch (UriFormatException) { return false; }
    }
    internal static IWebProxy GetProxy(Configuration c)
    {
        if (c.Mode == LauncherProxyMode.System) return SystemProxy;
        if (c.Mode == LauncherProxyMode.Direct) return new WebProxy();
        // Fail closed on broken persisted custom settings; never silently use a direct route.
        if (!TryAddress(c.Mode, c.Host, c.Port, out var address)) throw new HttpRequestException("代理主机或端口无效，请检查代理设置。");
        if(c.Mode==LauncherProxyMode.Socks5) return SocksBridge(c);
        var proxy = new WebProxy(address!);
        if (c.Authentication) proxy.Credentials = new NetworkCredential(c.Username, Unprotect(c.PasswordProtected));
        return proxy;
    }
    private static readonly object BridgesGate=new();
    private static readonly System.Collections.Generic.Dictionary<Configuration,MihaZupan.HttpToSocks5Proxy> Bridges=new();
    private static IWebProxy SocksBridge(Configuration c)
    {
        lock(BridgesGate)
        {
            if(!Bridges.TryGetValue(c,out var bridge))
            {
                bridge=c.Authentication ? new MihaZupan.HttpToSocks5Proxy(c.Host,c.Port,c.Username,Unprotect(c.PasswordProtected)) : new MihaZupan.HttpToSocks5Proxy(c.Host,c.Port);
                Bridges.Add(c,bridge);
            }
            return bridge;
        }
    }
    public static void Shutdown() { lock(BridgesGate) { foreach(var b in Bridges.Values)b.StopInternalServer();Bridges.Clear(); } }
    public static string Protect(string password) => password.Length == 0 ? "" : Convert.ToBase64String(
        ProtectedData.Protect(Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes("CMUmcl proxy"), DataProtectionScope.CurrentUser));
    public static string Unprotect(string encrypted)
    {
        if (encrypted.Length == 0) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(encrypted), Encoding.UTF8.GetBytes("CMUmcl proxy"), DataProtectionScope.CurrentUser)); }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        { throw new HttpRequestException("无法读取代理密码，请重新输入。", ex); }
    }

    // For third-party clients that do not expose handler injection. Never modifies Windows proxy settings.
    private sealed class FollowingProxy : IWebProxy, ICredentials
    {
        public ICredentials? Credentials { get => this; set { } }
        public Uri? GetProxy(Uri destination) => LauncherProxy.GetProxy(Snapshot()).GetProxy(destination);
        public bool IsBypassed(Uri host) => LauncherProxy.GetProxy(Snapshot()).IsBypassed(host);
        public NetworkCredential? GetCredential(Uri uri, string authType)
        {
            var config = Snapshot();
            if (config.Mode == LauncherProxyMode.Direct) return null;
            if (config.Mode == LauncherProxyMode.System) return SystemProxy.Credentials?.GetCredential(uri, authType);
            if (!TryAddress(config.Mode, config.Host, config.Port, out var address) || uri.Scheme != address!.Scheme || uri.Host != address.Host || uri.Port != address.Port) return null;
            return LauncherProxy.GetProxy(config).Credentials?.GetCredential(uri, authType);
        }
    }
}
