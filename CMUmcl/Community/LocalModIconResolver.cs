// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace CMUmcl.Community;

// Resolve the exact installed file, never guess a project from a filename or display name.
internal sealed class LocalModIconResolver : IDisposable
{
    private readonly HttpClient _api;
    private readonly ResourceIconLoader _icons;
    private readonly SemaphoreSlim _slots=new(2,2);
    private readonly CancellationTokenSource _lifetime=new();
    private readonly ConcurrentDictionary<string,Lazy<Task<BitmapSource?>>> _cache=new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string,(long Size,long Modified,string Hash)> _hashes=new(StringComparer.OrdinalIgnoreCase);
    internal LocalModIconResolver():this(CMUmcl.Network.LauncherProxy.CreateHandler(false,false),CMUmcl.Network.LauncherProxy.CreateHandler(false,false)){}
    internal LocalModIconResolver(HttpMessageHandler api,HttpMessageHandler icons)
    {
        _api=CommunityHttp.CreateApiClient(api,h=>h==CommunityHosts.ModrinthApi,true);_icons=new ResourceIconLoader(icons);
    }
    internal async Task<BitmapSource?> LoadAsync(string path)
    {
        bool entered=false;
        try
        {
            var ct=_lifetime.Token;await _slots.WaitAsync(ct).ConfigureAwait(false);entered=true;
            string hash=await Task.Run(()=>Hash(path,ct),ct).ConfigureAwait(false);
            return await _cache.GetOrAdd(hash,h=>new Lazy<Task<BitmapSource?>>(()=>ResolveAsync(h,ct))).Value.ConfigureAwait(false);
        }
        catch{return null;}
        finally{if(entered)_slots.Release();}
    }
    private string Hash(string path,CancellationToken ct)
    {
        var info=new FileInfo(path);long length=info.Length,modified=info.LastWriteTimeUtc.Ticks;
        if(_hashes.TryGetValue(path,out var cached)&&cached.Size==length&&cached.Modified==modified)return cached.Hash;
        if((info.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked file");
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        using var sha=SHA1.Create();byte[] buffer=new byte[65536];int read;
        while((read=stream.Read(buffer,0,buffer.Length))>0){ct.ThrowIfCancellationRequested();sha.TransformBlock(buffer,0,read,buffer,0);}
        sha.TransformFinalBlock(Array.Empty<byte>(),0,0);
        info.Refresh();if(!info.Exists||info.Length!=length||info.LastWriteTimeUtc.Ticks!=modified)throw new IOException("File changed");
        string hash=BitConverter.ToString(sha.Hash!).Replace("-","").ToLowerInvariant();_hashes[path]=(length,modified,hash);return hash;
    }
    private async Task<BitmapSource?> ResolveAsync(string hash,CancellationToken ct)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(30));ct=deadline.Token;
        using var version=await _api.GetAsync("https://api.modrinth.com/v2/version_file/"+hash+"?algorithm=sha1",HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false);
        if(version.StatusCode==HttpStatusCode.NotFound)return null;version.EnsureSuccessStatusCode();
        var data=await CommunityHttp.ReadJsonAsync<JsonElement>(version,ResourceSourceId.Modrinth,ct).ConfigureAwait(false);
        if(!data.TryGetProperty("project_id",out var id)||id.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(id.GetString()))return null;
        using var project=await _api.GetAsync("https://api.modrinth.com/v2/project/"+Uri.EscapeDataString(id.GetString()!),HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false);project.EnsureSuccessStatusCode();
        var metadata=await CommunityHttp.ReadJsonAsync<JsonElement>(project,ResourceSourceId.Modrinth,ct).ConfigureAwait(false);
        if(!metadata.TryGetProperty("icon_url",out var url)||url.ValueKind!=JsonValueKind.String)return null;
        return await _icons.LoadAsync(url.GetString(),ct).ConfigureAwait(false);
    }
    public void Dispose(){_lifetime.Cancel();_api.Dispose();_icons.Dispose();}
}
