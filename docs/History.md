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

## 0.0.9 — SMTP Port 自動連動 TLS 與進階 Relay 設定

| 項目 | 內容 |
|---|---|
| 日期 | 2026-10-13 |
| 時間 | 16:33:43 UTC+08:00（Asia/Taipei） |
| Commit | 本次提交 — `SMTP Port 自動連動 TLS 與進階 Relay 設定`（實際 hash 以 Git history 為準） |
| 編撰者 | `Codex` |

### 編撰檔案名稱

- `README.md`、`docs/History.md`
- `src/AlarmSystem.Desktop/MainWindow.xaml`
- `src/AlarmSystem.Desktop/Resources/Strings.cs`、`Strings.resx`
- `src/AlarmSystem.Desktop/ViewModels/EmailNotificationTestViewModel.cs`
- `src/AlarmSystem.Infrastructure/Notifications/Email/EmailOptions.cs`
- `src/AlarmSystem.Infrastructure/Notifications/Email/EmailSmtpConfigurationMode.cs`
- `src/AlarmSystem.Infrastructure/Notifications/Email/EmailSmtpPortSecurityPolicy.cs`
- `src/AlarmSystem.Worker/Program.cs`、`appsettings.json`
- `tests/AlarmSystem.Desktop.Tests/ViewModels/EmailNotificationTestViewModelTests.cs`
- `tests/AlarmSystem.IntegrationTests/Notifications/Email/EmailOptionsTests.cs`

### 開發內容與功能

- 新增一般與進階 SMTP 設定模式，將標準 Port 與安全模式集中為 `587 -> StartTls`、`465 -> SslTls`（MailKit `SslOnConnect`）的共用政策；Desktop 連線測試、實際寄送與 Worker 正式通道均使用相同設定模型及底層驗證。
- Desktop 一般模式改以 587／465 下拉選單操作，安全模式僅顯示且會隨 Port 即時更新；未加密 Relay 選項移至進階設定，特殊 Port、安全模式與明確 Relay 確認仍可供核准的內部 SMTP 環境使用。
- 一般模式拒絕自訂 Port、不相容安全模式、`SecurityMode=None` 及 Relay 確認旗標；進階模式仍要求未加密 Relay 必須明確確認且不可使用密碼驗證，不提供 STARTTLS 失敗後降級明文的行為。
- 模式切換保留 SMTP 帳號、寄件者、收件者及進階 Port／安全模式，不會自動啟用未加密 Relay；離開明文 Relay 條件或切換模式時清除 Relay 確認。
- Worker 預設設定新增 `ConfigurationMode=Standard`；舊設定未包含此欄位時採 `Advanced` 相容模式，保留既有特殊 Port 與明確安全模式，避免靜默覆蓋客戶設定。

### 測試與驗證

- `dotnet build AlarmSystem.sln --no-restore --disable-build-servers -m:1 -nr:false -v:minimal`：成功，0 warning、0 error。
- `dotnet test AlarmSystem.sln --no-build --no-restore --disable-build-servers -m:1 -nr:false -v:minimal`：255 passed、0 failed、0 skipped（Application 18、Integration 176、Desktop 61）；Domain 測試專案目前沒有測試案例。
- `dotnet format AlarmSystem.sln --no-restore --verify-no-changes --verbosity minimal`：通過。
- `git diff --check`、Desktop XAML／RESX XML 解析、Worker `appsettings.json` JSON 解析及變更內容秘密掃描：通過。
- 使用者已完成 Desktop UI 人工測試並確認操作正常；本次自動驗證未連線真實 SMTP、LINE、Teams 或 Dongle 裝置。

### 設定、相容性與尚待確認

