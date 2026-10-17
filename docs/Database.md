# 分離式 SQL Server 與階段 2A 儲存

已實作 SQL 連線診斷、iFIX 來源 metadata 檢查、AlarmSystem migration／schema 驗證工具，以及階段 2A 的 ALARM／OPERATOR 唯讀輪詢、`AlarmEvents` 登錄與 `PollingCheckpoints` 原子更新。階段 2A 只有離線自動化測試證據；iFIX PC 尚未上線，真實來源連線與現場連續輪詢不得宣稱完成。

架構決策見 [`ADR-0001`](adr/0001-separate-source-and-alarm-store.md)。

## 連線角色

| 角色 | 用途 | 允許命令 | 禁止事項 |
|---|---|---|---|
| `Source` | iFIX 來源 `IFIX-ALARM.dbo.FIXALARMS` | `test-connection`、`inspect-source` | initializer、`alarm.*` schema 驗證／DDL |
| `AlarmStore` | AlarmSystem 自有事件、狀態與後續 outbox | `test-connection`、`initialize`、`verify-schema` | `inspect-source`、作為 iFIX 來源替代品 |

角色由私有 JSON 的 `SqlServer.Role` 明確指定。角色不相符時，DatabaseTool 會在進行目標 SQL 操作前拒絕命令。`test-connection` 可測試兩種角色，輸出也包含角色。

來源與儲存端應使用不同 SQL login。來源帳號只授予 `dbo.FIXALARMS` metadata／必要 SELECT 權限；AlarmStore 初始化帳號才需要 DDL，正式 Worker 再改用較低權限帳號。

## 設定檔

建立兩份不受版本控制的本機設定：

```powershell
Copy-Item config/sql-connection.source.example.json config/sql-connection.source.local.json
Copy-Item config/sql-connection.alarmstore.example.json config/sql-connection.alarmstore.local.json
```

來源格式：

```json
{
  "SqlServer": {
    "Role": "Source",
    "Server": "tcp:SOURCE_SQL_HOST,1433",
    "Database": "IFIX-ALARM",
    "Authentication": "SqlPassword",
    "UserName": "YOUR_SOURCE_SQL_USER",
    "Password": "YOUR_SOURCE_SQL_PASSWORD",
    "Encrypt": true,
    "TrustServerCertificate": false,
    "ConnectionTimeoutSeconds": 10
  }
}
```

AlarmStore 格式相同，但 `Role` 必須是 `AlarmStore`，且 `Server`、`Database`、帳號與密碼均指向獨立儲存端。請勿把真實值貼入文件、issue、log 或 commit。

`Authentication` 只接受 `SqlPassword`。`Encrypt`／`TrustServerCertificate` 必須明確提供，timeout 範圍為 1–120 秒。正式環境預設應採 `Encrypt=true`、`TrustServerCertificate=false`；若測試環境明確接受自簽憑證風險，只能在私有設定中調整。

`.gitignore` 排除通用舊檔與所有角色化私有檔：

```gitignore
/config/sql-connection.local.json
/config/sql-connection.*.local.json
```

## DatabaseTool

### 來源 SQL

```powershell
dotnet run --project tools/AlarmSystem.DatabaseTool -- test-connection --config config/sql-connection.source.local.json
dotnet run --project tools/AlarmSystem.DatabaseTool -- inspect-source --config config/sql-connection.source.local.json
```

`inspect-source` 只讀取 catalog，列出 `dbo.FIXALARMS` 欄位、型別、NULL、PK 與 index。加上 `--include-msgid-stats` 會掃描來源資料統計 `ALM_MSGID` NULL／重複值，僅能在核准時段使用。

現場唯讀檢查已確認：

- 核准來源 database：`IFIX-ALARM`。
- SQL instance 回報為 `GD-TN1-PC1\SQLEXPRESS`，Express Edition 64-bit，版本 `17.0.1145.4`。
- `dbo.FIXALARMS` 有 32 個 nullable 欄位，沒有 PK／index。
- `ALM_NATIVETIMEIN` 為 `datetime`（scale 3），`ALM_MSGID` 為 nullable `uniqueidentifier`。
- Area 實際欄位為 `ALM_ALMAREA`，不是先前暫定的 `ALM_AREA`；檢查器已依現場 schema 更新。
- 為避免未核准的大表掃描，尚未執行 `ALM_MSGID` 統計。

### AlarmStore SQL

先測試獨立 AlarmStore 連線，再做 dry run：

