using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArcadeRemoteExecute.Config;

namespace ArcadeRemoteExecute.Server;

public class UpdateServer
{
    private readonly ServerConfig _config;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private string _updatesFolder = "";
    private string _configFolder = "";

    public UpdateServer(ServerConfig config)
    {
        _config = config;
        _updatesFolder = Path.GetFullPath(string.IsNullOrWhiteSpace(config.UpdatesFolder) ? "./Updates" : config.UpdatesFolder);
        _configFolder = Path.GetFullPath(string.IsNullOrWhiteSpace(config.ConfigFolder) ? "./Config" : config.ConfigFolder);
    }

    public async Task RunAsync(CancellationToken cancel = default)
    {
        if (!Directory.Exists(_updatesFolder))
        {
            Directory.CreateDirectory(_updatesFolder);
            Console.WriteLine($"[Server] 更新目录已创建: {_updatesFolder}");
        }

        var host = string.IsNullOrWhiteSpace(_config.ListenHost) ? "+" : _config.ListenHost;
        var prefix = $"http://{host}:{_config.Port}/";
        _listener = new HttpListener();
        _listener.Prefixes.Add(prefix);
        _listener.Start();
        Console.WriteLine($"[Server] 监听 {prefix}，更新目录: {_updatesFolder}");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                var context = await _listener.GetContextAsync().WaitAsync(_cts.Token).ConfigureAwait(false);
                _ = HandleRequestAsync(context, _cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _listener?.Stop();
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancel)
    {
        var request = context.Request;
        var response = context.Response;
        try
        {
            var path = request.Url?.AbsolutePath.TrimStart('/') ?? "";
            if (request.HttpMethod == "GET")
            {
                if (path.Equals("manifest", StringComparison.OrdinalIgnoreCase) || path == "")
                {
                    await ServeManifestAsync(response, cancel).ConfigureAwait(false);
                    return;
                }
                if (path.StartsWith("download/", StringComparison.OrdinalIgnoreCase))
                {
                    var fileName = Uri.UnescapeDataString(path["download/".Length..].TrimStart('/'));
                    await ServeFileAsync(response, fileName, cancel).ConfigureAwait(false);
                    return;
                }
                if (path.Equals("config", StringComparison.OrdinalIgnoreCase))
                {
                    await ServeConfigAsync(response, cancel).ConfigureAwait(false);
                    return;
                }
                if (path.Equals("freeplay", StringComparison.OrdinalIgnoreCase))
                {
                    await ServeGetFreePlayAsync(response, cancel).ConfigureAwait(false);
                    return;
                }
            }
            if (request.HttpMethod == "POST" && path.Equals("freeplay", StringComparison.OrdinalIgnoreCase))
            {
                await ServePostFreePlayAsync(request, response, cancel).ConfigureAwait(false);
                return;
            }
            response.StatusCode = 404;
            response.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Server] 请求处理异常: {ex.Message}");
            try { response.StatusCode = 500; response.Close(); } catch { }
        }
    }

    private async Task ServeManifestAsync(HttpListenerResponse response, CancellationToken cancel)
    {
        var manifest = BuildManifest();
        response.ContentType = "application/json; charset=utf-8";
        response.AddHeader("Cache-Control", "no-cache");
        var json = JsonSerializer.Serialize(manifest, _jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes.AsMemory(0, bytes.Length), cancel).ConfigureAwait(false);
        response.Close();
    }

