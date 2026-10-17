# 階段 2A：SQL 雙輪詢與 AlarmEvents 儲存

## 狀態

| 分類 | 狀態 |
|---|---|
| Application 雙 stream 協調、完整 timestamp 群組切批、錯誤復原 | Implemented / Offline Tested |
| iFIX SQL reader、欄位 mapping、參數化有界查詢 | Implemented / SQL contract Offline Tested |
| AlarmStore repository、0002 migration、schema manifest | Implemented / migration checksum Offline Tested |
| 真實 iFIX Source SELECT、連續輪詢與現場效能 | Pending On-Site Verification |
| AlarmStates、ACK 關聯、自動通知、outbox、來源清理 | Not Implemented（2A 範圍外） |

## 資料流與責任

`Worker` 只負責不可重疊的排程、取消、有限 exponential backoff 與結構化結果 log。`AlarmPollingService` 依序協調 `ALARM`、`ACK_OPERATOR`，不依賴 SQL。`SqlAlarmReader`／`SqlOperatorReader` 只讀 `dbo.FIXALARMS`；`SqlAlarmEventIngestionRepository` 只寫核准的 AlarmStore。2A 不解析 ACK，也不取得或呼叫任何 `INotificationChannel`。

```text
ALARM reader (ALM_MSGTYPE=ALARM) ───────┐
                                         ├─ AlarmPollingService
OPERATOR reader (ALM_MSGTYPE=OPERATOR) ─┘      │ validation / source-time conversion
                                                ▼
                                   AlarmStore serializable transaction
                                      ├─ insert/compare AlarmEvents
                                      └─ upsert stream PollingCheckpoint
```

兩個 stream 的 checkpoint 名稱固定為 `ALARM` 與 `ACK_OPERATOR`。OPERATOR 的內容即使包含 `ACK` 文字，2A 仍保存為：

```text
OriginalMessageType = OPERATOR
EventType = OPERATOR
AckMatchStatus = NotApplicable
```

## Source 欄位 mapping

| FIXALARMS | AlarmEvents | 2A 行為 |
|---|---|---|
| `ALM_MSGID` | `SourceMessageId` | 非 NULL 才能由 2A 安全登錄 |
| `ALM_NATIVETIMEIN` | `SourceEventTime` | 保存原始無 offset 本地時間，不以目前時間補值 |
| `ALM_PHYSLNODE` | `SourceNode` | nullable |
| `ALM_OPNODE` | `OperatorNode` | nullable |
| `ALM_TAGNAME` | `TagName` | OPERATOR 可為 NULL |
| `ALM_MSGTYPE` | `OriginalMessageType`／`EventType` | ALARM→ALARM；OPERATOR→OPERATOR |
| `ALM_ALMSTATUS` | `AlarmStatus` | 原值保存 |
| `ALM_ALMPRIORITY` | `RawPriority` | 原值保存；`AlarmLevel=NULL`，正式 mapping 待確認 |
| `ALM_ALMAREA` | `Area` | 使用已確認欄位，不使用 `ALM_AREA` |
| `ALM_VALUE` | `RawValue` | 原值保存，不做 2B 變化比較 |
| `ALM_DESCR` | `Description` | 原值保存 |
| `ALM_UNIT` | `RawUnit` | 原值保存 |

`SourceTimeZoneId` 保存設定的 Windows time-zone ID。只有非 invalid／ambiguous 的本地時間才轉成 `EventTimeUtc`；無法唯一換算時保持 `NULL`，並在 `DataQualityFlags` 記錄受控旗標。`IngestedAtUtc` 來自可注入 `TimeProvider`。

## 分頁、overlap 與 checkpoint

每個 SQL 查詢都有 lower／upper bound，並固定 `ORDER BY ALM_NATIVETIMEIN`，再以 `ALM_MSGID` 作確定性排序輔助；GUID 排序不被解讀成事件發生先後。Reader 使用 `TOP(MaxBatchSize + 1)` 判斷邊界：只把完整 timestamp 群組交給 Application。如果第一個群組本身已超過上限，回報 `timestamp_group_too_large`，避免永遠重讀前 N 筆或錯誤越過其餘同時間事件。

