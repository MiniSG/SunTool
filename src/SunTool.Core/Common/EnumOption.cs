namespace SunTool.Core.Common;

/// <summary>콤보박스에 enum 값을 한글 라벨로 보여주기 위한 쌍.</summary>
public sealed record EnumOption<T>(T Value, string Label)
    where T : struct, Enum
{
    public override string ToString() => Label;
}
