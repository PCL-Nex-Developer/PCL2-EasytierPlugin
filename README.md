# EasyTier 联机大厅插件

这是面向 PCL Nex 的 EasyTier 联机大厅插件。插件通过 PCL 的通用扩展点接入启动器，为联机大厅提供基于 EasyTier 的网络隧道能力。

## 插件信息

| 项目 | 内容 |
| --- | --- |
| 插件 ID | `pclnex.easytier` |
| 插件名称 | `EasyTier 联机大厅` |
| 当前版本 | `1.0.0` |
| 作者 | `Nex(XueLing)` |
| 运行时 | `.NET` DLL 插件 |
| 入口程序集 | `PCL.EasyTierPlugin.dll` |
| 最低 SDK API | `1.2.0` |

插件声明的能力：

```json
[
	"RegisterExtension",
	"ContributeTools",
	"ContributeSettings"
]
```

其中 `RegisterExtension` 用于注册联机大厅相关扩展点，`ContributeTools` 和 `ContributeSettings` 用于向启动器工具页、设置页贡献界面入口。

## 功能范围

当前插件主要负责：

- 注册 EasyTier 联机隧道提供器。
- 注册联机大厅服务适配器。
- 向 PCL 工具页添加联机大厅入口。
- 向 PCL 设置页添加联机相关设置入口。
- 管理 EasyTier 联机所需的本地状态、账号缓存和公告缓存。

插件本身不替代 PCL 主程序。它需要由支持插件系统的 PCL Nex 版本加载。

## 目录结构

```text
PCL.EasyTierPlugin/
	EasyTier/                 EasyTier 进程、节点、转发和模型相关代码
	Lobby/                    联机大厅流程、房间和事件处理逻辑
	Natayark/                 Natayark 账号授权相关逻辑
	Scaffolding/              本地脚手架协议客户端/服务端
	PCL.EasyTierPlugin.Test/  测试项目
	EasyTierLobbyPlugin.cs    插件入口
	plugin.json               PCL 插件清单
	manifest.json             远程版本索引示例
```

## 开发环境

需要安装 .NET SDK，并使用支持 Windows 目标框架的构建环境。

当前项目目标框架：

```text
net8.0-windows
```

构建时需要：

- .NET SDK
- Windows targeting 支持
- 与当前项目相邻的 PCL 主仓库源码

当前插件仍引用主仓库中的本地项目：

```text
PCL.Core
PCL.Plugin.Abstractions
```

后续如果需要做成完全独立的公开 SDK 插件，应继续把依赖的宿主能力抽象到 `PCL.Plugin.Abstractions`，再逐步移除对 `PCL.Core` 的直接引用。

## 构建

在插件项目目录运行：

```powershell
dotnet build .\PCL.EasyTierPlugin.csproj --configuration Debug --property:Platform=AnyCPU
```

如果在非 Windows 环境构建，可能需要额外传入：

```powershell
--property:EnableWindowsTargeting=true
```

## 打包

运行：

```powershell
dotnet build .\PCL.EasyTierPlugin.csproj -t:PublishPlugin --configuration Release --property:Platform=AnyCPU
```

打包产物会生成到：

```text
artifacts/pclnex.easytier/
```

目录中至少包含：

```text
PCL.EasyTierPlugin.dll
plugin.json
```

把该目录作为插件包目录放入 PCL 插件目录后，即可由启动器加载。

## 插件清单

`plugin.json` 是启动器加载插件时读取的清单文件。当前内容的关键字段如下：

```json
{
	"id": "pclnex.easytier",
	"name": "EasyTier 联机大厅",
	"version": "1.0.0",
	"author": "Nex(XueLing)",
	"runtime": "dotnet",
	"entryAssembly": "PCL.EasyTierPlugin.dll",
	"minApiVersion": "1.2.0"
}
```

修改插件 ID、版本号或最低 API 版本时，需要同步检查：

- `EasyTierLobbyPlugin.cs` 中的 `[Plugin(...)]` 元数据。
- `PCL.EasyTierPlugin.csproj` 中的 `PublishPlugin` 输出目录。
- `manifest.json` 中的远程版本索引信息。
- 状态存储 key 前缀是否需要迁移或兼容旧值。

## 扩展点

插件通过 PCL 的通用扩展点注册联机能力：

```csharp
PluginExtensionPoints.LobbyTunnelProvider
PluginExtensionPoints.LobbyService
```

入口类会在加载时调用：

```csharp
extensions.RegisterLobbyTunnelProvider(provider);
extensions.RegisterLobbyService(service, displayName: "EasyTier Lobby Service");
```

因此宿主需要提供 `RegisterExtension` 能力对应的扩展注册 API。

## 状态与兼容

插件当前使用的新状态 key 前缀为：

```text
Plugin.pclnex.easytier.
```

为了兼容早期构建，插件仍会尝试读取旧前缀：

```text
Plugin.pcl.easytier.lobby.
```

这样可以避免用户升级后丢失 EULA 状态、公告缓存或账号刷新 token。

## 测试

测试项目位于：

```text
PCL.EasyTierPlugin.Test/
```

可以运行：

```powershell
dotnet build .\PCL.EasyTierPlugin.Test\PCL.EasyTierPlugin.Test.csproj --configuration Debug --property:Platform=AnyCPU
```

## 说明

这是 PCL Nex 插件生态的一部分。插件运行在启动器进程内，不是安全沙箱；请只加载可信来源的插件包。
