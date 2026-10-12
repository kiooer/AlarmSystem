# AlarmSystem

圖控警報系統的 C#/.NET solution 骨架。

目前已建立通知共用契約、Teams webhook adapter、LINE Messaging API adapter 與 LINE 人工測試 UI；尚未實作 iFIX/ODBC、SQL Server、警報輪詢、去重、outbox、完整派送流程、CSV、保留清理或完整 WPF 管理功能。需求與開發約束請以根目錄的 `AGENTS.md` 為準。

## 專案

- `AlarmSystem.Domain`：領域模型與規則。
- `AlarmSystem.Application`：應用流程與外部介面抽象。
- `AlarmSystem.Infrastructure`：資料庫、iFIX、Teams、CSV 與安全 adapter。
- `AlarmSystem.Worker`：背景服務入口。
- `AlarmSystem.Desktop`：WPF 管理介面入口。
- `AlarmSystem.Domain.Tests`：領域單元測試。
- `AlarmSystem.Application.Tests`：應用層單元測試。
- `AlarmSystem.Desktop.Tests`：WPF LINE 測試工具的 ViewModel 與 service 測試。
- `AlarmSystem.IntegrationTests`：基礎設施與跨模組整合測試。

## 目前狀態

本機只有 .NET 10 SDK，因此骨架暫以 `net10.0` / `net10.0-windows` 建立。目標 .NET 與 Windows 版本仍屬待確認事項；開始功能開發前應先定案。

任何外部服務秘密都不會加入 repository 設定。

## Dongle License 與正式通知邊界

Worker 供目前及未來正式派送流程解析的 `IEnumerable<INotificationChannel>`，只包含經
`LicensedNotificationChannel` 包裝的 LINE 與 Teams 通道。每次正式發送都先檢查各自的
`NotificationFeature`；只有通道本身 `Enabled` 且對應 Dongle feature 授權成功時，才會進入樣板渲染及 HTTP
transport。LINE、Teams 的授權彼此獨立，並已預留 Email feature；目前沒有 Email adapter。

尚未取得 Dongle 廠商規格，因此目前 Worker 使用明確的 fail-closed authorizer，所有正式 LINE／Teams
發送都會在 transport 前回傳永久失敗 `license_dongle_unavailable`。此狀態不代表 Dongle SDK 已完成或硬體可用。
未授權結果只使用下列非敏感錯誤碼：

- `license_feature_not_licensed`
- `license_expired`
- `license_dongle_unavailable`
- `license_validation_failed`

`Licensing:Dongle` 只保存 provider 名稱、檢查 timeout 與 LINE／Teams／Email feature mapping 等非敏感設定。
repository 預設為 `Provider=Unconfigured` 且 feature code 留空，不包含序號、授權 blob 或虛構的 feature code。
正式整合前仍須由廠商提供並確認：

- SDK、runtime、driver 與可再散佈條款。
- LINE、Teams、Email 的正式 feature code 與授權內容格式。
- 簽章／真偽驗證方式、授權過期判定及系統時間異常的處理語意。
- Dongle 未插入、執行中拔除、重新插入、讀取 timeout 與 SDK 錯誤的回傳語意。
- Windows 與 Windows Server 支援版本、service account 存取權限，以及 x86／x64 架構與部署需求。

取得規格後應只替換 Infrastructure 的 Dongle authorizer，不改變 Application decorator、通知 transport 或 Desktop
測試工具的邊界。任何 Dongle 序號、原始授權內容及 SDK 原始回應都不得寫入 log、CSV 或版本控制。

## Teams 發報模組

目前已建立第一階段的 Teams webhook adapter：

- 從部署內容 `templates/teams-alarm-card.json` 載入並驗證 Adaptive Card 1.4 樣板。
- 透過 JSON DOM 注入事件資料，避免未跳脫的字串取代破壞 JSON。
- 將卡片包裝成 Teams webhook message payload。
- 將 HTTP 408、429、5xx、timeout 與傳輸錯誤分類為可重試；其他非成功 HTTP 狀態分類為永久失敗。
- 不記錄 endpoint、payload 或回應內容。

Teams 預設停用。啟用前須透過環境變數、.NET user-secrets 或正式環境核准的秘密儲存提供
`Notifications:Teams:Endpoint`，不得把 webhook URL 提交至 Git。實際 Teams 入口、認證方式、目的頻道、
Adaptive Card 支援版本與 rate limit 尚待確認，因此目前測試只使用本機 HTTP stub，不會傳送至正式 Teams。

## LINE 群組警報模組

LINE transport 是與 Teams 平行、可插拔的 `INotificationChannel` 實作，兩者直接接收同一份
`AlarmNotification`，但 LINE 不依賴 Teams renderer、Adaptive Card 或 Teams 設定。Worker 對正式流程只暴露各自的
授權 decorator，不把原始 transport 當作 `INotificationChannel` 註冊。LINE adapter
以獨立的 `templates/line-alarm-card.json` 樣板，將相同警報資料渲染為 LINE Flex Message Bubble，並呼叫 LINE Messaging API Push Message 的官方固定 HTTPS
endpoint。每次請求第一次就使用 `AlarmNotification.EventId` 的 D 格式作為 `X-Line-Retry-Key`，相同事件
重送時會維持相同 key。

