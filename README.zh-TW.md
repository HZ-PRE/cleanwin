<div align="center">

<img src="src/CleanOptimize.App/Assets/logo.png" alt="CleanOptimize 標誌" width="80" />

# CleanOptimize · 清理優化大師

Windows 清理與系統管理工具，支援掃描預覽、風險提示、隔離還原和磁碟空間分析。

[![Release](https://img.shields.io/github/v/release/HZ-PRE/cleanwin?label=Release)](https://github.com/HZ-PRE/cleanwin/releases/latest)
[![Build](https://github.com/HZ-PRE/cleanwin/actions/workflows/verify.yml/badge.svg)](https://github.com/HZ-PRE/cleanwin/actions/workflows/verify.yml)
[![License](https://img.shields.io/badge/License-BSL--1.0-blue)](LICENSE)
[![Windows](https://img.shields.io/badge/Windows-10%20%2F%2011%20x64-0078D4)](#下載與使用)
[![.NET](https://img.shields.io/badge/.NET-8-512BD4)](#從原始碼執行)

[下載最新版](https://github.com/HZ-PRE/cleanwin/releases/latest) · [功能介紹](#功能介紹) · [建置與測試](#建置與測試) · [問題回報](https://github.com/HZ-PRE/cleanwin/issues)

[简体中文](README.md) | [English](README.en.md) | 繁體中文 | [日本語](README.ja.md)

</div>

## 下載與使用

支援 **Windows 10 / Windows 11，x64**。安裝版與免安裝版均內含 .NET 執行階段，使用時無需另外安裝 .NET。

在 [Releases](https://github.com/HZ-PRE/cleanwin/releases) 頁面選擇正式版本，下載對應附件：

| 檔案 | 用途 |
|---|---|
| `CleanOptimizeSetup-<版本>.exe` | 完整安裝套件，提供安裝精靈與解除安裝程式 |
| `CleanOptimize-<版本>-win-x64.zip` | 免安裝版，完整解壓縮後執行 `CleanOptimize.exe` |
| `SHA256SUMS.txt` | 安裝套件與免安裝版的 SHA256 校驗值 |

首次使用建議先掃描，查看清理項目、風險及影響，再確認執行。需要保留的檔案或資料夾可加入白名單。遇到清理失敗，可開啟「查看未清理原因」，查看路徑、原因和處理建議。

## 功能介紹

| 模組 | 主要功能 |
|---|---|
| 首頁 | 健康評分、掃描摘要、CPU 與記憶體監控、磁碟容量、多磁碟切換、快捷清理和最近活動 |
| 智慧掃描 | 彙整多類清理規則，依風險顯示結果，支援預覽明細、選擇項目和查看清理結果 |
| 清理空間 | 系統暫存檔、應用程式快取、瀏覽器快取、隱私痕跡、資源回收筒及自訂清理資料夾 |
| 空間分析 | 資料夾佔用分析、大型檔案搜尋、大小與類型篩選、開啟位置和回收操作；長路徑詳細資料可捲動查看 |
| 啟動項目 | 查看和管理登錄、啟動資料夾及登入排程工作中的啟動項目，顯示可取得的開機耗時 |
| 應用程式管理 | 查看已安裝應用程式及圖示、呼叫應用程式的解除安裝程式、偵測可識別的解除安裝殘留 |
| 工具箱 | 隔離區管理、重複檔案偵測、空資料夾清理、檔案粉碎、清理歷史、系統維護狀態及 Windows 工具入口 |
| 設定與系統匣 | 深色 / 淺色 / 跟隨系統主題、8 種語言、視窗位置記憶、自動清理、結果通知及版本更新 |

**快捷清理**可從首頁、系統匣和命令面板進入，只掃描預設可選的低風險系統、應用程式和瀏覽器快取，確認結果後執行。**自動清理**預設關閉，啟用後在應用程式執行期間依設定週期執行，結果通知可獨立開關。

常用快速鍵：`Ctrl+K` 開啟命令面板，`Ctrl+1` 至 `Ctrl+8` 切換頁面，`Esc` 取消支援取消的掃描工作。首頁隱藏後停止系統監控取樣，大量檔案清單使用虛擬化顯示。

## 清理保護與資料儲存

- **範圍與白名單校驗**：清理規則受允許目錄、系統關鍵目錄和使用者目錄保護約束。重複檔案回收、空資料夾清理及檔案粉碎會對使用者明確選取的目標分別校驗，不會因此放寬自動清理範圍。
- **風險提示**：低風險項目可預設選取；隱私痕跡等需要確認的項目不會預設勾選。執行前可查看項目說明和影響。
- **隔離還原**：啟用隔離區後，支援還原的清理項目會移入隔離區，預設保留 30 天。隔離量與實際釋放空間分開統計，移入隔離區不等於已釋放磁碟空間。
- **永久操作**：檔案粉碎、清空資源回收筒、永久刪除隔離項目無法透過本應用程式還原；其他清理是否可還原取決於規則和隔離設定。
- **路徑邊界**：清理拒絕 UNC、網路磁碟和裝置路徑，並防止透過目錄連結跨越清理邊界。
- **失敗說明**：區分檔案佔用、權限不足、路徑過長等情況，提供處理建議。優先保留未清理明細，最多顯示 1,000 筆，並顯示未清理總數。

設定、歷史記錄和隔離資料儲存在 `%LOCALAPPDATA%\CleanOptimize`。解除安裝應用程式會保留這些資料，便於重新安裝後查看和還原隔離檔案。

匿名統計與當機報告選項仍保留，但目前版本停用上報，不上傳使用記錄、檔名、路徑或檔案內容。更新功能會向 GitHub 請求公開版本資訊，並在確認後下載更新檔案；本機掃描與清理無需連網。

## 檢查更新

啟動時檢查公開正式版本；發現較新版本後，在頂部 Logo 旁顯示紅色 **NEW**，在「檢查更新」旁顯示「可更新」。點擊標籤開啟更新視窗，選擇「確認更新」後下載安裝套件，校驗 SHA256，再啟動安裝程式。

- 背景檢查快取 5 分鐘；手動點擊「檢查更新」會重新請求，正在進行的請求會合併。
- 下載支援取消；下載、校驗或安裝程式啟動失敗時，目前應用程式保持執行。
- 正在掃描、清理或解除安裝應用程式時，需要先結束操作，再進行更新。
- 草稿、預覽版及 Actions 建置產物不屬於正式更新；相同或較低版本不會觸發更新提示。

**建置成功卻檢查不到更新？** 請確認版本已在 [Releases](https://github.com/HZ-PRE/cleanwin/releases) 公開發佈，附件包含已上傳完成的 `CleanOptimizeSetup-<版本>.exe` 和 `SHA256SUMS.txt`，且版本號高於本機版本。舊流程產生的草稿需要點擊 **Publish release** 後，客戶端才能偵測到。

目前更新來源為 `HZ-PRE/cleanwin` 的公開正式 Release。SHA256 用於校驗檔案完整性；預設發佈流程未啟用自動程式碼簽章，也未整合 SignPath。

## 建置與測試

### 從原始碼執行

開發環境需要 Windows 和 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。以下命令在 PowerShell 中執行：

```powershell
git clone https://github.com/HZ-PRE/cleanwin.git
cd cleanwin
dotnet build ./src/CleanOptimize.App/CleanOptimize.App.csproj -c Release
./src/CleanOptimize.App/bin/Release/net8.0-windows/CleanOptimize.exe
```

專案使用 WPF 與 Windows Forms，應用程式碼未引入第三方 NuGet 套件。

### 自動驗證

```powershell
# 建置、連網入口檢查、語言資源檢查和核心迴歸測試
./scripts/verify.ps1 -Configuration Debug
./scripts/verify.ps1 -Configuration Release

# 應用程式完整自測：包括核心檢查、WPF 介面與視窗生命週期檢查
Start-Process ./src/CleanOptimize.App/bin/Release/net8.0-windows/CleanOptimize.exe `
    -ArgumentList '--selftest' -Wait
```

測試使用獨立暫存資料目錄，涵蓋刪除保護、白名單、隔離還原、佔用檔案、重複檔案回收、取消與並行、更新校驗、多語言及主題配置。真實清理規則的機器掃描檢查不執行系統垃圾刪除；檔案操作測試使用測試樣本。自動檢查不能取代所有裝置上的人工互動驗證。

### 本機打包

產生安裝套件還需要 [Inno Setup](https://jrsoftware.org/isinfo.php)。發佈指令碼讀取專案版本號，產生內含執行階段的安裝套件、免安裝版及校驗檔案，並執行發佈程式自測和 ZIP 完整性校驗。

```powershell
./scripts/publish.ps1 -RequireInstaller

# 工具不在 PATH 或預設安裝位置時，指定實際路徑
./scripts/publish.ps1 -DotnetPath C:/tools/dotnet/dotnet.exe `
    -IsccPath C:/tools/Inno/ISCC.exe -RequireInstaller
```

產物位於 `dist/`。指令碼同時產生供開發偵錯使用的框架相依目錄，對外散佈的安裝套件和 ZIP 均使用內含執行階段的自包含版本。

從零清除編譯快取、執行 Debug / Release 驗證並打包：

```powershell
./scripts/full-test.ps1 -DotnetPath dotnet -IsccPath C:/tools/Inno/ISCC.exe
```

安裝套件與免安裝版驗收：

```powershell
./scripts/test-packages.ps1
```

此指令碼會解壓縮免安裝版、安裝到新的測試目錄、校驗安裝檔案、執行自測，並測試執行中的解除安裝；若偵測到本機已安裝同一產品，會停止以避免覆蓋。驗證記錄儲存在 `dist/`。

### 修改版本與 GitHub 發佈

本機預設版本在 [CleanOptimize.App.csproj](src/CleanOptimize.App/CleanOptimize.App.csproj) 的 `<Version>` 中設定，格式為 `主版本.次版本.修訂號`，例如 `0.0.3`。也可以使用 `publish.ps1 -Version 0.0.3` 暫時指定打包版本。

[Release 工作流程](.github/workflows/release.yml)支援兩種執行方式：

| 方式 | 結果 |
|---|---|
| 推送版本標籤，如 `v0.0.3` | 以標籤版本建置和驗證，上傳安裝套件、免安裝版與校驗檔案；附件上傳成功後公開發佈 Release |
| Actions → Release → Run workflow，選擇分支 | 建置並上傳 `CleanOptimize-windows` 產物，不公開發佈正式版本 |

先提交並推送準備發佈的程式碼，再於該提交建立一個尚未使用的版本標籤。以下以 `v0.0.3` 為例：

```powershell
git tag v0.0.3
git push origin v0.0.3
```

標籤建置使用標籤中的版本號覆蓋專案版本，因此建議同時更新專案檔案，保持本機與雲端版本一致。帶有後綴的標籤（如 `v0.0.3-preview.1`）會發佈為預覽版，不向正式版客戶端提示更新。一般分支推送和 PR 由 [Verify 工作流程](.github/workflows/verify.yml)執行建置及核心迴歸檢查。

## 專案結構

```text
src/
  CleanOptimize.App/   WPF 介面、主題、系統匣與更新服務
  CleanOptimize.Core/  清理規則、安全保護、分析與本機儲存
  SelfTestRunner/      核心自動測試入口
scripts/              建置、測試、打包與語言資源產生指令碼
.github/workflows/    GitHub Actions 驗證和發佈流程
```

## 回饋與支持

問題和功能建議請提交至 [Issues](https://github.com/HZ-PRE/cleanwin/issues)，附上應用程式版本、Windows 版本、重現步驟及錯誤提示。分享記錄或截圖前，請檢查並遮蓋個人路徑等敏感資訊。

使用微信搜尋「**馋熊冰**」。微信聯絡與自願贊賞入口也可在應用程式「關於」頁面查看。

<table>
  <tr><th>微信搜尋</th><th>微信贊賞</th></tr>
  <tr>
    <td align="center"><img src="src/CleanOptimize.App/Assets/wechat-search.jpg" alt="微信搜尋：馋熊冰" width="280" /></td>
    <td align="center"><img src="src/CleanOptimize.App/Assets/wx_pay.png" alt="微信贊賞 QR 碼" width="200" /></td>
  </tr>
</table>

## 授權條款

本專案採用 [Boost Software License 1.0（BSL-1.0）](LICENSE)，允許商業使用、修改與散佈。散佈原始碼或其衍生原始碼時，須保留著作權聲明及授權條款全文。完整條件與免責聲明請參閱 [LICENSE](LICENSE)。
