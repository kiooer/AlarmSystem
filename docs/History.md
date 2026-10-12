# 開發歷程

本文件記錄圖控警報系統每次完成提交（commit）的開發內容，供版本追蹤、測試回溯與交付查核使用。

## 紀錄規則

- 每次建立 commit 前，必須先在本文件最上方新增一筆紀錄，最新紀錄置頂。
- 每筆紀錄至少包含版本、日期、時間、commit、編撰檔案名稱、內容、功能、測試結果及尚待確認事項。
- 日期與時間使用 `Asia/Taipei`，格式為 `YYYY-MM-DD HH:mm:ss UTC+08:00`。
- 只記錄實際完成並經檔案或測試證實的內容，不得把規劃項目描述為既有成果。
- 若紀錄與程式變更位於較早的 commit，填寫其短雜湊值與訊息。
- 若紀錄本身包含在即將建立的同一個 commit，標示為「本次提交」並記錄訊息；實際雜湊值以 Git history 為準，避免因修改自身內容而無限改變 commit hash。
- 不得在本文件記錄 connection string 密碼、webhook URL、token 或其他秘密。

---

## 0.0.4 — 補充 LINE Flex renderer 註解

| 項目 | 內容 |
|---|---|
| 日期 | 2026-10-12 |
| 時間 | 08:21:00 UTC+08:00（Asia/Taipei） |
| Commit | 本次提交 — `補充LINE Flex renderer註解`（實際 hash 以 Git history 為準） |
| 編撰者 | `Codex` |

### 編撰檔案名稱

- `src/AlarmSystem.Infrastructure/Notifications/Line/LineFlexMessageRenderer.cs`
- `docs/History.md`

### 開發內容與功能

- 為 `LineFlexMessageRenderer.Render` 補充 XML 文件註解，說明輸入、輸出、例外情境，以及每次渲染使用深拷貝與 JSON DOM 填值的安全保證。

### 測試與驗證

- `dotnet build src/AlarmSystem.Infrastructure/AlarmSystem.Infrastructure.csproj --no-restore`：命令回報失敗，但輸出為 0 warning、0 error，未提供可診斷原因。
- `git diff --check`：於提交前執行。

### 設定、相容性與尚待確認

- 僅文件註解變更；沒有程式行為、資料庫 schema、設定、部署、秘密或相容性變更。

---

## 0.0.3 — Line 警報卡片及 WPF 發送測試

| 項目 | 內容 |
|---|---|
| 日期 | 2026-10-11 |
| 時間 | 23:44:26 UTC+08:00（Asia/Taipei） |
| Commit | 本次提交 — `Line警報卡片及WPF發送測試成功`（實際 hash 以 Git history 為準） |
| 編撰者 | `Codex` |

### 編撰檔案名稱

- `AGENTS.md`、`README.md`、`docs/History.md`、`templates/line-alarm-card.json`
- `src/AlarmSystem.Infrastructure/Notifications/Line/LineFlexMessageRenderer.cs`、`LineFlexMessageTemplateProvider.cs`、`LineMessagingApiNotificationChannel.cs`、`LineOptions.cs`
- `src/AlarmSystem.Worker/AlarmSystem.Worker.csproj`、`Program.cs`、`appsettings.json`
- `src/AlarmSystem.Desktop/AlarmSystem.Desktop.csproj`、`App.xaml.cs`、`Resources/Strings.cs`、`Resources/Strings.resx`、`Services/LineNotificationTestService.cs`、`ViewModels/LineNotificationTestViewModel.cs`
- `tests/AlarmSystem.IntegrationTests/AlarmSystem.IntegrationTests.csproj`、`Notifications/Line/LineFlexMessageRendererTests.cs`、`LineMessagingApiNotificationChannelTests.cs`、`LineOptionsTests.cs`
- `tests/AlarmSystem.Desktop.Tests/AlarmSystem.Desktop.Tests.csproj`、`Services/LineNotificationTestServiceTests.cs`、`ViewModels/LineNotificationTestViewModelTests.cs`