單輪使用固定 upper bound。第一頁由 `watermark - overlap`（或首次 lookback）開始；後續頁只從已完整提交的 timestamp 之後繼續。每頁 insert／duplicate comparison 與部分 checkpoint 在同一 AlarmStore transaction；整個有界視窗讀完後才把 watermark 推到固定 upper bound。晚到資料若落在 overlap 內，下一輪會被重讀並由唯一索引去重；超過 overlap 的晚到保證仍取決於現場延遲分布，必須上線量測。

## 去重與 fail-safe 限制

- 已有 GUID：資料庫 `UX_AlarmEvents_SourceMessageId` 保證 `SourceSystem + SourceMessageId` 唯一。重讀時 repository 比對所有保存的來源內容；完全一致計為 duplicate，不新增 row。
- 同 GUID 異內容：回報 `identity_collision` 並 rollback 該批，checkpoint 不前進。
- GUID 為 NULL：因來源沒有 PK，內容 fingerprint 無法證明兩筆完全相同資料是重複或兩個真實事件。2A 回報 `unresolvable_source_identity` 並保留進度，不宣稱 hash 能完全解決。
- 來源時間 NULL：`AlarmEvents.SourceEventTime` 不可為 NULL，2A 回報 `source_timestamp_missing`，不以 `UtcNow` 偽造。
- 失敗時嘗試只更新 checkpoint 的 `LastErrorCode`，不改 watermark；若 AlarmStore 本身不可用，Worker 仍保留原始例外供診斷。

`SourceFingerprint` 的 0001 unique index 仍保留供未來核准策略使用；2A 不對 NULL GUID 自動產生 fingerprint。

## 離線可重現測試

`AlarmSystem.Application.Tests/Polling` 使用 fake readers、記憶體 repository 與固定時間來源，涵蓋：ALARM／OPERATOR 分流、ACK-like OPERATOR、NULL Tag、重讀去重、同 timestamp 不同 GUID、完整群組分頁、群組過大、NULL GUID、GUID collision、NULL source time、未知 priority、晚到事件、交易 commit 前中斷、重啟重放、呼叫端取消及夏令時間歧義。Infrastructure 測試驗證實際欄位 mapping、`ALM_ALMAREA`、SQL 參數與禁止的 `SELECT *`／`NOLOCK`／time-column function，以及 0002 migration 內容與 checksum。

一般 `dotnet test AlarmSystem.sln` 不會讀取 private SQL JSON 或連線任何 Source。SQL Server foundation 測試與 repository DML 測試分別只有在 `ALARMSYSTEM_RUN_SQL_INTEGRATION_TESTS=true`／`ALARMSYSTEM_RUN_SQL_INGESTION_TESTS=true`，並提供已確認的隔離 AlarmStore 設定時才可執行；repository 測試還要求沒有 Worker 同時使用該 database。

## 啟用與現場驗收

部署前先對 AlarmStore 執行：

```powershell
dotnet run --project tools/AlarmSystem.DatabaseTool -- initialize --config config/sql-connection.alarmstore.local.json --dry-run
# 經備份、目標與變更核准後，才可使用 --apply 及精確 server/database 雙重確認。
dotnet run --project tools/AlarmSystem.DatabaseTool -- verify-schema --config config/sql-connection.alarmstore.local.json
```

之後再安全提供 `AlarmPolling__SourceConfigPath`／`AlarmPolling__AlarmStoreConfigPath` 並設定 `Enabled=true`。私有 JSON 不可放入 publish；正式服務帳號必須對 Source 只有必要 SELECT，對 AlarmStore 只有執行 2A DML 所需權限。

以下均為 **Pending On-Site Verification**：Source SQL 實際連線與 SELECT、真實 ALARM／OPERATOR、GUID NULL／重複統計、時間精度與時區、同 timestamp 群組規模、五秒輪詢成本與 index 需求、ODBC 延遲、遠端斷線恢復、逐筆對帳、一小時穩定性，以及實際 Windows Service 安裝／停止／復原。

## 2B／2C 接入點

2B 應新增獨立的 `AlarmEvents` 消費 checkpoint，從不可變事件重建／更新 `AlarmStates`；Source checkpoint 只證明來源擷取完成，不代表狀態處理完成。2C 才可解析已保存 OPERATOR、辨識 ACK、建立 `RelatedAlarmId`／match status。兩階段都不應回頭修改 2A 的來源讀取語意。
