using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SunTool.Modules.BatchRename.Logic.Rules;

/// <summary>문자열 또는 정규식을 찾아 바꿉니다.</summary>
public sealed partial class ReplaceRule : RenameRuleBase
{
    private Regex? _cachedRegex;
    private string? _cachedKey;

    [ObservableProperty]
    private string _find = string.Empty;

    [ObservableProperty]
    private string _replaceWith = string.Empty;

    [ObservableProperty]
    private bool _useRegex;

    [ObservableProperty]
    private bool _ignoreCase = true;

    [ObservableProperty]
    private bool _firstOnly;

    public override string Apply(string name, RenameContext context)
    {
        if (string.IsNullOrEmpty(Find))
        {
            return name;
        }

        var replacement = ReplaceWith ?? string.Empty;
        if (UseRegex)
        {
            var regex = GetRegex();
            try
            {
                return FirstOnly ? regex.Replace(name, replacement, 1) : regex.Replace(name, replacement);
            }
            catch (RegexMatchTimeoutException)
            {
                throw new RenameRuleException("정규식 처리 시간이 너무 깁니다.");
            }
        }

        var comparison = IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!FirstOnly)
        {
            return name.Replace(Find, replacement, comparison);
        }

        var at = name.IndexOf(Find, comparison);
        return at < 0 ? name : name.Remove(at, Find.Length).Insert(at, replacement);
    }

    protected override string GetRuleName() => "찾아 바꾸기";

    protected override string BuildSummary()
    {
        var kind = UseRegex ? "정규식" : "문자열";
        var target = string.IsNullOrEmpty(ReplaceWith) ? "(삭제)" : $"\"{ReplaceWith}\"";
        return $"{kind} \"{Find}\" → {target}" + (FirstOnly ? " (처음 하나만)" : string.Empty);
    }

    private Regex GetRegex()
    {
        var key = $"{IgnoreCase}|{Find}";
        if (_cachedRegex is not null && _cachedKey == key)
        {
            return _cachedRegex;
        }

        try
        {
            var options = RegexOptions.CultureInvariant | (IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
            _cachedRegex = new Regex(Find, options, TimeSpan.FromSeconds(1));
            _cachedKey = key;
            return _cachedRegex;
        }
        catch (ArgumentException ex)
        {
            throw new RenameRuleException($"정규식 오류: {ex.Message}");
        }
    }
}
