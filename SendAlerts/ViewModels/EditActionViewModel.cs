using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SendAlerts.Core.Interfaces;
using SendAlerts.Models;
using SendAlerts.Services;

namespace SendAlerts.ViewModels;

/// <summary>
/// TB1-2/TB1-3: 新增/編輯 Action 對話框 ViewModel
/// </summary>
public partial class EditActionViewModel : ViewModelBase
{
    private readonly AlertActionConfig? _existingConfig;

    /// <summary>
    /// 是否為編輯模式（false = 新增模式）
    /// </summary>
    public bool IsEditMode => _existingConfig != null;

    /// <summary>
    /// 對話框標題
    /// </summary>
    public string Title => IsEditMode ? "Edit Action" : "Add Action";

    /// <summary>
    /// 可選擇的動作類型
    /// </summary>
    public ObservableCollection<ActionTypeItem> ActionTypes { get; } = new();

    /// <summary>
    /// 選中的動作類型
    /// </summary>
    [ObservableProperty]
    private ActionTypeItem? _selectedActionType;

    /// <summary>
    /// 是否可以變更類型（編輯模式下不可變更）
    /// </summary>
    public bool CanChangeType => !IsEditMode;

    // === 共用屬性 ===
    [ObservableProperty] private string _instanceId = string.Empty;
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private int _cooldownSeconds = 30;
    [ObservableProperty] private bool _debugMode = false;

    // === CommandLine 專用 ===
    [ObservableProperty] private string _command = string.Empty;

    // === Telegram 專用 ===
    [ObservableProperty] private string _telegramBotToken = string.Empty;
    [ObservableProperty] private string _telegramChatId = string.Empty;

    // === LineNotify 專用 ===
    [ObservableProperty] private string _lineNotifyAccessToken = string.Empty;

    // === Email 專用 ===
    [ObservableProperty] private string _smtpHost = string.Empty;
    [ObservableProperty] private int _smtpPort = 587;
    [ObservableProperty] private bool _smtpUseSsl = true;
    [ObservableProperty] private string _smtpUsername = string.Empty;
    [ObservableProperty] private string _smtpPassword = string.Empty;
    [ObservableProperty] private string _emailFromAddress = string.Empty;
    [ObservableProperty] private string _emailToAddresses = string.Empty;

    // === HttpWebhook 專用 ===
    [ObservableProperty] private string _webhookUrl = string.Empty;
    [ObservableProperty] private string _webhookHttpMethod = "POST";
    [ObservableProperty] private string _webhookBodyTemplate = string.Empty;

    // === 驗證 ===
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _hasError;

    /// <summary>
    /// 確認結果
    /// </summary>
    public bool DialogResult { get; private set; }

    /// <summary>
    /// 關閉請求事件
    /// </summary>
    public event Action? CloseRequested;

    /// <summary>
    /// 建構子 - 新增模式
    /// </summary>
    public EditActionViewModel() : this(null) { }

    /// <summary>
    /// 建構子 - 編輯模式
    /// </summary>
    public EditActionViewModel(AlertActionConfig? existingConfig)
    {
        _existingConfig = existingConfig;

        // 載入可用的動作類型
        foreach (var type in AlertActionFactory.GetImplementedTypes())
        {
            ActionTypes.Add(new ActionTypeItem(type));
        }

        if (existingConfig != null)
        {
            // 編輯模式：載入現有設定
            LoadFromConfig(existingConfig);
        }
        else
        {
            // 新增模式：選擇預設類型
            SelectedActionType = ActionTypes.FirstOrDefault();
            GenerateInstanceId();
        }
    }

    /// <summary>
    /// 從現有設定載入
    /// </summary>
    private void LoadFromConfig(AlertActionConfig config)
    {
        SelectedActionType = ActionTypes.FirstOrDefault(t => t.Type == config.ActionType);
        InstanceId = config.InstanceId;
        IsEnabled = config.IsEnabled;
        CooldownSeconds = config.CooldownSeconds;
        DebugMode = config.DebugMode;

        switch (config.ActionType)
        {
            case AlertActionType.CommandLine:
                Command = config.Command ?? string.Empty;
                break;
            case AlertActionType.Telegram:
                TelegramBotToken = config.TelegramBotToken ?? string.Empty;
                TelegramChatId = config.TelegramChatId ?? string.Empty;
                break;
            case AlertActionType.LineNotify:
                LineNotifyAccessToken = config.LineNotifyAccessToken ?? string.Empty;
                break;
            case AlertActionType.Email:
                SmtpHost = config.SmtpHost ?? string.Empty;
                SmtpPort = config.SmtpPort ?? 587;
                SmtpUseSsl = config.SmtpUseSsl ?? true;
                SmtpUsername = config.SmtpUsername ?? string.Empty;
                SmtpPassword = config.SmtpPassword ?? string.Empty;
                EmailFromAddress = config.EmailFromAddress ?? string.Empty;
                EmailToAddresses = config.EmailToAddresses ?? string.Empty;
                break;
            case AlertActionType.HttpWebhook:
                WebhookUrl = config.WebhookUrl ?? string.Empty;
                WebhookHttpMethod = config.WebhookHttpMethod ?? "POST";
                WebhookBodyTemplate = config.WebhookBodyTemplate ?? string.Empty;
                break;
        }
    }

