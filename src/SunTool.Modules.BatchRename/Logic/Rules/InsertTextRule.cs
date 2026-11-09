using CommunityToolkit.Mvvm.ComponentModel;

namespace SunTool.Modules.BatchRename.Logic.Rules;

/// <summary>앞/끝/중간(위치·특정 문자 앞뒤)에 같은 텍스트를 넣습니다.</summary>
public sealed partial class InsertTextRule : RenameRuleBase
{
    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private TextPosition _position = TextPosition.Prefix;

    [ObservableProperty]
    private int _index;

    [ObservableProperty]
    private bool _fromEnd;

    [ObservableProperty]
    private string _anchorText = string.Empty;

    [ObservableProperty]
    private bool _ignoreCase = true;

    public override string Apply(string name, RenameContext context) =>
        string.IsNullOrEmpty(Text)
            ? name
            : TextPlacement.Insert(name, Text, Position, Index, FromEnd, AnchorText, IgnoreCase);

    protected override string GetRuleName() => "텍스트 삽입";

    protected override string BuildSummary() =>
        $"{TextPlacement.Describe(Position, Index, FromEnd, AnchorText)}에 \"{Text}\" 넣기";
}