- `Notifications:Email` 新增非敏感的 `ConfigurationMode` 設定；新部署預設為 `Standard`，舊部署缺少欄位時維持 `Advanced` 相容行為。Desktop 目前沒有正式設定持久化機制，UI 模式與欄位只在目前視窗生命週期內保存。
- 未修改 IPv4 only SMTP transport、MailKit 寄送流程、TLS 憑證驗證、QUIT 警告處理、Email 錯誤碼、Desktop 手動測試授權例外或 Worker Dongle License 保護；未修改 LINE／Teams、共用警報契約、資料庫 schema 或 migration。
- 未提交 SMTP 密碼、App Password、授權金鑰、token、郵件本文或其他秘密；未追蹤的架構圖不納入本次提交。

---

## 0.0.8 — 寄件功能完成及 SMTP 測試完成

| 項目 | 內容 |
|---|---|
| 日期 | 2026-10-13 |
| 時間 | 13:47:03 UTC+08:00（Asia/Taipei） |
| Commit | 本次提交 — `寄件功能完成及smtp測試完成`（實際 hash 以 Git history 為準） |
| 編撰者 | `Codex` |

### 編撰檔案名稱

- `docs/History.md`
- `src/AlarmSystem.Desktop/Resources/Strings.cs`、`Strings.resx`
- `src/AlarmSystem.Desktop/ViewModels/EmailNotificationTestViewModel.cs`
- `src/AlarmSystem.Infrastructure/Notifications/Email/EmailSmtpDiagnostics.cs`
- `tests/AlarmSystem.Desktop.Tests/ViewModels/EmailNotificationTestViewModelTests.cs`
- `tests/AlarmSystem.IntegrationTests/Notifications/Email/EmailOptionsTests.cs`

### 開發內容與功能

- 將 Desktop 的 SMTP 連線測試與實際 Email 寄送測試統一由同一個設定映射建立 `EmailOptions`；兩條路徑共用 Host、Port、安全模式、未加密 Relay 控制、驗證模式、帳號、密碼與 timeout，寄送路徑再加入寄件者、收件人及收件群組代碼。
- 保留連線與寄送的不同驗證範圍：連線測試不要求寄件資料；寄送設定驗證失敗改顯示寄件者、收件人與群組代碼提示，不再讓加密 SMTP 因通用訊息而誤顯示「未加密 Relay 必須明確確認」。
- 每次 SMTP 連線或寄送操作均建立新的 Correlation ID 並先清除前次結果；本機設定驗證失敗會回報 `PermanentFailure`、`email_configuration_invalid`、`Validation` 階段及受控的設定錯誤診斷，不再沿用前次成功狀態、錯誤碼或 Correlation ID。
- 保留 Desktop 人工測試不檢查 Dongle License 的既有邊界；正式 Worker Email 通道仍由 `LicensedNotificationChannel(NotificationFeature.Email)` 保護。IPv4 only、TLS 憑證驗證與成功寄送後 QUIT 警告不覆蓋成功結果的行為未變更。

### 測試與驗證

- `dotnet build AlarmSystem.sln --no-restore --disable-build-servers -m:1 -nr:false -v:minimal`：成功，0 warning、0 error。
- `dotnet test AlarmSystem.sln --no-build --no-restore --disable-build-servers -m:1 -nr:false -v:minimal`：237 passed、0 failed、0 skipped（Application 18、Integration 168、Desktop 51）；Domain 測試專案目前沒有測試案例。
- `dotnet format AlarmSystem.sln --no-restore --verify-no-changes --verbosity minimal`：通過；沙箱內因 named pipe 權限失敗，允許必要權限後完成驗證。
- `git diff --check`、Desktop `Strings.resx` XML 解析：通過；僅顯示 `EmailSmtpDiagnostics.cs` 的 LF／CRLF 工作目錄轉換提示。
- 使用者已於 Desktop UI 完成人工驗收：SMTP 連線測試成功，Port 587 與 Port 465 均成功寄送，收件信箱已收到郵件且顯示正常。驗收紀錄不包含 SMTP 密碼、App Password、完整地址或郵件本文。

### 設定、相容性與尚待確認

