using ArcadeRemoteExecute.Client;
using ArcadeRemoteExecute.Config;
using ArcadeRemoteExecute.Server;

var isServer = args.Contains("--server", StringComparer.OrdinalIgnoreCase);

if (isServer)
{
    var serverConfig = ConfigLoader.LoadServer();
    var server = new UpdateServer(serverConfig);
    Console.WriteLine("按 Ctrl+C 停止服务端");
    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); server.Stop(); };
    await server.RunAsync(cts.Token).ConfigureAwait(false);
    return;
}

// 街机端：进程守护 + 定时向云端请求更新
var clientConfig = ConfigLoader.LoadClient();
var watcher = new ProcessWatcher(clientConfig);
var updateClient = new UpdateClient(clientConfig);

Console.WriteLine($"目标进程: {clientConfig.TargetProcess}");
Console.WriteLine($"启动命令: {clientConfig.LaunchCommand}");
Console.WriteLine($"云端地址: {clientConfig.ServerUrl}");
Console.WriteLine("按 Ctrl+C 退出");

// 启动时先检查进程并拉取一次更新
watcher.EnsureProcessLaunched();
await updateClient.CheckAndApplyUpdatesAsync().ConfigureAwait(false);

var ctsClient = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; ctsClient.Cancel(); };

var processTask = Task.Run(async () =>
{
    while (!ctsClient.Token.IsCancellationRequested)
    {
        await Task.Delay(TimeSpan.FromSeconds(clientConfig.ProcessCheckIntervalSeconds), ctsClient.Token).ConfigureAwait(false);
        watcher.EnsureProcessLaunched();
    }
}, ctsClient.Token);

var updateTask = Task.Run(async () =>
{
    while (!ctsClient.Token.IsCancellationRequested)
    {
        await Task.Delay(TimeSpan.FromSeconds(clientConfig.UpdateCheckIntervalSeconds), ctsClient.Token).ConfigureAwait(false);
        await updateClient.CheckAndApplyUpdatesAsync(ctsClient.Token).ConfigureAwait(false);
    }
}, ctsClient.Token);

await Task.WhenAll(processTask, updateTask).ConfigureAwait(false);
