# AlarmSystem

圖控警報系統的 C#/.NET solution 骨架。

目前已建立通知共用契約、Teams webhook adapter、LINE Messaging API adapter、Email SMTP adapter，以及 LINE／Email 人工測試 UI；尚未實作 iFIX/ODBC、SQL Server、警報輪詢、去重、outbox、完整派送流程、CSV、保留清理或完整 WPF 管理功能。需求與開發約束請以根目錄的 `AGENTS.md` 為準。

## 專案

- `AlarmSystem.Domain`：領域模型與規則。
- `AlarmSystem.Application`：應用流程與外部介面抽象。
- `AlarmSystem.Infrastructure`：Teams、LINE、Email SMTP、未來資料庫／iFIX／CSV 與安全 adapter。
- `AlarmSystem.Worker`：背景服務入口。
- `AlarmSystem.Desktop`：WPF 管理介面入口。
- `AlarmSystem.Domain.Tests`：領域單元測試。
- `AlarmSystem.Application.Tests`：應用層單元測試。
- `AlarmSystem.Desktop.Tests`：WPF LINE／Email 測試工具的 ViewModel 與 service 測試。
- `AlarmSystem.IntegrationTests`：基礎設施與跨模組整合測試。

## 目前狀態

本機只有 .NET 10 SDK，因此骨架暫以 `net10.0` / `net10.0-windows` 建立。目標 .NET 與 Windows 版本仍屬待確認事項；開始功能開發前應先定案。

任何外部服務秘密都不會加入 repository 設定。

## Dongle License 與正式通知邊界

Worker 供目前及未來正式派送流程解析的 `IEnumerable<INotificationChannel>`，只包含經
`LicensedNotificationChannel` 包裝的 LINE、Teams 與 Email 通道。每次正式發送都先檢查各自的
`NotificationFeature`；只有通道本身 `Enabled` 且對應 Dongle feature 授權成功時，才會進入樣板渲染及 HTTP
或 SMTP transport。LINE、Teams、Email 的授權彼此獨立；Email 使用既有數值不變的
`NotificationFeature.Email`，沒有建立第二套授權契約。

尚未取得 Dongle 廠商規格，因此目前 Worker 使用明確的 fail-closed authorizer，所有正式 LINE／Teams／Email
發送都會在 transport 前回傳永久失敗 `license_dongle_unavailable`。Desktop 的互動式手動測試／展示頁是刻意隔離的例外，
不檢查 Dongle；此例外不代表 Dongle SDK 已完成、硬體可用或正式自動發報已取得授權。
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

## Email SMTP 警報模組

Email 是與 Teams／LINE 平行的 `INotificationChannel`。正式 Worker 只把
`LicensedNotificationChannel(NotificationFeature.Email)` 暴露給 `IEnumerable<INotificationChannel>`；原始
`EmailSmtpNotificationChannel` 僅以具體型別註冊供 decorator 使用。授權拒絕或授權檢查非取消例外時，不會讀取
Email 樣板、建立 SMTP client、連線或寄送。因目前 provider 固定 fail closed，即使 Email 設定完整且 `Enabled=true`，
也會先得到 `license_dongle_unavailable`，不代表已具備真實 Dongle 發送能力。

SMTP transport 使用 MailKit／MimeKit。`Standard` 設定模式以 Port 作為唯一安全來源：587 固定使用
`StartTls`（要求 STARTTLS），465 固定使用 `SslTls`（MailKit `SslOnConnect`，連線即 TLS）；設定驗證會拒絕其他
Port／安全模式組合。`Advanced` 模式保留自訂 Port 與三種安全模式：`StartTls`、`SslTls`、`None`。所有模式都不會在
TLS 失敗後自動降級。`None` 只允許搭配
`AllowUnencryptedInternalRelay=true`、`AuthenticationMode=None` 的明確內部 Relay 設定；部署人員仍須確認該 host
確實位於核准的內部網路。密碼驗證只允許用於加密模式。Email 可設定多位收件人，對外只透過
`DestinationReference` 顯示收件群組代碼，不以此欄位暴露完整收件清單。

部署設定位於 `Notifications:Email`。repository 預設停用且不包含 `Password`；正式密碼只可由環境變數
`Notifications__Email__Password`、.NET user-secrets 或核准的秘密儲存提供。以下欄位皆為鍵名示例，不包含可用主機、
帳號、地址或密碼：