```powershell
dotnet run --project tools/AlarmSystem.DatabaseTool -- test-connection --config config/sql-connection.alarmstore.local.json
dotnet run --project tools/AlarmSystem.DatabaseTool -- initialize --config config/sql-connection.alarmstore.local.json --dry-run
```

套用時必須以設定檔內的精確值完成雙重確認：

```powershell
dotnet run --project tools/AlarmSystem.DatabaseTool -- initialize --config config/sql-connection.alarmstore.local.json --apply --confirm-server "<ALARM_STORE_SERVER>" --confirm-database "<ALARM_STORE_DATABASE>"
dotnet run --project tools/AlarmSystem.DatabaseTool -- verify-schema --config config/sql-connection.alarmstore.local.json
```

initializer 不建立 database、不含 `DROP`／`TRUNCATE`，也不修改 `dbo.FIXALARMS`。Apply 使用 transaction、`sp_getapplock`、migration SHA-256 checksum 與雙重目標確認。Worker 啟動時不會自動執行 initializer。

`0001_CreateAlarmFoundation` 建立：

| 物件 | 用途 |
|---|---|
| `alarm.AlarmEvents` | 不可變 ALARM／ACK 來源事件與唯一索引基礎 |
| `alarm.AlarmStates` | 每一警報週期的狀態基礎 |
| `alarm.PollingCheckpoints` | 來源 stream watermark 基礎 |
| `alarm.SchemaMigrations` | migration ID、checksum、時間與工具版本 |

`0002_SupportOperatorIngestion` 不修改已套用的 0001，僅調整 `alarm.AlarmEvents`：

- `CK_AlarmEvents_EventType` 接受 `ALARM`、`OPERATOR`、`ACK`。
- 新增 nullable `RawPriority nvarchar(128)`、`RawUnit nvarchar(128)`，保留 `ALM_ALMPRIORITY` 與 `ALM_UNIT`。
- 新增 nullable `DataQualityFlags nvarchar(256)`，記錄 priority mapping 待定與來源本地時間歧義／無效等受控旗標。
- 不修改 `AlarmStates`，也不建立 outbox、delivery 或通知資料表。

0002 是向前 migration；回復若移除 raw 欄位或重新禁止 `OPERATOR` 會造成資料遺失／相容性破壞，因此沒有自動 down script。若部署必須回復，應先停止 Worker、確認備份與既有 OPERATOR／raw data，再以另行核准的版本化 migration 處理，不可直接修改已套用的 0002 或 migration history。

`verify-schema` 唯讀檢查 schema、table、欄位、PK／index、CHECK／FK 與 migration history，不會自動修正。

2026-10-16 現場驗證已確認獨立 AlarmStore：SQL instance `Admin\SQLEXPRESS`、database `alarmcall`、SQL Server `17.0.1000.7` Express 64-bit。經使用者明確授權後已套用 `0001_CreateAlarmFoundation`；套用後與 SQL integration test 後兩次 `verify-schema` 均回報 `IsValid=true`、`Issues=[]`，重跑 dry-run 回報 migration `IsApplied=true`。

## 安全與既有來源端物件

在分離決策確認前，使用者曾核准工具依當時設定將 `0001_CreateAlarmFoundation` 套用到遠端 `IFIX-ALARM`。套用後已驗證四個 `alarm` tables 完整，且 migration 本身不含 `FIXALARMS` DDL。

這些來源端 `alarm.*` 物件目前保留。移除它們需要另行核准，且必須先確認備份、相依性、登入目標與精確物件；本次不執行刪除或 rollback。

## 階段 2A Worker 設定

repository 的 `src/AlarmSystem.Worker/appsettings.json` 只含無秘密且停用的預設。`SourceConfigPath`／`AlarmStoreConfigPath` 是兩份私有 JSON 的路徑；JSON 本身不會被 Worker 專案複製到 build／publish。

```json
{
  "AlarmPolling": {
    "Enabled": false,
    "IntervalSeconds": 5,
    "OverlapSeconds": 60,
    "MaxBatchSize": 500,
    "CommandTimeoutSeconds": 10,
    "InitialLookbackHours": 24,
    "MaxFailureBackoffSeconds": 60,
    "SourceTimeZoneId": "Taipei Standard Time",
    "SourceConfigPath": "",
    "AlarmStoreConfigPath": ""
  }
}
```

