namespace ArcadeRemoteExecute.Config;

/// <summary>
/// 街机端（Windows）配置
/// </summary>
public class ClientConfig
{
    /// <summary>要监控的进程名（如 sinmai.exe，不含路径）</summary>
    public string TargetProcess { get; set; } = "sinmai.exe";

    /// <summary>进程不存在或被杀死时启动的批处理或 exe 完整路径</summary>
    public string LaunchCommand { get; set; } = "";

    /// <summary>云端更新服务地址（如 http://192.168.1.100:5000）</summary>
    public string ServerUrl { get; set; } = "http://localhost:5000";

    /// <summary>更新包下载与解压的目标目录（留空则使用程序当前目录）</summary>
    public string UpdateTargetDir { get; set; } = "";

    /// <summary>检查进程存活的间隔（秒）</summary>
    public int ProcessCheckIntervalSeconds { get; set; } = 5;

    /// <summary>向云端请求更新并比对 hash 的间隔（秒）</summary>
    public int UpdateCheckIntervalSeconds { get; set; } = 60;

    /// <summary>从云端下载的配置文件保存到本地的完整路径（留空则不下载配置）</summary>
    public string LocalConfigPath { get; set; } = "";

    /// <summary>是否覆盖配置文件中的 IsFreePlay（免费游玩）开关。不填则使用云端文件原样；true/false 则下载后强制改为该值</summary>
    public bool? OverrideFreePlay { get; set; }
}
