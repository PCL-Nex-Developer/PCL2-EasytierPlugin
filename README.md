# EasyTier 联机大厅插件

这是面向 PCL Nex 的 EasyTier 联机大厅插件。插件通过 PCL 的工具页和设置页扩展点接入启动器，提供兼容 PCL-CE Scaffolding 协议的 EasyTier 联机大厅。

## 插件信息

| 项目 | 内容 |
| --- | --- |
| 插件 ID | `pclnex.easytier` |
| 插件名称 | `EasyTier 联机大厅` |
| 当前开发版本 | `1.0.7` |
| 作者 | `Nex(XueLing)` |
| 入口程序集 | `PCL.EasyTierPlugin.dll` |
| 最低 SDK API | `1.2.1.0` |
| 最低宿主版本 | `3.0.0` |

插件声明的能力：

```json
[
  "ContributeTools",
  "ContributeSettings"
]
```

PCL2-Nex 插件市场通过主仓库 `dev` 分支的 `plugins.json` 注册本插件：

- 插件清单：`https://github.com/PCL-Nex-Developer/PCL2-EasytierPlugin/raw/refs/heads/main/manifest.json`
- 插件仓库：`https://github.com/PCL-Nex-Developer/PCL2-EasytierPlugin`

## 功能范围

- 创建和加入 PCL-CE `U/XXXX-XXXX-XXXX-XXXX` 大厅。
- 使用 EasyTier 2.6.4 建立 P2P 或中继连接。
- 支持 Scaffolding 心跳、成员列表、Minecraft 端口查询和协议探测。
- 自动转发 Minecraft 局域网端口并向加入端广播世界。
- 支持 Natayark OAuth、refresh token 持久化和大厅登录前置检查。
- 支持本地联机用户名实时保存，并在创建或加入大厅时立即生效。
- 支持公告、NAT 测试、连接类型、延迟和成员列表展示。
- 对世界刷新执行取消、单飞和按端口去重。

## 目录结构

```text
src/PclNex.EasyTierLobby/              插件源码
tests/PclNex.EasyTierLobby.UiSmoke/    UI 与关键行为回归检查
scripts/pack.ps1                       本地标准打包脚本
.github/workflows/release.yml          Tag 发布工作流
plugin.json                            PCL 插件加载清单
manifest.json                          插件市场版本索引
```

## 开发环境

需要 Windows 和 .NET 10 SDK。仓库默认使用与远端 Action 相同的相邻目录布局：

```text
workspace/
  PCL-CE/                       PCL2-Nex 主仓库
  PCLNexOther/
    PCL2-EasytierPlugin/        本仓库
```

也可以通过 `PclHostRoot` 指定本机 PCL2-Nex 源码目录。

## 构建

```powershell
dotnet build .\src\PclNex.EasyTierLobby\PclNex.EasyTierLobby.csproj `
  --configuration Release `
  --property:Platform=AnyCPU `
  --property:PclHostRoot="C:\path\to\PCL2-Nex"
```

## 测试

```powershell
dotnet run --project .\tests\PclNex.EasyTierLobby.UiSmoke\PclNex.EasyTierLobby.UiSmoke.csproj `
  --configuration Release `
  --property:Platform=AnyCPU `
  --property:PclHostRoot="C:\path\to\PCL2-Nex"
```

## 打包

```powershell
.\scripts\pack.ps1 -Version 1.0.7 -PclHostRoot "C:\path\to\PCL2-Nex"
```

打包脚本会依次执行 `PublishPlugin` 和 UI smoke，并生成：

```text
artifacts/pclnex.easytier/
  PCL.EasyTierPlugin.dll
  PCL.EasyTierPlugin.deps.json
  System.Management.dll
  plugin.json

artifacts/pclnex.easytier-v1.0.7.pclx
```

## 发布

推送 `v*` 标签会触发 `.github/workflows/release.yml`：

1. 检出插件和 PCL2-Nex 主仓库。
2. 构建插件并运行 UI smoke。
3. 生成 `pclnex.easytier-v<version>.pclx`。
4. 创建 GitHub Release。
5. 计算真实 SHA-256 并更新 `manifest.json`。
6. 将更新后的 `manifest.json` 和 `plugin.json` 提交回 `main`。

`manifest.json` 不预写未发布版本的虚假哈希；新版本条目由发布工作流在包生成后写入。

## 安装

可以通过 PCL Nex 插件市场安装发布版本，也可以将 `artifacts/pclnex.easytier` 目录复制到 PCL 插件目录后重启启动器。

插件运行在启动器进程内，不是安全沙箱，请只加载可信来源的插件包。
