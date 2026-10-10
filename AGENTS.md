# 圖控警報系統 Team — Codex 開發指引

本文件是本專案的開發約束與需求基線，適用於此檔案所在目錄及其所有子目錄。若子目錄另有 `AGENTS.md`，以較接近該程式碼的檔案為準。

> 重要：目前只確認需求與架構方向，不代表任何程式、資料表、服務、畫面、部署流程或外部整合已經完成。Codex 必須先檢查儲存庫現況，再以實際檔案與測試結果描述進度，不得把本文件中的目標寫成既有成果。

## 1. 專案目標

建立一套在 Windows 環境運作的 C#/.NET 圖控警報通知系統。主要資料流為：

```text
iFIX 警報資料
    │ ODBC
    ▼
SQL Server Express
    │ 週期輪詢
    ▼
.NET Windows Worker Service
    ├── 事件正規化、ALARM/ACK 判斷、時間戳去重
    ├── HIGH / MEDIUM / LOW 分級與通知路由
    ├── Teams Adaptive Card 通知
    └── CSV / 應用程式紀錄、保留與清理

WPF 管理介面 ── 設定、狀態、查詢、測試與多語顯示
未來擴充 ───── LINE、TTS 等通知通道
```

系統應優先保證事件不漏送、重啟後不重複送、外部通道故障不阻塞資料擷取，並讓現場人員能追查每筆事件及通知結果。

## 2. 決策狀態

### 2.1 已確認的需求與方向

- 技術主體為 C#/.NET，執行環境為 Windows。
- 後端使用 .NET Worker Service，部署為 Windows Service，定期輪詢警報資料。
- 主要資料路徑為 iFIX 經 ODBC 寫入或提供資料至 SQL Server Express，再由 Worker Service 查詢 SQL Server Express；除非後續另有決策，Worker 不應直接依賴 iFIX UI。
- 系統需處理至少兩種事件：`ALARM`（警報發生）與 `ACK`（警報確認）。ACK 是獨立事件，不得只覆寫原 ALARM 而失去稽核軌跡。
- 事件需依來源時間戳進行增量輪詢與去重；去重狀態必須持久化，不能只存在記憶體。
- 警報分級至少包含 `HIGH`、`MEDIUM`、`LOW`，內部使用固定 enum 或值物件，不在各模組散落字串比較。
- 第一階段通知通道為 Microsoft Teams，訊息內容採外部 JSON 樣板產生 Adaptive Card。
- LINE 與 TTS 為後續擴充通道；第一階段架構需可插拔，但不得在未確認需求前提前實作完整整合。
- 系統需產生 CSV 紀錄，並提供三個月資料保留與自動清理機制。
- 管理端採 WPF，所有使用者可見文字必須可在地化，不得直接硬編碼於 View 或 ViewModel。
- 設定、密碼與權杖必須分離；敏感資料不得提交至 Git，也不得寫入一般 log 或 CSV。
- 必須具備單元、整合及關鍵失敗復原測試，並以可重現的驗收案例證明去重、重啟復原及通知結果追蹤。

### 2.2 尚未證實為既有成果

除非儲存庫中的程式、migration、測試或部署紀錄能證明，以下一律視為「尚未完成或狀態未知」：

- iFIX/ODBC 連線、SQL Server Express schema 與實際資料匯入。
- Windows Service 安裝、啟動、復原及服務帳號設定。
- ALARM/ACK 關聯、去重 ledger、watermark 或 outbox 資料表。
- Teams 實際送出、權杖或 webhook 設定、Adaptive Card 在目標租戶的相容性。
- CSV 寫入、三個月清理排程、WPF 畫面、多語資源及 LINE/TTS。
- CI、安裝包、監控、壓力測試與正式環境驗收。

## 3. 開發原則

