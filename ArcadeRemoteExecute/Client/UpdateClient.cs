using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArcadeRemoteExecute.Config;
using ArcadeRemoteExecute.Server;

namespace ArcadeRemoteExecute.Client;

/// <summary>
/// 向云端请求清单，按 hash 下载变更的 zip，解压到“该 zip 名称”的文件夹下
/// </summary>
public class UpdateClient
{
    private readonly ClientConfig _config;
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private string _targetDir = "";

    public UpdateClient(ClientConfig config)
    {
        _config = config;
        _targetDir = string.IsNullOrWhiteSpace(config.UpdateTargetDir)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(config.UpdateTargetDir);
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _http.DefaultRequestHeaders.Add("User-Agent", "ArcadeRemoteExecute/1.0");
    }

    public async Task CheckAndApplyUpdatesAsync(CancellationToken cancel = default)
    {
        var baseUrl = _config.ServerUrl.TrimEnd('/');
        await DownloadAndApplyConfigAsync(baseUrl, cancel).ConfigureAwait(false);

        var manifestUrl = $"{baseUrl}/manifest";
        UpdateManifest? remoteManifest;
        try
        {
            var json = await _http.GetStringAsync(manifestUrl, cancel).ConfigureAwait(false);
            remoteManifest = JsonSerializer.Deserialize<UpdateManifest>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[更新] 获取清单失败: {ex.Message}");
            return;
        }

        // 云端无 zip 时只跳过更新，绝不删除或清理本地已有文件/文件夹
        if (remoteManifest?.Files == null || remoteManifest.Files.Count == 0)
        {
            Console.WriteLine("[更新] 云端无更新包");
            return;
        }

        // 仅处理清单中的项：需要则下载并解压；从不根据「本地有而清单没有」删除任何内容
        foreach (var entry in remoteManifest.Files)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
                continue;
            var needDownload = await NeedDownloadAsync(entry.Name, entry.Hash, cancel).ConfigureAwait(false);
            if (!needDownload)
                continue;
            var zipPath = Path.Combine(_targetDir, Path.GetFileName(entry.Name));
            try
            {
                await DownloadZipAsync(baseUrl, entry.Name, zipPath, cancel).ConfigureAwait(false);
                ExtractToFolderNamedAfterZip(zipPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[更新] 下载或解压失败 {entry.Name}: {ex.Message}");
            }
        }
    }

