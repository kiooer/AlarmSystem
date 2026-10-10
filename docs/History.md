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
