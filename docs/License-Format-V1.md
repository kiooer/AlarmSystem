# AlarmSystem Demo License Format V1

## 範圍與信任邊界

Demo License 是離線、綁定單一 Windows 主機的通知模組授權。AlarmSystem 只包含驗證端，不包含 LicenseIssuer、私鑰、簽發 API 或可產生正式 License 的工具。正式信任公鑰必須經核准後編譯進 `DemoLicenseTrustStore`；不得由 `appsettings.json`、環境變數或 WPF 畫面替換。

目前 repository **未包含正式信任公鑰**，所以 Release build 會對所有尚未核准的 `.lic` 維持 fail closed。為供本機 UI 驗證，Debug build 只有在 `DOTNET_ENVIRONMENT=Development` 時才信任 `dev-ui-20261017` 公開測試金鑰；同一 Debug build 在其他環境也不信任。該測試私鑰只在一次性簽章程序的記憶體存在，沒有寫入磁碟或 repository；本機產生的 `.lic` 位於 Git 忽略的 `artifacts/local-test/`，不會隨 commit 散布。自動測試另在測試程序記憶體內建立臨時 P-256 金鑰。完成獨立 LicenseIssuer 後，必須另行核准 `kid` 與 SubjectPublicKeyInfo 公鑰，再做正式端對端驗收。

## JWS Compact 格式

檔案副檔名為 `.lic`，上限 64 KB。內容是三段 base64url、無 padding 的 JWS Compact Serialization：

```text
BASE64URL(header).BASE64URL(payload).BASE64URL(signature)
```

Header 必須恰好包含：

```json
{ "alg": "ES256", "kid": "approved-key-id" }
```

只接受 `ES256`（ECDSA P-256／SHA-256）；簽章固定是 64-byte IEEE P1363 `R || S`，不是 ASN.1 DER。未知 `kid`、其他演算法、未知欄位、缺欄位及重複欄位一律拒絕。

Payload 必須恰好包含：

```json
{
  "formatVersion": 1,
  "productId": "AlarmSystem",
  "licenseId": "DEMO-000001",
  "licenseType": "DEMO",
  "machineIdHash": "SHA256_MACHINE_FINGERPRINT",
  "issuedAtUtc": "2026-10-17T08:00:00Z",
  "notBeforeUtc": "2026-10-17T08:00:00Z",
  "expiresAtUtc": "2026-11-16T08:00:00Z",
  "features": [1, 2, 3]
}
```

日期必須是帶 `Z` 的 UTC ISO-8601；`issuedAtUtc <= notBeforeUtc`。V1 只接受 `expiresAtUtc - notBeforeUtc` 恰為 15 或 30 天。到期判斷採半開區間：`notBeforeUtc <= nowUtc < expiresAtUtc`，所以到期瞬間立即失效。匯入與重新啟動都不會重算期限。

`NotificationFeature` 數值是相容性契約：

| 數值 | 模組 |
|---:|---|
| 1 | LINE |
| 2 | Teams |
| 3 | Email |

feature 必須至少一個、不可重複且不可包含未知值。

## Machine ID

`MachineFingerprintProvider` 在 Windows 讀取 MachineGuid、主機板產品資訊與系統產品資訊，正規化後以 SHA-256 形成只供簽發使用的 Machine ID。UI 只顯示 hash，不顯示原始登錄值，也不使用 MAC Address 作為唯一來源。

Windows 重新安裝、MachineGuid 改變、主機板更換、VM clone／重建或虛擬硬體識別改變時，Machine ID 可能改變，必須由受控流程重新簽發。一般網卡更換不應單獨改變 Machine ID，但仍須在目標實機與 VMware 範本流程驗收。

## 時間完整性與保存

預設檔案：

```text
%ProgramData%\AlarmSystem\Licensing\demo.lic
%ProgramData%\AlarmSystem\Licensing\license-state.dat
```