### 開發內容與功能

- 將既有 LINE Push Message 正式通道改為 Flex Message；Teams 與 LINE 仍直接共用 `AlarmNotification` 等既有通知契約，原 LINE 純文字 renderer 保留作相容功能。
- 新增獨立 Flex Bubble JSON 樣板、JSON DOM renderer 與可重新載入的樣板 provider；依警報等級及事件狀態填入色碼、欄位與完整 EventId，防止超過 Bubble 30 KB 上限或無效樣板時發送 HTTP。
- Worker 與 Desktop 載入同一份樣板；專案設定將樣板複製至 build／publish 輸出。WPF 人工測試 UI 改為預覽 Flex Bubble，發送仍透過正式 LINE channel、保留原有秘密保護及人工確認機制。
- 更新 LINE 設定、README 操作說明與 AGENTS 開發基線，未修改 Teams 樣板或 Teams 發送程式。

### 測試與驗證

- `dotnet restore AlarmSystem.sln`：成功；NuGet 弱點資料來源無法連線，出現 `NU1900` 警告。
- `dotnet build AlarmSystem.sln --no-restore --verbosity minimal -m:1`：成功，0 error；同樣有 `NU1900` 警告。
- `dotnet test AlarmSystem.sln --no-build --no-restore --verbosity minimal -m:1`：111 passed、0 failed、0 skipped（整合測試 82、Desktop 測試 29）。
- `dotnet format AlarmSystem.sln --verify-no-changes --no-restore` 與 `git diff --check`：通過。
- Worker `dotnet publish` 成功，發佈目錄中的 LINE 樣板與來源雜湊一致；變更檔案的常見秘密模式掃描未發現真實 token、Bearer 值或 GroupId。
- 自動 HTTP 測試使用本機 stub；未進行真實 LINE 發送或正式 LINE 用戶端卡片驗收。

### 設定、相容性與尚待確認

- LINE 新增無秘密的 `TemplatePath` 設定；部署時仍需安全提供 GroupId 與 channel access token。正式 LINE 發報格式由純文字改為 Flex Message，接收端顯示需在實際 LINE 群組人工驗收。
- 無資料庫 schema、migration、polling、outbox 或正式派送協調流程變更；Webhook、配額、tenant 與中央 gateway 仍未實作。

---

## 0.0.2 — 加入 LINE 發送及測試 WPF

| 項目 | 內容 |
|---|---|
| 日期 | 2026-10-11 |
| 時間 | 19:32:07 UTC+08:00（Asia/Taipei） |
| Commit | 本次提交 — `加入line發送及測試WPF`（實際 hash 以 Git history 為準） |
| 編撰者 | `Codex` |

### 編撰檔案名稱

- `AGENTS.md`、`AlarmSystem.sln`、`README.md`、`docs/History.md`
- `src/AlarmSystem.Infrastructure/Notifications/Line/LineOptions.cs`
- `src/AlarmSystem.Infrastructure/Notifications/Line/LineAlarmTextRenderer.cs`
- `src/AlarmSystem.Infrastructure/Notifications/Line/LineMessagingApiNotificationChannel.cs`
- `src/AlarmSystem.Worker/Program.cs`、`src/AlarmSystem.Worker/appsettings.json`
- `src/AlarmSystem.Desktop/AlarmSystem.Desktop.csproj`、`App.xaml`、`App.xaml.cs`、`AssemblyInfo.cs`、`MainWindow.xaml`、`MainWindow.xaml.cs`
- `src/AlarmSystem.Desktop/Resources/Strings.cs`、`Strings.resx`
- `src/AlarmSystem.Desktop/Services/ILineNotificationTestService.cs`、`ILineSendConfirmationService.cs`、`LineNotificationTestService.cs`、`LineSendConfirmationService.cs`
- `src/AlarmSystem.Desktop/ViewModels/LineNotificationTestViewModel.cs`、`RelayCommand.cs`
- `tests/AlarmSystem.IntegrationTests/Notifications/Line/LineOptionsTests.cs`、`LineAlarmTextRendererTests.cs`、`LineMessagingApiNotificationChannelTests.cs`
- `tests/AlarmSystem.Desktop.Tests/AlarmSystem.Desktop.Tests.csproj`、`Services/LineNotificationTestServiceTests.cs`、`ViewModels/LineNotificationTestViewModelTests.cs`

