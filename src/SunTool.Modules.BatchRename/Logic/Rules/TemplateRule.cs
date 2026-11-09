using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SunTool.Modules.BatchRename.Logic.Rules;

/// <summary>
/// 토큰으로 이름 전체를 새로 만듭니다.
/// {name} 현재 이름, {orig} 원래 이름, {n} / {n:000} 번호, {ext} 확장자, {parent} 상위 폴더 이름,
/// {date:yyyyMMdd} 수정일, {created:yyyyMMdd} 생성일, {today:yyyyMMdd} 오늘, {count} 전체 개수
/// </summary>
public sealed partial class TemplateRule : RenameRuleBase
{
    private static readonly Regex TokenRegex = new(@"\{(?<key>[a-zA-Z]+)(?::(?<fmt>[^}]*))?\}", RegexOptions.Compiled);

    [ObservableProperty]
    private string _template = "{name}_{n:000}";

    [ObservableProperty]
    private int _start = 1;

    [ObservableProperty]
    private int _step = 1;

    [ObservableProperty]
    private bool _perFolder;

    public static string TokenHelp =>
        "{name} 현재 이름 · {orig} 원래 이름 · {n} 또는 {n:000} 번호 · {ext} 확장자 · {parent} 상위 폴더\n" +
        "{date:yyyyMMdd} 수정일 · {created:yyyyMMdd} 생성일 · {today:yyyy-MM-dd} 오늘 · {count} 전체 개수";

    public override string Apply(string name, RenameContext context)
    {
        if (string.IsNullOrEmpty(Template))
        {
            return name;
        }

        return TokenRegex.Replace(Template, match => Resolve(match, name, context));
    }

    protected override string GetRuleName() => "서식 템플릿";

    protected override string BuildSummary() => $"\"{Template}\"";

    private string Resolve(Match match, string name, RenameContext context)
    {
        var key = match.Groups["key"].Value.ToLowerInvariant();
        var format = match.Groups["fmt"].Success ? match.Groups["fmt"].Value : null;
        var item = context.Item;

        try
        {
            return key switch
            {
                "name" => name,
                "orig" => item.Stem,
                "n" => FormatNumber((long)Start + ((long)Step * (PerFolder ? context.IndexInFolder : context.Index)), format),
                "ext" => item.Extension.TrimStart('.'),
                "parent" => Path.GetFileName(item.Directory.TrimEnd(Path.DirectorySeparatorChar)),
                "date" or "modified" => item.Modified.ToString(format ?? "yyyyMMdd", CultureInfo.InvariantCulture),
                "created" => item.Created.ToString(format ?? "yyyyMMdd", CultureInfo.InvariantCulture),
                "today" => DateTime.Now.ToString(format ?? "yyyyMMdd", CultureInfo.InvariantCulture),
                "count" => context.Count.ToString(CultureInfo.InvariantCulture),
                _ => match.Value, // 모르는 토큰은 그대로 둠
            };
        }
        catch (FormatException)
        {
            throw new RenameRuleException($"서식이 올바르지 않습니다: {match.Value}");
        }
    }

    private static string FormatNumber(long number, string? format) =>
        string.IsNullOrEmpty(format)
            ? number.ToString(CultureInfo.InvariantCulture)
            : number.ToString(format, CultureInfo.InvariantCulture);
}
