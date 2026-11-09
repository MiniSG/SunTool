using CommunityToolkit.Mvvm.ComponentModel;

namespace SunTool.Modules.BatchRename.Logic.Rules;

public enum RemoveMode
{
    /// <summary>특정 문자열을 모두 지웁니다.</summary>
    Text,

    /// <summary>위치와 길이로 지웁니다.</summary>
    Range,
}

/// <summary>문자열 또는 위치 범위를 지웁니다.</summary>
public sealed partial class RemoveRule : RenameRuleBase
{
    [ObservableProperty]
    private RemoveMode _mode = RemoveMode.Text;

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private bool _ignoreCase = true;

    [ObservableProperty]
    private int _startIndex;

    [ObservableProperty]
    private int _length = 1;

    [ObservableProperty]
    private bool _fromEnd;

    public override string Apply(string name, RenameContext context)
    {
        if (Mode == RemoveMode.Text)
        {
            return string.IsNullOrEmpty(Text)
                ? name
                : name.Replace(Text, string.Empty, IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }

        var length = name.Length;
        var count = Math.Clamp(Length, 0, length);
        var offset = Math.Clamp(StartIndex, 0, length);
        var start = FromEnd ? length - offset - count : offset;
        start = Math.Clamp(start, 0, length);
        count = Math.Min(count, length - start);
        return count <= 0 ? name : name.Remove(start, count);
    }

    protected override string GetRuleName() => "삭제";

    protected override string BuildSummary() => Mode == RemoveMode.Text
        ? $"\"{Text}\" 모두 지우기"
        : FromEnd
            ? $"끝에서 {StartIndex}번째부터 {Length}글자 지우기"
            : $"{StartIndex}번째부터 {Length}글자 지우기";
}