### 開發內容與功能

- 新增可插拔的 LINE Messaging API Push Message 通道，直接使用既有 `AlarmNotification`、`INotificationChannel` 與 `NotificationSendResult`；Teams 與 LINE 分別註冊於 Worker。
- 新增 LINE 繁體中文純文字 renderer、5,000 UTF-16 code units 上限、固定官方 HTTPS endpoint、以 EventId 產生的穩定 retry key、HTTP 錯誤及取消分類。
- 新增 WPF 人工測試畫面，使用正式 LINE renderer 與 channel 預覽和發送；提供輸入驗證、遮罩目的地、雙重確認、非同步發送、取消、結果分類與繁體中文 `.resx` 資源。
- token 僅在發送時由 `PasswordBox` 取得；UI 不持久化 token 或 GroupId，視窗關閉時清除密碼欄位。
- 更新 README 的部署設定、測試 UI 操作及功能邊界，並更新 AGENTS 的 LINE 實作基線。

### 測試與驗證

- `dotnet restore AlarmSystem.sln`：成功；NuGet 弱點資料來源無法連線，出現 `NU1900` 警告。
- `dotnet build AlarmSystem.sln --no-restore --verbosity minimal -m:1`：成功，0 error；同樣有 `NU1900` 警告。
- `dotnet test AlarmSystem.sln --no-build --no-restore --verbosity minimal -m:1`：93 passed、0 failed、0 skipped（整合測試 65、Desktop 測試 28）。
- `dotnet format AlarmSystem.sln --verify-no-changes --no-restore` 與 `git diff --check`：通過。
- Desktop 在未輸入 token 的情況下可啟動；自動 HTTP 測試全部使用本機 stub，未向真實 LINE 或 Teams 發送。
- 變更內容的常見 token、Bearer 值與 GroupId 模式掃描未發現真實秘密。

### 設定、相容性與尚待確認

- Worker 新增預設停用、無 token 的 `Notifications:Line` 設定；Desktop 新增 .NET 10 相容的 DI／HttpClientFactory 套件與 `net10.0-windows` 測試專案。
- 沒有資料庫 schema、migration 或正式部署變更。正式 LINE 發送仍需人工安全提供有效的 GroupId 與 channel access token，並完成目標群組驗收。
- 警報輪詢、outbox、retry scheduler、正式派送協調、Webhook、配額與多客戶管理尚未實作。

---

## 0.0.1 — 調整 Teams JSON 卡片

| 項目 | 內容 |
|---|---|
| 日期 | 2026-10-10 |
| 時間 | 19:42:17 UTC+08:00（Asia/Taipei） |
| Commit | 本次提交 — `調整json卡片`（實際 hash 以 Git history 為準） |
| 編撰者 | `kiooer` |

### 編撰檔案名稱

- `docs/History.md`
- `templates/teams-alarm-card.json`
- `src/AlarmSystem.Application/Abstractions/Notifications/AlarmNotification.cs`
- `src/AlarmSystem.Infrastructure/Notifications/Teams/TeamsAdaptiveCardRenderer.cs`
- `tests/AlarmSystem.IntegrationTests/Notifications/Teams/TeamsAdaptiveCardRendererTests.cs`

### 開發內容與功能