- Port 465 維持 `SslOnConnect`，Port 587 維持 `StartTls`；未取消未加密 Relay 的明確確認規則，亦未停用 TLS、憑證名稱或信任鏈驗證。
- 沒有新增或變更 SMTP 設定鍵、正式授權失敗碼、LINE／Teams transport、共用通知契約、資料庫 schema 或 migration；未新增秘密保存機制，也未提交 SMTP 密碼、App Password、Dongle 金鑰或其他秘密。
- Desktop 實機驗收證實目前互動式測試環境可連線、驗證與寄送；正式 Worker 自動發報仍待實際 Dongle Provider、正式部署身分、SMTP 正式設定及未來 Dispatcher／outbox 流程完成後另行驗收。
- 未追蹤的架構圖不納入本次提交。

---

## 0.0.7 — 修正 Email SMTP 測試介面－待測試（後續提交）

| 項目 | 內容 |
|---|---|
| 日期 | 2026-10-13 |
| 時間 | 11:02:12 UTC+08:00（Asia/Taipei） |
| Commit | 本次提交 — `修正 Email SMTP 測試介面-待測試`（實際 hash 以 Git history 為準） |
| 編撰者 | `Codex` |

### 編撰檔案名稱

- `AlarmSystem.sln`、`README.md`、`docs/History.md`
- `src/AlarmSystem.Desktop/MainWindow.xaml`
- `src/AlarmSystem.Desktop/Resources/Strings.cs`、`Strings.resx`
- `src/AlarmSystem.Desktop/Services/IEmailNotificationTestService.cs`、`EmailNotificationTestService.cs`
- `src/AlarmSystem.Desktop/ViewModels/EmailNotificationTestViewModel.cs`
- `src/AlarmSystem.Infrastructure/AlarmSystem.Infrastructure.csproj`
- `src/AlarmSystem.Infrastructure/Notifications/Email/IEmailSmtpClient.cs`、`MailKitEmailSmtpClient.cs`
- `src/AlarmSystem.Infrastructure/Notifications/Email/EmailSmtpDiagnostics.cs`、`EmailSmtpErrorClassifier.cs`、`EmailSmtpNotificationChannel.cs`
- `src/AlarmSystem.Infrastructure/Properties/AssemblyInfo.cs`
- `tools/AlarmSystem.SmtpDiagnostic/AlarmSystem.SmtpDiagnostic.csproj`、`Program.cs`
- `tests/AlarmSystem.Desktop.Tests/Services/EmailNotificationTestServiceTests.cs`
- `tests/AlarmSystem.Desktop.Tests/ViewModels/EmailNotificationTestViewModelTests.cs`
- `tests/AlarmSystem.IntegrationTests/Notifications/Email/EmailSmtpNotificationChannelTests.cs`
- `tests/AlarmSystem.IntegrationTests/Notifications/Email/EmailSmtpIpv4SocketConnectorTests.cs`

### 開發內容與功能

- 強化 Email SMTP 測試與正式 transport 的受控診斷，記錄階段、例外型別、最深層例外、Socket／native／SMTP status code、原始 host、實際 IPv4、port、安全模式、耗時、取消與 timeout 狀態；不記錄密碼、AUTH 資料或郵件本文。
- 區分連線、驗證、寄送與中斷階段；SMTP QUIT／Dispose 失敗只留下 `email_smtp_disconnect_warning`，不覆蓋已完成的連線測試或成功寄送結果；寄送中斷且結果不明仍回傳 `email_smtp_send_status_unknown`。
- 新增獨立 `AlarmSystem.SmtpDiagnostic` 工具，直接使用與 Desktop／Worker 相同的 MailKit transport factory，不經 WPF、Email channel、Dongle、SMTP 驗證或寄信，可安全比對 MailKit、.NET、執行架構與連線例外。
- Email SMTP transport 改為 IPv4 only：以原始 host 做 DNS 解析，只保留 IPv4，使用 IPv4 TCP Socket 依序嘗試全部地址，再把已連線 Socket、原始 host、port 與 `SecureSocketOptions` 交給 MailKit；不使用 IPv6、不固定 Gmail IP，也不降低 TLS／憑證驗證。
- DNS 沒有 IPv4 時新增明確的可重試錯誤 `email_smtp_ipv4_address_not_found`；既有 `email_timeout`、`email_smtp_transport_error`、TLS、驗證及 SMTP command 錯誤碼維持相容。
- WPF Email 測試頁顯示安全的 SMTP 階段診斷與實際 IPv4；互動式測試仍直接使用原始 Email transport、不檢查 Dongle，正式 Worker Email 通道仍使用 `LicensedNotificationChannel(NotificationFeature.Email)`。

