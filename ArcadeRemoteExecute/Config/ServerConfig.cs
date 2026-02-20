namespace ArcadeRemoteExecute.Config;

/// <summary>
/// 云端（Ubuntu）服务端配置
/// </summary>
public class ServerConfig
{
    /// <summary>存放更新 zip 的文件夹路径</summary>
    public string UpdatesFolder { get; set; } = "./Updates";

    /// <summary>监听端口</summary>
    public int Port { get; set; } = 5000;

    /// <summary>监听地址（空则监听所有接口 0.0.0.0）</summary>
    public string ListenHost { get; set; } = "";

    /// <summary>存放配置文件的文件夹（如 AquaMai.toml），GET /config 时返回该目录下的配置文件</summary>
    public string ConfigFolder { get; set; } = "./Config";

    /// <summary>配置文件名（如 AquaMai.toml），需放在 ConfigFolder 下</summary>
    public string ConfigFileName { get; set; } = "AquaMai.toml";
}