`license-state.dat` 保存 License ID 與 `LastSeenUtc`，使用 Windows DPAPI `LocalMachine` 保護。建立目錄時 ACL 會移除繼承，只授權 Worker 執行身分、LocalSystem 與本機 Administrators 完全控制。授權讀取、更新狀態或匯入失敗時 fail closed。

若 `nowUtc + 2 分鐘 < LastSeenUtc`，狀態為 `TimeAnomaly` 並拒絕 Demo 授權。兩分鐘是集中設定的暫定容許值。狀態遺失、DPAPI 解密失敗、內容損毀或 License ID 不符也會拒絕；刪除狀態檔不會重置試用期。匯入先完整驗證，再以同目錄暫存檔及 replace/move 保存；失敗時嘗試回復先前 License 與狀態。

完全離線且攻擊者具有本機管理員權限、可還原完整虛擬機快照或可竄改執行檔時，無法保證絕對防重置。正式安全評估必須把 VM snapshot、映像複製、DPAPI machine secret 備份與服務帳號權限納入威脅模型。

## Dongle 與 Demo 合成規則

每次正式 LINE、Teams 或 Email 發送前，`LicensedNotificationChannel` 都重新呼叫 `CompositeLicenseAuthorizer`：

1. Dongle 已授權該 feature：立即授權，Demo 不參與。
2. Dongle 正常不可用、未授權該 feature或已到期：檢查 Demo。
3. Dongle 回報安全驗證失敗：直接拒絕，不降級至 Demo。
4. Demo 必須同時通過簽章、產品、類型、主機、UTC 期限、時間完整性與 feature 檢查。

狀態讀取同樣逐 feature 合併來源：若 Dongle 與 Demo 分別授權不同模組，整體來源顯示 `Dongle + Demo`，表格中的每一列仍標示實際的 Dongle 或 Demo；有效 Dongle 不會被誤顯示成 Demo。

Demo 到期或無效時，decorator 在 HTTP／SMTP transport 前回傳永久授權失敗。現有 Desktop Teams／LINE／Email 手動測試豁免政策不變；這些人工測試不代表正式自動發報已授權。

## 本機管理通道與操作

Worker 提供範圍受限的 Windows Named Pipe `AlarmSystem.LicenseManagement.v1`，只支援：

- `ReadLicenseStatus`
- `PreviewLicenseImport`
- `ImportLicense`
- `RefreshLicenseStatus`

Pipe 僅在本機開啟；Worker 以 Windows impersonation 驗證 client，僅允許與 Worker 相同的 Windows SID 或本機 Administrators。所有正式驗證、DPAPI 狀態與寫入都在 Worker 端執行，不提供 HTTP API。

WPF 第四個「Demo License 測試」分頁操作順序：

1. 複製 Machine ID，交由受控的未來 LicenseIssuer 流程簽發。
2. 選擇不超過 64 KB 的 `.lic`。
3. 按「驗證 License」檢視 License ID、期間、Machine ID match 與 feature 摘要。
4. 確認 Worker 顯示已連線；按「匯入／啟用」並在確認對話框再次核對摘要。
5. 按「重新整理狀態」及「重新檢查授權」確認實際生效來源與各模組結果。

Worker 未啟動或身分未獲授權時，WPF 只做本機唯讀預覽並明確顯示「尚未啟用」；不會直接寫 `demo.lic` 或 `license-state.dat`。

## 待正式驗收

- 核准並嵌入正式 `kid`／公鑰，與獨立 LicenseIssuer 做已知向量及端對端測試。
- 在實際 Windows Service 帳號下驗證 ProgramData ACL、DPAPI、UAC 管理員連線與升級／回復。
- 在核准 Windows／Windows Server、實體機與 VMware 環境驗證 Machine ID 穩定性及重新簽發流程。
- 驗證服務重啟、系統校時、休眠、DST、VM snapshot 還原與 License 換發操作。
- Dongle SDK 完成後，以廠商實際錯誤語意驗證「正常不可用」與「安全失敗」的分類。