```text
Notifications__Email__Enabled=true
Notifications__Email__ConfigurationMode=Standard
Notifications__Email__Host
Notifications__Email__Port=587
Notifications__Email__AllowUnencryptedInternalRelay=false
Notifications__Email__AuthenticationMode=Password
Notifications__Email__Username
Notifications__Email__Password
Notifications__Email__SenderAddress
Notifications__Email__SenderDisplayName=AlarmSystem
Notifications__Email__Recipients__0
Notifications__Email__DestinationReference
Notifications__Email__TimeoutSeconds=15
Notifications__Email__TemplatePath=templates/email-alarm.html
```

`ConfigurationMode=Standard` 時 Worker 與 Desktop 一樣依 Port 自動決定安全模式，無須另設 `SecurityMode`。
`ConfigurationMode=Advanced` 時才讀取 `SecurityMode` 並允許特殊 Port。為保持既有部署相容，舊設定若沒有
`ConfigurationMode`，會視為 `Advanced`，原有特殊 Port 與安全模式不會被改寫；部署人員確認可採標準規則後，應明確改為
`Standard`。未加密 Relay 即使在 `Advanced` 仍必須明確設定允許，且只能使用 `AuthenticationMode=None`。

啟用時會驗證 Host、Port、傳輸安全、驗證模式、寄件者、至少一位收件人、非敏感群組代碼，以及 1–120 秒的
timeout；同一個 `TimeoutSeconds` 分別套用於 Connect、Authenticate 與 Send 階段。Disconnect 使用獨立的 5 秒清理上限，
不沿用已取消的主要操作 token，也不新增設定欄位。設定不完整時 Worker 會在啟動組裝階段拒絕設定，不會嘗試連線。HTML 內容由獨立的
`templates/email-alarm.html` 載入，所有警報動態文字均先做 HTML encoding，並同時建立純文字備援。樣板缺失、
placeholder 不完整或訊息無效時回傳永久失敗，不會建立 SMTP client。Email adapter 只允許 `ALARM`；`ACK`
回傳 `email_ack_not_notified` 且不連線。尚未實作的正式 Dispatcher 未來仍須負責新警報、未 ACK 重發條件、上限、
去重及 outbox，adapter 本身不承擔排程。

SMTP 會分別記錄 Connect（含 MailKit 在 `ConnectAsync` 內完成的 STARTTLS／TLS 協商）、Authenticate、Send 與
Disconnect 階段，結構化診斷只包含 correlation ID、受控分類、外層／最深層／驗證例外類型、SMTP／Socket／native
狀態碼、host、port、實際 `SecureSocketOptions`、耗時、取消與 timeout 狀態。它不記錄帳號、密碼、AUTH 資料、
郵件本文、附件、原始例外訊息或完整 protocol transcript。Connect／Authenticate 階段的連線、
協定或 I/O 暫時故障、timeout 與 SMTP 4xx 維持可重試；驗證失敗、TLS／不支援設定、郵件格式錯誤與 SMTP 5xx
為永久失敗。

`SendAsync` 正常返回後即確認主要寄送成功；後續 QUIT／Disconnect 或 Dispose 失敗只記錄
`email_smtp_disconnect_warning`，不會把成功改成失敗或觸發重送。若 Send 階段因 timeout、Socket、I/O、協定或連線中斷
而無法確認伺服器是否接受郵件，則回傳永久失敗 `email_smtp_send_status_unknown`，要求人工確認後再決定是否重送。
SMTP 沒有 LINE retry key 等伺服器端冪等保證；未來 Dispatcher／delivery ledger 必須保留此狀態，不得把它當成一般
可重試傳輸錯誤直接重送。

## LINE 發報測試 UI

Desktop 提供人工測試與診斷畫面，使用正式的 `LineFlexMessageRenderer` 與
`LineMessagingApiNotificationChannel`；它不會自行組裝 LINE HTTP request，也不是正式警報派送流程。啟動方式：

```text
dotnet run --project src/AlarmSystem.Desktop/AlarmSystem.Desktop.csproj
```

啟動後可輸入既有 LINE 群組的 `GroupId`、channel access token、timeout 與測試警報內容，預覽將送出的 Flex Bubble JSON。
按下發送前必須先勾選真實外部發送確認，接著在第二次確認對話框核對遮罩後的目的地與事件資料；取消任一階段都不會呼叫 LINE。發送中可按「取消」。

LINE 與 Email 分頁都是刻意隔離的手動測試／功能展示路徑，直接建立各自未包裝的 transport，不檢查 Dongle
License；測試成功不代表正式發報已取得授權。未來 Desktop 互動式測試頁亦採相同邊界。此例外沒有 `DemoMode`
或 Worker 設定開關，也不可供 Worker、背景服務、排程、API 或未來 outbox 使用；Worker 的正式通道仍一律透過
`LicensedNotificationChannel`。

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

## Email SMTP 測試 UI