### 測試與驗證

- `dotnet build AlarmSystem.sln --no-restore -m:1 -v:minimal`：成功，0 warning、0 error。
- `dotnet test AlarmSystem.sln --no-build --no-restore -m:1 -v:minimal`：232 passed、0 failed、0 skipped（Application 18、Integration 166、Desktop 48）；Domain 測試專案目前沒有測試案例。
- `dotnet format AlarmSystem.sln --no-restore --verify-no-changes --verbosity minimal`：通過；沙箱內因 named pipe 權限失敗，允許必要權限後完成驗證。
- `git diff --check`：通過，僅顯示既有工作目錄的 LF／CRLF 轉換提示。
- 自動測試以注入的 DNS／Socket connect stub 與 `IEmailSmtpClient` fake 驗證 IPv6 過濾、IPv4 輪替、無 IPv4、timeout、取消、錯誤分類、Socket 釋放、TLS callback 未被覆寫、QUIT 警告及 UI 呈現；沒有寄送真實郵件。
- 以獨立工具實際測試 `smtp.gmail.com:465 / SslOnConnect` 與 `:587 / StartTls`：兩者均解析並完成 IPv4 TCP 連線，但 MailKit TLS 握手回傳 `email_smtp_tls_failed`、`MailKit.Security.SslHandshakeException` 與 native code `-2146893042`，因此安全 SMTP 連線與實際寄送仍標示為待測試，未宣稱通過。

### 設定、相容性與尚待確認

- 沒有新增 IPv4／IPv6 切換設定；Email transport 固定使用 IPv4 only。Port 465 仍使用 `SslOnConnect`，Port 587 仍使用 `StartTls`，沒有停用 TLS、忽略憑證錯誤或自動降級明文。
- `AlarmSystem.Infrastructure` 新增 logging abstractions 參考供結構化安全診斷使用；沒有變更既有 SMTP 設定鍵、郵件模型、LINE／Teams transport、Dongle License 邊界、資料庫 schema 或 migration。
- 尚須在實際 Desktop 使用者及 Windows Service 帳號環境驗證 TLS、憑證、SMTP 驗證與寄送；目前實機結果只證實 IPv4 DNS／TCP 路徑已執行，不代表 Gmail 登入或郵件寄送成功。
- 本次未提交 SMTP 密碼、App Password、Dongle 金鑰、AUTH 內容、郵件本文或其他秘密；未追蹤的架構圖不納入本次提交。

---

## 0.0.7 — 修正 Email SMTP 測試介面

| 項目 | 內容 |
|---|---|
| 日期 | 2026-10-12 |
| 時間 | 15:30:48 UTC+08:00（Asia/Taipei） |
| Commit | 本次提交 — `修正Email SMTP測試介面`（實際 hash 以 Git history 為準） |
| 編撰者 | `Codex` |

### 編撰檔案名稱

