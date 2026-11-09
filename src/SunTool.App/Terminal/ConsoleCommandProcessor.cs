using System.Text;
using SunTool.Core.Diagnostics;
using SunTool.Core.Logging;

namespace SunTool.App.Terminal;

/// <summary>터미널 입력 줄을 해석해 등록된 IConsoleCommand 를 실행합니다.</summary>
public sealed class ConsoleCommandProcessor
{
    private readonly Dictionary<string, IConsoleCommand> _commands;

    public ConsoleCommandProcessor(IEnumerable<IConsoleCommand> commands)
    {
        _commands = new Dictionary<string, IConsoleCommand>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in commands)
        {
            _commands[command.Name] = command;
        }
    }

    public IReadOnlyCollection<IConsoleCommand> Commands => _commands.Values;

    public void Execute(string line, IConsoleContext context)
    {
        var tokens = Tokenize(line);
        if (tokens.Count == 0)
        {
            return;
        }

        if (!_commands.TryGetValue(tokens[0], out var command))
        {
            context.Write($"알 수 없는 명령입니다: {tokens[0]}  ('help' 로 목록 확인)", LogLevel.Warning);
            return;
        }

        try
        {
            command.Execute(tokens.Skip(1).ToList(), context);
        }
        catch (Exception ex)
        {
            context.Write($"'{command.Name}' 실행 중 오류: {ex}", LogLevel.Error);
        }
    }

    /// <summary>공백으로 나누되 "따옴표 안"은 하나로 취급합니다.</summary>
    public static List<string> Tokenize(string line)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in line)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }
}