LINE 預設停用；停用不影響 Teams。啟用前必須：

1. 在 LINE Official Account Manager 啟用 **Allow bot to join group chats**，並將 bot 加入目標群組。
2. 透過受控的外部或一次性流程取得目標 `GroupId`。本系統不接收 webhook，也不會自動發現、登錄或維護 `GroupId`。
3. 使用環境變數、.NET user-secrets 或部署環境核准的秘密儲存提供設定。不得把真實 `GroupId` 或 channel access token 提交至 Git。

部署時使用下列設定鍵；`GroupId` 與 `ChannelAccessToken` 的值不放在文件或 repository 範例中：

```text
Notifications__Line__Enabled=true
Notifications__Line__GroupId
Notifications__Line__ChannelAccessToken
Notifications__Line__TimeoutSeconds=10
Notifications__Line__TemplatePath=templates/line-alarm-card.json
```

本機開發可對 `src/AlarmSystem.Worker` 使用 .NET user-secrets 設定同名的冒號分隔鍵；正式部署則使用環境變數或核准的秘密儲存。不要在 shell history、文件或版本控制中留下實際值。

啟用時 `GroupId` 與 `ChannelAccessToken` 必須有值；timeout 必須大於零且不超過兩分鐘。repository
內的 `appsettings.json` 只保留停用且不含 token 的安全預設。Flex Bubble JSON 超過 30 KB 時會在呼叫 HTTP 前回傳永久失敗，不會截斷警報內容。樣板缺失或無效時同樣不會送出。樣板會複製到 Worker 與 Desktop 的輸出及發佈目錄；Windows Service 以執行檔目錄為基準載入預設相對路徑。原 `LineAlarmTextRenderer` 保留作相容功能，正式 LINE channel 發送 Flex Message。

目前 Worker 尚未具有警報輪詢、outbox、retry scheduler 或派送協調流程；此次只建立並註冊 LINE
adapter。也未包含配額／用量／月費管理、tenant 隔離、集中式 LINE gateway、webhook receiver、回覆訊息、
指令機器人或 rich menu。所有自動測試均使用本機 `HttpMessageHandler` stub，不會連線正式
LINE 或 Teams endpoint。

## LINE 發報測試 UI

Desktop 提供人工測試與診斷畫面，使用正式的 `LineFlexMessageRenderer` 與
`LineMessagingApiNotificationChannel`；它不會自行組裝 LINE HTTP request，也不是正式警報派送流程。啟動方式：

```text
dotnet run --project src/AlarmSystem.Desktop/AlarmSystem.Desktop.csproj
```

啟動後可輸入既有 LINE 群組的 `GroupId`、channel access token、timeout 與測試警報內容，預覽將送出的 Flex Bubble JSON。
按下發送前必須先勾選真實外部發送確認，接著在第二次確認對話框核對遮罩後的目的地與事件資料；取消任一階段都不會呼叫 LINE。發送中可按「取消」。

此互動式 Desktop 頁面是刻意隔離的手動測試／功能展示路徑，直接建立未包裝的 LINE transport，不檢查 Dongle
License；測試成功不代表正式發報已取得授權。此例外沒有 `DemoMode` 或 Worker 設定開關，也不可供 Worker、背景服務、
排程、API 或未來 outbox 使用。未來 Teams／Email 測試頁若建立，也必須維持相同的 Desktop-only 邊界。

此 UI 的發送會呼叫真實 LINE Messaging API，因此只有在 LINE Official Account 已啟用
**Allow bot to join group chats**、bot 已加入目標群組，而且操作者明確決定進行實際測試時才能使用。`GroupId`
與 token 必須由受控流程取得；測試 UI 不會持久化兩者，token 僅在按下發送時由 `PasswordBox` 傳入 service，
不會保存到設定、recent values 或 log，視窗關閉時也會清除。請勿把真實 `GroupId` 或 token 提交至 Git。

不連線 LINE 的人工 smoke test：

1. 啟動 Desktop，確認預設測試資料與訊息預覽正常顯示。
2. 不輸入 `GroupId` 或 token，確認發送按鈕不可用。
3. 輸入測試欄位但不勾選確認，確認仍不可發送。
4. 確認 token 欄位使用遮罩顯示，且取消確認對話框後沒有送出。
5. 關閉視窗，確認應用程式正常結束；token 不會保留。

測試 UI 不接收 webhook、不探索 `GroupId`，也不代表正式警報輪詢、outbox、retry scheduler 或派送協調已完成；配額、用量、tenant 與集中式 gateway 同樣不在範圍內。
