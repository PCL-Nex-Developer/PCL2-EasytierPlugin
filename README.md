# EasyTier 联机大厅插件

这是面向新版 PCL Nex 插件引擎的 EasyTier 联机大厅插件。联机协议与主要行为源自 [PCL Community Edition](https://github.com/PCL-Community/PCL-CE)，现已从启动器源码中剥离为独立 PCLX，并通过 `PCL.Mixin` 接入 PCL Nex。

## 当前版本

| 项目 | 内容 |
| --- | --- |
| 插件 ID | `pclnex.easytier` |
| 开发版本 | `2.0.0` |
| PCL.Core BaseVersion | `2026.07.1` |
| EasyTier | `2.6.4` |
| 入口程序集 | `lib/PCL.EasyTierPlugin.dll` |
| Mixin 配置 | `mixins/pclnex.easytier.mixins.json` |

## 功能

- 创建和加入 `U/XXXX-XXXX-XXXX-XXXX` EasyTier 大厅。
- Natayark OAuth 登录、refresh token 持久化与实名状态检查。
- 自动下载 AMD64 或 ARM64 对应的 EasyTier 2.6.4。
- Scaffolding 心跳、成员列表、Minecraft 端口查询和协议探测。
- 加入端自动建立 TCP/UDP 端口转发并广播 Minecraft 局域网世界。
- P2P/中继模式、TCP/UDP、IPv6、对称 NAT 打洞和自定义节点设置。
- 插件独立配置、日志与 EasyTier 孤儿进程清理。
- 在“百宝箱”的 CE 原位置恢复“瞅眼服务器”查询卡片。

## 插件结构

插件直接迁移 PCL-CE 的 EasyTier Core 与原版页面，不再维护单独设计的联机 UI：

- `CeLink`：PCL-CE 的大厅、EasyTier、Scaffolding、Natayark 与 Minecraft Ping 实现。
- `CeUi/PageTools/PageToolsGameLink`：PCL-CE 原版联机大厅页。
- `CeUi/PageSetup/PageSetupGameLink`：PCL-CE 原版联机设置页。
- `CeUi/ServerQuery`：PCL-CE 原版 Minecraft 服务器查询控件。
- `CeCompat`：独立插件运行所需的最小配置、资源与宿主兼容层。

导航只通过 `PCL.Mixin` 接回 CE 的原位置：

- `PageToolsLeftMixin` 在“工具”左栏首部恢复“联机 -> 大厅”。
- `PageSetupLeftMixin` 在“设置”左栏的“游戏管理”后恢复“工具 -> 联机”。
- `PageToolsTestMixin` 在“百宝箱”的皮肤卡片后恢复“瞅眼服务器”。
- 两个 `PageGet` 注入分别返回上面的 CE 原版页面。

插件不再使用顶部“联机”按钮、`FormMainMixin`、自绘页面宿主或旧 `LoadAsync` / `UnloadAsync` 生命周期。构建时引用目标版本的 `PCL.Core` 与启动器 UI 类型，PCLX 中不会打包 `PCL.Core.dll`、`Plain Craft Launcher 2.dll` 或 `PCL.Plugin.Abstractions.dll`。

## 开发环境

需要 Windows、.NET 10 SDK，以及相邻的最新 PCL2-Nex 源码：

```text
Desktop/
  PCL2-Nex/
  PCL2-EasytierPlugin/
```

先构建 PCL.Core：

```powershell
dotnet build ..\PCL2-Nex\PCL.Core\PCL.Core.csproj `
  --configuration Release `
  --property:Platform=AnyCPU
```

再构建插件：

```powershell
dotnet build .\src\PclNex.EasyTierLobby\PclNex.EasyTierLobby.csproj `
  --configuration Release `
  --property:Platform=AnyCPU `
  --property:PclHostRoot="..\PCL2-Nex"
```

## 测试与打包

```powershell
.\scripts\pack.ps1 -Version 2.0.0 -PclHostRoot "..\PCL2-Nex"
```

输出结构：

```text
artifacts/pclnex.easytier/
  plugin.json
  README.md
  lib/PCL.EasyTierPlugin.dll
  mixins/pclnex.easytier.mixins.json

artifacts/pclnex.easytier-2.0.0-anycpu.pclx
```

PCLX 中不会包含 `PCL.Core.dll` 或主程序程序集。启用、禁用或更新 Mixin 插件后需要重启启动器。

## 来源与许可

- 原始联机实现：[PCL-Community/PCL-CE](https://github.com/PCL-Community/PCL-CE)
- 插件目标宿主：[PCL-Nex-Developer/PCL2-Nex](https://github.com/PCL-Nex-Developer/PCL2-Nex)
- 插件开发文档：[PCL2-Nex Wiki](https://github.com/PCL-Nex-Developer/PCL2-Nex/wiki)
- 网络组件：[EasyTier](https://github.com/EasyTier/EasyTier)

本仓库继续遵循根目录 [LICENSE](LICENSE)。