- 依核准參考圖片重排 Teams Adaptive Card，加入警報圖示、設備警報通知標題及 severity badge。
- 卡片資訊調整為區域、設備、Tag、警報內容、發生時間及目前狀態。
- 新增 Alarm ID、發報系統 `AlarmGateway` 及「請立即確認設備狀況」提醒區塊。
- `AlarmNotification` 新增可選的 `Equipment` 欄位；空白設備顯示為 `—`。
- severity 同時對應文字顏色及容器樣式：HIGH／Attention、MEDIUM／Warning、LOW／Good。
- 事件狀態顯示 mapping：`ALARM -> Active / UnACK`、`ACK -> Acknowledged`。
- 保持 Adaptive Card 1.4，並繼續使用 JSON DOM 安全注入事件內容。

### 測試與驗證

- JSON 由 PowerShell `ConvertFrom-Json` 解析成功。
- `dotnet build AlarmSystem.sln --no-restore --verbosity minimal -m:1`：成功，0 warning、0 error。
- `dotnet test AlarmSystem.sln --no-build --no-restore --verbosity minimal -m:1`：22 passed、0 failed、0 skipped。
- 測試新增設備顯示、severity 容器樣式及 ALARM／ACK 狀態 mapping 驗證。
- 本次修改檔案 `dotnet format --verify-no-changes`：通過。
- `git diff --check`：通過。

### 設定、相容性與尚待確認

- 沒有資料庫 schema、migration、部署或秘密設定變更。
- Adaptive Card 1.4 無法保證完全重現圖片中的圓角紅色外框與膠囊 badge；實際呈現仍受 Teams 用戶端 renderer 影響。
- 尚未使用真實 Teams endpoint 驗證桌面版、網頁版及手機版的最終卡片外觀。

---

## 0.0.1 — 建立開發歷程制度

| 項目 | 內容 |
|---|---|
| 日期 | 2026-10-10 |
| 時間 | 19:19:12 UTC+08:00（Asia/Taipei） |
| Commit | 本次提交 — `增加開發紀錄`（實際 hash 以 Git history 為準） |
| 編撰者 | `kiooer` |

### 編撰檔案名稱

- `docs/History.md`
- `AGENTS.md`

### 開發內容與功能

- 建立專案開發歷程文件與固定紀錄格式。
- 補登版本 `0.0.1` 的 Teams 發報功能、編撰檔案、測試結果、設定變更及未決事項。
- 規定後續每次建立 commit 前都必須先新增歷程紀錄。
- 明確規定歷程不得包含 webhook URL、token、密碼或其他秘密。
- 定義同一 commit 內的歷程紀錄方式，避免 commit hash 自我引用造成無限變更。

### 測試與驗證

- `git diff --check`：通過。
- 本次只修改 Markdown 文件及開發規範，未執行 build、test 或 migration dry-run。

### 設定、相容性與尚待確認

- 沒有程式行為、資料庫 schema、應用程式設定、部署、秘密或相容性變更。

---

## 0.0.1 — Teams 發報功能

| 項目 | 內容 |
|---|---|
| 日期 | 2026-10-10 |
| 時間 | 19:11:55 UTC+08:00（Asia/Taipei） |
| Commit | `6787fa2` — `Teams發報功能撰寫` |
| 編撰者 | `kiooer` |
| 變更規模 | 20 個檔案，新增 915 行、刪除 3 行 |

### 編撰檔案名稱

#### 文件

- `README.md`

#### Domain

- `src/AlarmSystem.Domain/Events/AlarmEventType.cs`
- `src/AlarmSystem.Domain/Events/AlarmSeverity.cs`

#### Application

- `src/AlarmSystem.Application/Abstractions/Notifications/AlarmNotification.cs`
- `src/AlarmSystem.Application/Abstractions/Notifications/INotificationChannel.cs`
- `src/AlarmSystem.Application/Abstractions/Notifications/NotificationSendResult.cs`

#### Infrastructure

- `src/AlarmSystem.Infrastructure/AlarmSystem.Infrastructure.csproj`
- `src/AlarmSystem.Infrastructure/Notifications/Teams/TeamsAdaptiveCardRenderer.cs`
- `src/AlarmSystem.Infrastructure/Notifications/Teams/TeamsCardTemplateProvider.cs`
- `src/AlarmSystem.Infrastructure/Notifications/Teams/TeamsOptions.cs`
- `src/AlarmSystem.Infrastructure/Notifications/Teams/TeamsWebhookNotificationChannel.cs`

