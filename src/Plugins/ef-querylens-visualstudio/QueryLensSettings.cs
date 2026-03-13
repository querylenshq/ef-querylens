#pragma warning disable VSEXTPREVIEW_SETTINGS

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Settings;

namespace EFQueryLens.VisualStudio;

internal static class QueryLensSettings
{
    [VisualStudioContribution]
    public static SettingCategory Category { get; } = new("queryLens", "%EFQueryLens.Settings.Category.DisplayName%")
    {
        Description = "%EFQueryLens.Settings.Category.Description%",
        SearchKeywords = ["%EFQueryLens.Settings.Category.SearchKeywords%"],
    };

    [VisualStudioContribution]
    public static Setting.Integer MaxCodeLensPerDocument { get; } = new(
        "maxCodeLensPerDocument",
        "%EFQueryLens.Settings.MaxCodeLensPerDocument.DisplayName%",
        Category,
        defaultValue: 50)
    {
        Description = "%EFQueryLens.Settings.MaxCodeLensPerDocument.Description%",
        Minimum = 1,
        Maximum = 500,
        SearchKeywords = ["%EFQueryLens.Settings.MaxCodeLensPerDocument.SearchKeywords%"],
    };

    [VisualStudioContribution]
    public static Setting.Integer CodeLensDebounceMilliseconds { get; } = new(
        "codeLensDebounceMilliseconds",
        "%EFQueryLens.Settings.CodeLensDebounceMilliseconds.DisplayName%",
        Category,
        defaultValue: 250)
    {
        Description = "%EFQueryLens.Settings.CodeLensDebounceMilliseconds.Description%",
        Minimum = 0,
        Maximum = 5000,
        SearchKeywords = ["%EFQueryLens.Settings.CodeLensDebounceMilliseconds.SearchKeywords%"],
    };

    [VisualStudioContribution]
    public static Setting.Boolean UseModelFilter { get; } = new(
        "useModelFilter",
        "%EFQueryLens.Settings.UseModelFilter.DisplayName%",
        Category,
        defaultValue: false)
    {
        Description = "%EFQueryLens.Settings.UseModelFilter.Description%",
        SearchKeywords = ["%EFQueryLens.Settings.UseModelFilter.SearchKeywords%"],
    };

    [VisualStudioContribution]
    public static Setting.Boolean EnableVerboseLogs { get; } = new(
        "enableVerboseLogs",
        "%EFQueryLens.Settings.EnableVerboseLogs.DisplayName%",
        Category,
        defaultValue: true)
    {
        Description = "%EFQueryLens.Settings.EnableVerboseLogs.Description%",
        SearchKeywords = ["%EFQueryLens.Settings.EnableVerboseLogs.SearchKeywords%"],
    };
}

#pragma warning restore VSEXTPREVIEW_SETTINGS
