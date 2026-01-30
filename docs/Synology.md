# Synology DSM Webhook 整合指南

本文件說明如何將 Synology NAS 的系統通知透過 DSM 內建「自訂 Webhook」功能，轉發至 SendAlerts Alert Center。

```
Synology DSM (NAS 事件)
    │
    ▼ HTTP POST → /api/send
    │
SendAlerts Alert Center
    │
    ▼ 執行警報動作
[Telegram] [Discord] [Email] [Command Line]...
```

## 前置需求

1. **SendAlerts** 已安裝並運行，且 HTTP API 已啟用
2. **Synology DSM 7.2** 或更高版本（支援自訂 Webhook）
3. NAS 與 SendAlerts 主機位於同一網路（或已設定 Port Forwarding）
4. 已在 SendAlerts 中設定至少一個 Alert Group

## SendAlerts 端設定

### 步驟 1：啟用 HTTP API

1. 開啟 SendAlerts → **Settings**
2. 勾選 **Enable HTTP API**
3. 設定 Port（預設 `58080`）
4. 設定 **API Key**（自訂一串密鑰，例如 `my-secret-key-123`）
5. 儲存設定並重啟程式

### 步驟 2：確認 API 可連線

在 NAS 或同網路的電腦上，用 curl 測試：

```bash
curl -X POST http://<PC_IP>:58080/api/send \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: my-secret-key-123" \
  -d '{"GroupName":"Default","CustomMessage":"測試連線"}'
```

回傳 `200 OK` 即代表連線正常。

## Synology DSM 端設定

### 步驟 1：進入 Webhook 設定

1. 登入 DSM → **控制台** → **通知設定** → **Webhook**
2. 點選 **新增** → **自訂**

### 步驟 2：填寫 Webhook 設定

| 欄位 | 值 |
|---|---|
| Provider 名稱 | `SendAlerts` |
| Webhook URL | `http://<PC_IP>:58080/api/send` |
| HTTP Method | `POST` |
| HTTP Header | `X-Api-Key: my-secret-key-123` |

Content-Type 預設為 `application/json`。

### 步驟 3：設定 Body (JSON)

在 Body / Payload 欄位填入：

```json
{
  "GroupName": "Critical",
  "CustomMessage": "@@TEXT@@"
}
```

- **`@@TEXT@@`**：DSM 自動替換為實際通知內容（如「硬碟 1 出現壞軌」）
- **`GroupName`**：對應 SendAlerts 中的 Alert Group 名稱，可依需求改為 `Warning`、`Default` 等

### 步驟 4：測試並套用

1. 點選 **測試** 確認收到通知
2. 點選 **套用**

## 適用場景

Synology DSM 支援以下事件類型的通知（皆可透過此 Webhook 轉發）：

- **儲存空間**：硬碟 S.M.A.R.T. 異常、壞軌、RAID 降級 (Degraded)、空間不足
- **系統**：CPU/系統溫度過高、風扇故障、記憶體錯誤
- **電源**：UPS 斷電、電池電量低、供電恢復
- **網路**：網路介面斷線、IP 衝突
- **安全性**：登入失敗、帳號被鎖定、防火牆事件
- **套件**：Hyper Backup 失敗、Surveillance Station 異常

## 進階用法

### 依嚴重程度分組

可建立多個 Webhook，將不同嚴重程度的事件導向不同 Alert Group：

| DSM 通知規則 | Webhook Body | 說明 |
|---|---|---|
| 儲存空間嚴重警告 | `{"GroupName":"Critical","CustomMessage":"@@TEXT@@"}` | 立即通知管理員 |
| 系統溫度警告 | `{"GroupName":"Warning","CustomMessage":"@@TEXT@@"}` | 一般警告 |
| 套件更新通知 | `{"GroupName":"Info","CustomMessage":"@@TEXT@@"}` | 低優先資訊 |

### 防火牆注意事項

若 SendAlerts 主機有防火牆，請確保已開放 API Port（預設 `58080`）的 TCP 連入。

- **Windows 防火牆**：新增輸入規則 → TCP Port `58080`
- **Linux iptables**：`sudo iptables -A INPUT -p tcp --dport 58080 -j ACCEPT`
