# 分離式 SQL Server Database Foundation

本階段已實作 SQL 連線診斷、iFIX 來源 metadata 檢查，以及 AlarmSystem 儲存 schema 的 migration／驗證工具。尚未實作警報輪詢、ACK 關聯、去重流程、outbox 或 Worker 資料存取。

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

`verify-schema` 唯讀檢查 schema、table、欄位、PK／index、CHECK／FK 與 migration history，不會自動修正。

2026-10-16 現場驗證已確認獨立 AlarmStore：SQL instance `Admin\SQLEXPRESS`、database `alarmcall`、SQL Server `17.0.1000.7` Express 64-bit。經使用者明確授權後已套用 `0001_CreateAlarmFoundation`；套用後與 SQL integration test 後兩次 `verify-schema` 均回報 `IsValid=true`、`Issues=[]`，重跑 dry-run 回報 migration `IsApplied=true`。

## 安全與既有來源端物件

在分離決策確認前，使用者曾核准工具依當時設定將 `0001_CreateAlarmFoundation` 套用到遠端 `IFIX-ALARM`。套用後已驗證四個 `alarm` tables 完整，且 migration 本身不含 `FIXALARMS` DDL。

這些來源端 `alarm.*` 物件目前保留。移除它們需要另行核准，且必須先確認備份、相依性、登入目標與精確物件；本次不執行刪除或 rollback。

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

## 後續階段

來源與 AlarmStore 的基礎連線及 schema 已驗證。下一階段仍須依核准的來源欄位語意、時間精度、ALARM／ACK 值、ACK 關聯與 `ALM_MSGID` 品質，才能實作實際輪詢、正規化、去重與狀態更新；不得把本次 database foundation 驗證視為完整資料流程已完成。