    partial void OnSelectedActionTypeChanged(ActionTypeItem? value)
    {
        if (!IsEditMode && value != null)
        {
            GenerateInstanceId();
        }
    }

    /// <summary>
    /// 自動產生 InstanceId
    /// </summary>
    private void GenerateInstanceId()
    {
        if (SelectedActionType == null) return;

        var typeName = SelectedActionType.Type.ToString();
        var timestamp = DateTime.Now.ToString("HHmmss");
        InstanceId = $"{typeName}_{timestamp}";
    }

    /// <summary>
    /// 驗證輸入
    /// </summary>
    private bool Validate()
    {
        ErrorMessage = string.Empty;
        HasError = false;

        if (string.IsNullOrWhiteSpace(InstanceId))
        {
            ErrorMessage = "Instance ID 不可為空";
            HasError = true;
            return false;
        }

        if (SelectedActionType == null)
        {
            ErrorMessage = "請選擇動作類型";
            HasError = true;
            return false;
        }

        // 根據類型驗證必填欄位
        switch (SelectedActionType.Type)
        {
            case AlertActionType.CommandLine:
                if (string.IsNullOrWhiteSpace(Command))
                {
                    ErrorMessage = "命令不可為空";
                    HasError = true;
                    return false;
                }
                break;

            case AlertActionType.Telegram:
                if (string.IsNullOrWhiteSpace(TelegramBotToken))
                {
                    ErrorMessage = "Bot Token 不可為空";
                    HasError = true;
                    return false;
                }
                if (string.IsNullOrWhiteSpace(TelegramChatId))
                {
                    ErrorMessage = "Chat ID 不可為空";
                    HasError = true;
                    return false;
                }
                break;

            case AlertActionType.LineNotify:
                if (string.IsNullOrWhiteSpace(LineNotifyAccessToken))
                {
                    ErrorMessage = "Access Token 不可為空";
                    HasError = true;
                    return false;
                }
                break;

            case AlertActionType.HttpWebhook:
                if (string.IsNullOrWhiteSpace(WebhookUrl))
                {
                    ErrorMessage = "Webhook URL 不可為空";
                    HasError = true;
                    return false;
                }
                if (!Uri.TryCreate(WebhookUrl, UriKind.Absolute, out _))
                {
                    ErrorMessage = "Webhook URL 格式無效";
                    HasError = true;
                    return false;
                }
                break;
        }

        return true;
    }

    /// <summary>
    /// 建立 AlertActionConfig
    /// </summary>
    public AlertActionConfig? CreateConfig()
    {
        if (!Validate() || SelectedActionType == null) return null;

        var config = new AlertActionConfig
        {
            InstanceId = InstanceId.Trim(),
            ActionType = SelectedActionType.Type,
            IsEnabled = IsEnabled,
            CooldownSeconds = CooldownSeconds,
            DebugMode = DebugMode
        };

        switch (SelectedActionType.Type)
        {
            case AlertActionType.CommandLine:
                config.Command = Command;
                break;
            case AlertActionType.Telegram:
                config.TelegramBotToken = TelegramBotToken;
                config.TelegramChatId = TelegramChatId;
                break;
            case AlertActionType.LineNotify:
                config.LineNotifyAccessToken = LineNotifyAccessToken;
                break;
            case AlertActionType.Email:
                config.SmtpHost = SmtpHost;
                config.SmtpPort = SmtpPort;
                config.SmtpUseSsl = SmtpUseSsl;
                config.SmtpUsername = SmtpUsername;
                config.SmtpPassword = SmtpPassword;
                config.EmailFromAddress = EmailFromAddress;
                config.EmailToAddresses = EmailToAddresses;
                break;
            case AlertActionType.HttpWebhook:
                config.WebhookUrl = WebhookUrl;
                config.WebhookHttpMethod = WebhookHttpMethod;
                config.WebhookBodyTemplate = WebhookBodyTemplate;
                break;
        }

        return config;
    }

    [RelayCommand]
    private void Confirm()
    {
        if (!Validate()) return;

        DialogResult = true;
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        CloseRequested?.Invoke();
    }
}

/// <summary>
/// 動作類型選項
/// </summary>
public class ActionTypeItem
{
    public AlertActionType Type { get; }
    public string DisplayName { get; }
    public string Icon { get; }

    public ActionTypeItem(AlertActionType type)
    {
        Type = type;
        DisplayName = AlertActionFactory.GetDisplayName(type);
        Icon = type switch
        {
            AlertActionType.CommandLine => "\u2318",
            AlertActionType.Telegram => "\u2708",
            AlertActionType.LineNotify => "\u260E",
            AlertActionType.Email => "\u2709",
            AlertActionType.HttpWebhook => "\u21C4",
            AlertActionType.SystemShutdown => "\u23FB",
            _ => "\u2022"
        };
    }

    public override string ToString() => $"{Icon} {DisplayName}";
}
