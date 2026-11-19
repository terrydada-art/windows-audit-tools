# windows-audit-tools

[繁體中文](#繁體中文) | [English](#english)

---

## 繁體中文

兩個輕量的 Windows 事件記錄稽核小工具，C# + WinForms，目標 **.NET Framework 4.8**（Windows 10 1903+ / Windows 11 內建），
不需要安裝任何執行階段或第三方套件，編譯後的 exe 只有約 55 KB，**未使用 UPX 等壓縮殼**，降低防毒軟體誤判。

| 工具 | 用途 | 權限 |
|---|---|---|
| **LoginAuditor** | 讀取「安全性」記錄檔的登入成功 (4624) / 失敗 (4625)，排除系統帳號，標示登入失敗與少數（可能異常）帳號 | 系統管理員（啟動時自動要求 UAC） |
| **PowerEventViewer** | 讀取「系統」記錄檔的開機 / 關機 / 睡眠 / 休眠事件 | 一般使用者即可 |

兩者皆可依日期區間篩選、隨時停止，並匯出 Excel (.xlsx)。

### 介面語言

支援繁體中文與英文：中文版 Windows 自動顯示中文，其他語言的 Windows 顯示英文。
也可以用命令列參數強制指定（可放在捷徑的「目標」欄位）：

```powershell
PowerEventViewer.exe /lang:en   # 英文
PowerEventViewer.exe /lang:zh   # 繁體中文
```

匯出的 Excel 欄位標題會跟著介面語言；事件說明欄則是 Windows 本身的語言。

### 下載

到 [Releases](../../releases) 下載 `LoginAuditor-vX.Y.Z.zip` 或 `PowerEventViewer-vX.Y.Z.zip`，解壓縮後直接執行。
可用 `SHA256SUMS.txt` 驗證檔案：

```powershell
Get-FileHash .\LoginAuditor-v1.0.0.zip -Algorithm SHA256
```

> 執行檔未經程式碼簽章，第一次執行時 Windows SmartScreen 可能會提示，選「其他資訊 → 仍要執行」。
> 建議放在 `C:\Program Files\` 等僅系統管理員可寫入的資料夾。

### LoginAuditor

- 排除 `SYSTEM`、`NT AUTHORITY\SYSTEM`、`ANONYMOUS LOGON`、電腦帳號（`xxx$`）的**成功**登入。
  登入失敗一律保留，避免攻擊者以這些名稱暴力嘗試時被隱藏。
- 顏色：🟥 登入失敗　🟪 少數帳號（出現次數 ≤ max(2, 總筆數 3%)）　⬛ 正常。
- 依欄位名稱（`TargetUserName`、`LogonType`、`IpAddress`）取值，不受 Windows 版本欄位順序差異影響。

### PowerEventViewer

| 事件 ID | 來源 | 類型 |
|---|---|---|
| 12 / 13 | Kernel-General | 開機 / 關機 |
| 6005 / 6006 / 6008 | EventLog | 開機 / 正常關機 / 非正常關機 |
| 41 / 42 / 45 | Kernel-Power | 異常關機 / 睡眠 / 從休眠恢復 |
| 1074 | User32 | 使用者或程式發起關機/重啟 |
| 1 | Power-Troubleshooter | 從睡眠喚醒 |
| 其他 | Kernel-Power、Power-Troubleshooter | 內容含 hibernate/休眠 → 休眠；sleep/睡眠 → 睡眠 |

說明欄顯示 Windows 的本地化事件訊息。

### 安全設計

- 事件內容視為不可信資料（4625 的帳號名稱可由遠端任意指定，1074 的關機說明可由使用者自訂）：
  移除控制字元與 Unicode 雙向覆寫字元以防畫面偽裝，並限制長度。
- Excel 匯出所有儲存格皆為純文字（inlineStr），不會被當成公式執行（防 Formula Injection）；XML 一律跳脫。
- 匯出先寫入暫存檔再取代目標，失敗不會留下損毀檔案。
- 啟動時 `SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32)`，防止 DLL 劫持。
- 事件查詢以 XPath 交由事件記錄服務篩選，查詢字串只含常數與程式產生的時間，無注入風險。
- 單次最多 1,000,000 筆，避免耗盡記憶體（同時符合 Excel 列數上限）。
- 電源事件的關鍵字比對只套用在電源相關來源，不掃描「安全性」記錄檔，
  避免一般使用者以含 "sleep" 字樣的命令列（4688 事件）偽造睡眠紀錄。

### 自行編譯

需求：.NET SDK 6 以上，以及 .NET Framework 4.8 Developer Pack（或讓 SDK 自動還原 reference assemblies）。

```powershell
dotnet build windows-audit-tools.sln -c Release
```

輸出位於 `LoginAuditor\bin\Release\net48\`、`PowerEventViewer\bin\Release\net48\`。
推送 `v*` 標籤時，GitHub Actions 會自動編譯並建立 Release。

### 專案結構

```
LoginAuditor/       登入稽核
PowerEventViewer/   電源事件檢視器
Shared/             共用：XlsxWriter (無相依 .xlsx 輸出)、TextSafety (不可信字串清理)、Lang (中/英介面)、AppIcon
tools/              make-icons.ps1：重新產生兩個程式的 app.ico (原創向量繪製)
```

### 授權

[GPL-3.0](LICENSE)

---

## English

Two lightweight Windows event-log auditing tools written in C# + WinForms, targeting **.NET Framework 4.8**
(built into Windows 10 1903+ / Windows 11). No runtime or third-party packages to install; each compiled exe is
about 55 KB and is **not packed with UPX** or any other packer, which keeps antivirus false positives down.

| Tool | Purpose | Privileges |
|---|---|---|
| **LoginAuditor** | Reads successful (4624) / failed (4625) logons from the **Security** log, hides system accounts, and highlights failed logons and rare (possibly suspicious) accounts | Administrator (UAC prompt on launch) |
| **PowerEventViewer** | Reads boot / shutdown / sleep / hibernate events from the **System** log | Standard user |

Both tools filter by date range, can be stopped at any time, and export to Excel (.xlsx).

### UI language

English and Traditional Chinese are supported. The UI is shown in Chinese on Chinese Windows and in English on
any other display language. You can force a language with a command-line switch (e.g. in a shortcut's Target field):

```powershell
PowerEventViewer.exe /lang:en   # English
PowerEventViewer.exe /lang:zh   # Traditional Chinese
```

Exported Excel column headers follow the UI language; the event description column uses Windows' own language.

### Download

Get `LoginAuditor-vX.Y.Z.zip` or `PowerEventViewer-vX.Y.Z.zip` from [Releases](../../releases), unzip, and run.
Verify downloads against `SHA256SUMS.txt`:

```powershell
Get-FileHash .\LoginAuditor-v1.0.0.zip -Algorithm SHA256
```

> The executables are not code-signed, so Windows SmartScreen may warn on first run ("More info → Run anyway").
> Placing them in an admin-only folder such as `C:\Program Files\` is recommended.

### LoginAuditor

- Hides **successful** logons by `SYSTEM`, `NT AUTHORITY\SYSTEM`, `ANONYMOUS LOGON`, and computer accounts (`xxx$`).
  Failed logons are always shown, so brute-force attempts using those names cannot hide.
- Colors: 🟥 failed logon　🟪 rare account (count ≤ max(2, 3% of records))　⬛ normal.
- Fields are read by name (`TargetUserName`, `LogonType`, `IpAddress`), not by position, so differences in
  field order between Windows versions do not matter.

### PowerEventViewer

| Event ID | Provider | Category |
|---|---|---|
| 12 / 13 | Kernel-General | Boot / Shutdown |
| 6005 / 6006 / 6008 | EventLog | Boot / Clean shutdown / Unexpected shutdown |
| 41 / 42 / 45 | Kernel-Power | Unexpected shutdown / Sleep / Resume from hibernate |
| 1074 | User32 | Shutdown or restart initiated by a user or process |
| 1 | Power-Troubleshooter | Wake from sleep |
| Others | Kernel-Power, Power-Troubleshooter | Data containing hibernate/休眠 → Hibernate; sleep/睡眠 → Sleep |

The detail column shows Windows' localized event message.

### Security design

- Event data is treated as untrusted (4625 account names can be chosen by a remote attacker; 1074 shutdown
  comments are user-supplied): control characters and Unicode bidi override characters are neutralized to
  prevent display spoofing, and field length is capped.
- Every exported Excel cell is plain text (inlineStr), so nothing is evaluated as a formula (no formula
  injection); all XML is escaped.
- Export writes to a temp file and then replaces the target, so a failure never leaves a corrupt file.
- `SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32)` at startup guards against DLL hijacking.
- Filtering is done by the Event Log service via XPath; the query contains only constants and
  program-generated timestamps, so there is no injection path.
- At most 1,000,000 records per scan to bound memory use (also within Excel's row limit).
- Power-event keyword matching only applies to power-related providers and the Security log is not scanned,
  so a standard user cannot fake sleep records with a command line containing "sleep" (event 4688).

### Building from source

Requirements: .NET SDK 6 or later, plus the .NET Framework 4.8 Developer Pack (or let the SDK restore the
reference assemblies automatically).

```powershell
dotnet build windows-audit-tools.sln -c Release
```

Output goes to `LoginAuditor\bin\Release\net48\` and `PowerEventViewer\bin\Release\net48\`.
Pushing a `v*` tag makes GitHub Actions build and publish a release automatically.

### Project layout

```
LoginAuditor/       Logon auditor
PowerEventViewer/   Power event viewer
Shared/             Shared code: XlsxWriter (dependency-free .xlsx writer), TextSafety (untrusted-text sanitizer), Lang (zh/en UI), AppIcon
tools/              make-icons.ps1: regenerates both app.ico files (original vector drawings)
```

### License

[GPL-3.0](LICENSE)
