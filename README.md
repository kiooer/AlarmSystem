# AlarmSystem

圖控警報系統的 C#/.NET solution 骨架。

目前只建立專案、資料夾、專案參考及基礎樣板，尚未實作 iFIX/ODBC、SQL Server、警報輪詢、去重、Teams、CSV、保留清理或 WPF 管理功能。需求與開發約束請以根目錄的 `AGENTS.md` 為準。

## 專案

- `AlarmSystem.Domain`：領域模型與規則。
- `AlarmSystem.Application`：應用流程與外部介面抽象。
- `AlarmSystem.Infrastructure`：資料庫、iFIX、Teams、CSV 與安全 adapter。
- `AlarmSystem.Worker`：背景服務入口。
- `AlarmSystem.Desktop`：WPF 管理介面入口。
- `AlarmSystem.Domain.Tests`：領域單元測試。
- `AlarmSystem.Application.Tests`：應用層單元測試。
- `AlarmSystem.IntegrationTests`：基礎設施與跨模組整合測試。

## 目前狀態

本機只有 .NET 10 SDK，因此骨架暫以 `net10.0` / `net10.0-windows` 建立。目標 .NET 與 Windows 版本仍屬待確認事項；開始功能開發前應先定案。

尚未執行套件還原，也未把任何外部服務或秘密加入設定。