`Enabled=false` 時不驗證檔案是否存在，也不建立 SQL reader/repository 或嘗試連線。`Enabled=true` 時才驗證數值、時區、路徑及兩個 SQL role。Worker 不呼叫 `DatabaseInitializer`；部署人員必須先用 DatabaseTool dry-run、核准 apply，再執行 `verify-schema`。

## 雙輪詢與 transaction

每輪依序處理 `ALARM` 與 `ACK_OPERATOR`。兩者各自讀取／保存 checkpoint：

1. 讀取 stream checkpoint；首次使用 `InitialLookbackHours`，其後由 watermark 減去 overlap。
2. 把目前 UTC 轉成設定來源時區的本地時間，固定為本輪查詢上界。
3. SQL reader 以 `ALM_MSGTYPE`、下界、上界、分頁 cursor 參數查詢 `dbo.FIXALARMS`，明確列出 12 個核准欄位。
4. Reader 多取一筆，只回傳完整 `ALM_NATIVETIMEIN` 群組；同一 timestamp 群組超過 `MaxBatchSize` 時回報錯誤，不跨過群組。
5. Application 驗證訊息類型、來源時間、GUID、欄位長度與排序，並依 `SourceTimeZoneId` 轉換 `EventTimeUtc`。歧義或無效本地時間不猜測 UTC，改存 `NULL` 與資料品質旗標。
6. AlarmStore repository 在 serializable transaction 內以 `SourceSystem + SourceMessageId` 鎖定／比對既有內容，新增事件或計為一致重讀，再 upsert checkpoint 後 commit。
7. 同 GUID 異內容回報 `identity_collision`；NULL GUID 回報 `unresolvable_source_identity`。兩者都不提交該批事件或推進 watermark。

完整欄位 mapping 與錯誤復原說明見 [`Stage2A-Polling.md`](Stage2A-Polling.md)。Source 讀取和 AlarmStore transaction 使用不同 connection，沒有分散式 transaction。只要事件尚未在 AlarmStore 安全 commit，checkpoint 就不會前進；重啟後會由持久化 checkpoint 與 overlap 重讀。

## 測試

一般 solution 測試不連線真實 SQL。SQL 整合測試預設跳過，只有同時符合下列條件才可啟用：

- 私有設定為 `Role=AlarmStore`。
- database 是已核准的隔離測試庫。
- database 名稱不是 `IFIX-ALARM` 或 `IFIX_ALARM`。
- `ALARMSYSTEM_SQL_TEST_CONFIRM_DATABASE` 與設定 database 精確相符。

```powershell
$env:ALARMSYSTEM_RUN_SQL_INTEGRATION_TESTS = "true"
$env:ALARMSYSTEM_SQL_TEST_CONFIG = "C:\secure\sql-alarmstore-test.local.json"
$env:ALARMSYSTEM_SQL_TEST_CONFIRM_DATABASE = "ALARM_SYSTEM_TEST"
dotnet test tests/AlarmSystem.IntegrationTests --filter FullyQualifiedName~SqlServerDatabaseIntegrationTests
```

Repository DML／transaction 測試會套用待處理 migration、寫入受控 fixture 並清理，因此另需明確確認沒有 Worker 使用該隔離 database：

```powershell
$env:ALARMSYSTEM_RUN_SQL_INGESTION_TESTS = "true"
dotnet test tests/AlarmSystem.IntegrationTests --filter FullyQualifiedName~SqlAlarmEventIngestionRepositoryTests
```

一般測試使用記憶體 fake，不讀取兩份私有設定，也不連線 Source。SQL 整合測試仍維持明確 opt-in；階段 2A 開發環境若無可用的核准 AlarmStore，migration dry-run／repository SQL 實測須如實標記未執行，不得以 fake 結果取代。

## 後續階段與現場待驗證

階段 2A 不更新 `AlarmStates`。2B 應以已保存 `AlarmEvents` 的獨立消費進度建立狀態，不能把 Source checkpoint 當成狀態處理完成；2C 才可辨識 OPERATOR 中的 ACK 並建立關聯。通知、outbox、retry scheduler、來源清理與 index 變更均不在 2A。

iFIX 主機上線後仍須驗證真實 SELECT、ALARM／OPERATOR 欄位值、`ALM_MSGID` NULL／重複率、時間精度／時區、同 timestamp 大群組、每五秒查詢成本、是否需要來源 index、ODBC 晚到資料、斷線恢復補讀、逐筆對帳與一小時穩定性；目前全部標記為 **Pending On-Site Verification**。
