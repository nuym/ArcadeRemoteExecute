# ArcadeRemoteExecute

街机远程更新与守护程序：一个程序同时支持**云端服务端**与**街机客户端**两种模式，用于从云端下发更新包与配置，并在街机上守护游戏进程、按需重启。

---

## 项目介绍

- **云端（服务端）**：运行在 Ubuntu 等 Linux 上，提供 HTTP 服务，用于：
  - 提供更新包清单（zip 列表及 hash）
  - 提供 zip 下载、配置文件下载
  - 提供远程 **freeplay（免费游玩）** 开关的读写接口，便于远程切换投币/免费

- **街机（客户端）**：运行在 Windows 10 街机上，用于：
  - **进程守护**：监控指定进程（如 `sinmai.exe`），若不存在或被结束则自动执行配置的批处理/exe 重新启动
  - **更新拉取**：按间隔向云端请求清单，仅对 hash 变化的 zip 下载并解压到「以 zip 名称命名的文件夹」，不删除本地已有文件
  - **配置同步**：从云端下载配置文件（如 AquaMai.toml）到本地指定路径，并支持按云端 freeplay 开关覆盖 `IsFreePlay`
  - **远程 freeplay + 重启**：当云端 freeplay 变更时，自动写配置、结束游戏进程，由守护在数秒内重新启动游戏

同一套代码，通过启动参数区分模式：加 `--server` 为云端，不加为街机。

---

## 如何编译

### 环境要求

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) 或更高（如 .NET 10，需 `global.json` 中 `rollForward` 支持）

### 编译步骤

在项目根目录执行：

```bash
dotnet build
```

发布为单机可执行文件（可选）：

```bash
# Windows 街机用
dotnet publish -c Release -r win-x64 --self-contained

# Linux 云端用
dotnet publish -c Release -r linux-x64 --self-contained
```

输出在 `ArcadeRemoteExecute/bin/Release/net8.0/win-x64/publish/` 或 `linux-x64/publish/`，将对应平台的可执行文件及同目录下的依赖（或单文件）拷贝到目标机器即可。

---

## 如何运行

### 云端（Ubuntu / Linux）

1. 将编译或发布后的程序放到服务器，并准备好**配置文件**（见下方「配置文件」）。确保 `server_config.json` 与可执行文件在同一目录。
2. 在服务器上创建好 `Updates`（放 zip）和 `Config`（放配置与 freeplay 状态）目录（或按 `server_config.json` 中路径配置）。
3. 启动服务端（任选一种方式）：

**方式 A：在已发布目录中运行**

发布后或从 GitHub Actions 下载并解压的产物在一个目录里（如 `publish/linux-x64/` 或解压得到的文件夹）。先进入该目录再执行：

```bash
cd publish/linux-x64   # 或你解压后的目录
chmod +x ArcadeRemoteExecute   # 若提示权限不足则执行
./ArcadeRemoteExecute --server
```

**方式 B：在源码目录用 dotnet 运行**

```bash
dotnet run --project ArcadeRemoteExecute -- --server
```

按 `Ctrl+C` 可停止服务。服务监听 `http://0.0.0.0:5000`（默认），街机需能访问该 IP 和端口。

---

### 街机（Windows 10）

1. 将编译或发布后的程序放到街机，并准备好**客户端配置文件**（见下方「配置文件」）。
2. 确保 `client_config.json` 中 `ServerUrl` 指向云端地址（如 `http://云端IP:5000`），`LaunchCommand` 指向你的启动批处理或 exe。
3. 直接运行（不加 `--server`）：

```bash
ArcadeRemoteExecute.exe
```

或：

```bash
dotnet run --project ArcadeRemoteExecute
```

启动后会：先检查目标进程是否存在、不存在则执行 `LaunchCommand`，拉取一次更新与配置；之后定时检查进程存活并定时向云端请求更新。按 `Ctrl+C` 退出。

建议将街机端设为开机自启或由任务计划程序启动，以便断电恢复后自动拉更新并启动游戏。

