using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SunTool.Modules.BatchRename.Logic.Rules;

public enum CaseMode
{
    Upper,
    Lower,
    TitleCase,
    FirstLetterUpper,
}

/// <summary>영문 대소문자를 바꿉니다.</summary>
public sealed partial class CaseRule : RenameRuleBase
{
    [ObservableProperty]
    private CaseMode _mode = CaseMode.Lower;

    public override string Apply(string name, RenameContext context) => Mode switch
    {
        CaseMode.Upper => name.ToUpperInvariant(),
        CaseMode.Lower => name.ToLowerInvariant(),
        CaseMode.TitleCase => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant()),
        CaseMode.FirstLetterUpper => name.Length == 0
            ? name
            : char.ToUpperInvariant(name[0]) + name[1..].ToLowerInvariant(),
        _ => name,
    };

    protected override string GetRuleName() => Loc.T("대소문자");

    protected override string BuildSummary() => Mode switch
    {
        CaseMode.Upper => Loc.T("모두 대문자"),
        CaseMode.Lower => Loc.T("모두 소문자"),
        CaseMode.TitleCase => Loc.T("단어 첫 글자만 대문자"),
        CaseMode.FirstLetterUpper => Loc.T("첫 글자만 대문자"),
        _ => Mode.ToString(),
    };
}