1. 先讀現有 solution、README、設定範例、migration 與測試，再決定修改位置；不要假設專案是空的。
2. 每次變更保持可建置、可測試；不要順手重構與需求無關的程式碼。
3. 核心商業邏輯不得依賴 WPF、ODBC、SQL Server、Teams 或檔案系統，外部能力均透過介面注入。
4. 所有時間運算集中使用可注入的時間來源。事件資料保存原始來源時間與其時區/偏移；內部比較與排程使用 UTC，UI 再轉為設定時區。
5. 資料擷取採至少一次（at-least-once）思維，透過資料庫唯一鍵與冪等處理達到使用者可見的不重複通知。
6. 不得只靠「最後讀取時間」排除事件。輪詢需允許安全重疊，再交由持久化去重判斷，以涵蓋晚到資料與同毫秒多筆事件。
7. 資料庫交易範圍要短；讀取來源、落地事件、建立通知工作與實際呼叫外部服務不可包在同一長交易內。
8. 外部通知失敗應記錄可重試狀態，不得讓輪詢服務永久停住，也不得無上限快速重試。
9. 刪除或清理必須可觀測、分批且只作用於明確資料範圍；不得用模糊路徑或未加條件的刪除命令。
10. 未決事項不得暗中固化成難以更改的常數。可採本文件的「暫定預設」，但需集中設定並在文件與程式碼中標為待確認。

## 4. 建議 solution 與模組邊界

若儲存庫尚無既定結構，採以下結構作為預設；若已有合理結構，沿用並維持相同責任分離，不要只為符合名稱而搬移檔案。

```text
src/
  AlarmSystem.Domain/           # Event、Severity、Delivery 等領域模型與規則
  AlarmSystem.Application/      # 輪詢、正規化、去重、派送及清理 use cases
  AlarmSystem.Infrastructure/   # SQL/ODBC、檔案、Teams、設定與持久化 adapter
  AlarmSystem.Worker/           # Windows Worker Service composition root
  AlarmSystem.Desktop/          # WPF 管理介面
tests/
  AlarmSystem.Domain.Tests/
  AlarmSystem.Application.Tests/
  AlarmSystem.IntegrationTests/
templates/
  teams-alarm-card.json
docs/
```

建議的核心介面如下，名稱可配合現有慣例調整：

- `IAlarmSource`：依查詢範圍讀取來源列，不承擔通知。
- `IAlarmNormalizer`：把 iFIX/SQL 欄位轉成內部事件並驗證必要欄位。
- `IEventStore`：原子地登錄事件、判斷唯一性並保存處理狀態。
- `INotificationChannel`：通知通道抽象；Teams、未來 LINE/TTS 各自實作。
- `INotificationQueue` 或 outbox repository：保存待送、重試與最終結果。
- `ICsvAuditWriter`：寫入規格固定的稽核 CSV。
- `IRetentionService`：執行分批保留清理並輸出統計。
- `IClock`/`.NET TimeProvider`：測試可控制的時間來源。

Worker 與 WPF 可共用 Application/Infrastructure，但 WPF 不得直接操控 Worker 內部物件。跨程序設定變更需有明確的保存、驗證與重新載入機制。

## 5. 警報事件模型

核心事件至少需要下列語意；實際 SQL 欄名與型別須依來源 schema 確認：

| 欄位 | 說明 |
|---|---|
| `EventId` | 系統內部不可變識別碼 |
| `SourceSystem` | 來源系統，預期為 iFIX |
| `SourceEventId` | 來源若有穩定唯一鍵則保留；沒有時可為 null |
| `Tag` | 點位或設備識別 |
| `Area` | 區域/產線/群組，若來源提供 |
| `EventType` | `ALARM` 或 `ACK` |
| `Severity` | `HIGH`、`MEDIUM` 或 `LOW` |
| `Message` | 正規化後的警報文字 |
| `SourceTimestamp` | 來源記錄的事件時間，須保留 offset 或已知時區 |
| `IngestedAtUtc` | 本系統首次登錄時間 |
| `AlarmCorrelationKey` | ACK 關聯原 ALARM 的鍵；規則待確認 |
| `RawReference` | 可追查來源列的非敏感參照，不預設保存完整原始資料 |

保留 ALARM 與 ACK 的原始事件時間，ACK 不得改寫 ALARM 的 `SourceTimestamp`。若 ACK 無法可靠關聯原 ALARM，仍須保存為可追查的未關聯事件，並記錄原因；不得直接丟棄。

### 5.1 分級

