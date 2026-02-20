using System.Text.Json;

namespace ArcadeRemoteExecute.Config;

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static ClientConfig LoadClient(string path = "client_config.json")
    {
        if (!File.Exists(path))
        {
            var defaultConfig = new ClientConfig();
            File.WriteAllText(path, JsonSerializer.Serialize(defaultConfig, JsonOptions));
            return defaultConfig;
        }
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ClientConfig>(json) ?? new ClientConfig();
    }

    public static ServerConfig LoadServer(string path = "server_config.json")
    {
        if (!File.Exists(path))
        {
            var defaultConfig = new ServerConfig();
            File.WriteAllText(path, JsonSerializer.Serialize(defaultConfig, JsonOptions));
            return defaultConfig;
        }
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ServerConfig>(json) ?? new ServerConfig();
    }
}
