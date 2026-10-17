# AlarmSystem Software License Format V2

## 安全邊界與目前狀態

Software License V2 是 AlarmSystem 客戶端的離線驗證格式。產品 repository 只包含驗證、匯入與授權查詢，不包含私鑰、簽發 API、LicenseIssuer 或可由客戶自行產生有效 License 的功能。

目前尚無正式 USB 硬體簽發金鑰，也尚無核准的正式簽發公鑰。`SoftwareLicenseTrustStore` 的產品預設信任清單為空，不能由 `appsettings.json`、環境變數或 WPF 畫面注入；因此目前 Release 對所有未核准 V2 License 維持 fail closed。自動測試只在測試程序記憶體產生臨時 ECDSA P-256 金鑰，測試私鑰與公鑰均不進入產品組件。

V1 的既有 Debug／Development 測試信任行為保持不變，但不會套用到 V2。正式公鑰的加入、Key A／B 實體 USB 簽章與撤銷／輪替驗收均屬後續階段，詳見根目錄 [`開發目標.md`](../開發目標.md)。

## JWS 外層格式

檔案副檔名為 `.lic`，內容是 ASCII JWS Compact Serialization：

```text
BASE64URL(UTF8(header)).BASE64URL(UTF8(payload)).BASE64URL(signature)
```

- 演算法固定為 ES256：ECDSA P-256／SHA-256。
- signature 固定為 64-byte IEEE P1363 `R || S`，不接受 DER 簽章。
- header 只能且必須包含 `alg`、`kid`；`alg` 必須精確等於 `ES256`。
- `kid` 必須出現在編譯進產品的信任庫。架構支援多組 `kid`，供未來 Key A／B 與輪替使用。
- base64url 不接受 padding、非法字元或不合法長度。
- header／payload 不接受註解、尾逗號、重複欄位、未知欄位或缺少欄位。
- 檔案必須大於 0 且不得超過 65,536 bytes。

## Payload 契約

Payload 必須精確包含下列欄位：

```json
{
  "formatVersion": 2,
  "productId": "AlarmSystem",
  "licenseId": "LICENSE-2026-000001",
  "licenseType": "DEMO",
  "bindingMode": "MACHINE",
  "machineIdHash": "64_CHARACTER_SHA256_HEX_VALUE",
  "issuedAtUtc": "2026-10-17T08:00:00Z",
  "notBeforeUtc": "2026-10-17T08:00:00Z",
  "expiresAtUtc": "2027-01-15T08:00:00Z",
  "features": [1, 2, 3]
}
```

上例只是已解碼的 payload，不是完整 `.lic`，不能直接匯入。

| 欄位 | 規則 |
|---|---|
| `formatVersion` | 必須為 JSON number `2` |
| `productId` | 必須精確為 `AlarmSystem` |
| `licenseId` | 非空白、去除前後空白後不得改變，最長 128 字元 |
| `licenseType` | `DEMO`、`SUBSCRIPTION`、`PERPETUAL` |
| `bindingMode` | `MACHINE`、`NONE` |
| `machineIdHash` | MACHINE 時為 64 位十六進位 SHA-256；NONE 時必須是 JSON `null` |
| `issuedAtUtc` | UTC ISO-8601 字串，必須帶 `Z` |
| `notBeforeUtc` | UTC ISO-8601 字串，必須帶 `Z`，且不得早於 issued time |
| `expiresAtUtc` | 有限授權為 UTC `Z` 字串；永久授權必須是 JSON `null` |
| `features` | 非空且不重複的整數陣列：LINE=1、Teams=2、Email=3 |

所有名稱與 enum 值均區分大小寫。到期邊界為 `nowUtc >= expiresAtUtc`；在 `nowUtc < notBeforeUtc` 時，簽章與內容可以通過預覽／匯入，但狀態為 `Pending`，不提供任何正式通知權限。

## 授權類型與綁定政策

### DEMO

- `notBeforeUtc` 與 `expiresAtUtc` 由受控簽發流程指定，不再限於 V1 的 15／30 天。
- 支援 MACHINE 與 NONE。
- 必須有 `expiresAtUtc`，且 `expiresAtUtc > notBeforeUtc`。

### SUBSCRIPTION

- 由簽發內容決定有限起訖時間。
- 本版本只接受 MACHINE；NONE 一律拒絕。
- 必須有 `expiresAtUtc`，且 `expiresAtUtc > notBeforeUtc`。
- 換發使用新的 License ID；本版本不連線線上訂閱服務。

### PERPETUAL

- 只接受 MACHINE，`machineIdHash` 必須合法且必須符合目前主機。
- `expiresAtUtc` 必須為 JSON `null`；UI 顯示「永久有效」與 Remaining Days `N/A`。
- 永久只免除到期時間，不免除簽章、產品、啟用日、主機與 feature 驗證。