- 內部只接受 `HIGH`、`MEDIUM`、`LOW` 三個正規值。
- iFIX 原始優先級到三個等級的 mapping 必須設定化且有未知值處理。
- 未知或空白優先級不得默認提升或降低；先隔離/記錄資料品質問題，具體 fallback 待業務確認。
- 通知顏色、路由及是否啟用可依 severity 設定，但不應改變事件本身的原始等級資料。

## 6. 輪詢、watermark 與去重

### 6.1 必要行為

- 輪詢查詢應採穩定排序，至少使用 `SourceTimestamp` 加上可決定先後的來源鍵；只有時間戳不足以處理同時多筆資料。
- 每輪查詢保留可設定的重疊時間窗，以重讀近期列並涵蓋晚到資料。
- 每筆正規化事件先寫入持久化 event store；資料庫唯一約束成功者才建立通知工作，衝突者視為已處理重複項。
- watermark 只能在該批資料安全登錄後前進。程序在「讀到資料但尚未登錄」時終止，重啟後必須可以重讀。
- 服務第一次啟動的回溯起點、最大批次、輪詢間隔、重疊窗與資料缺口處理全部設定化。

### 6.2 去重鍵

優先採用 iFIX/來源提供且已證實穩定的唯一事件 ID。若來源沒有唯一 ID，暫定以正規化後的複合欄位產生 canonical fingerprint，例如：

```text
SourceSystem + SourceEventId（若有）
或
SourceSystem + Tag + EventType + normalized(SourceTimestamp) + AlarmCorrelationKey/來源序號
```

不得在尚未確認來源精度與語意前，直接把 `Tag + Timestamp` 當成最終去重鍵，因為同一點位可能在同一時間精度內產生多筆不同事件。複合鍵的欄位、大小寫、空白、時間精度與 hash 演算法屬於阻擋正式上線的待確認事項。最終鍵須由資料庫 unique index 保證，而不只是在程式中先查後寫。

### 6.3 通知冪等

事件唯一不代表派送只會執行一次。每個 `EventId + Channel + Destination` 應有獨立 delivery/outbox 記錄與唯一約束，至少包含：

- `Pending`、`Sending`、`Succeeded`、`RetryScheduled`、`FailedPermanent` 狀態。
- 嘗試次數、最近一次錯誤摘要、下一次重試時間、建立/完成時間。
- 外部通道回傳的非敏感訊息 ID（若有）。

重試採有上限的 exponential backoff 加 jitter；上限、間隔與永久失敗條件待確認並設定化。服務重啟後需接續未完成項目。若通道本身不支援 idempotency key，系統需清楚記錄「呼叫成功但回應遺失」可能造成的極少數重送風險。

## 7. Teams Adaptive Card

### 7.1 樣板規則

- 樣板為部署內容，不編譯進通知邏輯；預設位置為 `templates/teams-alarm-card.json`。
- 啟動或重新載入時驗證 JSON、必要欄位與支援的 Adaptive Card 版本。樣板錯誤不得令輪詢停止，應使該通道進入明確 unhealthy 狀態。
- 禁止以未跳脫的字串取代方式注入內容。先解析 JSON，再以 JSON API 設定值，或使用能正確 JSON encode 的受控樣板引擎。
- Log 不得輸出完整 endpoint、access token、authorization header 或含秘密的 payload。
- Teams 實際傳送方式、驗證方法、目標頻道與租戶支援版本尚未確認；adapter 必須隔離此差異。

### 7.2 基準 JSON

下列為需另存成 `templates/teams-alarm-card.json` 的基準內容。`{{...}}` 是語意 placeholder；實際 renderer 可使用等價語法，但欄位 mapping 必須集中管理並有測試。此版面是可開發的起點，不代表已經過目標 Teams 環境驗收。

```json
{
  "type": "AdaptiveCard",
  "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
  "version": "1.4",
  "body": [
    {
      "type": "TextBlock",
      "text": "{{severityLabel}}｜{{eventTypeLabel}}",
      "weight": "Bolder",
      "size": "Large",
      "color": "{{adaptiveColor}}",
      "wrap": true
    },
    {
      "type": "TextBlock",
      "text": "{{message}}",
      "wrap": true
    },
    {
      "type": "FactSet",
      "facts": [
        { "title": "Tag", "value": "{{tag}}" },
        { "title": "Area", "value": "{{areaDisplay}}" },
        { "title": "Event", "value": "{{eventTypeLabel}}" },
        { "title": "Time", "value": "{{sourceTimeDisplay}}" },
        { "title": "Event ID", "value": "{{eventId}}" }
      ]
    }
  ]
}
```

