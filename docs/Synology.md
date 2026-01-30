# Synology DSM Webhook 整合指南

本文件說明如何將 Synology NAS 的系統通知透過 DSM 內建「Webhook」功能發送警報。

## Synology Webhook 限制

DSM 的 Webhook 通知供應商只有 **一個 URL 輸入欄位**，不支援自訂 HTTP Header 或 Body。
系統會以 GET/POST 呼叫該 URL，並將 `@@TEXT@@` 替換為實際通知內容。

因此，所有參數（API Key、Chat ID 等）都必須編碼在 URL 中。

## 方案一：直接呼叫 Telegram Bot API

不需經過 SendAlerts，DSM 直接呼叫 Telegram API。

### 前置需求

1. 已建立 Telegram Bot，取得 **Bot Token**
2. 已取得目標 **Chat ID**

### Webhook URL

```
https://api.telegram.org/bot<BOT_TOKEN>/sendMessage?chat_id=<CHAT_ID>&text=@@TEXT@@
```

### 範例

```
https://api.telegram.org/bot500000:AAHITW-PGLiif28og7YSntAjAmY-CQQ/sendMessage?chat_id=507777757&text=@@TEXT@@
```

DSM 會將 `@@TEXT@@` 替換為通知內容（如「硬碟 1 出現壞軌」），Telegram Bot 直接發送訊息。

## 方案二：直接呼叫 Discord Webhook

### 前置需求

1. 已建立 Discord Webhook，取得 **Webhook URL**

### Webhook URL

```
<DISCORD_WEBHOOK_URL>?content=@@TEXT@@
```

### 範例

```
https://discord.com/api/webhooks/123456789/abcdefg?content=@@TEXT@@
```

> **注意**：Discord Webhook 預設接受 POST JSON body，直接用 query parameter `content` 可能不被支援。
> 如需穩定方案，建議使用方案三透過 SendAlerts 轉發。

## 方案三：透過 SendAlerts HTTP API

將 DSM 通知轉發至 SendAlerts Alert Center，再由 SendAlerts 統一分派到多個管道。

```
Synology DSM (NAS 事件)
    │
    ▼ HTTP GET → /api/send?key=...&group=...&message=@@TEXT@@
    │
SendAlerts Alert Center
    │
    ▼ 執行警報動作
[Telegram] [Discord] [Email] [Command Line]...
```

### 前置需求

1. **SendAlerts** 已安裝並運行，且 HTTP API 已啟用
2. **Synology DSM 7.2** 或更高版本
3. NAS 與 SendAlerts 主機位於同一網路（或已設定 Port Forwarding）
4. 已在 SendAlerts 中設定至少一個 Alert Group

### SendAlerts 端設定

1. 開啟 SendAlerts → **Settings**
2. 勾選 **Enable HTTP API**
3. 設定 Port（預設 `58080`）
4. 設定 **API Key**
5. 儲存設定並重啟程式

### Webhook URL

```
http://<PC_IP>:58080/api/send?key=<API_KEY>&group=<GROUP_NAME>&message=@@TEXT@@
```

### 範例

```
http://192.168.1.100:58080/api/send?key=my-secret-key-123&group=Critical&message=@@TEXT@@
```

### 測試

在 DSM Webhook 設定頁面點選 **測試**，確認 SendAlerts 收到通知。

## Synology DSM 設定步驟

1. 登入 DSM → **控制台** → **通知設定** → **Webhook**
2. 點選 **新增** → 選擇 **自訂**
3. **Provider 名稱**：填入 `SendAlerts`（或 `Telegram` 等）
4. **Webhook URL**：貼上上方對應的 URL
5. 點選 **測試** 確認正常
6. **套用**

## 適用場景

Synology DSM 支援以下事件類型的通知：

- **儲存空間**：硬碟 S.M.A.R.T. 異常、壞軌、RAID 降級 (Degraded)、空間不足
- **系統**：CPU/系統溫度過高、風扇故障、記憶體錯誤
- **電源**：UPS 斷電、電池電量低、供電恢復
- **網路**：網路介面斷線、IP 衝突
- **安全性**：登入失敗、帳號被鎖定、防火牆事件
- **套件**：Hyper Backup 失敗、Surveillance Station 異常

## 進階：依嚴重程度分組

可建立多個 Webhook Provider，將不同嚴重程度的事件導向不同 Alert Group：

| DSM 通知規則 | Webhook URL |
|---|---|
| 儲存空間嚴重警告 | `http://192.168.1.100:58080/api/send?key=KEY&group=Critical&message=@@TEXT@@` |
| 系統溫度警告 | `http://192.168.1.100:58080/api/send?key=KEY&group=Warning&message=@@TEXT@@` |
| 套件更新通知 | `http://192.168.1.100:58080/api/send?key=KEY&group=Info&message=@@TEXT@@` |

## 防火牆注意事項

若 SendAlerts 主機有防火牆，請確保已開放 API Port（預設 `58080`）的 TCP 連入。

- **Windows 防火牆**：新增輸入規則 → TCP Port `58080`
- **Linux iptables**：`sudo iptables -A INPUT -p tcp --dport 58080 -j ACCEPT`