以同一個 Desktop 命令啟動後切換至「Email SMTP 測試」分頁。一般模式只需選擇 Port 587 或 465，畫面會立即以唯讀欄位
顯示對應的 STARTTLS 或 SSL/TLS，操作人員不能自行建立不相容組合。只有展開「進階 SMTP 設定」後，才能輸入特殊 Port、
選擇安全模式及明確確認內部未加密 Relay；收合進階設定會恢復標準 Port／TLS 對應並清除未加密 Relay 確認，但不清除帳號、
寄件者或收件人。可另外輸入驗證方式、
寄件者、收件人、收件群組代碼、timeout 與測試 ALARM，並預覽主旨、純文字與 HTML。密碼保留在 `PasswordBox`，
只在按下測試或寄送時傳入 service，不寫入 ViewModel、設定或 log，關閉視窗時清除。

「測試 SMTP 連線」只要求 SMTP host／port、安全模式、驗證帳密與 timeout，會執行 connect、必要的 authenticate
與 disconnect，絕不呼叫 SMTP send；完成、失敗、逾時、取消或設定驗證失敗都會立即顯示本地化結果對話框，
並顯示失敗／警告階段、安全分類、例外類型、Socket 狀態、階段耗時與 correlation ID。QUIT 失敗會顯示非致命警告，
不會推翻已成功完成的連線驗證或郵件寄送結果。
寄件者、收件人與群組代碼只在實際寄送時驗證。「寄送測試 Email」
另需勾選真實寄送確認，再於對話框確認非敏感群組代碼、寄件者、收件人數、EventId 與 Tag。兩種操作都可取消，
且只存在於 Desktop 互動式測試路徑；它們直接使用原始 Email transport，不檢查 Dongle License。正式 Worker 自動發報
仍需同時滿足 Email Enabled、有效 SMTP 設定及 `NotificationFeature.Email` 授權。

不連線 SMTP 的人工 smoke test：

1. 啟動 Desktop 並切換 Email 分頁，確認 HTML／純文字預覽與 ALARM-only 警語可見。
2. 確認頁面顯示手動測試不檢查 Dongle、且測試成功不代表正式發報授權的警語。
3. 確認密碼欄位遮罩顯示；缺少必要欄位、密碼或人工確認時，按下操作按鈕會顯示驗證訊息且不會連線或寄送。
4. 關閉視窗並重新開啟，確認 SMTP 密碼與輸入設定沒有被保存。

所有 Email 自動測試使用記憶體內的 SMTP fake；IPv4 transport 測試另以注入的 DNS 與 Socket connect stub 驗證
IPv6 過濾、多個 IPv4 輪替及資源釋放，不會連線或寄送至任何真實 SMTP server。

### MailKit SMTP 獨立連線診斷

`tools/AlarmSystem.SmtpDiagnostic` 直接使用正式環境相同的 IPv4-only MailKit transport factory 執行 `ConnectAsync`；不經 WPF、
`EmailSmtpNotificationChannel`、Dongle gate、SMTP 驗證或寄信。它只輸出安全診斷欄位，不啟用 protocol logger，
也不接受帳號或密碼。預設為 Gmail 465／`SslOnConnect`／60 秒：

transport 會以原始 host 進行 DNS 解析，只保留 IPv4 並依序嘗試；TCP 成功後把已連線的 IPv4 Socket 與原始 host
交給 MailKit，因此 TLS 主機名稱及憑證驗證仍以原始 host 為準，不會固定 IP、不使用 IPv6，也不會在 TLS 失敗時降級。

```text
dotnet run --project tools/AlarmSystem.SmtpDiagnostic -- smtp.gmail.com 465 SslOnConnect 60
dotnet run --project tools/AlarmSystem.SmtpDiagnostic -- smtp.gmail.com 587 StartTls 60
```

輸出包含 MailKit／.NET 版本、程序架構、host、實際 IPv4、port、`SecureSocketOptions`、`IsConnected`／`IsSecure`、耗時、
外層與最深層例外型別、驗證例外型別、Socket／native／SMTP status code，以及 timeout／呼叫端取消狀態。
Connect 成功後以獨立 5 秒上限執行 QUIT；QUIT 失敗只列為 `DisconnectWarning`，不改寫 Connect 結果。

若受限 Windows token 下得到 native code `-2146893042`（`0x8009030E`／`SEC_E_NO_CREDENTIALS`），但在一般互動式
Windows token 下成功，代表 Schannel 無法在受限程序取得 TLS client credential；這不是 Gmail 密碼錯誤，也不能以
停用憑證驗證或降級明文修正。請從一般 PowerShell／Windows Service 的實際服務帳號執行工具與 Desktop 交叉測試，
並以相同 .NET runtime、host、port、安全模式與 timeout 比較結果。