建議暫定顏色 mapping：`HIGH -> Attention`、`MEDIUM -> Warning`、`LOW -> Good`。此 mapping 屬視覺預設，須由使用者驗收後才算定案。所有 display 時間須帶時區或清楚標示時區；空值以本地化的 `—` 顯示，不得把字面值 `null` 傳給使用者。

## 8. CSV、應用程式紀錄與保留清理

CSV 是可攜式稽核輸出，不應取代結構化 application log 或資料庫 delivery ledger。

- 每筆 CSV 至少包含 UTC 寫入時間、來源事件時間、事件 ID、Tag、ALARM/ACK、severity、目的通道、派送狀態與非敏感錯誤碼。
- CSV 欄位順序與 header 視為相容性契約；欄位變更需更新文件與測試。
- 文字、逗號、雙引號及換行必須依 RFC 4180 類型規則正確跳脫；編碼與是否含 BOM 尚待確認。
- 多執行緒/多程序寫入須避免交錯與檔案損壞。檔案輪替粒度、目錄及最大大小皆設定化。
- 路徑不得由事件內容拼接，且服務帳號只授予必要目錄權限。
- 記錄中不得包含 connection string 密碼、Teams/LINE token、完整 personal data 或未經核准的原始 payload。

「三個月」保留規則適用於 CSV、應用程式 log，以及不再需要的事件/delivery 歷史資料，但各資料種類可有獨立開關。必須先確認「三個曆月」或「90 天」；在確認前不得把兩者宣稱為等價。建議預設採 UTC 基準的三個曆月，並保留設定覆寫。

清理工作需：

- 定期、分批、可取消且有互斥保護。
- 只刪除早於 cutoff 且已達最終狀態的資料；`Pending`/可重試 delivery 不得因年齡直接刪除。
- 刪除前以明確目錄與檔名規則篩選，不跟隨不受信任的捷徑或路徑。
- 輸出 cutoff、掃描數、刪除數、失敗數與耗時，但不洩漏秘密。
- 時間邊界、夏令時間、鎖檔、權限不足及重跑均需測試。

## 9. WPF 多語管理介面

WPF 為管理與診斷介面，不是輪詢服務持續運作的必要條件；關閉 WPF 不得停止 Windows Service。

建議採 MVVM。View 僅負責呈現與 binding；設定驗證、連線測試、服務狀態查詢及業務規則放在可測試服務中。所有 UI 字串、錯誤訊息、severity/event type 顯示名稱、日期格式及無障礙文字使用 `.resx` 或現有專案統一的 localization provider。

預期畫面範圍如下，實際資訊架構待 wireframe 確認：

- 儀表板：Worker 狀態、最後成功輪詢、watermark、queue 深度與通道健康度。
- 事件查詢：依時間、Tag、ALARM/ACK、severity、通知狀態篩選。
- 設定：輪詢、資料庫、通知路由、CSV、保留期與顯示語言。
- 診斷：測試資料庫/Teams 連線、檢視遮罩後的錯誤、匯出診斷資訊。

WPF 顯示及保存秘密時應使用遮罩與安全儲存 API；讀回後不可回填明文。修改設定需先驗證並採原子寫入，失敗時保留上一份有效設定。已確認需「多語」，但第一版具體語言清單尚未決定；`zh-TW` 與 `en-US` 僅為建議起點，不得當成已確認範圍。

## 10. 設定與秘密管理

非敏感設定採 .NET Options pattern，啟動時執行 strongly typed validation。可使用 `appsettings.json`、環境專用檔與環境變數，但正式秘密必須來自部署環境核准的安全儲存（例如 Windows Credential Manager、DPAPI 保護檔或組織既有 secret store；具體方案待確認）。

建議設定區段：

