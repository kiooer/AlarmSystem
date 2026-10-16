# ADR-0001：分離 iFIX 來源 SQL 與 AlarmSystem 儲存 SQL

- 狀態：已核准
- 日期：2026-10-16

## 決策

iFIX 來源與 AlarmSystem 自有資料必須位於不同的 SQL 連線目標，並使用不同設定檔與最低權限帳號：

- `Source`：現場核准來源為 `IFIX-ALARM.dbo.FIXALARMS`，只供 metadata 檢查與未來警報讀取。
- `AlarmStore`：另一個由部署人員核准的 SQL Server/database，保存 `alarm.AlarmEvents`、`AlarmStates`、`PollingCheckpoints`、migration history，以及後續 outbox／delivery 資料。

`SqlServer.Role` 為必填。DatabaseTool 在開啟 SQL 連線前強制命令與角色相符：

- `inspect-source` 只接受 `Source`。
- `initialize`、`verify-schema` 只接受 `AlarmStore`。
- `test-connection` 接受兩者並回報角色。

## 理由

來源資料由 iFIX／現場系統擁有，不應混入 AlarmSystem 自有 schema、migration 權限與保留清理生命週期。分離後可對來源帳號只授予必要的 metadata／SELECT 權限，也可獨立備份、升級、清理與復原 AlarmSystem 資料。

## 已知現況與後續處理

在本決策確認前，`0001_CreateAlarmFoundation` 曾依當時核准的設定套用至遠端 `IFIX-ALARM`，因此該來源 database 目前已有 `alarm` schema 與四個 foundation tables；`dbo.FIXALARMS` 未被 migration 修改。移除這些物件是破壞性 schema 變更，必須另行確認備份、相依性與精確目標後執行，不包含在本次角色分離變更中。

新的 AlarmStore endpoint、database 名稱、帳號、TLS 與權限尚待部署人員提供及驗收；不得沿用 Source 設定繞過角色檢查。
