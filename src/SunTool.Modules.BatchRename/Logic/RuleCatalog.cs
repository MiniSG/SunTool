using SunTool.Core.Common;
using SunTool.Modules.BatchRename.Logic.Rules;

namespace SunTool.Modules.BatchRename.Logic;

public sealed record RuleDescriptor(string Name, string Description, string IconGlyph, Func<RenameRuleBase> Create);

/// <summary>"규칙 추가" 버튼에 나타나는 규칙 목록. 새 규칙은 여기에 한 줄 추가합니다.</summary>
public static class RuleCatalog
{
    public static IReadOnlyList<RuleDescriptor> All { get; } =
    [
        new("텍스트 삽입", "이름 앞/끝/중간(위치, 특정 문자 앞뒤)에 같은 텍스트를 넣습니다.", "", () => new InsertTextRule()),
        new("번호 매기기", "정렬 순서대로 001, 002… 같은 번호를 붙입니다.", "", () => new NumberingRule()),
        new("찾아 바꾸기", "문자열이나 정규식을 찾아 다른 문자열로 바꿉니다.", "", () => new ReplaceRule()),
        new("삭제", "특정 문자열이나 위치 범위를 지웁니다.", "", () => new RemoveRule()),
        new("대소문자", "영문 대소문자를 바꿉니다.", "", () => new CaseRule()),
        new("서식 템플릿", "{name}_{n:000} 같은 토큰으로 이름 전체를 만듭니다.", "", () => new TemplateRule()),
    ];
}

/// <summary>콤보박스용 한글 선택지.</summary>
public static class RenameChoices
{
    public static IReadOnlyList<EnumOption<TextPosition>> TextPositions { get; } =
    [
        new(TextPosition.Prefix, "이름 앞"),
        new(TextPosition.Suffix, "이름 끝"),
        new(TextPosition.AtIndex, "N번째 글자 위치"),
        new(TextPosition.BeforeText, "특정 문자 앞"),
        new(TextPosition.AfterText, "특정 문자 뒤"),
    ];

    public static IReadOnlyList<EnumOption<RemoveMode>> RemoveModes { get; } =
    [
        new(RemoveMode.Text, "특정 문자열"),
        new(RemoveMode.Range, "위치 범위"),
    ];

    public static IReadOnlyList<EnumOption<CaseMode>> CaseModes { get; } =
    [
        new(CaseMode.Lower, "모두 소문자"),
        new(CaseMode.Upper, "모두 대문자"),
        new(CaseMode.TitleCase, "단어 첫 글자만 대문자"),
        new(CaseMode.FirstLetterUpper, "첫 글자만 대문자"),
    ];

    public static IReadOnlyList<EnumOption<SortKey>> SortKeys { get; } =
    [
        new(SortKey.NaturalName, "이름 (자연 정렬: 2 < 10)"),
        new(SortKey.Name, "이름 (문자 순)"),
        new(SortKey.Modified, "수정한 날짜"),
        new(SortKey.Created, "만든 날짜"),
        new(SortKey.Size, "크기"),
        new(SortKey.Manual, "직접 지정 (위/아래 버튼)"),
    ];
}