---

## 配置文件

配置文件为 JSON，需与可执行文件放在同一目录（或指定路径）。首次运行若不存在会生成默认文件。

---

### 云端：`server_config.json`

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `UpdatesFolder` | 存放更新 zip 的目录 | `"./Updates"` |
| `Port` | HTTP 监听端口 | `5000` |
| `ListenHost` | 监听地址，空为 `0.0.0.0` | `""` |
| `ConfigFolder` | 存放配置文件的目录（如 AquaMai.toml） | `"./Config"` |
| `ConfigFileName` | 配置文件名，放在 ConfigFolder 下 | `"AquaMai.toml"` |

freeplay 状态会保存在 `ConfigFolder/freeplay.json`，由程序自动读写。

示例：

```json
{
  "UpdatesFolder": "./Updates",
  "Port": 5000,
  "ListenHost": "",
  "ConfigFolder": "./Config",
  "ConfigFileName": "AquaMai.toml"
}
```

部署时在 `Config` 下放置 `AquaMai.toml`（及你需要的其他配置），在 `Updates` 下放置要下发的 zip 文件。

---

### 街机：`client_config.json`

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `TargetProcess` | 要监控的进程名（如 sinmai.exe） | `"sinmai.exe"` |
| `LaunchCommand` | 进程不存在或被结束时执行的批处理或 exe 的**完整路径** | `""` |
| `ServerUrl` | 云端地址（如 http://192.168.1.100:5000） | `"http://localhost:5000"` |
| `UpdateTargetDir` | 更新包下载与解压目录，留空为程序当前目录 | `""` |
| `ProcessCheckIntervalSeconds` | 检查进程存活的间隔（秒） | `5` |
| `UpdateCheckIntervalSeconds` | 向云端请求更新/配置的间隔（秒） | `60` |
| `LocalConfigPath` | 从云端下载的配置要保存到的本地**完整路径**，留空则不下载配置 | `""` |
| `OverrideFreePlay` | 本地是否覆盖配置里的 IsFreePlay：`true`/`false` 覆盖，`null` 不覆盖（以云端 /freeplay 为准） | `null` |

示例：

```json
{
  "TargetProcess": "sinmai.exe",
  "LaunchCommand": "C:\\Games\\Arcade\\start.bat",
  "ServerUrl": "http://192.168.1.100:5000",
  "UpdateTargetDir": "",
  "ProcessCheckIntervalSeconds": 5,
  "UpdateCheckIntervalSeconds": 60,
  "LocalConfigPath": "C:\\Games\\AquaMai\\AquaMai.toml",
  "OverrideFreePlay": null
}
```

- 配置了 `LocalConfigPath` 时，街机端会按间隔从云端拉取配置并写入该路径；若云端提供 `/freeplay` 接口，会优先使用云端的 freeplay 值并可在变更时自动重启游戏。
- 云端 Updates 为空时，街机不会删除任何本地已有文件，仅跳过 zip 更新。
- **云端 Config 文件夹为空**（没有配置文件名如 AquaMai.toml）时，`GET /config` 返回 404，街机端只会报「下载配置失败」，**不会覆盖**本地配置文件，也**不会**根据 freeplay 改写或重启游戏；本地原有配置与 freeplay 状态保持不变。

---

## 云端 API 简要说明

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/manifest` | 返回更新包清单（zip 文件名与 SHA256） |
| GET | `/download/{文件名}` | 下载指定 zip |
| GET | `/config` | 下载配置文件（如 AquaMai.toml） |
| GET | `/freeplay` | 获取当前 freeplay 状态，返回 `{"freePlay": true/false}` |
| POST | `/freeplay` | 设置 freeplay。请求体可为 `{"freePlay": true}` 或 `true`/`false`，或查询参数 `?freePlay=true` |

远程修改 freeplay 后，街机在下次拉取时会应用新配置并结束游戏进程，由守护自动重新启动游戏。

---

## 许可证

按项目仓库约定使用。
