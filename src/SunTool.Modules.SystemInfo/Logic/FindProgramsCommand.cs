using System.Collections;
using SunTool.Core.Diagnostics;
using SunTool.Core.Logging;

namespace SunTool.Modules.SystemInfo.Logic;

/// <summary>
/// 터미널 명령: programs &lt;검색어&gt;
/// 감지가 안 되거나 버전이 안 나올 때, 그 PC 에 실제로 어떤 이름·환경 변수로 설치되어 있는지 확인합니다.
/// 결과를 복사해 감지 목록(SunTool.software.json)에 패턴을 추가하면 됩니다.
/// </summary>
public sealed class FindProgramsCommand : IConsoleCommand
{
    public string Name => "programs";

    public string Usage => "programs <검색어>";

    public string Description => "설치 프로그램·환경 변수에서 검색어를 찾습니다 (예: programs pylon).";

    public void Execute(IReadOnlyList<string> args, IConsoleContext context)
    {
        if (args.Count == 0)
        {
            context.Write("사용법: programs <검색어>   예) programs pylon, programs opencv", LogLevel.Warning);
            return;
        }

        var keyword = string.Join(' ', args);
        var programs = InstalledProgramReader.ReadAll()
            .Where(p => p.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                        || p.Publisher.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        context.Write($"[설치 프로그램] '{keyword}' {programs.Count}건");
        foreach (var program in programs)
        {
            context.Write($"  {program.DisplayName} | 버전 {ValueText.OrDash(program.DisplayVersion)} | {ValueText.OrDash(program.Publisher)}" +
                          $"{(program.InstallLocation.Length > 0 ? $" | {program.InstallLocation}" : string.Empty)}{(program.Hidden ? " [숨김 항목]" : string.Empty)}");
        }

        var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.Machine, EnvironmentVariableTarget.User })
        {
            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables(target))
            {
                var name = entry.Key?.ToString() ?? string.Empty;
                var value = entry.Value?.ToString() ?? string.Empty;
                if (name.Equals("PATH", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var part in value.Split(';').Where(p => p.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                    {
                        variables[$"PATH 항목 ({target})|{part}"] = part;
                    }
                }
                else if (name.Contains(keyword, StringComparison.OrdinalIgnoreCase) || value.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    variables[name] = value;
                }
            }
        }

        context.Write($"[환경 변수] {variables.Count}건");
        foreach (var (name, value) in variables)
        {
            context.Write(name.StartsWith("PATH 항목", StringComparison.Ordinal) ? $"  {name.Split('|')[0]}: {value}" : $"  {name} = {value}");
        }
    }
}
