// SPDX-License-Identifier: GPL-3.0-only
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CMUmcl.Content;

internal sealed class SchematicPreviewSession : IDisposable
{
    internal const string ComponentId = "schematic-preview";
    internal static string? RootOverride { get; set; }
    internal static string ComponentRoot => RootOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CMUmcl", "components", ComponentId);
    internal int WorkerId => _process.Id;
    internal string TemporaryDirectory => _temporary;
    private readonly string _temporary;
    private readonly Process _process;
    private PreviewProcessJob? _job;
    private readonly SemaphoreSlim _commands = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private TaskCompletionSource<bool>? _pending;
    private readonly TaskCompletionSource<bool> _cleanup = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task CleanupTask => _cleanup.Task;
    private int _disposeState;
    private bool Disposed => Volatile.Read(ref _disposeState) != 0;
    internal string Summary { get; private set; } = "";
    internal event Action<string>? Failed;
    internal IntPtr NativeWindow { get; private set; }
    internal long PresentedFrames { get; private set; }
    internal long NativeInputEvents { get; private set; }
    internal int SurfaceWidth { get; private set; }
    internal int SurfaceHeight { get; private set; }
    internal double DpiScale { get; private set; }
    internal bool SurfaceVisible { get; private set; }

    private SchematicPreviewSession(string executable, string input, string pack, IntPtr parent, int? meshThreads)
    {
        _temporary = Path.Combine(Path.GetTempPath(), "CMUmcl-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temporary);
        _process = new Process();
        try
        {
            CopyInput(input, Path.Combine(_temporary, "input.litematic"), 64L * 1024 * 1024);
            CopyInput(pack, Path.Combine(_temporary, "pack.zip"), 256L * 1024 * 1024);
            string request = Path.Combine(_temporary, "request.json");
            var payload = new Dictionary<string,object> { ["input"]=Path.Combine(_temporary,"input.litematic"), ["pack"]=Path.Combine(_temporary,"pack.zip"), ["parent"]=parent.ToInt64(), ["parent_process"]=Process.GetCurrentProcess().Id };
            if(meshThreads.HasValue) payload["threads"]=meshThreads.Value;
            File.WriteAllText(request,JsonSerializer.Serialize(payload),new UTF8Encoding(false));
            var start = new ProcessStartInfo(executable, "\"" + request + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = _temporary, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
            start.EnvironmentVariables.Clear();
            foreach (string key in new[] { "SystemRoot", "WINDIR", "TEMP", "TMP", "USERPROFILE", "LOCALAPPDATA" })
            { string? value = Environment.GetEnvironmentVariable(key); if (value != null) start.EnvironmentVariables[key] = value; }
            _process.StartInfo = start;
            _pending = NewPending();
            _process.Start();
            _job = new PreviewProcessJob(_process.Handle);
            _ = ReadEvents();
            _ = _process.StandardError.ReadToEndAsync();
        }
        catch { Dispose(); throw; }
    }

    internal static async Task<SchematicPreviewSession> OpenAsync(string input, string pack, IntPtr parent, CancellationToken cancellation, int timeoutMilliseconds = 100000, int? meshThreads = null)
    {
        string executable = ResolveInstalled();
        cancellation.ThrowIfCancellationRequested();
        var session = await Task.Run(() => new SchematicPreviewSession(executable, input, pack, parent, meshThreads));
        try { await session.Wait(session._pending!.Task, cancellation, timeoutMilliseconds); cancellation.ThrowIfCancellationRequested(); return session; }
        catch { session.Dispose(); await session.CleanupTask; throw; }
    }

    private static TaskCompletionSource<bool> NewPending() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private async Task ReadEvents()
    {
        try
        {
            string? line;
            while ((line = await _process.StandardOutput.ReadLineAsync()) != null)
            {
                if (line.Length > 32768) throw new IOException("预览进程返回的数据过长。");
                using var message = JsonDocument.Parse(line);
                string kind = message.RootElement.GetProperty("event").GetString() ?? "";
                if (kind == "error") throw new IOException(message.RootElement.GetProperty("message").GetString());
                if (kind == "ready")
                {
                    ReadSurface(message.RootElement);
                    Summary = $"{message.RootElement.GetProperty("blocks").GetInt64():N0} 个方块 · {message.RootElement.GetProperty("triangles").GetInt64():N0} 个三角形";
                    _pending?.TrySetResult(true);
                }
                else if (kind == "ack") { ReadSurface(message.RootElement); _pending?.TrySetResult(true); }
            }
            if (!Disposed) throw new IOException("预览进程已退出，请重新打开预览。");
        }
        catch (Exception ex) { _pending?.TrySetException(ex); _lifetime.Cancel(); if (!Disposed) Failed?.Invoke(ex.Message); }
    }

    private async Task Wait(Task task, CancellationToken cancellation, int milliseconds)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token);
        var delay = Task.Delay(milliseconds, linked.Token);
        Task completed = await Task.WhenAny(task, delay);
        if (completed == task) { linked.Cancel(); await task; return; }
        cancellation.ThrowIfCancellationRequested();
        if (_lifetime.IsCancellationRequested) throw new IOException("预览进程已停止。");
        throw new TimeoutException("预览超时，请尝试较小的原理图或资源包。");
    }

