using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using SendAlerts.Resources;

namespace SendAlerts.Services;

/// <summary>
/// 多語系化服務
/// 優先順序: 使用者設定 > OS 語系 > 英文預設
/// </summary>
public class LocalizationService : INotifyPropertyChanged
{
    private static LocalizationService? _instance;
    private static readonly object _lock = new();

    private readonly ResourceManager _resourceManager;
    private CultureInfo _currentCulture;

    /// <summary>
    /// 支援的語系清單
    /// </summary>
    public static readonly IReadOnlyList<LanguageOption> SupportedLanguages = new List<LanguageOption>
    {
        new("auto", "Auto (System Default)", "自動"),
        new("en", "English", "English"),
        new("zh-TW", "繁體中文", "繁體中文"),
        new("ja", "日本語", "日本語")
    };

    /// <summary>
    /// 單例實例
    /// </summary>
    public static LocalizationService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new LocalizationService();
                }
            }
            return _instance;
        }
    }

    /// <summary>
    /// 目前的語系
    /// </summary>
    public CultureInfo CurrentCulture => _currentCulture;

    /// <summary>
    /// 目前的語系代碼
    /// </summary>
    public string CurrentLanguageCode => _currentCulture.Name;

    public event PropertyChangedEventHandler? PropertyChanged;

    private LocalizationService()
    {
        _resourceManager = new ResourceManager(typeof(Strings));
        _currentCulture = CultureInfo.CurrentUICulture;
    }

    /// <summary>
    /// 初始化語系設定
    /// </summary>
    /// <param name="languageSetting">使用者設定的語系代碼 (null 或 "auto" 表示自動偵測)</param>
    public void Initialize(string? languageSetting)
    {
        CultureInfo targetCulture;

        if (string.IsNullOrEmpty(languageSetting) || languageSetting == "auto")
        {
            // 自動偵測 OS 語系
            targetCulture = GetSystemCulture();
        }
        else
        {
            // 使用指定的語系
            try
            {
                targetCulture = new CultureInfo(languageSetting);
            }
            catch (CultureNotFoundException)
            {
                // 無效的語系代碼，使用系統預設
                targetCulture = GetSystemCulture();
            }
        }

        SetCulture(targetCulture);
    }

    /// <summary>
    /// 取得系統語系，如果不支援則回傳英文
    /// </summary>
    private static CultureInfo GetSystemCulture()
    {
        var systemCulture = CultureInfo.CurrentUICulture;

        // 檢查是否為支援的語系
        var cultureName = systemCulture.Name;

        // 繁體中文
        if (cultureName.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase) ||
            cultureName.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase))
        {
            return new CultureInfo("zh-TW");
        }

        // 日文
        if (cultureName.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
        {
            return new CultureInfo("ja");
        }

        // 預設英文
        return new CultureInfo("en");
    }

    /// <summary>
    /// 設定語系
    /// </summary>
    public void SetCulture(CultureInfo culture)
    {
        _currentCulture = culture;

        // 設定執行緒文化
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        // 通知屬性變更
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    /// <summary>
    /// 變更語系
    /// </summary>
    /// <param name="languageCode">語系代碼 (如 "en", "zh-TW", "ja")</param>
    public void ChangeLanguage(string languageCode)
    {
        if (languageCode == "auto")
        {
            SetCulture(GetSystemCulture());
        }
        else
        {
            try
            {
                SetCulture(new CultureInfo(languageCode));
            }
            catch (CultureNotFoundException)
            {
                // 無效的語系代碼，忽略
            }
        }
    }

    /// <summary>
    /// 取得本地化字串
    /// </summary>
    /// <param name="key">資源鍵</param>
    /// <returns>本地化字串，找不到時回傳鍵名</returns>
    public string GetString(string key)
    {
        try
        {
            var value = _resourceManager.GetString(key, _currentCulture);
            return value ?? key;
        }
        catch
        {
            return key;
        }
    }

    /// <summary>
    /// 索引器 - 取得本地化字串
    /// </summary>
    public string this[string key] => GetString(key);

    /// <summary>
    /// 取得格式化的本地化字串
    /// </summary>
    public string GetString(string key, params object[] args)
    {
        var format = GetString(key);
        try
        {
            return string.Format(_currentCulture, format, args);
        }
        catch
        {
            return format;
        }
    }
}

/// <summary>
/// 語系選項
/// </summary>
public class LanguageOption
{
    /// <summary>語系代碼</summary>
    public string Code { get; }

    /// <summary>顯示名稱 (英文)</summary>
    public string DisplayName { get; }

    /// <summary>原生名稱</summary>
    public string NativeName { get; }

    public LanguageOption(string code, string displayName, string nativeName)
    {
        Code = code;
        DisplayName = displayName;
        NativeName = nativeName;
    }

    public override string ToString() => NativeName;
}