- `README.md`、`docs/History.md`
- `src/AlarmSystem.Desktop/App.xaml.cs`、`MainWindow.xaml`、`MainWindow.xaml.cs`
- `src/AlarmSystem.Desktop/Resources/Strings.cs`、`Strings.resx`
- `src/AlarmSystem.Desktop/Services/IEmailNotificationTestService.cs`、`EmailNotificationTestService.cs`
- `src/AlarmSystem.Desktop/Services/IEmailTestResultDialogService.cs`、`EmailTestResultDialogService.cs`
- `src/AlarmSystem.Desktop/ViewModels/EmailNotificationTestViewModel.cs`
- `src/AlarmSystem.Infrastructure/Notifications/Email/EmailOptions.cs`、`EmailSmtpNotificationChannel.cs`
- `tests/AlarmSystem.Desktop.Tests/Services/EmailNotificationTestServiceTests.cs`
- `tests/AlarmSystem.Desktop.Tests/ViewModels/EmailNotificationTestViewModelTests.cs`
- `tests/AlarmSystem.IntegrationTests/Notifications/Email/EmailOptionsTests.cs`、`EmailSmtpNotificationChannelTests.cs`

### 開發內容與功能

- 將 Desktop Email SMTP 互動式測試／展示頁調整為與 LINE 測試頁相同的授權例外：直接使用原始 Email transport，不注入或檢查 Dongle authorizer；Worker 正式 Email 通道仍由 `LicensedNotificationChannel(NotificationFeature.Email)` 保護。
- 移除 Email 測試頁的授權狀態與重新檢查授權控制，更新本地化警語，明示測試成功不代表正式自動發報已取得授權；沒有新增 `DemoMode`、環境變數或 Worker 授權繞過開關。
- 修正唯讀 `PreviewText` 的 WPF binding 為 `OneWay`，避免 Desktop 啟動時因預設 TwoWay binding 對唯讀屬性拋出例外。
- 讓 SMTP 連線測試與 Email 寄送測試按鈕在非忙碌狀態可操作；設定缺漏時改為顯示明確驗證結果，實際寄送仍保留勾選確認與第二次確認對話框。
- 將 SMTP 連線設定驗證與郵件內容／目的地驗證分離；連線測試只要求 host、port、安全模式、驗證帳密與 timeout，不要求寄件者、收件人或群組代碼，也不呼叫 SMTP send。
- 新增本地化 SMTP 連線測試結果對話框；成功、設定驗證失敗、SMTP 失敗、timeout 與取消都會立即提示，且不顯示密碼或完整敏感設定。
- Email channel 對無效設定改回傳永久失敗 `email_configuration_invalid`，不建立 SMTP client；timeout 仍回傳可重試的 `email_timeout`。

### 測試與驗證

- `dotnet build AlarmSystem.sln --no-restore --disable-build-servers -m:1 -nr:false`：成功，0 warning、0 error。
- `dotnet test AlarmSystem.sln --no-restore --disable-build-servers -m:1 -nr:false`：205 passed、0 failed、0 skipped（Application 18、Integration 142、Desktop 45）；Domain 測試專案目前沒有測試案例。
- `dotnet format AlarmSystem.sln --verify-no-changes --no-restore --verbosity minimal`：通過；沙箱內因 named pipe 權限失敗，允許必要權限後完成驗證。
- `git diff --check`：通過；僅既有未納入提交的 `AGENTS.md` 換行提示。
- SMTP 自動測試使用記憶體內 `IEmailSmtpClient` fake，LINE／Teams 回歸測試使用本機 stub；未連線真實 SMTP、LINE、Teams 或 Dongle 裝置，未寄送郵件。

### 設定、相容性與尚待確認

- Desktop Email 手動測試的授權邊界變更為不檢查 Dongle，僅限互動式測試／展示路徑；正式 Worker、背景服務、排程、API 與未來 outbox 不得使用此例外。
- SMTP 連線測試不再要求郵件寄送欄位；實際 Email 寄送仍驗證寄件者、收件人、群組代碼、模板與 ALARM 事件資料，並維持密碼不持久化及關閉視窗清除行為。
- 真實 SMTP host／port、安全模式、企業 Relay、防火牆、憑證信任與帳號政策仍須由部署環境確認；`email_timeout` 表示連線、TLS、驗證或中斷流程未在設定時間內完成。
- 沒有修改 Worker DI、NotificationFeature 數值、正式 License 失敗碼、LINE／Teams 核心 transport、資料庫 schema 或 migration；沒有提交 SMTP 密碼、Dongle 金鑰或其他秘密。