```text
AlarmSource       # 連線名稱、查詢/批次、來源時區
Polling           # interval、overlap、initial lookback
SeverityMapping   # iFIX 值到 HIGH/MEDIUM/LOW
Notifications
  Teams           # enabled、destination reference、template、retry policy
  Line            # 第一階段預設 disabled
  Tts             # 第一階段預設 disabled
Csv               # enabled、目錄、輪替、編碼
Retention         # period、schedule、batch size
Localization      # UI culture、顯示時區
```

規則：

- repository 只提供無秘密的 `appsettings.example.json` 或等價範例，使用明顯 placeholder。
- connection string 中的密碼、webhook URL、token、憑證私鑰不得提交。
- 設定載入失敗要指出區段與欄位，但錯誤內容要遮罩。
- 動態重新載入只套用已驗證的完整 snapshot；不得讓半套設定進入執行中服務。
- 開發環境可以使用 .NET user-secrets；不得假設它適合 Windows Service 正式部署。

## 11. 安全與可靠性

- Windows Service 使用專用最低權限帳號；只授予 SQL、log/CSV 目錄及必要網路目的地權限。
- SQL 查詢一律參數化。來源表/欄名若需設定，必須採 allowlist 驗證，不能當成一般參數直接拼接。
- 對所有外部輸入設長度、null、編碼及允許值檢查；保留原始值與顯示值的界線。
- HTTP client 使用 factory、timeout、取消 token 及受控 retry；不要自行無限迴圈。
- 健康狀態至少區分來源資料庫、event store、通知通道、CSV 與 cleanup，避免單一綠燈掩蓋局部故障。
- Log 採結構化欄位與 correlation ID，錯誤內容經秘密遮罩；不可吞掉例外或只寫自由文字。
- schema 變更使用 migration 並提供向前/回復說明；正式資料庫不得在未知備份狀態下執行破壞性變更。
- 關機需停止接收新批次、等待或安全釋放進行中工作，確保下次重啟可以重放。

## 12. 測試要求

### 12.1 單元測試

- iFIX 欄位正規化、ALARM/ACK、severity mapping 與未知值。
- 時間戳時區、精度、邊界、排序、overlap 與 watermark 前進規則。
- 去重 fingerprint 的穩定性與不同事件不可誤判為相同。
- Teams model/JSON rendering：特殊字元、換行、空值、多語與 schema 驗證。
- CSV escaping、固定欄位順序、編碼與輪替命名。
- 保留 cutoff、曆月/天數策略與最終狀態保護。
- retry/backoff 上限與永久失敗分類。

### 12.2 整合測試

- 使用與正式相同 major version 的 SQL Server 相容環境測試 migration、唯一約束、並行登錄及查詢排序；若 SQL Server Express 與其他 edition 有差異，要補目標環境 smoke test。
- 以 fake/stub 通知 server 驗證成功、timeout、429、5xx、無效回應及重啟後續送。
- 驗證程序在批次不同階段中止後，事件不遺失且已成功項目不重複建立 delivery。
- 驗證 CSV 鎖檔、磁碟/權限失敗不會破壞輪詢一致性。
- WPF ViewModel 與 localization fallback 測試；關鍵流程另做基本 UI smoke test。

測試不可連線正式 iFIX、正式 Teams channel 或使用真實秘密。測試資料需包含同時間戳多事件、晚到事件、重複列、無法關聯 ACK、未知 severity、Unicode Tag/訊息及超長訊息。

## 13. 驗收基線

在待確認參數補齊後，第一階段至少需通過以下情境，才可宣稱完成：

1. 給定已確認的 iFIX/SQL 測試資料，Worker 可連續輪詢並正確產生 ALARM 與 ACK 事件。
2. 同一來源批次重讀、overlap 重讀及服務重啟後，不會新增重複 event/delivery。
3. 同時間戳的兩筆不同事件均被保留，不因時間戳去重而漏失。
4. HIGH/MEDIUM/LOW 依核准 mapping 正確分類與路由；未知值有可追查處置。
5. Teams 測試目的地收到內容正確、特殊字元安全、時間與時區清楚的 Adaptive Card。
6. Teams 暫時失敗時事件仍落地，依設定重試；恢復後可補送且可查到每次結果。
7. CSV 能由核准工具正確開啟，欄位、Unicode、逗號、引號與換行不錯位。
8. 清理工作只移除超過核准保留期且可刪除的資料，不刪除待送項目，並留下統計紀錄。
9. WPF 可切換所有核准語言，無關鍵硬編碼字串；關閉 UI 後 Worker 持續執行。
10. 一般 log、CSV、例外訊息、設定範例及版本控制歷史中不含真實秘密。
11. 安裝、升級、回復、服務帳號權限與故障診斷步驟有可重現文件。

