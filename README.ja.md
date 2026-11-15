<div align="center">

<img src="src/CleanOptimize.App/Assets/logo.png" alt="CleanOptimize ロゴ" width="80" />

# CleanOptimize for Windows

スキャン結果のプレビュー、リスクの説明、隔離ファイルの復元、ディスク使用量の分析に対応した Windows 向けクリーンアップ・システム管理ツール。

[![Release](https://img.shields.io/github/v/release/HZ-PRE/cleanwin?label=Release)](https://github.com/HZ-PRE/cleanwin/releases/latest)
[![Build](https://github.com/HZ-PRE/cleanwin/actions/workflows/verify.yml/badge.svg)](https://github.com/HZ-PRE/cleanwin/actions/workflows/verify.yml)
[![License](https://img.shields.io/badge/License-BSL--1.0-blue)](LICENSE)
[![Windows](https://img.shields.io/badge/Windows-10%20%2F%2011%20x64-0078D4)](#ダウンロードと使い方)
[![.NET](https://img.shields.io/badge/.NET-8-512BD4)](#ソースから実行)

[最新版をダウンロード](https://github.com/HZ-PRE/cleanwin/releases/latest) · [主な機能](#主な機能) · [ビルドとテスト](#ビルドとテスト) · [不具合の報告](https://github.com/HZ-PRE/cleanwin/issues)

[简体中文](README.md) | [English](README.en.md) | [繁體中文](README.zh-TW.md) | 日本語

</div>

## ダウンロードと使い方

**Windows 10 / Windows 11、x64** に対応しています。インストーラー版とポータブル版には .NET ランタイムが含まれるため、利用時に .NET を別途インストールする必要はありません。

[Releases](https://github.com/HZ-PRE/cleanwin/releases) ページで正式版を選び、添付ファイルをダウンロードしてください。

| ファイル | 用途 |
|---|---|
| `CleanOptimizeSetup-<バージョン>.exe` | セットアップウィザードとアンインストーラーを備えた完全版インストーラー |
| `CleanOptimize-<バージョン>-win-x64.zip` | ポータブル版。すべてのファイルを展開し、`CleanOptimize.exe` を実行 |
| `SHA256SUMS.txt` | インストーラーとポータブル版の SHA256 チェックサム |

初回はまずスキャンを実行し、対象項目、リスク、影響を確認してからクリーンアップを承認してください。残したいファイルやフォルダーはホワイトリストに追加できます。失敗した項目は未清掃の理由を表示する画面で、パス、原因、対処方法を確認できます。

## 主な機能

| 画面・機能 | 主な内容 |
|---|---|
| ホーム | ヘルススコア、スキャン概要、CPU・メモリ監視、ディスク容量、ドライブ切り替え、クイッククリーンアップ、最近の操作 |
| スマートスキャン | 複数カテゴリをスキャンし、リスク別に結果を表示。詳細のプレビュー、対象の選択、クリーンアップ結果の確認に対応 |
| 空き容量の確保 | システム一時ファイル、アプリとブラウザーのキャッシュ、プライバシー関連データ、ごみ箱、ユーザー指定フォルダー |
| ディスク分析 | フォルダー別使用量、大容量ファイルの検索、サイズ・種類による絞り込み、保存場所を開く操作、ごみ箱への移動。長いパスの詳細はスクロール表示 |
| スタートアップ | レジストリ、スタートアップフォルダー、ログオン時のスケジュールタスクを確認・管理。取得可能な場合は起動時間も表示 |
| アプリ管理 | インストール済みアプリとアイコンの表示、各アプリのアンインストーラーの起動、識別可能な残存データの検出 |
| ツールボックス | 隔離領域の管理、重複ファイル検出、空フォルダーの整理、ファイルの完全削除、清掃履歴、システムメンテナンス状態、Windows ツールへのアクセス |
| 設定と通知領域 | ライト・ダーク・システム連動テーマ、8 言語、ウィンドウ位置の保存、自動クリーンアップ、結果通知、バージョン更新 |

**クイッククリーンアップ**はホーム、通知領域のメニュー、コマンドパレットから利用できます。既定で選択対象となる低リスクのシステム・アプリ・ブラウザーキャッシュのみをスキャンし、結果の承認後に処理します。**自動クリーンアップ**は初期状態では無効です。有効にすると、アプリの実行中に設定した周期で動作します。結果通知は個別に切り替えられます。

ショートカット：`Ctrl+K` でコマンドパレットを開き、`Ctrl+1`～`Ctrl+8` で画面を切り替えます。`Esc` はキャンセル対応のスキャンを中止します。ホームが非表示になるとシステム監視のサンプリングを停止し、大量のファイル一覧には UI 仮想化を使用します。

## クリーンアップの保護機能とローカルデータ

- **対象範囲とホワイトリストの検証**：清掃ルールは許可されたフォルダーの範囲内で動作し、システムの重要フォルダーやユーザーフォルダーを保護します。重複ファイルの回収、空フォルダーの整理、ファイルの完全削除では、ユーザーが明示的に選んだ対象を個別に検証します。これによって自動清掃の対象範囲が広がることはありません。
- **リスクの説明**：低リスクの項目は既定で選択される場合があります。プライバシー関連データなど、確認が必要な項目は自動選択されません。処理前に説明と影響を確認できます。
- **隔離と復元**：隔離機能が有効な場合、復元に対応する清掃対象は隔離領域へ移動し、既定で 30 日間保持されます。隔離した容量と実際に解放した容量は別々に集計します。隔離への移動ではディスクの空き容量は増えません。
- **元に戻せない操作**：ファイルの完全削除、ごみ箱を空にする操作、隔離項目の永久削除は、本アプリでは復元できません。その他の清掃で復元できるかどうかは、ルールと隔離設定によって異なります。
- **パスの境界**：清掃では UNC パス、ネットワークドライブ、デバイスパスを拒否し、ディレクトリリンクを介して対象範囲を越えることを防ぎます。
- **失敗理由の表示**：ファイルの使用中、権限不足、パスの長さなどを区別し、対処方法を案内します。未清掃項目の詳細を優先して保持し、最大 1,000 件の理由と未清掃の総件数を表示します。

設定、履歴、隔離データは `%LOCALAPPDATA%\CleanOptimize` に保存します。アプリをアンインストールしてもこのデータは保持されるため、再インストール後に隔離ファイルの確認と復元ができます。

匿名統計とクラッシュ報告の設定項目は残していますが、現在のバージョンでは送信を無効にしています。利用履歴、ファイル名、パス、ファイル内容はアップロードしません。更新機能は GitHub に公開バージョン情報を問い合わせ、承認後に更新ファイルをダウンロードします。ローカルのスキャンと清掃はオフラインで利用できます。

## 更新の確認

起動時に公開正式版を確認します。新しいバージョンがある場合は、上部のロゴ横に赤い **NEW**、更新確認ボタンの横に更新可能の表示が出ます。表示をクリックすると更新ダイアログが開きます。更新を承認するとインストーラーをダウンロードし、SHA256 を検証してから起動します。

- バックグラウンドの確認結果は 5 分間キャッシュします。手動で更新を確認すると再取得し、同時に進行中のリクエストはまとめます。
- ダウンロードはキャンセルできます。ダウンロード、検証、インストーラーの起動に失敗しても、現在のアプリは終了しません。
- スキャン、清掃、アプリのアンインストールが進行中の場合は、処理を終えてから更新してください。
- 下書き、プレリリース、Actions のビルド成果物は正式更新の対象外です。同じバージョンや古いバージョンでは更新通知を表示しません。

**ビルドは成功したのに更新が見つからない場合**：[Releases](https://github.com/HZ-PRE/cleanwin/releases) に正式公開されていること、`CleanOptimizeSetup-<バージョン>.exe` と `SHA256SUMS.txt` のアップロードが完了していること、インストール済みのバージョンより新しいことを確認してください。旧ワークフローが作成した下書きは、**Publish release** を実行するまでクライアントから検出できません。

更新元は `HZ-PRE/cleanwin` の公開正式 Release です。SHA256 はファイルの整合性検証に使用します。既定の公開ワークフローでは自動コード署名を有効にしておらず、SignPath も連携していません。

## ビルドとテスト

### ソースから実行

開発には Windows と [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) が必要です。以下のコマンドは PowerShell で実行してください。

```powershell
git clone https://github.com/HZ-PRE/cleanwin.git
cd cleanwin
dotnet build ./src/CleanOptimize.App/CleanOptimize.App.csproj -c Release
./src/CleanOptimize.App/bin/Release/net8.0-windows/CleanOptimize.exe
```

WPF と Windows Forms を使用しています。アプリケーションコードにサードパーティーの NuGet パッケージ依存関係はありません。

### 自動検証

```powershell
# ビルド、通信処理箇所の検査、言語リソース検査、コア回帰テスト
./scripts/verify.ps1 -Configuration Debug
./scripts/verify.ps1 -Configuration Release

# アプリ全体のセルフテスト：コア、WPF UI、ウィンドウのライフサイクル
Start-Process ./src/CleanOptimize.App/bin/Release/net8.0-windows/CleanOptimize.exe `
    -ArgumentList '--selftest' -Wait
```

テストは独立した一時データフォルダーを使用し、削除保護、ホワイトリスト、隔離からの復元、使用中ファイル、重複ファイルの回収、キャンセルと並行処理、更新ファイルの検証、多言語・テーマのレイアウトを確認します。実機での清掃ルールのスキャン検証ではシステムの不要ファイルを削除せず、ファイル操作のテストでは専用サンプルを使用します。自動検証だけですべての端末の手動操作確認を代替することはできません。

### ローカルでのパッケージ作成

インストーラーの作成には [Inno Setup](https://jrsoftware.org/isinfo.php) も必要です。公開スクリプトはプロジェクトのバージョンを読み取り、ランタイムを含むインストーラー、ポータブル版、チェックサムファイルを生成します。公開用アプリのセルフテストと ZIP の整合性検証も実行します。

```powershell
./scripts/publish.ps1 -RequireInstaller

# ツールが PATH や既定のインストール先にない場合は、実際のパスを指定
./scripts/publish.ps1 -DotnetPath C:/tools/dotnet/dotnet.exe `
    -IsccPath C:/tools/Inno/ISCC.exe -RequireInstaller
```

成果物は `dist/` に出力します。開発・デバッグ用のフレームワーク依存ディレクトリも生成しますが、配布するインストーラーと ZIP はどちらもランタイムを含む自己完結型です。

ビルドキャッシュを削除し、Debug / Release の検証からパッケージ作成までを最初から実行するには：

```powershell
./scripts/full-test.ps1 -DotnetPath dotnet -IsccPath C:/tools/Inno/ISCC.exe
```

インストーラーとポータブル版の検証：

```powershell
./scripts/test-packages.ps1
```

このスクリプトはポータブル版を展開し、新しいテストフォルダーにインストールしてファイルを検証し、セルフテストとアプリ実行中のアンインストールを確認します。同じ製品がすでにインストールされている場合は、上書きを避けるため停止します。検証ログは `dist/` に保存します。

### バージョン設定と GitHub での公開

ローカルの既定バージョンは [CleanOptimize.App.csproj](src/CleanOptimize.App/CleanOptimize.App.csproj) の `<Version>` で設定します。`メジャー.マイナー.パッチ` 形式（例：`0.0.3`）を使用してください。`publish.ps1 -Version 0.0.3` で、そのパッケージ作成時のみバージョンを指定することもできます。

[Release ワークフロー](.github/workflows/release.yml)には、次の 2 つの実行方法があります。

| 実行方法 | 結果 |
|---|---|
| `v0.0.3` などのバージョンタグを push | タグのバージョンでビルド・検証し、インストーラー、ポータブル版、チェックサムをアップロード。すべてのアップロード成功後に Release を公開 |
| Actions → Release → Run workflow でブランチを選択 | ビルドして `CleanOptimize-windows` 成果物をアップロード。正式版は公開しない |

公開するコードを先にコミットして push し、そのコミットに未使用のバージョンタグを作成してください。`v0.0.3` を使用する例：

```powershell
git tag v0.0.3
git push origin v0.0.3
```

タグからのビルドでは、プロジェクトのバージョンをタグのバージョンで上書きします。ローカルとクラウドを一致させるため、プロジェクトファイルも更新することを推奨します。`v0.0.3-preview.1` のように接尾辞が付いたタグはプレリリースとなり、正式版クライアントには更新を通知しません。通常のブランチ push と PR では、[Verify ワークフロー](.github/workflows/verify.yml)がビルドとコア回帰テストを実行します。

## プロジェクト構成

```text
src/
  CleanOptimize.App/   WPF UI、テーマ、通知領域、更新サービス
  CleanOptimize.Core/  清掃ルール、保護機能、分析、ローカル保存
  SelfTestRunner/      コア自動テストのエントリーポイント
scripts/              ビルド、テスト、パッケージ、言語リソース生成スクリプト
.github/workflows/    GitHub Actions の検証・公開ワークフロー
```

## フィードバックとサポート

不具合や機能の提案は [Issues](https://github.com/HZ-PRE/cleanwin/issues) に投稿してください。アプリのバージョン、Windows のバージョン、再現手順、エラーメッセージを添えてください。ログやスクリーンショットを共有する前に、個人のファイルパスなどの機密情報を確認し、必要な部分を伏せてください。

WeChat で **馋熊冰** を検索してください。WeChat の連絡先と任意の寄付用コードは、アプリの「このアプリについて」画面からも確認できます。

<table>
  <tr><th>WeChat で検索</th><th>WeChat で寄付</th></tr>
  <tr>
    <td align="center"><img src="src/CleanOptimize.App/Assets/wechat-search.jpg" alt="WeChat で 馋熊冰 を検索" width="280" /></td>
    <td align="center"><img src="src/CleanOptimize.App/Assets/wx_pay.png" alt="WeChat 寄付用 QR コード" width="200" /></td>
  </tr>
</table>

## ライセンス

本プロジェクトは [Boost Software License 1.0（BSL-1.0）](LICENSE) を採用しており、商用利用、改変、再配布が認められています。ソースコードまたは派生したソースコードを配布する際は、著作権表示とライセンス全文を保持してください。詳細な条件と免責事項は [LICENSE](LICENSE) を参照してください。