    private void ReadSurface(JsonElement message)
    {
        if (message.GetProperty("mode").GetString() != "native-dx12") throw new IOException("扩展未提供原生 GPU 预览。");
        NativeWindow = new IntPtr(message.GetProperty("hwnd").GetInt64());
        PresentedFrames = message.GetProperty("frames").GetInt64(); NativeInputEvents = message.GetProperty("inputs").GetInt64();
        SurfaceWidth = message.GetProperty("width").GetInt32(); SurfaceHeight = message.GetProperty("height").GetInt32();
        DpiScale = message.GetProperty("scale").GetDouble(); SurfaceVisible = message.GetProperty("visible").GetBoolean();
    }

    internal async Task CommandAsync(object command, CancellationToken cancellation)
    {
        await _commands.WaitAsync(cancellation);
        try
        {
            if (Disposed) throw new ObjectDisposedException(nameof(SchematicPreviewSession));
            _pending = NewPending();
            await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(command));
            await _process.StandardInput.FlushAsync();
            await Wait(_pending.Task, cancellation, 25000);
        }
        catch { Dispose(); await CleanupTask; throw; }
        finally { _commands.Release(); }
    }

    internal static void CheckRegularPath(string path)
    {
        for (string? part = Path.GetFullPath(path); part != null; part = Path.GetDirectoryName(part))
        {
            try { if ((File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0) throw new IOException("预览不读取链接文件或链接目录。"); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
    private static void CopyInput(string source, string destination, long limit)
    {
        CheckRegularPath(source);
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length == 0 || input.Length > limit) throw new IOException("文件为空或超过预览大小限制。");
        using var output = File.Create(destination); input.CopyTo(output);
    }
    internal static string Hash(string file)
    { using var sha = SHA256.Create(); using var input = File.OpenRead(file); return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant(); }

    internal static string ResolveInstalled()
    {
        string current = Path.Combine(ComponentRoot, "current.json"); CheckRegularPath(current);
        _ = InstalledVersion() ?? throw new FileNotFoundException("请先安装预览扩展。");
        using var active = JsonDocument.Parse(File.ReadAllText(current));
        string version = active.RootElement.GetProperty("version").GetString() ?? "";
        if (!Version.TryParse(version, out var parsed) || parsed.ToString() != version) throw new IOException("扩展版本记录无效。");
        string folder = Path.Combine(ComponentRoot, "versions", version); CheckRegularPath(folder);
        if (ValidateFolder(folder) != version) throw new IOException("扩展目录与清单版本不一致。");
        if(active.RootElement.TryGetProperty("source",out var source) && source.GetString()=="online-signed") {
            string receipt=Path.Combine(folder,"signed-feed.json"); CheckRegularPath(receipt);
            if(new FileInfo(receipt).Length>128*1024)throw new IOException("扩展签名记录过大。");
            var release=SchematicPreviewProtocol.Read(File.ReadAllBytes(receipt),VerificationKeyOverride??SchematicPreviewProtocol.PublicKey);
            if(release.Version!=version || !Hash(Path.Combine(folder,"SchematicPreviewWorker.exe")).Equals(release.EntrySha256,StringComparison.OrdinalIgnoreCase))throw new CryptographicException("已安装扩展与签名记录不符。");
        }
        return Path.Combine(folder, "SchematicPreviewWorker.exe");
    }

    internal static string ValidateFolder(string folder)
    {
        string manifest = Path.Combine(folder, "component.json"); CheckRegularPath(manifest);
        if (new FileInfo(manifest).Length > 32768) throw new IOException("组件清单过大。");
        using var json = JsonDocument.Parse(File.ReadAllText(manifest)); var root = json.RootElement;
        if (root.GetProperty("component").GetString() != ComponentId || root.GetProperty("apiVersion").GetInt32() != 2) throw new IOException("此预览需要原生 GPU 扩展（接口版本 2），请安装新版扩展。");
        string version = root.GetProperty("version").GetString() ?? "";
        if (!Version.TryParse(version, out var parsed) || parsed.ToString() != version) throw new IOException("扩展版本无效。");
        _ = CMUmcl.Update.UpdateProtocol.ParseVersion(version);
        string executable = Path.Combine(folder, "SchematicPreviewWorker.exe"); CheckRegularPath(executable);
        if (new FileInfo(executable).Length > 128L * 1024 * 1024 || !Hash(executable).Equals(root.GetProperty("entrySha256").GetString(), StringComparison.OrdinalIgnoreCase)) throw new IOException("扩展程序校验失败。");
        return version;
    }

    internal static void InstallLocal(string manifest)
    {
        using var installLock = AcquireInstallLock();
        string source = Path.GetDirectoryName(Path.GetFullPath(manifest))!;
        string version = ValidateFolder(source);
        CheckRegularPath(ComponentRoot); Directory.CreateDirectory(ComponentRoot);
        string staging = Path.Combine(ComponentRoot, "staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            foreach (string name in new[] { "component.json", "SchematicPreviewWorker.exe", "LICENSE", "NOTICE", "SOURCE.txt" })
                CopyInput(Path.Combine(source, name), Path.Combine(staging, name), 128L * 1024 * 1024);
            ValidateFolder(staging);
            CommitVersion(staging, version, "local");
        }
        finally { RemoveOwnedDirectory(staging, ComponentRoot, "staging-"); }
    }

    internal static string? VerificationKeyOverride { get; set; }
    internal static string? InstalledVersion()
    {
        string current = Path.Combine(ComponentRoot,"current.json"); CheckRegularPath(current);
        if(!File.Exists(current)) return null;
        if(new FileInfo(current).Length>32768) throw new IOException("扩展状态文件过大。");
        using var json=JsonDocument.Parse(File.ReadAllText(current));
        string version=json.RootElement.GetProperty("version").GetString()??"";
        if(!Version.TryParse(version,out var parsed)||parsed.ToString()!=version) throw new IOException("扩展版本记录无效。");
        _ = CMUmcl.Update.UpdateProtocol.ParseVersion(version);
        return version;
    }
    internal static FileStream AcquireInstallLock()
    {
        CheckRegularPath(ComponentRoot); Directory.CreateDirectory(ComponentRoot);
        string path=Path.Combine(ComponentRoot,"install.lock"); CheckRegularPath(path);
        try { return new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None); }
        catch(IOException) { throw new IOException("另一个扩展安装正在进行，请稍后重试。"); }
    }
    internal static void CommitVersion(string staging,string version,string source)
    {
        CheckRegularPath(staging);
        string versions=Path.Combine(ComponentRoot,"versions");CheckRegularPath(versions);Directory.CreateDirectory(versions);
        string target=Path.Combine(versions,version);CheckRegularPath(target);
        if(Directory.Exists(target)) {
            ValidateFolder(target);
            if(Hash(Path.Combine(target,"SchematicPreviewWorker.exe"))!=Hash(Path.Combine(staging,"SchematicPreviewWorker.exe")))throw new IOException("同版本扩展内容不同，请提升扩展版本。");
            if(source=="online-signed") { CheckRegularPath(Path.Combine(target,"signed-feed.json")); File.Copy(Path.Combine(staging,"signed-feed.json"),Path.Combine(target,"signed-feed.json"),true); }
        } else Directory.Move(staging,target);
        string next=Path.Combine(ComponentRoot,"current-"+Guid.NewGuid().ToString("N")+".json");
        string current=Path.Combine(ComponentRoot,"current.json");CheckRegularPath(current);
        try {
            File.WriteAllText(next,JsonSerializer.Serialize(new {version,source}));
            if(File.Exists(current))File.Replace(next,current,null);else File.Move(next,current);
        } finally { if(File.Exists(next))File.Delete(next); }
    }

    internal static void RemoveOwnedDirectory(string path, string parent, string prefix)
    {
        string full = Path.GetFullPath(path);
        if (!string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith(prefix, StringComparison.Ordinal)) throw new IOException("清理目录越界。");
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (!Directory.Exists(full)) return;
            CheckRegularPath(full);
            try { Directory.Delete(full, true); return; }
            catch (IOException) when (attempt < 9) { Thread.Sleep(50); }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0) return; _lifetime.Cancel(); _pending?.TrySetCanceled();
        _job?.Dispose();
        _ = Task.Run(() => {
            try {
                try { if (!_process.HasExited) { _process.Kill(); _process.WaitForExit(2000); } } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
                _process.Dispose();
                RemoveOwnedDirectory(_temporary, Path.GetTempPath(), "CMUmcl-preview-");
                _cleanup.TrySetResult(true);
            }
            catch (Exception ex) { _cleanup.TrySetException(ex); }
        });
    }
}