## Machine ID

MACHINE 模式沿用 V1 的 `IMachineFingerprintProvider`。Windows 穩定識別資訊經正規化後計算 SHA-256，輸出 64 位十六進位字串；比較時不區分十六進位大小寫並使用固定時間比較。NONE 模式不比較 Machine ID，且 payload 必須明確保存 `machineIdHash: null`。

Machine ID 的組成、VM clone／snapshot、主機板或 OS 變更後的穩定性仍需在目標 Windows／VMware 環境實測；它不是祕密，也不是簽發金鑰。

## 儲存、時間狀態與換發

V2 使用與 V1 完全分開的預設檔案：

```text
%ProgramData%\AlarmSystem\Licensing\software-v2.lic
%ProgramData%\AlarmSystem\Licensing\software-license-state.dat
```

狀態檔保存 License ID、License 內容 SHA-256 fingerprint 與 `LastSeenUtc`，使用 Windows DPAPI LocalMachine 保護。目錄使用既有受限 ACL；只有 Worker 執行身分、LocalSystem 與本機 Administrators 可管理。匯入由 Worker 在完整驗證後原子寫入狀態與 License，失敗時盡可能回復舊檔。

- 相同 License ID、相同已簽內容：允許重匯入；簽章期限不會延長，`LastSeenUtc` 不會倒退。
- 相同 License ID、不同已簽內容：拒絕，避免以同一識別覆蓋期限或權限。
- 不同 License ID：允許用有效的新授權換發現有、過期或永久授權；沿用全域時間下限，不能藉換發清除回溯異常。
- License 存在但狀態遺失、損毀、無法解密、License ID／fingerprint 不符：fail closed。
- 預設判定 `nowUtc + 2 分鐘 < LastSeenUtc` 為時間回溯；容許值可用非敏感設定調整，但不得超過 10 分鐘。
- V2 不讀寫 `demo.lic` 或 `license-state.dat`，V1 與 V2 可以同時安裝。

## 三來源授權規則

正式 Worker 仍只透過 `LicensedNotificationChannel` 在 transport 前檢查集中式 `INotificationLicenseAuthorizer`。LINE、Teams、Email 各自判斷，優先順序為：

```text
Dongle > Software V2 > Demo V1
```

不同 feature 可由不同來源組成聯集，同一 feature 只顯示最高優先有效來源。Dongle 正常未連接、正常到期或沒有該 feature 時可向下檢查；Dongle 明確硬體安全驗證失敗時禁止任何 fallback。V2 無效不會撤銷另一個獨立有效的 Dongle feature。Desktop 的 Teams／LINE／Email 人工發送頁維持既有例外，不代表正式 Worker 已獲授權。

## WPF 與 Named Pipe

WPF「Software License 管理」頁沿用受 Windows 身分限制的 `AlarmSystem.LicenseManagement.v1` Named Pipe。正式預覽、匯入、狀態重新驗證與寫檔都由 Worker 執行；Worker 未連線時只有本機唯讀預覽，不能啟用或寫入 ProgramData。

狀態畫面顯示 License ID、類型、格式版本、來源、狀態、綁定、Machine ID／match、日期、剩餘天數、features、簽章結果、Worker 連線與非敏感診斷。多來源時 feature 表格是實際來源的權威顯示。

## 非敏感設定

```text
Licensing__Software__LicensePath
Licensing__Software__StatePath
Licensing__Software__ClockRollbackToleranceSeconds=120
Licensing__Software__MaximumLicenseBytes=65536
```

公鑰、私鑰與 `kid` 信任關係都不是部署設定項。正式公鑰只能在核准、審查與已知向量測試後以程式變更加入 trust store。

## 測試與正式上線阻擋事項

自動測試以 runtime ephemeral P-256 key 驗證格式、三種 License、綁定、期限、換發、重啟、時間回溯、損毀狀態、三來源聯集與 transport fail-closed；不接觸真實 USB、LINE、Teams 或 SMTP。

正式上線前仍需完成：

- 取得主／備 USB 硬體簽發金鑰與正式公鑰，核准正式 `kid`。
- 在另一個 private repository 建立受控 LicenseIssuer，完成硬體簽章與自我驗證。
- 以 Key A／B 已知向量確認 P1363 編碼、輪替、撤銷與災難復原。
- 在目標 Windows Service 帳號驗證 Named Pipe ACL、ProgramData ACL、DPAPI、更新／回復與 Machine ID 穩定性。
- 取得正式 Dongle SDK 後驗證正常不可用與硬體安全錯誤的真實分類。