    private async Task<bool> NeedDownloadAsync(string fileName, string remoteHash, CancellationToken cancel)
    {
        var localPath = Path.Combine(_targetDir, Path.GetFileName(fileName));
        if (!File.Exists(localPath))
            return true;
        var localHash = ComputeFileHash(localPath);
        return !string.Equals(localHash, remoteHash, StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeFileHash(string filePath)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(filePath);
        var hash = sha.ComputeHash(fs);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task DownloadZipAsync(string baseUrl, string fileName, string zipPath, CancellationToken cancel)
    {
        var url = $"{baseUrl}/download/{Uri.EscapeDataString(fileName)}";
        await using var stream = await _http.GetStreamAsync(url, cancel).ConfigureAwait(false);
        await using var fs = File.Create(zipPath);
        await stream.CopyToAsync(fs, cancel).ConfigureAwait(false);
        Console.WriteLine($"[更新] 已下载: {fileName}");
    }

    /// <summary>
    /// 解压到“该 zip 名称”的文件夹下（不含 .zip 的文件夹名）
    /// </summary>
    private void ExtractToFolderNamedAfterZip(string zipPath)
    {
        var dirName = Path.GetFileNameWithoutExtension(zipPath);
        if (string.IsNullOrEmpty(dirName))
            dirName = Path.GetFileName(zipPath);
        var extractDir = Path.Combine(Path.GetDirectoryName(zipPath)!, dirName);
        Directory.CreateDirectory(extractDir);
        ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);
        Console.WriteLine($"[更新] 已解压到: {extractDir}");
    }

    /// <summary>
    /// 从云端获取 freeplay 并写回本地配置；若 freeplay 变更则写文件、关闭游戏并由守护重启。配置内容优先用云端 /config，失败则用本地已有文件。
    /// </summary>
    private async Task DownloadAndApplyConfigAsync(string baseUrl, CancellationToken cancel)
    {
        var localPath = _config.LocalConfigPath?.Trim();
        if (string.IsNullOrEmpty(localPath))
            return;

        bool? freePlayToApply;
        try
        {
            freePlayToApply = await FetchRemoteFreePlayAsync(baseUrl, cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[更新] 获取 FreePlay 状态失败: {ex.Message}");
            return;
        }

        if (!freePlayToApply.HasValue)
            freePlayToApply = _config.OverrideFreePlay;
        if (!freePlayToApply.HasValue)
            return;

        string? content = null;
        try
        {
            var configUrl = $"{baseUrl}/config";
            content = await _http.GetStringAsync(configUrl, cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[更新] 下载云端配置失败: {ex.Message}，将使用本地配置文件更新 FreePlay");
        }

        if (string.IsNullOrEmpty(content) && File.Exists(localPath))
        {
            try
            {
                content = await File.ReadAllTextAsync(localPath, cancel).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[更新] 读取本地配置失败: {ex.Message}");
                return;
            }
        }

        if (string.IsNullOrEmpty(content))
        {
            Console.WriteLine("[更新] 无配置内容可写（云端无配置且本地无文件），跳过 FreePlay 更新");
            return;
        }

        try
        {
            content = ApplyFreePlayOverride(content, freePlayToApply.Value);
            var dir = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(localPath, content, cancel).ConfigureAwait(false);
            Console.WriteLine($"[更新] 已写入配置到: {localPath}，IsFreePlay = {freePlayToApply.Value}");
            TryApplyFreePlayAndRestartGame(freePlayToApply.Value, localPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[更新] 写入配置失败: {ex.Message}");
        }
    }

    private async Task<bool?> FetchRemoteFreePlayAsync(string baseUrl, CancellationToken cancel)
    {
        try
        {
            var json = await _http.GetStringAsync($"{baseUrl}/freeplay", cancel).ConfigureAwait(false);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("freePlay", out var fp))
                return fp.GetBoolean();
        }
        catch { }
        return null;
    }

    private void TryApplyFreePlayAndRestartGame(bool appliedFreePlay, string localConfigPath)
    {
        var lastAppliedPath = Path.Combine(Path.GetDirectoryName(localConfigPath) ?? "", ".last_freeplay");
        var lastApplied = ReadLastAppliedFreePlay(lastAppliedPath);
        if (lastApplied.HasValue && lastApplied.Value == appliedFreePlay)
            return;

        try
        {
            KillTargetProcess();
            Console.WriteLine($"[更新] FreePlay 已改为 {appliedFreePlay}，已关闭游戏，将由守护进程重新启动");
            File.WriteAllText(lastAppliedPath, appliedFreePlay ? "True" : "False");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[更新] 关闭游戏或保存状态失败: {ex.Message}");
        }
    }

    private static bool? ReadLastAppliedFreePlay(string path)
    {
        if (!File.Exists(path))
            return null;
        var s = File.ReadAllText(path).Trim();
        if (s.Equals("True", StringComparison.OrdinalIgnoreCase)) return true;
        if (s.Equals("False", StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }

    private void KillTargetProcess()
    {
        var name = _config.TargetProcess?.Trim() ?? "";
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        if (string.IsNullOrEmpty(name))
            return;
        foreach (var p in Process.GetProcessesByName(name))
        {
            try
            {
                p.Kill(entireProcessTree: true);
            }
            finally
            {
                p.Dispose();
            }
        }
    }

    /// <summary>
    /// 将 TOML 内容中的 IsFreePlay 改为指定值（支持被注释的行，覆盖时会取消注释）
    /// </summary>
    private static string ApplyFreePlayOverride(string tomlContent, bool isFreePlay)
    {
        var valueStr = isFreePlay ? "true" : "false";
        // 匹配可选 # 与空白 + IsFreePlay = true/false，统一改为未注释的 IsFreePlay = value
        var pattern = @"#?\s*IsFreePlay\s*=\s*(?:true|false)";
        return Regex.Replace(tomlContent, pattern, $"IsFreePlay = {valueStr}", RegexOptions.IgnoreCase);
    }
}