---

## 0.0.6 — Email SMTP 通知與授權測試介面

| 項目 | 內容 |
|---|---|
| 日期 | 2026-10-12 |
| 時間 | 11:30:03 UTC+08:00（Asia/Taipei） |
| Commit | 本次提交 — `加入email發報功能`（實際 hash 以 Git history 為準） |
| 編撰者 | `Codex` |

### 編撰檔案名稱

- `README.md`、`docs/History.md`、`templates/email-alarm.html`
- `src/AlarmSystem.Infrastructure/AlarmSystem.Infrastructure.csproj`
- `src/AlarmSystem.Infrastructure/Notifications/Email/EmailOptions.cs`、`EmailSecurityMode.cs`、`EmailAuthenticationMode.cs`
- `src/AlarmSystem.Infrastructure/Notifications/Email/EmailMessageContent.cs`、`EmailMessageBuilder.cs`、`EmailTemplateProvider.cs`
- `src/AlarmSystem.Infrastructure/Notifications/Email/IEmailSmtpClient.cs`、`MailKitEmailSmtpClient.cs`、`EmailSmtpErrorClassifier.cs`、`EmailSmtpNotificationChannel.cs`
- `src/AlarmSystem.Worker/AlarmSystem.Worker.csproj`、`Program.cs`、`appsettings.json`
- `src/AlarmSystem.Desktop/AlarmSystem.Desktop.csproj`、`App.xaml.cs`、`MainWindow.xaml`、`MainWindow.xaml.cs`
- `src/AlarmSystem.Desktop/Resources/Strings.cs`、`Strings.resx`
- `src/AlarmSystem.Desktop/Services/IEmailNotificationTestService.cs`、`EmailNotificationTestService.cs`、`IEmailSendConfirmationService.cs`、`EmailSendConfirmationService.cs`
- `src/AlarmSystem.Desktop/ViewModels/EmailNotificationTestViewModel.cs`
- `tests/AlarmSystem.Application.Tests/Notifications/LicensedNotificationChannelTests.cs`
- `tests/AlarmSystem.IntegrationTests/AlarmSystem.IntegrationTests.csproj`、`Notifications/Email/EmailOptionsTests.cs`、`EmailTemplateProviderTests.cs`、`EmailSmtpNotificationChannelTests.cs`、`LicensedEmailNotificationChannelTests.cs`
- `tests/AlarmSystem.Desktop.Tests/AlarmSystem.Desktop.Tests.csproj`、`Services/EmailNotificationTestServiceTests.cs`、`ViewModels/EmailNotificationTestViewModelTests.cs`

### 開發內容與功能

- 新增 MailKit／MimeKit SMTP adapter，直接實作既有 `INotificationChannel`，支援 STARTTLS、連線即 TLS、明確允許的無驗證內部未加密 Relay、帳號密碼驗證、多收件人、HTML 與純文字 alternative body、timeout 及呼叫端取消。
- 新增獨立 HTML 樣板、placeholder 完整性檢查與 Email message builder；所有警報動態 HTML 文字均先編碼，HIGH／MEDIUM／LOW 使用受控色碼，樣板缺失或無效時不建立 SMTP client。
- Email adapter 僅接受 `ALARM`；`ACK` 回傳 `email_ack_not_notified` 且不連線。SMTP 4xx、timeout 與傳輸錯誤分類為可重試，SMTP 5xx、驗證、TLS、格式與設定錯誤分類為永久失敗。
- Worker 註冊原始 Email concrete transport，供 `IEnumerable<INotificationChannel>` 解析的則是現有 `LicensedNotificationChannel` 與 `NotificationFeature.Email` 包裝；Email 授權失敗不影響 LINE／Teams 的獨立 feature。
- Desktop 新增 Email SMTP 分頁、樣板預覽、授權狀態、無寄信連線測試、雙重人工確認與實際測試寄送。Email 連線與寄送均受既有 License Gate 控制；LINE 分頁原有的 Desktop-only 授權例外不變。
- SMTP 密碼只由 `PasswordBox` 在操作時傳入，不加入可 binding 的 ViewModel 字串屬性、不持久化，並在關閉視窗時清除。

