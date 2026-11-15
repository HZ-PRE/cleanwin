<div align="center">

<img src="src/CleanOptimize.App/Assets/logo.png" alt="CleanOptimize 标志" width="80" />

# CleanOptimize · 清理优化大师

Windows 清理与系统管理工具，支持扫描预览、风险提示、隔离恢复和磁盘空间分析。

[![Release](https://img.shields.io/github/v/release/HZ-PRE/cleanwin?label=Release)](https://github.com/HZ-PRE/cleanwin/releases/latest)
[![Build](https://github.com/HZ-PRE/cleanwin/actions/workflows/verify.yml/badge.svg)](https://github.com/HZ-PRE/cleanwin/actions/workflows/verify.yml)
[![Windows](https://img.shields.io/badge/Windows-10%20%2F%2011%20x64-0078D4)](#下载与使用)
[![.NET](https://img.shields.io/badge/.NET-8-512BD4)](#从源码运行)

[下载最新版](https://github.com/HZ-PRE/cleanwin/releases/latest) · [功能介绍](#功能介绍) · [构建与测试](#构建与测试) · [问题反馈](https://github.com/HZ-PRE/cleanwin/issues)

简体中文 | [English](README.en.md) | [繁體中文](README.zh-TW.md) | [日本語](README.ja.md)

</div>

## 下载与使用

支持 **Windows 10 / Windows 11，x64**。安装版和绿色版均内置 .NET 运行时，使用时无需另外安装 .NET。

在 [Releases](https://github.com/HZ-PRE/cleanwin/releases) 页面选择正式版本，下载对应附件：

| 文件 | 用途 |
|---|---|
| `CleanOptimizeSetup-<版本>.exe` | 完整安装包，提供安装向导和卸载程序 |
| `CleanOptimize-<版本>-win-x64.zip` | 绿色版，完整解压后运行 `CleanOptimize.exe` |
| `SHA256SUMS.txt` | 安装包和绿色包的 SHA256 校验值 |

首次使用建议先扫描，查看清理项目、风险及影响，再确认执行。需要保留的文件或目录可以加入白名单。遇到清理失败，可打开“查看未清理原因”，查看路径、原因和处理建议。

## 功能介绍

| 模块 | 主要功能 |
|---|---|
| 首页 | 健康评分、扫描摘要、CPU 与内存监控、磁盘容量、多盘切换、快捷清理和最近活动 |
| 智能扫描 | 汇总多类清理规则，按风险展示结果，支持预览明细、选择项目和查看清理结果 |
| 清理空间 | 系统临时文件、应用缓存、浏览器缓存、隐私痕迹、回收站及自定义清理目录 |
| 空间分析 | 文件夹占用分析、大文件查找、大小与类型筛选、打开位置和回收操作；长路径详情可滚动查看 |
| 启动项 | 查看和管理注册表、启动文件夹及登录计划任务中的启动项，展示可获取的开机耗时 |
| 应用管理 | 查看已安装应用及图标、调用应用卸载程序、检测可识别的卸载残留 |
| 工具箱 | 隔离区管理、重复文件检测、空文件夹清理、文件粉碎、清理历史、系统维护状态及 Windows 工具入口 |
| 设置与托盘 | 深色 / 浅色 / 跟随系统主题、8 种语言、窗口记忆、自动清理、结果通知及版本更新 |

**快捷清理**可从首页、托盘和命令面板进入，只扫描默认可选的低风险系统、应用和浏览器缓存，确认结果后执行。**自动清理**默认关闭，启用后在应用运行期间按设置周期执行，结果通知可单独开关。

常用快捷键：`Ctrl+K` 打开命令面板，`Ctrl+1` 至 `Ctrl+8` 切换页面，`Esc` 取消支持取消的扫描任务。首页隐藏后停止系统监控采样，大量文件列表使用虚拟化显示。

## 清理保护与数据存储

- **范围与白名单校验**：清理规则受允许目录、系统关键目录和用户目录保护约束。重复文件回收、空目录清理及文件粉碎按用户明确选择的目标单独校验，不会因此放开自动清理范围。
- **风险提示**：低风险项目可默认选择；隐私痕迹等需要确认的项目不默认勾选。执行前可查看项目说明和影响。
- **隔离恢复**：启用隔离区后，支持恢复的清理项目移入隔离区，默认保留 30 天。隔离量与实际释放空间分开统计，移入隔离区不等于已释放磁盘空间。
- **永久操作**：文件粉碎、清空回收站、永久删除隔离项目无法通过本应用恢复；其他清理是否可恢复取决于规则和隔离设置。
- **路径边界**：清理拒绝 UNC、网络盘和设备路径，并防止通过目录链接跨越清理边界。
- **失败说明**：区分文件占用、权限不足、路径过长等情况，给出处理建议。未清理明细优先保留，最多展示 1,000 条，并显示未清理总数。

设置、历史记录和隔离数据保存在 `%LOCALAPPDATA%\CleanOptimize`。卸载应用会保留这些数据，便于重新安装后查看和恢复隔离文件。

匿名统计与崩溃报告选项保留，但当前版本禁用上报，不上传使用记录、文件名、路径或文件内容。更新功能会向 GitHub 请求公开版本信息，并在确认后下载更新文件；本地扫描与清理无需联网。

## 检查更新

启动时检查公开正式版本；发现更高版本后，在顶部 Logo 旁显示红色 **NEW**，在“检查更新”旁显示“可更新”。点击标签打开更新框，选择“确认更新”后下载安装包，校验 SHA256，再启动安装程序。

- 后台检查缓存 5 分钟；手动点击“检查更新”会重新请求，正在进行的请求会合并。
- 下载支持取消；下载、校验或安装程序启动失败时，当前应用保持运行。
- 正在扫描、清理或卸载应用时，需要先结束操作，再进行更新。
- 草稿、预览版及 Actions 构建产物不属于正式更新；相同或更低版本不会触发更新提示。

**构建成功但检查不到更新？** 请确认版本已在 [Releases](https://github.com/HZ-PRE/cleanwin/releases) 公开发布，附件包含已上传完成的 `CleanOptimizeSetup-<版本>.exe` 和 `SHA256SUMS.txt`，且版本号高于本机版本。旧流程生成的草稿需要点击 **Publish release** 后才可被客户端发现。

当前更新源为 `HZ-PRE/cleanwin` 的公开正式 Release。SHA256 用于校验文件完整性；默认发布流程未启用自动代码签名，也未接入 SignPath。

## 构建与测试

### 从源码运行

开发环境需要 Windows 和 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。以下命令在 PowerShell 中执行：

```powershell
git clone https://github.com/HZ-PRE/cleanwin.git
cd cleanwin
dotnet build ./src/CleanOptimize.App/CleanOptimize.App.csproj -c Release
./src/CleanOptimize.App/bin/Release/net8.0-windows/CleanOptimize.exe
```

项目使用 WPF 与 Windows Forms，应用代码未引入第三方 NuGet 包。

### 自动验证

```powershell
# 构建、联网入口检查、语言资源检查和核心回归测试
./scripts/verify.ps1 -Configuration Debug
./scripts/verify.ps1 -Configuration Release

# 应用完整自测：包括核心检查、WPF 界面与窗口生命周期检查
Start-Process ./src/CleanOptimize.App/bin/Release/net8.0-windows/CleanOptimize.exe `
    -ArgumentList '--selftest' -Wait
```

测试使用独立临时数据目录，覆盖删除保护、白名单、隔离恢复、占用文件、重复文件回收、取消与并发、更新校验、多语言及主题布局。真实清理规则的机器扫描检查不执行系统垃圾删除；文件操作测试使用测试样本。自动检查不能替代所有设备上的人工交互验证。

### 本地打包

生成安装包还需要 [Inno Setup](https://jrsoftware.org/isinfo.php)。发布脚本读取项目版本号，生成自包含安装包、绿色包及校验文件，并运行发布程序自测和 ZIP 完整性校验。

```powershell
./scripts/publish.ps1 -RequireInstaller

# 工具不在 PATH 或默认安装位置时，指定实际路径
./scripts/publish.ps1 -DotnetPath C:/tools/dotnet/dotnet.exe `
    -IsccPath C:/tools/Inno/ISCC.exe -RequireInstaller
```

产物位于 `dist/`。脚本同时生成供开发调试的框架依赖目录，对外分发的安装包和 ZIP 均使用内置运行时的自包含版本。

从零清理编译缓存、执行 Debug / Release 验证并打包：

```powershell
./scripts/full-test.ps1 -DotnetPath dotnet -IsccPath C:/tools/Inno/ISCC.exe
```

安装包和绿色包验收：

```powershell
./scripts/test-packages.ps1
```

该脚本解压绿色包、安装到新的测试目录、校验安装文件、运行自测并测试运行中卸载；如果检测到本机已安装同一产品，会停止以避免覆盖。验证日志保存在 `dist/`。

### 修改版本与 GitHub 发布

本地默认版本在 [CleanOptimize.App.csproj](src/CleanOptimize.App/CleanOptimize.App.csproj) 的 `<Version>` 中设置，格式为 `主版本.次版本.修订号`，例如 `0.0.3`。也可以使用 `publish.ps1 -Version 0.0.3` 临时指定打包版本。

[Release 工作流](.github/workflows/release.yml)支持两种运行方式：

| 方式 | 结果 |
|---|---|
| 推送版本标签，如 `v0.0.3` | 以标签版本构建和验证，上传安装包、绿色包与校验文件；附件上传成功后公开发布 Release |
| Actions → Release → Run workflow，选择分支 | 构建并上传 `CleanOptimize-windows` 产物，不公开发布正式版本 |

先提交并推送待发布代码，再在该提交上创建一个尚未使用的版本标签。以下以 `v0.0.3` 为例：

```powershell
git tag v0.0.3
git push origin v0.0.3
```

标签构建使用标签中的版本号覆盖项目版本，因此建议同时更新项目文件，保持本地与云端版本一致。带后缀的标签（如 `v0.0.3-preview.1`）会发布为预览版，不向正式版客户端提示更新。普通分支推送和 PR 由 [Verify 工作流](.github/workflows/verify.yml)执行构建及核心回归检查。

## 项目结构

```text
src/
  CleanOptimize.App/   WPF 界面、主题、托盘与更新服务
  CleanOptimize.Core/  清理规则、安全保护、分析与本地存储
  SelfTestRunner/      核心自动测试入口
scripts/              构建、测试、打包与语言资源生成脚本
.github/workflows/    GitHub Actions 验证和发布流程
```

## 反馈与支持

问题和功能建议请提交到 [Issues](https://github.com/HZ-PRE/cleanwin/issues)，附上应用版本、Windows 版本、复现步骤及错误提示。分享日志或截图前，请检查并遮盖个人路径等敏感信息。

微信搜一搜「**馋熊冰**」。微信联系和自愿赞赏入口也可在应用“关于”页面查看。

<table>
  <tr><th>微信搜一搜</th><th>微信赞赏</th></tr>
  <tr>
    <td align="center"><img src="src/CleanOptimize.App/Assets/wechat-search.jpg" alt="微信搜一搜：馋熊冰" width="280" /></td>
    <td align="center"><img src="src/CleanOptimize.App/Assets/wx_pay.png" alt="微信赞赏二维码" width="200" /></td>
  </tr>
</table>
