using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SunTool.Modules.BatchRename.Logic.Rules;

/// <summary>
/// 정렬된 순서대로 번호를 붙입니다.
/// Format 의 {n} 자리에 번호가 들어갑니다 (예: "_{n}" → "_001", "({n})" → "(001)").
/// </summary>
public sealed partial class NumberingRule : RenameRuleBase
{
    public const int MaxPadding = 12;

    [ObservableProperty]
    private int _start = 1;

    [ObservableProperty]
    private int _step = 1;

    [ObservableProperty]
    private int _padding = 3;

    [ObservableProperty]
    private string _format = "_{n}";

    [ObservableProperty]
    private TextPosition _position = TextPosition.Suffix;

    [ObservableProperty]
    private int _index;

    [ObservableProperty]
    private bool _fromEnd;

    [ObservableProperty]
    private string _anchorText = string.Empty;

    [ObservableProperty]
    private bool _ignoreCase = true;

    /// <summary>true 면 기존 이름을 버리고 번호(서식)만 남깁니다.</summary>
    [ObservableProperty]
    private bool _replaceName;

    /// <summary>true 면 폴더마다 번호를 처음부터 다시 셉니다 (하위 폴더 포함 시).</summary>
    [ObservableProperty]
    private bool _perFolder;

    public override string Apply(string name, RenameContext context)
    {
        var sequence = PerFolder ? context.IndexInFolder : context.Index;
        var number = (long)Start + ((long)Step * sequence);
        var text = BuildText(number);
        return ReplaceName
            ? text
            : TextPlacement.Insert(name, text, Position, Index, FromEnd, AnchorText, IgnoreCase);
    }

    public string BuildText(long number)
    {
        var digits = FormatNumber(number, Padding);
        var format = Format ?? string.Empty;
        return format.Contains("{n}", StringComparison.Ordinal)
            ? format.Replace("{n}", digits, StringComparison.Ordinal)
            : format + digits;
    }

    public static string FormatNumber(long number, int padding)
    {
        var pad = Math.Clamp(padding, 0, MaxPadding);
        var digits = Math.Abs(number).ToString(CultureInfo.InvariantCulture).PadLeft(pad, '0');
        return number < 0 ? "-" + digits : digits;
    }

    protected override string GetRuleName() => "번호 매기기";

    protected override string BuildSummary()
    {
        var example = $"{BuildText(Start)}, {BuildText((long)Start + Step)}…";
        var where = ReplaceName ? "이름을 번호로 바꾸기" : $"{TextPlacement.Describe(Position, Index, FromEnd, AnchorText)}에 번호";
        return $"{where}: {example}" + (PerFolder ? " (폴더별)" : string.Empty);
    }
}