    private UpdateManifest BuildManifest()
    {
        var manifest = new UpdateManifest();
        if (!Directory.Exists(_updatesFolder))
            return manifest;

        foreach (var file in Directory.EnumerateFiles(_updatesFolder, "*.zip", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            var hash = ComputeFileHash(file);
            manifest.Files.Add(new UpdateEntry { Name = name, Hash = hash });
        }
        return manifest;
    }

    private static string ComputeFileHash(string filePath)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(filePath);
        var hash = sha.ComputeHash(fs);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task ServeFileAsync(HttpListenerResponse response, string fileName, CancellationToken cancel)
    {
        if (string.IsNullOrEmpty(fileName) || fileName.Contains(".."))
        {
            response.StatusCode = 400;
            response.Close();
            return;
        }
        var fullPath = Path.Combine(_updatesFolder, Path.GetFileName(fileName));
        if (!File.Exists(fullPath))
        {
            response.StatusCode = 404;
            response.Close();
            return;
        }
        response.ContentType = "application/zip";
        response.AddHeader("Content-Disposition", "attachment; filename=\"" + Uri.EscapeDataString(Path.GetFileName(fileName)) + "\"");
        var length = new FileInfo(fullPath).Length;
        response.ContentLength64 = length;
        await using var fs = File.OpenRead(fullPath);
        await fs.CopyToAsync(response.OutputStream, cancel).ConfigureAwait(false);
        response.Close();
    }

    private async Task ServeConfigAsync(HttpListenerResponse response, CancellationToken cancel)
    {
        var fileName = string.IsNullOrWhiteSpace(_config.ConfigFileName) ? "AquaMai.toml" : _config.ConfigFileName.Trim();
        var fullPath = Path.Combine(_configFolder, fileName);
        if (!File.Exists(fullPath))
        {
            response.StatusCode = 404;
            response.Close();
            return;
        }
        response.ContentType = "text/plain; charset=utf-8";
        response.AddHeader("Cache-Control", "no-cache");
        var length = new FileInfo(fullPath).Length;
        response.ContentLength64 = length;
        await using var fs = File.OpenRead(fullPath);
        await fs.CopyToAsync(response.OutputStream, cancel).ConfigureAwait(false);
        response.Close();
    }

    private string FreePlayFilePath => Path.Combine(_configFolder, "freeplay.json");

    private async Task ServeGetFreePlayAsync(HttpListenerResponse response, CancellationToken cancel)
    {
        var (freePlay, _) = ReadFreePlayFile();
        var json = JsonSerializer.Serialize(new { freePlay }, _jsonOptions);
        response.ContentType = "application/json; charset=utf-8";
        response.AddHeader("Cache-Control", "no-cache");
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes.AsMemory(0, bytes.Length), cancel).ConfigureAwait(false);
        response.Close();
    }

    private async Task ServePostFreePlayAsync(HttpListenerRequest request, HttpListenerResponse response, CancellationToken cancel)
    {
        bool? newValue = null;
        if (request.ContentLength64 > 0 && request.InputStream != null)
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            var body = await reader.ReadToEndAsync(cancel).ConfigureAwait(false);
            body = body.Trim();
            if (body.StartsWith('{'))
            {
                try
                {
                    var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("freePlay", out var fp))
                        newValue = fp.GetBoolean();
                }
                catch { }
            }
            else if (body.Equals("true", StringComparison.OrdinalIgnoreCase))
                newValue = true;
            else if (body.Equals("false", StringComparison.OrdinalIgnoreCase))
                newValue = false;
        }
        if (request.Url?.Query != null && !newValue.HasValue)
        {
            var query = request.Url.Query.TrimStart('?');
            foreach (var part in query.Split('&'))
            {
                var kv = part.Split('=', 2, StringSplitOptions.None);
                if (kv.Length == 2 && kv[0].Equals("freePlay", StringComparison.OrdinalIgnoreCase))
                {
                    var v = Uri.UnescapeDataString(kv[1]);
                    if (v.Equals("true", StringComparison.OrdinalIgnoreCase)) newValue = true;
                    if (v.Equals("false", StringComparison.OrdinalIgnoreCase)) newValue = false;
                    break;
                }
            }
        }
        if (!newValue.HasValue)
        {
            response.StatusCode = 400;
            var err = Encoding.UTF8.GetBytes("{\"error\":\"需要 freePlay: true 或 false\"}");
            response.ContentLength64 = err.Length;
            response.ContentType = "application/json; charset=utf-8";
            await response.OutputStream.WriteAsync(err.AsMemory(0, err.Length), cancel).ConfigureAwait(false);
            response.Close();
            return;
        }
        Directory.CreateDirectory(_configFolder);
        var obj = new { freePlay = newValue.Value };
        await File.WriteAllTextAsync(FreePlayFilePath, JsonSerializer.Serialize(obj, _jsonOptions), cancel).ConfigureAwait(false);
        response.ContentType = "application/json; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(obj, _jsonOptions));
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes.AsMemory(0, bytes.Length), cancel).ConfigureAwait(false);
        response.Close();
    }

    private (bool freePlay, bool exists) ReadFreePlayFile()
    {
        var path = FreePlayFilePath;
        if (!File.Exists(path))
            return (false, false);
        try
        {
            var json = File.ReadAllText(path);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("freePlay", out var fp))
                return (fp.GetBoolean(), true);
        }
        catch { }
        return (false, true);
    }

    public void Stop()
    {
        _cts?.Cancel();
    }
}
