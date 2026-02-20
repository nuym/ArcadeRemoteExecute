using System.Diagnostics;
using ArcadeRemoteExecute.Config;

namespace ArcadeRemoteExecute.Client;

/// <summary>
/// 监控目标进程，不存在或被杀死时执行启动命令（批处理或 exe）
/// </summary>
public class ProcessWatcher
{
    private readonly ClientConfig _config;
    private DateTime _lastLaunchAttempt = DateTime.MinValue;
    private const int MinLaunchIntervalSeconds = 10; // 防止频繁拉起

    public ProcessWatcher(ClientConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// 进程名：配置里可以是 "sinmai.exe"，这里用不含扩展名匹配
    /// </summary>
    private string ProcessNameWithoutExtension
    {
        get
        {
            var name = _config.TargetProcess.Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name = name[..^4];
            return name;
        }
    }

    public bool IsTargetProcessRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName(ProcessNameWithoutExtension);
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (var p in processes)
                    p.Dispose();
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 若进程未运行则执行启动命令；批处理用 cmd /c 执行
    /// </summary>
    public void EnsureProcessLaunched()
    {
        if (IsTargetProcessRunning())
            return;

        var cmd = _config.LaunchCommand?.Trim();
        if (string.IsNullOrEmpty(cmd) || !File.Exists(cmd))
        {
            Console.WriteLine($"[守护] 进程 {_config.TargetProcess} 未运行，且启动命令不存在或未配置: {cmd}");
            return;
        }

        if ((DateTime.UtcNow - _lastLaunchAttempt).TotalSeconds < MinLaunchIntervalSeconds)
            return;

        _lastLaunchAttempt = DateTime.UtcNow;
        try
        {
            var ext = Path.GetExtension(cmd).ToLowerInvariant();
            if (ext == ".bat" || ext == ".cmd")
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{cmd}\"",
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    WorkingDirectory = Path.GetDirectoryName(cmd) ?? Environment.CurrentDirectory
                };
                Process.Start(startInfo);
                Console.WriteLine($"[守护] 已通过 cmd 启动: {cmd}");
            }
            else
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = cmd,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    WorkingDirectory = Path.GetDirectoryName(cmd) ?? Environment.CurrentDirectory
                };
                Process.Start(startInfo);
                Console.WriteLine($"[守护] 已启动: {cmd}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[守护] 启动失败: {ex.Message}");
        }
    }
}