### 測試與驗證

- `dotnet restore AlarmSystem.sln`：成功，所有專案均為最新狀態。
- `dotnet build AlarmSystem.sln`：成功，0 warning、0 error。
- `dotnet test AlarmSystem.sln`：204 passed、0 failed、0 skipped（Application 18、Integration 140、Desktop 46）；Domain 測試專案目前沒有測試案例。
- `dotnet format AlarmSystem.sln --verify-no-changes --no-restore --verbosity minimal`：通過；沙箱內首次執行因 named pipe 權限失敗，允許必要權限後完成驗證。
- `git diff --check`、Worker `appsettings.json` JSON 解析、Desktop `Strings.resx`／`MainWindow.xaml` XML 解析：通過。
- Email SMTP 測試全部使用記憶體內 `IEmailSmtpClient` fake；LINE／Teams 既有 HTTP 測試維持本機 `HttpMessageHandler` stub。未連線真實 SMTP、LINE、Teams 或 Dongle 裝置，未執行 migration dry-run，因本次沒有 schema 或 migration。

### 設定、相容性與尚待確認

- Infrastructure 新增 `MailKit` 4.18.1 套件參考；Worker 新增預設停用、無密碼的 `Notifications:Email` 設定與 Email 樣板輸出／發佈內容。SMTP 密碼仍須由環境變數、user-secrets 或核准的秘密儲存提供。
- 正式 Email 發送條件為通道 `Enabled`、Email Dongle feature 授權、SMTP 設定有效三者同時成立。現有 `UnavailableDongleLicenseAuthorizer` 仍會回傳 `license_dongle_unavailable`，沒有新增正式環境繞過開關。
- SMTP 沒有 LINE retry key 等伺服器端冪等保證；伺服器接受後若回應或斷線結果不明，未來重試可能重複寄送，須由正式 Dispatcher／delivery ledger 留存每次嘗試並提供人工判讀。
- 待完成真實 Dongle SDK／driver／Email feature code、SMTP 正式 host 與網路政策、憑證信任、寄件網域與 relay 規則、收件群組、流量限制及目標環境驗收；亦尚未實作正式 Dispatcher、未 ACK 重發判斷、上限、outbox 或 retry scheduler。
- 沒有修改共用警報／`NotificationSendResult` 契約、既有 NotificationFeature 數值、LINE／Teams 核心 transport、資料庫 schema 或 migration；沒有提交 SMTP 密碼、Dongle 金鑰或其他秘密。

---

## 0.0.5 — Dongle License 正式通知閘門

| 項目 | 內容 |
|---|---|
| 日期 | 2026-10-12 |
| 時間 | 10:32:54 UTC+08:00（Asia/Taipei） |
| Commit | 本次提交 — `加入授權管控`（實際 hash 以 Git history 為準） |
| 編撰者 | `Codex` |

### 編撰檔案名稱

