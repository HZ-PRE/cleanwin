using System.Windows;
using CleanOptimize.Core.Store;

namespace CleanOptimize.App.Services;

/// <summary>
/// 轻量多语言服务：语言字典以合并 ResourceDictionary 叠加，
/// XAML 用 {DynamicResource key}、代码用 Loc.T(key) 取词。
/// 翻译回退在生成资源时完成；运行时只加载当前语言的完整字典。
/// </summary>
public static class Loc
{
    /// <summary>支持的语言（value = 字典文件名）。</summary>
    public static readonly (string Code, string NativeName)[] Supported =
    [
        ("zh-CN", "简体中文"),
        ("zh-TW", "繁體中文"),
        ("en-US", "English"),
        ("ja-JP", "日本語"),
        ("ko-KR", "한국어"),
        ("fr-FR", "Français"),
        ("es-ES", "Español"),
        ("de-DE", "Deutsch"),
    ];

    public static event Action? LanguageChanged;

    private static string _current = "zh-CN";

    public static string Current => _current;

    /// <summary>设置语言并应用（"auto" 按系统语言解析）。</summary>
    public static void SetLanguage(string language)
    {
        var code = language == "auto" ? DetectSystemLanguage() : language;
        if (!Supported.Any(s => s.Code == code)) code = "zh-CN";
        var merged = Application.Current.Resources.MergedDictionaries;
        var uri = new Uri($"Assets/Langs/{code}.xaml", UriKind.Relative);
        if (_current == code && merged.Count == 3 && merged[2].Source == uri) return;
        // 先加载新字典；加载失败时仍保留当前可用语言。
        var dictionary = new ResourceDictionary { Source = uri };
        // 结构：[0]=主题色, [1]=控件, [2]=当前语言。每种语言已包含生成时的回退词条。
        for (int i = merged.Count - 1; i >= 2; i--)
            merged.RemoveAt(i);
        merged.Add(dictionary);
        _current = code;
        LanguageChanged?.Invoke();
    }

    /// <summary>应用启动时的语言（设置里保存的值，未保存过则 auto）。</summary>
    public static void ApplyFromSettings()
    {
        SetLanguage(string.IsNullOrWhiteSpace(SettingsService.Current.Language)
            ? "auto" : SettingsService.Current.Language);
    }

    public static string DetectSystemLanguage()
    {
        try
        {
            var name = System.Globalization.CultureInfo.CurrentUICulture.Name;
            if (name.StartsWith("zh")) return name.Contains("TW") || name.Contains("HK") || name.Contains("MO") ? "zh-TW" : "zh-CN";
            if (name.StartsWith("en")) return "en-US";
            if (name.StartsWith("ja")) return "ja-JP";
            if (name.StartsWith("ko")) return "ko-KR";
            if (name.StartsWith("fr")) return "fr-FR";
            if (name.StartsWith("es")) return "es-ES";
            if (name.StartsWith("de")) return "de-DE";
        }
        catch { }
        return "zh-CN";
    }

    /// <summary>代码取词；翻译回退已在构建时合并，缺失时返回 key 本身。</summary>
    public static string T(string key)
    {
        var v = Application.Current?.TryFindResource(key);
        return v as string ?? key;
    }

    /// <summary>规则元数据翻译：rule.&lt;id&gt;.field 缺失时回退 Core 中的中文原文。</summary>
    public static string RuleField(string ruleId, string field, string fallback)
    {
        var v = Application.Current?.TryFindResource($"rule.{ruleId}.{field}");
        return v as string ?? fallback;
    }

    public static string RuleName(string ruleId, string fallback) => RuleField(ruleId, "name", fallback);
    public static string RuleReason(string ruleId, string fallback) => RuleField(ruleId, "reason", fallback);
    public static string RuleImpact(string ruleId, string fallback) => RuleField(ruleId, "impact", fallback);

    /// <summary>Core 存储的中文类别名 → 本地化类别名（cat.* key）。</summary>
    private static readonly Dictionary<string, string> CategoryKeyMap = new()
    {
        ["系统"] = "cat.system", ["应用"] = "cat.apps", ["图片"] = "cat.images",
        ["视频"] = "cat.videos", ["音频"] = "cat.audio", ["文档"] = "cat.documents",
        ["压缩包"] = "cat.archives", ["其他"] = "cat.other",
    };

    public static string CategoryDisplay(string zhCategoryName)
    {
        return CategoryKeyMap.TryGetValue(zhCategoryName, out var key) ? T(key) : zhCategoryName;
    }
}