#### Worker 與 Desktop

- `src/AlarmSystem.Worker/AlarmSystem.Worker.csproj`
- `src/AlarmSystem.Worker/Program.cs`
- `src/AlarmSystem.Worker/appsettings.json`
- `src/AlarmSystem.Desktop/App.xaml.cs`

#### 測試

- `tests/AlarmSystem.IntegrationTests/AlarmSystem.IntegrationTests.csproj`
- `tests/AlarmSystem.IntegrationTests/Notifications/Teams/TeamsAdaptiveCardRendererTests.cs`
- `tests/AlarmSystem.IntegrationTests/Notifications/Teams/TeamsCardTemplateProviderTests.cs`
- `tests/AlarmSystem.IntegrationTests/Notifications/Teams/TeamsOptionsTests.cs`
- `tests/AlarmSystem.IntegrationTests/Notifications/Teams/TeamsWebhookNotificationChannelTests.cs`

### 開發內容與功能

- 建立 `ALARM`／`ACK` 事件類型與 `HIGH`／`MEDIUM`／`LOW` 嚴重程度 enum。
- 建立通用通知通道介面、警報通知模型及派送結果模型。
- 建立 Teams Adaptive Card 1.4 樣板解析、必要 placeholder 驗證及安全 JSON DOM 渲染。
- 支援事件 ID、Tag、Area、事件類型、severity、訊息及含 offset 的來源時間顯示。
- 套用暫定顏色 mapping：`HIGH -> Attention`、`MEDIUM -> Warning`、`LOW -> Good`。
- 建立可重新載入的 Teams 樣板 provider；樣板不存在或無效時將通道標示為 unhealthy，不讓 Worker 因樣板錯誤停止。
- 建立 Teams webhook 通知 adapter，以 Adaptive Card attachment message envelope 傳送 JSON。
- 將 HTTP 408、429、5xx、timeout 及傳輸錯誤分類為可重試；其他非成功狀態分類為永久失敗。
- 使用 `HttpClientFactory`、HTTPS endpoint 驗證、timeout 與取消權杖。
- Teams 預設停用；repository 內未保存 webhook URL、token 或其他秘密。
- 將 Teams 樣板複製至 Worker build／publish 輸出目錄。
- 修正 WPF 骨架中 `Application` 命名衝突，使 solution 可完整建置。

### 測試與驗證

- `dotnet restore AlarmSystem.sln`：成功。
- `dotnet build AlarmSystem.sln --no-restore --verbosity minimal -m:1`：成功，0 warning、0 error。
- `dotnet test AlarmSystem.sln --no-build --no-restore --verbosity minimal -m:1`：Teams 整合測試 21 passed、0 failed、0 skipped。
- 測試涵蓋特殊字元、Unicode、換行、空值、三種 severity 顏色、無效樣板、樣板恢復、成功 payload、HTTP 408／429／5xx／400、timeout、傳輸失敗、停用狀態與呼叫端取消。
- `git diff --check`：通過。
- 本次 Teams 相關 C# 檔案 `dotnet format --verify-no-changes`：通過。
- 常見秘密字串掃描：未發現實際 webhook URL、token 或密碼。

### 設定、相容性與尚待確認

- 新增 `Microsoft.Extensions.Http 10.0.12` 套件參考。
- 新增 `Notifications:Teams` 設定區段；`Endpoint` 必須由 user-secrets、環境變數或核准的秘密儲存提供。
- Adaptive Card 版本暫定為 `1.4`；目標 Teams 租戶相容性尚未實際驗收。
- Teams 實際入口、認證方式、目的頻道、正式 rate limit 與 Workflow／Webhook 方案尚待確認。
- 尚未使用真實 Teams endpoint 進行發送測試。
- outbox、持久化 delivery ledger、重試排程及警報輪詢尚未實作。
- 沒有資料庫 schema、migration 或部署變更。