- `README.md`、`docs/History.md`
- `src/AlarmSystem.Application/Abstractions/Licensing/NotificationFeature.cs`、`LicenseAuthorizationFailureCodes.cs`、`LicenseAuthorizationResult.cs`、`INotificationLicenseAuthorizer.cs`
- `src/AlarmSystem.Application/Notifications/LicensedNotificationChannel.cs`
- `src/AlarmSystem.Infrastructure/Licensing/DongleLicenseOptions.cs`、`UnavailableDongleLicenseAuthorizer.cs`
- `src/AlarmSystem.Worker/Program.cs`、`appsettings.json`
- `src/AlarmSystem.Desktop/MainWindow.xaml`、`Resources/Strings.cs`、`Resources/Strings.resx`、`ViewModels/LineNotificationTestViewModel.cs`
- `tests/AlarmSystem.Application.Tests/Licensing/LicenseAuthorizationResultTests.cs`、`Notifications/LicensedNotificationChannelTests.cs`
- `tests/AlarmSystem.IntegrationTests/Licensing/DongleLicenseOptionsTests.cs`、`Notifications/Line/LicensedLineNotificationChannelTests.cs`、`Notifications/Teams/LicensedTeamsNotificationChannelTests.cs`
- `tests/AlarmSystem.Desktop.Tests/Services/LineNotificationTestServiceTests.cs`、`ViewModels/LineNotificationTestViewModelTests.cs`

### 開發內容與功能

- 新增 LINE、Teams 與預留 Email 的正式通知 feature，以及受控、非敏感的授權結果與失敗碼契約。
- 新增 `LicensedNotificationChannel` decorator；正式發送先檢查 feature 授權，拒絕或授權檢查例外時 fail closed，不呼叫內層 renderer／HTTP transport，呼叫端取消則原樣向上傳遞。
- Worker 保留原始 LINE／Teams typed transport，但 `IEnumerable<INotificationChannel>` 只註冊授權包裝後的通道；LINE 與 Teams 使用獨立 feature。
- 在尚無 Dongle SDK 與廠商規格時加入 `UnavailableDongleLicenseAuthorizer`，所有正式通知回傳 `license_dongle_unavailable`；沒有假造 SDK、driver、授權內容或 feature code。
- Desktop LINE 人工測試頁維持直接使用原始 transport，不注入 Dongle authorizer；新增本地化警語，明示展示成功不代表正式發報授權，既有雙重確認、遮罩、取消與 token 不持久化行為不變。

### 測試與驗證

- `dotnet test AlarmSystem.sln --no-restore --verbosity minimal -m:1`：145 passed、0 failed、0 skipped（Application 17、Integration 97、Desktop 31）；Domain 測試專案目前沒有測試案例。
- `dotnet build AlarmSystem.sln --no-restore --verbosity minimal -m:1`：成功，0 error、2 個 `NU1900` 警告；警告原因為 NuGet 弱點資料來源無法連線。
- `dotnet format AlarmSystem.sln --verify-no-changes --no-restore`：通過；初次檢查發現的兩處測試檔換行格式已修正。
- Worker `appsettings.json` JSON 解析與 `git diff --check`：通過。
- 變更範圍的常見 GroupId、Bearer token 與 JWT 模式掃描：未發現疑似真實秘密。
- 所有新增 HTTP 測試均使用本機 `HttpMessageHandler` stub；未連線正式 LINE、Teams 或 Dongle 裝置。未執行 migration dry-run，因本次沒有資料庫 schema 或 migration 變更。

### 設定、相容性與尚待確認

- 新增無秘密的 `Licensing:Dongle` 設定區段，集中 provider、檢查 timeout 及 LINE／Teams／Email feature mapping；預設 provider 為 `Unconfigured`，feature code 保持空白。
- 正式 Worker 通知新增 fail-closed 相容性邊界：在真實 Dongle adapter 完成前，即使通道 `Enabled` 也不會送出；Desktop 人工展示路徑不受此限制且沒有 `DemoMode` 開關。
- 待廠商提供 SDK／driver、feature code、簽章與驗證流程、過期與拔除語意、Windows 支援版本、service account 權限及 x86／x64 部署需求。
- 沒有新增 Email adapter、警報輪詢、outbox、retry scheduler、資料庫 schema、migration 或秘密；既有 LINE／Teams transport 契約未變更。

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
