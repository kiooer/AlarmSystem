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