效能、最大通知延遲、允許遺漏/重複率、可用性及復原時間尚未有數字，不能自行宣稱符合 SLA。

## 14. 尚待確認事項

以下答案應記錄至 README、ADR 或需求文件；影響資料正確性或正式部署者在確認前不得上線。

### 14.1 阻擋資料模型與去重

- iFIX 版本、ODBC driver/DSN、資料實際流向，以及 SQL Server Express 的版本與部署拓撲。
- 來源 database/table/view、欄位定義、主鍵、索引、資料量、更新方式與可提供的查詢權限。
- ALARM/ACK 的實際識別值、ACK 如何關聯 ALARM、是否存在 RETURN/NORMAL/CLEAR 等其他狀態。
- 來源時間戳型別、精度、時區、是否可能被更正，以及同時間戳的穩定 tie-breaker。
- 可靠 `SourceEventId` 是否存在；若沒有，最終 fingerprint 欄位與正規化規則。
- iFIX priority 到 HIGH/MEDIUM/LOW 的正式 mapping 及未知值策略。

### 14.2 阻擋通知與操作流程

- Teams 採用的實際入口與認證方式、租戶限制、目的頻道、Adaptive Card 支援版本及 rate limit。
- 哪些 severity/event type 要通知哪些目的地；ACK 是否一定通知。
- 卡片語言、標題、欄位、顏色、mention、連結及是否包含可操作按鈕。
- 重試次數、最大延遲、永久失敗後告警方式及人工補送流程。
- LINE 使用 Messaging API 或其他核准方式；TTS 引擎、語言、聲音、播放端與安靜時段。

### 14.3 阻擋部署、UI 與驗收

- 目標 .NET 版本、支援的 Windows/Windows Server 版本、x64/x86 與發佈方式。
- Worker 與 WPF 是同機或分離，WPF 如何查詢/控制 service，以及誰有管理權限。
- 正式秘密儲存方案、service account、proxy、防火牆、憑證與 SQL 加密要求。
- 輪詢間隔、重疊窗、初次回溯、批次大小、尖峰事件量與 SQL Express 容量預估。
- CSV 正式 schema、檔名/輪替、編碼、保存目錄、可存取角色及是否需簽章/防竄改。
- 三個月是曆月或 90 天；各類資料的保留範圍、清理時間、備份/法遵例外。
- 第一版語言清單、預設語言、顯示時區、畫面權限、設定變更是否需稽核。
- 可量化 SLA/SLI、驗收資料集、負責驗收者及正式切換/回復條件。

## 15. Codex 交付規則

每次實作任務的回覆需明確列出：

- 實際新增/修改的檔案與行為。
- 採用的假設、使用的暫定預設及仍未解決的問題。
- 執行過的 build/test/migration dry-run 與結果；未執行者說明原因。
- 是否涉及 schema、設定、部署、秘密或相容性變更。
- 可由使用者重現的驗證步驟。

不要：

- 宣稱未執行的測試已通過，或未部署的功能已上線。
- 建立看似真實的 connection string、webhook、token、Tag、目的頻道或業務 mapping。
- 為讓 demo 通過而吞掉例外、停用 TLS 驗證、移除去重/唯一約束或把秘密寫入範例。
- 在未確認來源 schema 前寫死欄名與 ALARM/ACK 判斷。
- 修改、重新命名、移動或刪除 `sources/` 下的同步參考資料；該目錄唯讀，且內容可能在下一次同步時被替換。

若需求與本文件衝突，先指出衝突並取得決策；取得確認後，同一變更中更新本文件或對應 ADR，避免程式與決策基線分離。
